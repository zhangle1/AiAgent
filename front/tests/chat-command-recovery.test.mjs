import assert from "node:assert/strict";
import test from "node:test";
import { readRecoveryPointers, saveRecoveryPointer, removeRecoveryPointer } from "../lib/chat-command-recovery.ts";

test("pending pointers contain no prompt and a new operation replaces the old session pointer", () => {
  const data = new Map();
  globalThis.sessionStorage = { getItem: (key) => data.get(key) ?? null, setItem: (key, value) => data.set(key, value) };
  saveRecoveryPointer("first", { command_id: "first", session_id: "session", message: "private", attachment_ids: ["private-id"] });
  assert.ok(!JSON.stringify([...data.values()]).includes("private"));
  saveRecoveryPointer("second", { command_id: "second", session_id: "session", message: "same text" });
  assert.deepEqual(readRecoveryPointers().map((value) => value.command_id), ["second"]);
  removeRecoveryPointer("second");
  assert.deepEqual(readRecoveryPointers(), []);
});

test("unavailable storage never prevents sending", () => {
  globalThis.sessionStorage = { getItem() { throw new Error("blocked"); }, setItem() { throw new Error("blocked"); } };
  assert.deepEqual(readRecoveryPointers(), []);
  assert.doesNotThrow(() => saveRecoveryPointer("id", { command_id: "id", session_id: "s", message: "hello" }));
});
