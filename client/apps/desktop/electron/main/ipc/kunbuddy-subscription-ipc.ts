import { IPC, type ProviderPublic, type KunBuddyAccountStatus } from "@kunbuddy/shared";
import type { HostProcess } from "../host-process";
import type { IpcRegistrar } from "./types";
import { connectKunBuddySubscription, disconnectKunBuddySubscription, type KunBuddyLoginInput } from "../kunbuddy-subscription";
import type { KunBuddySessionSync } from "../kunbuddy-session-sync";
import { KunBuddyAccount } from "../kunbuddy-account";

export function registerKunBuddySubscriptionIpc(registrar: IpcRegistrar, getHost: () => HostProcess | null,
  enrich: (provider: ProviderPublic) => unknown, sync?: Pick<KunBuddySessionSync, "refresh" | "status">, accountFile?: string, onAccount?: (account: KunBuddyAccountStatus) => void) {
  const account = new KunBuddyAccount(getHost, accountFile);
  let tail = Promise.resolve();
  const enqueue = <T>(operation: () => Promise<T>): Promise<T> => {
    const result = tail.then(operation);
    tail = result.then(() => undefined, () => undefined);
    return result;
  };
  registrar.handleWithEvent(IPC.invoke.kunBuddySubscriptionLogin, async (event, input: KunBuddyLoginInput) => {
    registrar.assertMainWindowSender(event);
    return enqueue(async () => {
      const host = getHost();
      if (!host) throw new Error("host unavailable");
      const result = await connectKunBuddySubscription(input, { host });
      await account.connected(result);
      onAccount?.({ state: "authenticated", baseUrl: result.baseUrl, username: result.username, expiresAt: result.expiresAt });
      void sync?.refresh().catch(() => undefined);
      return { ...result, providers: result.providers.map(provider => enrich(provider)) };
    });
  });
  registrar.handleWithEvent(IPC.invoke.kunBuddySubscriptionLogout, async (event, providerId: string) => {
    registrar.assertMainWindowSender(event);
    return enqueue(async () => {
      const host = getHost();
      if (!host) throw new Error("host unavailable");
      const result = await disconnectKunBuddySubscription(providerId, { host });
      await account.disconnected();
      onAccount?.({ state: "signed_out", baseUrl: "", username: "" });
      return result;
    });
  });
  registrar.handleWithEvent(IPC.invoke.kunBuddyAccountStatus, async (event, force?: boolean) => {
    registrar.assertMainWindowSender(event);
    return enqueue(async () => { const result = await account.status(force === true); onAccount?.(result); return result; });
  });
  registrar.handleWithEvent(IPC.invoke.kunBuddySessionSyncStatus, async event => {
    registrar.assertMainWindowSender(event);
    return sync?.status() ?? { pending: 0, sessions: [] };
  });
  registrar.handleWithEvent(IPC.invoke.kunBuddySessionSyncRetry, async event => {
    registrar.assertMainWindowSender(event);
    await sync?.refresh();
    return sync?.status() ?? { pending: 0, sessions: [] };
  });
}
