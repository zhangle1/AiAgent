import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs";
import ts from "typescript";

const source = ts.transpileModule(fs.readFileSync(new URL("../components/chat/ChatStreamProvider.tsx", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX },
}).outputText;

function mount(t) {
  let now = 1000;
  let records = {};
  const timers = new Map();
  const effects = [];
  const calls = [];
  const events = [];
  let timerId = 0;
  t.mock.method(Date, "now", () => now);
  const previousWindow = globalThis.window;
  const previousDocument = globalThis.document;
  globalThis.window = {
    setTimeout(fn, delay) { timers.set(++timerId, { fn, at: now + delay }); return timerId; },
    setInterval(fn, delay) { timers.set(++timerId, { fn, at: now + delay, interval: delay }); return timerId; },
    clearTimeout(id) { timers.delete(id); },
    clearInterval(id) { timers.delete(id); },
    dispatchEvent(event) { events.push(event.type); },
  };
  globalThis.document = { addEventListener() {}, removeEventListener() {} };
  const react = {
    createContext: () => ({ Provider: "provider" }),
    useRef: (current) => ({ current }),
    useState: (initial) => [initial, (next) => { records = next; }],
    useEffect: (fn) => { effects.push(fn); },
    useCallback: (fn) => fn,
    useMemo: (fn) => fn(),
  };
  const api = {
    CHAT_STREAM_QUIET_AFTER_MS: 30000,
    getChatRuntimeId: () => "test-runtime",
    heartbeatCodexRuntime: async () => {},
    streamCompleteChat(request, emit, signal) {
      return new Promise((resolve, reject) => {
        calls.push({ request, emit, resolve, reject });
        signal.addEventListener("abort", () => reject(new DOMException("Stopped", "AbortError")));
      });
    },
  };
  const module = { exports: {} };
  new Function("require", "module", "exports", source)((name) => {
    if (name === "react") return react;
    if (name === "react/jsx-runtime") return { jsx: (_, props) => props.value };
    return api;
  }, module, module.exports);
  const context = module.exports.ChatStreamProvider({ children: null });
  const cleanups = effects.map((effect) => effect());
  t.after(async () => {
    cleanups.forEach((cleanup) => cleanup?.());
    await new Promise((resolve) => setImmediate(resolve));
    globalThis.window = previousWindow;
    globalThis.document = previousDocument;
  });
  const settle = () => new Promise((resolve) => setImmediate(resolve));
  const advance = async (ms) => {
    const target = now + ms;
    while (true) {
      const next = [...timers.entries()].filter(([, timer]) => timer.at <= target).sort((a, b) => a[1].at - b[1].at)[0];
      if (!next) break;
      const [id, timer] = next;
      now = timer.at;
      if (timer.interval) timer.at += timer.interval;
      else timers.delete(id);
      timer.fn();
      await settle();
    }
    now = target;
  };
  const fail = async () => { calls.at(-1).reject(new Error("Selected model is at capacity.")); await settle(); await advance(60); };
  return { context, calls, events, advance, settle, fail, records: () => records };
}

test("failure retries after 20 seconds with the same turn, attachments and model", async (t) => {
  const h = mount(t);
  const request = { session_id: "session", message: "hello", attachment_ids: ["image"], code_project_id: 3, codex_model_id: "model" };
  const id = h.context.startStream(request);
  await h.fail();
  assert.equal(h.records()[id].retryAt, 21000);
  await h.advance(19939);
  assert.equal(h.calls.length, 1);
  await h.advance(1);
  assert.equal(h.calls.length, 2);
  assert.deepEqual(h.calls[1].request, h.calls[0].request);
  assert.deepEqual(Object.keys(h.records()), [id]);
  assert.equal(h.records()[id].status, "streaming");
});

test("immediate retry consumes the pending timer and double clicks send once", async (t) => {
  const h = mount(t);
  const id = h.context.startStream({ session_id: "session", message: "hello" });
  await h.fail();
  h.context.retryStream(id);
  h.context.retryStream(id);
  await h.advance(21000);
  assert.equal(h.calls.length, 2);
});

test("cancelled automatic retry remains manually retryable", async (t) => {
  const h = mount(t);
  const id = h.context.startStream({ session_id: "session", message: "hello" });
  await h.fail();
  h.context.cancelRetry(id);
  await h.advance(21000);
  assert.equal(h.calls.length, 1);
  h.context.retryStream(id);
  assert.equal(h.calls.length, 2);
});

test("user stop does not schedule automatic retry", async (t) => {
  const h = mount(t);
  const id = h.context.startStream({ session_id: "session", message: "hello" });
  h.context.cancelStream(id);
  await h.settle();
  await h.advance(21000);
  assert.equal(h.records()[id].status, "stopped");
  assert.equal(h.calls.length, 1);
});

test("a new turn cancels the preceding failed turn's timer", async (t) => {
  const h = mount(t);
  h.context.startStream({ session_id: "session", message: "hello" });
  await h.fail();
  h.context.startStream({ session_id: "session", message: "new question" });
  h.calls.at(-1).resolve();
  await h.settle();
  await h.advance(21000);
  assert.equal(h.calls.length, 2);
});

test("an error event followed by normal transport close is failed, not completed", async (t) => {
  const h = mount(t);
  const id = h.context.startStream({ session_id: "session", message: "hello" });
  h.calls[0].emit({ type: "error", content: "capacity" });
  h.calls[0].resolve();
  await h.settle();
  assert.equal(h.records()[id].status, "error");
  assert.equal(h.records()[id].retryAt, 21000);
  assert.ok(h.events.includes("aiagent:chat-stream-failed"));
  assert.ok(!h.events.includes("aiagent:chat-stream-complete"));
});

test("transport errors preserve the model error and repeated failures restart the countdown", async (t) => {
  const h = mount(t);
  const id = h.context.startStream({ session_id: "session", message: "hello" });
  h.calls[0].emit({ type: "error", content: "Selected model is at capacity." });
  await h.fail();
  await h.advance(19940);
  assert.equal(h.calls.length, 2);
  h.calls[1].emit({ type: "error", content: "Model overloaded" });
  h.calls[1].reject(new Error("Chat stream ended before completion."));
  await h.settle();
  await h.advance(60);
  assert.equal(h.records()[id].errorMessage, "Model overloaded");
  assert.equal(h.records()[id].retryAt, 41000);
  await h.advance(19940);
  assert.equal(h.calls.length, 3);
});

test("success and other sessions cannot trigger or cancel the failed session's retry", async (t) => {
  const h = mount(t);
  h.context.startStream({ session_id: "failed-session", message: "hello" });
  await h.fail();
  const successId = h.context.startStream({ session_id: "other-session", message: "other question" });
  h.calls.at(-1).resolve();
  await h.settle();
  await h.advance(21000);
  assert.equal(h.records()[successId].status, "done");
  assert.equal(h.records()[successId].retryAt, undefined);
  assert.equal(h.calls.length, 3);
  assert.equal(h.calls[2].request.session_id, "failed-session");
});
