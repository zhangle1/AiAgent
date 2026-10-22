import type { ModelBinding, ProviderPublic } from "@kunbuddy/shared";

export const KUNBUDDY_SUBSCRIPTION_AUTH = "kunbuddy_subscription";

type Host = { call<T = any>(method: string, input?: unknown): Promise<T> };
type Dependencies = { host: Host; fetch?: typeof fetch };
type SubscriptionModel = { id: string; name?: string; context_window?: string | number | null; max_output_tokens?: string | number | null; supports_images?: boolean | null; is_default?: boolean };
type SubscriptionProfile = { id: string; name: string; api_style: string; models: SubscriptionModel[] };
export type KunBuddyLoginInput = { baseUrl: string; username: string; password: string };

export function normalizeKunBuddyUrl(input: string): string {
  const value = input.trim();
  if (!value || value.length > 2048) throw new Error("请输入 AiAgent 服务地址。");
  const url = new URL(value.includes("://") ? value : `http://${value}`);
  if (!["http:", "https:"].includes(url.protocol) || url.username || url.password || url.search || url.hash)
    throw new Error("服务地址仅支持 HTTP(S)，不能包含账号、密码、查询参数或片段。");
  return url.href.replace(/\/+$/, "").replace(/\/api\/v1$/i, "");
}

export class KunBuddyHttpError extends Error {
  readonly status: number;
  constructor(status: number, message: string) { super(message); this.status = status; }
}

async function jsonRequest(fetcher: typeof fetch, url: string, init: RequestInit) {
  let response: Response;
  try { response = await fetcher(url, { ...init, redirect: "error", signal: AbortSignal.timeout(20_000) }); }
  catch { throw new Error("无法连接 AiAgent，请检查服务地址、网络和证书。"); }
  if (!response.ok) {
    await response.body?.cancel();
    throw new KunBuddyHttpError(response.status, response.status === 401 ? "登录已失效或账号密码不正确，请重新登录坤伴订阅。"
      : `AiAgent 返回 HTTP ${response.status}，请检查服务版本和账号权限。`);
  }
  const reader = response.body?.getReader();
  const chunks: Uint8Array[] = []; let total = 0;
  if (!reader) throw new Error("AiAgent 返回了空响应。");
  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      total += value.byteLength;
      if (total > 2_000_000) { await reader.cancel(); throw new Error("服务端模型目录过大。"); }
      chunks.push(value);
    }
  } finally { reader.releaseLock(); }
  const bytes = new Uint8Array(total); let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  const text = new TextDecoder().decode(bytes);
  try { return JSON.parse(text); } catch { throw new Error("AiAgent 返回的响应不是有效 JSON。"); }
}

export function readKunBuddyCatalog(root: string, token: string, fetcher: typeof fetch = globalThis.fetch) {
  return jsonRequest(fetcher, root + "/api/v1/kunbuddy/models", { headers: { Authorization: `Bearer ${token}` } });
}

const positive = (value: unknown) => {
  const number = typeof value === "string" || typeof value === "number" ? Number(value) : 0;
  return Number.isSafeInteger(number) && number > 0 && number <= 2_000_000_000 ? number : 0;
};

export function subscriptionBindings(models: SubscriptionModel[]): ModelBinding[] {
  return models.map(model => ({
    id: model.id, alias: Array.from((model.name || model.id).trim()).slice(0, 60).join(""),
    // Zero delegates unknown limits to the runtime defaults; it is not a detected capacity.
    contextWindow: positive(model.context_window), maxTokens: positive(model.max_output_tokens),
    ...(positive(model.context_window) ? { contextWindowSource: "user" as const } : {}),
    ...(positive(model.max_output_tokens) ? { maxTokensSource: "user" as const } : {}),
    thinkingLevels: [], defaultThinkingLevel: null,
    ...(typeof model.supports_images === "boolean" ? { supportsImages: model.supports_images } : {}),
  }));
}

export async function connectKunBuddySubscription(input: KunBuddyLoginInput, dependencies: Dependencies) {
  if (!input || typeof input.baseUrl !== "string" || typeof input.username !== "string" || typeof input.password !== "string") throw new Error("请输入服务地址、账号和密码。");
  const root = normalizeKunBuddyUrl(input.baseUrl);
  const requestedUsername = input.username.trim();
  if (!requestedUsername || requestedUsername.length > 128 || !input.password || input.password.length > 4096) throw new Error("请输入有效的账号和密码。");
  const fetcher = dependencies.fetch ?? globalThis.fetch;
  const login = await jsonRequest(fetcher, root + "/api/v1/auth/kunbuddy-login", {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ username: requestedUsername, password: input.password }),
  });
  if (typeof login.access_token !== "string" || !login.access_token || login.access_token.length > 4096 || !/^[A-Za-z0-9._~+/-]+=*$/.test(login.access_token)) throw new Error("AiAgent 没有返回有效的订阅会话。");
  const token: string = login.access_token;
  const username: string = typeof login.username === "string" && login.username.length <= 128 ? login.username : requestedUsername;
  const authorization = { Authorization: `Bearer ${token}` };
  const created: string[] = [];
  const backups: Array<{ provider: ProviderPublic; secret?: string }> = [];
  try {
    const catalog = await jsonRequest(fetcher, root + "/api/v1/kunbuddy/models", { headers: authorization });
    if (!Array.isArray(catalog.profiles) || catalog.profiles.length > 200) throw new Error("AiAgent 返回的模型目录无效。");
    const profiles: SubscriptionProfile[] = catalog.profiles;
    if (!profiles.length) throw new Error("该账号暂无可用模型，请先在 AiAgent 配置 LLM 服务。");
    for (const profile of profiles) {
      if (!profile || typeof profile !== "object" || typeof profile.id !== "string" || !/^[a-zA-Z0-9_-]{1,128}$/.test(profile.id)
        || typeof profile.name !== "string"
        || !["chat_completions", "responses", "anthropic_messages", "google_generative_ai"].includes(profile.api_style)
        || !Array.isArray(profile.models) || !profile.models.length || profile.models.length > 2000
        || profile.models.some(model => !model || typeof model !== "object" || typeof model.id !== "string" || !/^[a-zA-Z0-9_-]{1,128}$/.test(model.id) || (model.name !== undefined && typeof model.name !== "string"))
        || new Set(profile.models.map(model => model.id)).size !== profile.models.length)
        throw new Error("AiAgent 返回的模型配置不受支持，请检查服务版本。");
    }
    if (new Set(profiles.map(profile => profile.id)).size !== profiles.length) throw new Error("AiAgent 返回了重复的配置档 ID。");
    profiles.sort((left, right) => Number(right.models.some(model => model.is_default === true)) - Number(left.models.some(model => model.is_default === true)));
    for (const profile of profiles) profile.models.sort((left, right) => Number(right.is_default === true) - Number(left.is_default === true));
    const { providers: existing } = await dependencies.host.call<{ providers: ProviderPublic[] }>("providers.list", {});
    const saved: ProviderPublic[] = [];
    for (const profile of profiles) {
      const baseUrl = `${root}/api/v1/kunbuddy/profiles/${encodeURIComponent(profile.id)}`;
      const previous = existing.find(provider => provider.authKind === KUNBUDDY_SUBSCRIPTION_AUTH && provider.baseUrl === baseUrl && provider.oauthAccountLabel === username);
      const config = { name: `坤伴订阅 · ${username} · ${profile.name || profile.id}`, vendorKey: "custom",
        type: "openai_compatible", protocol: "openai_compatible", authKind: KUNBUDDY_SUBSCRIPTION_AUTH,
        oauthAccountLabel: username, baseUrl, apiStyle: profile.api_style, models: subscriptionBindings(profile.models), secretValue: token };
      if (previous) {
        const secret = await dependencies.host.call<{ value?: string }>("providers.getSecret", { id: previous.id });
        backups.push({ provider: previous, secret: secret.value });
      }
      const result = await dependencies.host.call<{ provider: ProviderPublic }>(previous ? "providers.update" : "providers.create", previous ? { ...config, id: previous.id, enabled: true } : config);
      if (!previous) created.push(result.provider.id);
      saved.push(result.provider);
    }
    for (const previous of existing.filter(provider => provider.authKind === KUNBUDDY_SUBSCRIPTION_AUTH && provider.oauthAccountLabel === username
      && provider.baseUrl?.startsWith(root + "/api/v1/kunbuddy/profiles/") && !saved.some(item => item.id === provider.id))) {
      const secret = await dependencies.host.call<{ value?: string }>("providers.getSecret", { id: previous.id });
      backups.push({ provider: previous, secret: secret.value });
      await dependencies.host.call("providers.update", { id: previous.id, enabled: false });
      await dependencies.host.call("providers.setSecret", { id: previous.id });
    }
    return { providers: saved, username, expiresAt: typeof login.expires_at === "string" ? login.expires_at : undefined, baseUrl: root };
  } catch (error) {
    for (const id of created.reverse()) await dependencies.host.call("providers.delete", { id }).catch(() => undefined);
    for (const backup of backups.reverse()) {
      await dependencies.host.call("providers.update", backup.provider).catch(() => undefined);
      await dependencies.host.call("providers.setSecret", { id: backup.provider.id, secretValue: backup.secret }).catch(() => undefined);
    }
    await fetcher(root + "/api/v1/kunbuddy/logout", { method: "POST", headers: authorization, redirect: "error", signal: AbortSignal.timeout(5000) }).catch(() => undefined);
    throw error;
  }
}

export async function disconnectKunBuddySubscription(providerId: string, dependencies: Dependencies) {
  const { providers } = await dependencies.host.call<{ providers: ProviderPublic[] }>("providers.list", {});
  const provider = providers.find(item => item.id === providerId && item.authKind === KUNBUDDY_SUBSCRIPTION_AUTH);
  if (!provider?.baseUrl) throw new Error("订阅账号不存在。");
  const root = provider.baseUrl.replace(/\/api\/v1\/kunbuddy\/profiles\/[^/]+$/, "");
  normalizeKunBuddyUrl(root);
  const related = providers.filter(item => item.authKind === KUNBUDDY_SUBSCRIPTION_AUTH && item.baseUrl?.startsWith(root + "/api/v1/kunbuddy/profiles/") && item.oauthAccountLabel === provider.oauthAccountLabel);
  const tokens = new Set<string>();
  for (const item of related) {
    const secret = await dependencies.host.call<{ value?: string }>("providers.getSecret", { id: item.id });
    if (secret.value) tokens.add(secret.value);
  }
  const fetcher = dependencies.fetch ?? globalThis.fetch;
  for (const token of tokens) {
    let response: Response;
    try { response = await fetcher(root + "/api/v1/kunbuddy/logout", { method: "POST", headers: { Authorization: `Bearer ${token}` }, redirect: "error", signal: AbortSignal.timeout(10_000) }); }
    catch { throw new Error("无法连接 AiAgent 撤销订阅会话，请稍后重试。"); }
    if (!response.ok && response.status !== 401) throw new Error("退出订阅失败，请稍后重试。");
  }
  for (const item of related) await dependencies.host.call("providers.delete", { id: item.id });
  return { ok: true };
}
