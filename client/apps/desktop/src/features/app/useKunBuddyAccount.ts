import { useCallback, useEffect, useRef, useState } from "react";
import type { KunBuddyAccountStatus } from "@kunbuddy/shared";
import { api } from "../../lib/api";
import { useAppStore } from "../../stores/app-store";

export function useKunBuddyAccount(ready: boolean) {
  const [account, setAccount] = useState<KunBuddyAccountStatus | null>(null);
  const [checking, setChecking] = useState(false);
  const request = useRef<Promise<void> | null>(null);
  const mounted = useRef(false);
  const revision = useRef(0);
  const authenticating = useRef(false);
  const refresh = useCallback((force = true) => {
    if (authenticating.current) return Promise.resolve();
    if (request.current) return request.current;
    const generation = revision.current;
    setChecking(true);
    const operation = api.kunBuddyAccountStatus(force).then(value => {
      if (!mounted.current || generation !== revision.current) return;
      // Temporary network errors are not evidence of expiry. Local deadline still applies.
      setAccount(previous => value.state === "unavailable" && previous?.state === "authenticated"
        && previous.baseUrl === value.baseUrl && previous.username === value.username
        && (!previous.expiresAt || Date.parse(previous.expiresAt) > Date.now()) ? previous : value);
    }).catch(() => {
      if (mounted.current && generation === revision.current)
        setAccount(previous => previous?.state === "authenticated" && previous.expiresAt && Date.parse(previous.expiresAt) <= Date.now()
          ? { ...previous, state: "expired" } : previous ?? { state: "unavailable", baseUrl: "", username: "" });
    }).finally(() => { request.current = null; if (mounted.current) setChecking(false); });
    request.current = operation;
    return operation;
  }, []);
  useEffect(() => {
    mounted.current = true;
    if (!ready) return () => { mounted.current = false; };
    void refresh();
    const onFocus = () => { if (!document.hidden) void refresh(); };
    const onChanged = () => { revision.current++; void request.current?.finally(() => refresh()); if (!request.current) void refresh(); };
    const interval = setInterval(() => { void refresh(); }, 30_000);
    window.addEventListener("focus", onFocus);
    document.addEventListener("visibilitychange", onFocus);
    window.addEventListener("kunbuddy-account-changed", onChanged);
    const unsubscribe = api.onAgentEvent(event => {
      if (event.event.type === "error" || event.event.type === "agent_end") void refresh();
    });
    return () => {
      mounted.current = false; clearInterval(interval); unsubscribe();
      window.removeEventListener("focus", onFocus); document.removeEventListener("visibilitychange", onFocus);
      window.removeEventListener("kunbuddy-account-changed", onChanged);
    };
  }, [ready, refresh]);
  useEffect(() => {
    if (account?.state !== "authenticated" || !account.expiresAt) return;
    const remaining = Date.parse(account.expiresAt) - Date.now();
    const timer = setTimeout(() => { void refresh(); }, Math.max(0, Math.min(remaining + 50, 2_147_483_647)));
    return () => clearTimeout(timer);
  }, [account, refresh]);
  const login = async (input: { baseUrl: string; username: string; password: string }) => {
    authenticating.current = true;
    try {
      const result = await api.loginKunBuddySubscription(input);
      const state = useAppStore.getState();
      const settings = state.settings ?? await api.getSettings();
      const provider = result.providers.find(p => p.id === settings.defaultProviderId) ?? result.providers[0];
      const model = provider?.models.find(m => m.id === settings.defaultModelId) ?? provider?.models[0];
      if (provider && model) await api.setSettings({ ...settings, defaultProviderId: provider.id, defaultModelId: model.id });
      await state.refreshProviders();
      state.setPage("chat");
    } finally { authenticating.current = false; }
    await refresh();
  };
  return { account, checking, refresh, login };
}
