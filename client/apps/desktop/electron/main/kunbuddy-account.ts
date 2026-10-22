import { readFile, writeFile, rename, mkdir } from "node:fs/promises";
import { dirname } from "node:path";
import type { ProviderPublic, KunBuddyAccountStatus } from "@kunbuddy/shared";
import { KUNBUDDY_SUBSCRIPTION_AUTH, KunBuddyHttpError, normalizeKunBuddyUrl, readKunBuddyCatalog } from "./kunbuddy-subscription";

type Host = { call<T = any>(method: string, input?: unknown): Promise<T> };
type Hint = { baseUrl: string; username: string; expiresAt?: string; loggedOut?: boolean };

/** Account hints contain no password/token. Rust remains the only credential-store owner. */
export class KunBuddyAccount {
  private hint?: Hint;
  private loaded = false;
  private cached?: KunBuddyAccountStatus;
  private checkedAt = 0;
  private readonly file?: string;
  private readonly getHost: () => Host | null;
  private readonly fetcher: typeof fetch;
  constructor(getHost: () => Host | null, file?: string, fetcher: typeof fetch = globalThis.fetch) {
    this.getHost = getHost; this.file = file; this.fetcher = fetcher;
  }
  private async load() {
    if (this.loaded) return;
    this.loaded = true;
    if (!this.file) return;
    try {
      const value = JSON.parse(await readFile(this.file, "utf8"));
      if (typeof value.baseUrl === "string" && typeof value.username === "string" && value.username.length <= 128)
        this.hint = { baseUrl: normalizeKunBuddyUrl(value.baseUrl), username: value.username,
          expiresAt: typeof value.expiresAt === "string" && Number.isFinite(Date.parse(value.expiresAt)) ? value.expiresAt : undefined,
          loggedOut: value.loggedOut === true };
    } catch { /* Missing/corrupt public hints never expose or replace host credentials. */ }
  }
  private async persist() {
    if (!this.file || !this.hint) return;
    await mkdir(dirname(this.file), { recursive: true });
    await writeFile(this.file + ".tmp", JSON.stringify(this.hint), { mode: 0o600 });
    await rename(this.file + ".tmp", this.file);
  }
  async connected(result: { baseUrl: string; username: string; expiresAt?: string }) {
    await this.load();
    this.hint = { baseUrl: result.baseUrl, username: result.username,
      expiresAt: result.expiresAt && Number.isFinite(Date.parse(result.expiresAt)) ? result.expiresAt : undefined };
    this.cached = { ...this.hint, state: "authenticated" }; this.checkedAt = 0;
    await this.persist();
  }
  async disconnected() {
    await this.load();
    if (this.hint) { this.hint.loggedOut = true; await this.persist(); }
    this.cached = undefined; this.checkedAt = 0;
  }
  async status(force = false): Promise<KunBuddyAccountStatus> {
    await this.load();
    if (!force && this.cached && Date.now() - this.checkedAt < 30_000
      && (!this.cached.expiresAt || Date.parse(this.cached.expiresAt) > Date.now())) return this.cached;
    const host = this.getHost();
    if (!host) return { state: "unavailable", baseUrl: this.hint?.baseUrl ?? "", username: this.hint?.username ?? "" };
    const { providers } = await host.call<{ providers: ProviderPublic[] }>("providers.list", {});
    const candidates = providers.filter(p => p.authKind === KUNBUDDY_SUBSCRIPTION_AUTH && p.enabled !== false && p.baseUrl && p.oauthAccountLabel);
    const candidate = this.hint ? candidates.find(p => p.baseUrl?.startsWith(this.hint!.baseUrl + "/api/v1/kunbuddy/profiles/") && p.oauthAccountLabel === this.hint!.username) : candidates[0];
    if (!this.hint && candidate) this.hint = { baseUrl: normalizeKunBuddyUrl(candidate.baseUrl!.replace(/\/api\/v1\/kunbuddy\/profiles\/[^/]+$/, "")), username: candidate.oauthAccountLabel! };
    const details = { baseUrl: this.hint?.baseUrl ?? "", username: this.hint?.username ?? "", expiresAt: this.hint?.expiresAt };
    const finish = (state: KunBuddyAccountStatus["state"]) => { this.cached = { ...details, state }; this.checkedAt = Date.now(); return this.cached; };
    if (this.hint?.loggedOut || !candidate) return finish("signed_out");
    if (this.hint?.expiresAt && Date.parse(this.hint.expiresAt) <= Date.now()) return finish("expired");
    const { value: token } = await host.call<{ value?: string }>("providers.getSecret", { id: candidate.id });
    if (!token) return finish("expired");
    try {
      const catalog = await readKunBuddyCatalog(details.baseUrl, token, this.fetcher);
      if (!Array.isArray(catalog.profiles)) return finish("unavailable");
      return finish("authenticated");
    } catch (error) {
      return finish(error instanceof KunBuddyHttpError && error.status === 401 ? "expired" : "unavailable");
    }
  }
}
