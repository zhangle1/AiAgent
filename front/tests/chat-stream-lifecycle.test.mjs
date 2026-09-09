import assert from "node:assert/strict";
import test from "node:test";

globalThis.window = {
  location: { protocol: "http:", host: "localhost" },
  setTimeout(callback, delay) {
    return setTimeout(callback, delay >= 10_000 ? 15 : delay);
  },
  clearTimeout,
};

const { streamCompleteChat } = await import("../lib/chat-api.ts");

function installSocket(onSend) {
  globalThis.WebSocket = class FakeWebSocket {
    constructor() {
      queueMicrotask(() => this.onopen?.());
    }

    send() {
      onSend(this);
    }

    close() {}
  };
}

test("an opened stream that stops producing events reaches an error terminal state", async () => {
  installSocket(() => {});

  const result = await Promise.race([
    streamCompleteChat({ session_id: "idle-stream", message: "test", agent: "codex" }, () => {})
      .then(() => ({ kind: "resolved" }))
      .catch((error) => ({ kind: "rejected", error })),
    new Promise((resolve) => setTimeout(() => resolve({ kind: "hung" }), 120)),
  ]);

  assert.equal(result.kind, "rejected", "the UI must not remain streaming forever");
  assert.equal(result.error?.name, "ChatStreamError");
  assert.match(result.error?.message ?? "", /没有收到|未收到|事件/);
});

test("a WebSocket failure after request submission does not replay the turn over SSE", async () => {
  let fetchCalls = 0;
  globalThis.fetch = async () => {
    fetchCalls += 1;
    throw new Error("unexpected SSE replay");
  };
  installSocket((socket) => queueMicrotask(() => socket.onclose?.()));

  await assert.rejects(
    streamCompleteChat({ session_id: "closed-stream", message: "test", agent: "codex" }, () => {}),
    (error) => error?.name === "ChatStreamError",
  );
  assert.equal(fetchCalls, 0);
});

test("a client event handling failure does not replay an already submitted turn", async () => {
  let fetchCalls = 0;
  globalThis.fetch = async () => {
    fetchCalls += 1;
    throw new Error("unexpected SSE replay");
  };
  installSocket((socket) => queueMicrotask(() => socket.onmessage?.({ data: JSON.stringify({ type: "content", content: "partial" }) })));

  await assert.rejects(
    streamCompleteChat(
      { session_id: "event-failure", message: "test", agent: "codex" },
      () => { throw new Error("render callback failed"); },
    ),
    (error) => error?.name === "ChatStreamError",
  );
  assert.equal(fetchCalls, 0);
});

test("an explicit completed event remains a successful terminal state", async () => {
  installSocket((socket) => queueMicrotask(() => socket.onmessage?.({ data: JSON.stringify({ type: "completed" }) })));

  await streamCompleteChat({ session_id: "completed-stream", message: "test", agent: "codex" }, () => {});
});

test("connection setup cannot remain pending forever across WebSocket and SSE", async () => {
  globalThis.WebSocket = class NeverOpenedWebSocket {
    close() {}
  };
  globalThis.fetch = async () => new Promise(() => {});

  const result = await Promise.race([
    streamCompleteChat({ session_id: "connecting-stream", message: "test", agent: "codex" }, () => {})
      .then(() => ({ kind: "resolved" }))
      .catch((error) => ({ kind: "rejected", error })),
    new Promise((resolve) => setTimeout(() => resolve({ kind: "hung" }), 120)),
  ]);

  assert.equal(result.kind, "rejected", "connection setup must reach a terminal state");
  assert.equal(result.error?.name, "ChatStreamError");
});

test("an SSE response that stops producing bytes reaches an error terminal state", async () => {
  globalThis.WebSocket = class FailedWebSocket {
    constructor() {
      queueMicrotask(() => this.onerror?.());
    }

    close() {}
  };
  globalThis.fetch = async () => ({
    ok: true,
    body: {
      getReader: () => ({
        read: async () => new Promise(() => {}),
        cancel: async () => undefined,
      }),
    },
  });

  await assert.rejects(
    streamCompleteChat({ session_id: "idle-sse-stream", message: "test", agent: "codex" }, () => {}),
    (error) => error?.name === "ChatStreamError" && /没有收到/.test(error.message),
  );
});
