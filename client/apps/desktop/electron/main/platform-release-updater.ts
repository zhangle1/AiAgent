import { createHash, randomUUID } from "node:crypto";
import { createReadStream } from "node:fs";
import { mkdir, open, rename, rm, lstat, realpath } from "node:fs/promises";
import { join, resolve } from "node:path";
import type { KunBuddyAccountStatus, ProviderPublic, UpdateState } from "@kunbuddy/shared";
import { KUNBUDDY_SUBSCRIPTION_AUTH, normalizeKunBuddyUrl } from "./kunbuddy-subscription";

type Host = { call<T = any>(method: string, input?: unknown): Promise<T> };
type Release = { id: string; version: string; platform: string; architecture: string; filename: string; size: number; sha256: string; notes: string };
export type PlatformReleaseOptions = {
  currentVersion: string; directory: string; getHost: () => Host | null;
  send: (state: UpdateState) => void; install: (path: string) => Promise<void>;
  canInstall: boolean; platform?: string; architecture?: string; fetch?: typeof fetch;
};
const digest = (value: string) => createHash("sha256").update(value).digest("hex");
export function newerVersion(candidate: string, current: string): boolean {
  const parse = (value: string) => { const match = /^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})(?:-[a-zA-Z0-9.-]+)?$/.exec(value); return match?.slice(1, 4).map(Number); };
  const left = parse(candidate), right = parse(current); if (!left || !right) return false;
  for (let i = 0; i < 3; i++) if (left[i] !== right[i]) return left[i] > right[i];
  return !candidate.includes("-") && current.includes("-");
}

/** Account-bound installer transport. A new URL replaces all previous in-memory update authority. */
export class PlatformReleaseUpdater {
  private readonly options: PlatformReleaseOptions;
  private readonly platform: string;
  private readonly architecture: string;
  private readonly fetcher: typeof fetch;
  private account?: KunBuddyAccountStatus;
  private epoch = 0;
  private requests = new AbortController();
  private release?: Release;
  private downloaded?: string;
  private timer?: ReturnType<typeof setInterval>;
  private checking?: Promise<UpdateState>;
  private state: UpdateState;
  installing = false;
  constructor(options: PlatformReleaseOptions) {
    this.options = options; this.platform = options.platform ?? process.platform; this.architecture = options.architecture ?? process.arch;
    this.fetcher = options.fetch ?? globalThis.fetch;
    this.state = this.empty();
  }
  private empty(): UpdateState { return { delivery: "platform-installer", mode: "disabled", status: "idle", preference: "manual", defaultPreference: "manual", automaticSupported: false, currentVersion: this.options.currentVersion, releasesUrl: "", canInstall: this.options.canInstall }; }
  getState() { return this.state; }
  private patch(patch: Partial<UpdateState>) { this.state = { ...this.state, ...patch }; this.options.send(this.state); }
  setAccount(next: KunBuddyAccountStatus) {
    if (next.state === "unavailable" && this.account?.state === "authenticated" && next.baseUrl === this.account.baseUrl && next.username === this.account.username && (!this.account.expiresAt || Date.parse(this.account.expiresAt) > Date.now())) return;
    const active = next.state === "authenticated" && (!next.expiresAt || Date.parse(next.expiresAt) > Date.now());
    const key = active ? digest(normalizeKunBuddyUrl(next.baseUrl) + "\n" + next.username) : undefined;
    if (key === this.state.sourceKey) { if (active) this.account = next; return; }
    this.epoch++; this.requests.abort(); this.requests = new AbortController(); this.checking = undefined;
    this.release = undefined; this.downloaded = undefined; this.account = active ? { ...next, baseUrl: normalizeKunBuddyUrl(next.baseUrl) } : undefined;
    if (this.timer) clearInterval(this.timer); this.timer = undefined;
    this.state = { ...this.empty(), mode: active ? "manual" : "disabled", sourceKey: key, releasesUrl: this.account?.baseUrl ?? "" }; this.options.send(this.state);
    if (active) {
      void this.check(); this.timer = setInterval(() => { void this.check(); }, 30 * 60 * 1000); this.timer.unref?.();
    }
  }
  private async credentials() {
    const account = this.account, host = this.options.getHost();
    if (!account || !host || account.expiresAt && Date.parse(account.expiresAt) <= Date.now()) throw Error("请重新登录当前平台后检查更新。");
    const { providers } = await host.call<{ providers: ProviderPublic[] }>("providers.list", {});
    const provider = providers.find(p => p.authKind === KUNBUDDY_SUBSCRIPTION_AUTH && p.enabled !== false && p.oauthAccountLabel === account.username && p.baseUrl?.startsWith(account.baseUrl + "/api/v1/kunbuddy/profiles/"));
    if (!provider) throw Error("当前平台订阅不可用，请重新登录。");
    const { value } = await host.call<{ value?: string }>("providers.getSecret", { id: provider.id });
    if (!value) throw Error("当前平台登录已失效。");
    return { root: account.baseUrl, token: value };
  }
  private async latest(epoch: number): Promise<Release | undefined> {
    const sourceSignal = this.requests.signal;
    const { root, token } = await this.credentials();
    if (epoch !== this.epoch) throw Error("更新来源已切换。");
    const platform = this.platform === "win32" ? "windows" : this.platform === "darwin" ? "macos" : "unsupported";
    const response = await this.fetcher(`${root}/api/v1/kunbuddy/releases/latest?platform=${platform}&architecture=${this.architecture}`, { headers: { Authorization: `Bearer ${token}` }, redirect: "error", signal: AbortSignal.any([sourceSignal, AbortSignal.timeout(15000)]) });
    if (!response.ok) { await response.body?.cancel(); throw Error(response.status === 401 ? "平台登录已过期，请重新登录。" : `检查更新失败（HTTP ${response.status}）。`); }
    const reader = response.body?.getReader(); if (!reader) throw Error("版本响应为空。");
    let text = "", length = 0; const decoder = new TextDecoder();
    try { while (true) { const part = await reader.read(); if (part.done) break; length += part.value.byteLength; if (length > 65536) { await reader.cancel(); throw Error("版本响应过大。"); } text += decoder.decode(part.value, { stream: true }); } text += decoder.decode(); } finally { reader.releaseLock(); }
    const data = JSON.parse(text); if (data.release === null) return;
    const row: Release = data.release;
    if (!row || !/^[a-f0-9]{32}$/.test(row.id) || !/^[a-f0-9]{64}$/.test(row.sha256) || !Number.isSafeInteger(row.size) || row.size < 64 || row.size > 2 * 1024 ** 3 || row.platform !== platform || row.architecture !== this.architecture || !/^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})$/.test(row.version) || typeof row.filename !== "string" || row.filename.length > 200 || /[\\/\x00-\x1f]/.test(row.filename) || !(platform === "windows" ? /\.exe$/i : /\.(dmg|zip)$/i).test(row.filename) || typeof row.notes !== "string" || row.notes.length > 10000) throw Error("平台返回的安装包信息无效。");
    return row;
  }
  check(): Promise<UpdateState> {
    if (!this.account || this.state.status === "downloading" || this.state.status === "downloaded") return Promise.resolve(this.state);
    if (this.checking) return this.checking;
    const epoch = this.epoch;
    this.patch({ status: "checking", error: undefined });
    const operation = this.latest(epoch).then(row => {
      if (epoch !== this.epoch) return;
      this.release = row && newerVersion(row.version, this.options.currentVersion) ? row : undefined;
      this.downloaded = undefined;
      this.patch({ status: this.release ? "available" : "up-to-date", availableVersion: this.release?.version, installerName: this.release?.filename, releaseNotes: this.release?.notes, dismissed: false, manualReminder: !!this.release, progressPercent: undefined });
    }).catch(error => { if (epoch === this.epoch) this.patch({ status: "error", error: error instanceof Error && /^(平台|当前|请|版本|检查)/.test(error.message) ? error.message : "无法检查更新，请检查平台连接。" }); }).then(() => this.state).finally(() => { if (this.checking === operation) this.checking = undefined; });
    this.checking = operation; return operation;
  }
  async download() {
    if (this.state.status === "downloading" || this.state.status === "downloaded") return this.state;
    const row = this.release, epoch = this.epoch, sourceSignal = this.requests.signal; if (!row || !this.state.sourceKey) throw Error("请先检查当前平台的新版本。");
    let credentials: { root: string; token: string };
    try { credentials = await this.credentials(); } catch { if (epoch === this.epoch) this.patch({ status: "error", error: "平台登录不可用，请重新登录后下载。" }); return this.state; }
    const { root, token } = credentials; if (epoch !== this.epoch) return this.state;
    this.patch({ status: "downloading", progressPercent: 0, error: undefined, dismissed: false });
    const cache = resolve(this.options.directory, this.state.sourceKey); let temporary: string | undefined;
    try {
      await mkdir(cache, { recursive: true }); if ((await realpath(cache)).toLowerCase() !== cache.toLowerCase()) throw Error("更新缓存路径无效。");
      const extension = /\.exe$/i.test(row.filename) ? ".exe" : /\.dmg$/i.test(row.filename) ? ".dmg" : ".zip";
      const target = join(cache, row.id + "-" + row.sha256 + extension); temporary = target + "." + randomUUID() + ".part";
      if (epoch !== this.epoch) throw Error("更新来源已切换。");
      const response = await this.fetcher(root + "/api/v1/kunbuddy/releases/" + row.id + "/file", { headers: { Authorization: `Bearer ${token}` }, redirect: "error", signal: AbortSignal.any([sourceSignal, AbortSignal.timeout(30 * 60 * 1000)]) });
      if (!response.ok || !response.body) { await response.body?.cancel(); throw Error("安装包不可用，请刷新版本或重新登录。"); }
      const file = await open(temporary, "wx", 0o600); const reader = response.body.getReader(); const hash = createHash("sha256"); let size = 0, percent = -1;
      try {
        while (true) {
          const part = await reader.read(); if (part.done) break; if (epoch !== this.epoch) throw Error("更新来源已切换。");
          size += part.value.byteLength; if (size > row.size) { await reader.cancel(); throw Error("安装包大小校验失败。"); }
          hash.update(part.value); await file.writeFile(part.value); if (epoch !== this.epoch) throw Error("更新来源已切换。"); const next = Math.floor(size / row.size * 100); if (next !== percent) { percent = next; this.patch({ progressPercent: next }); }
        }
      } finally { reader.releaseLock(); await file.close(); }
      if (size !== row.size || hash.digest("hex") !== row.sha256) throw Error("安装包完整性校验失败，请重新下载。");
      if (epoch !== this.epoch) throw Error("更新来源已切换。");
      await rename(temporary, target); temporary = undefined; if (epoch !== this.epoch) throw Error("更新来源已切换。"); this.downloaded = target; this.patch({ status: "downloaded", progressPercent: 100 });
    } catch (error) {
      if (epoch === this.epoch) this.patch({ status: "error", error: error instanceof Error && /^(安装包|更新)/.test(error.message) ? error.message : "安装包下载失败，请重试。" });
    } finally { if (temporary) await rm(temporary, { force: true }).catch(() => undefined); }
    return this.state;
  }
  async install() {
    const epoch = this.epoch, file = this.downloaded, row = this.release;
    if (!this.options.canInstall || !file || !row || this.state.status !== "downloaded") throw Error("请在已打包的客户端中下载并打开安装包。");
    try {
      const latest = await this.latest(epoch); if (!latest || latest.id !== row.id || latest.sha256 !== row.sha256) throw Error("安装包已撤回或版本已变化，请重新检查更新。");
      const stat = await lstat(file); if (!stat.isFile() || stat.isSymbolicLink() || stat.size !== row.size || (await realpath(file)).toLowerCase() !== resolve(file).toLowerCase()) throw Error("安装包文件无效。");
      const hash = createHash("sha256"); for await (const chunk of createReadStream(file)) hash.update(chunk);
      if (hash.digest("hex") !== row.sha256) throw Error("安装包完整性校验失败。");
      if (epoch !== this.epoch) throw Error("更新来源已切换。");
      this.installing = true; await this.options.install(file);
    } catch (error) { this.installing = false; if (epoch === this.epoch) this.patch({ status: "error", error: error instanceof Error && /^(安装包|更新)/.test(error.message) ? error.message : "无法打开安装包，请重试。" }); throw Error(this.state.error ?? "更新来源已切换。"); }
  }
  dismiss() { this.patch({ dismissed: true }); }
  dispose() { this.epoch++; this.requests.abort(); if (this.timer) clearInterval(this.timer); }
}
