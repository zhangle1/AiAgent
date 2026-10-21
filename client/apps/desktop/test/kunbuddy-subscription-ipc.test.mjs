// Integration with the built shared package; run after rebuilding @kunbuddy/shared.
import assert from "node:assert/strict";
import { register } from "node:module";
import test from "node:test";
register(new URL("./helpers/ts-import-hooks.mjs", import.meta.url));

test("subscription IPC rejects secondary windows before touching credentials or the host", async () => {
  const { registerKunBuddySubscriptionIpc } = await import("../electron/main/ipc/kunbuddy-subscription-ipc.ts");
  const { IPC } = await import("@kunbuddy/shared");
  const handlers = new Map(); let hostLookups = 0;
  registerKunBuddySubscriptionIpc({
    handleWithEvent: (channel, handler) => handlers.set(channel, handler),
    assertMainWindowSender: event => { if (event.sender.id !== 1) throw Error("PERMISSION_DENIED"); },
  }, () => { hostLookups++; return null; }, provider => provider);
  assert.equal(typeof IPC.invoke.kunBuddySubscriptionLogin, "string", "The shared package must include the current subscription channels");
  const credentials = { baseUrl: "https://fixture.test", username: "fixture-user", password: "fixture-password" };
  await assert.rejects(() => handlers.get(IPC.invoke.kunBuddySubscriptionLogin)({ sender: { id: 2 } }, credentials), /PERMISSION_DENIED/);
  await assert.rejects(() => handlers.get(IPC.invoke.kunBuddySubscriptionLogout)({ sender: { id: 2 } }, "1"), /PERMISSION_DENIED/);
  await assert.rejects(() => handlers.get(IPC.invoke.kunBuddySessionSyncStatus)({ sender: { id: 2 } }), /PERMISSION_DENIED/);
  await assert.rejects(() => handlers.get(IPC.invoke.kunBuddySessionSyncRetry)({ sender: { id: 2 } }), /PERMISSION_DENIED/);
  assert.equal(hostLookups, 0);
});
