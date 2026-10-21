import { createHash, randomUUID } from "node:crypto";
import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import { join } from "node:path";
import type { ProviderPublic, UiMessage } from "@kunbuddy/shared";
import { KUNBUDDY_SUBSCRIPTION_AUTH, normalizeKunBuddyUrl } from "./kunbuddy-subscription";

type Host = { call<T = any>(method: string, input?: unknown): Promise<T> };
type Session = { id: string; title: string; providerId?: string; messages: UiMessage[]; messageStart?: number; hasMoreBefore?: boolean };
type Entry = { id: string; markdown: string };
type Binding = { root: string; username: string; providerId: string; pending: boolean; generation: number; acknowledged: Record<string, string>; lastUploaded?: string; error?: string };
type State = { clientId: string; sessions: Record<string, Binding> };
const hash = (value: string) => createHash("sha256").update(value).digest("hex");
const line = (value: string) => value.replace(/[\r\n]/g, " ");
async function confirmReceipt(response: Response, count: number) {
  const reader = response.body?.getReader();
  if (!reader) throw Error("upload_receipt_invalid");
  const chunks: Uint8Array[] = []; let bytes = 0;
  try {
    while (true) {
      const part = await reader.read(); if (part.done) break;
      bytes += part.value.byteLength;
      if (bytes > 8192) { await reader.cancel(); throw Error("upload_receipt_invalid"); }
      chunks.push(part.value);
    }
    const receipt = JSON.parse(Buffer.concat(chunks).toString("utf8"));
    if (!Number.isSafeInteger(receipt.document_id) || receipt.document_id <= 0 || typeof receipt.uri !== "string"
      || !/^viking:\/\/user\/[^/]+\/sessions\/[a-f0-9]{64}\.md$/.test(receipt.uri)
      || !/^[a-f0-9]{64}$/.test(receipt.sha256) || !Number.isSafeInteger(receipt.appended) || receipt.appended < 0 || receipt.appended > count)
      throw Error("upload_receipt_invalid");
  } catch { throw Error("upload_receipt_invalid"); }
  finally { reader.releaseLock(); }
}
function code(value: unknown) {
  const text = typeof value === "string" ? value : JSON.stringify(value, null, 2) ?? "";
  let length = 3;
  for (const match of text.matchAll(/`+/g)) length = Math.max(length, match[0].length + 1);
  const fence = "`".repeat(length);
  return `${fence}\n${text}\n${fence}`;
}

/** Deterministic serialization of visible, durable messages. No model call or hidden model prompt. */
export function conversationEntries(messages: UiMessage[]): Entry[] {
  const entries: Entry[] = [];
  for (const message of messages) {
    if (message.modelSystem || message.status === "streaming") continue;
    const role = { user: "用户", assistant: "助手", tool: "工具", system: "系统事件" }[message.role];
    let markdown = `## ${role} · ${line(message.createdAt)}\n\n${message.content || ""}`;
    if (message.command && message.command !== message.content) markdown += `\n\n### 原始输入\n\n${message.command}`;
    if (message.thinking) markdown += `\n\n### 推理记录\n\n${message.thinking}`;
    if (message.toolName) markdown += `\n\n工具：${line(message.toolName)}`;
    if (message.toolArgs !== undefined) markdown += `\n\n### 工具参数\n\n${code(message.toolArgs)}`;
    if (message.toolResult !== undefined) markdown += `\n\n### 工具结果\n\n${code(message.toolResult)}`;
    for (const attachment of message.attachments ?? []) markdown += `\n\n附件：${line(attachment.name)}（${line(attachment.kind)}，仅记录引用，未上传附件文件）${attachment.text ? "\n\n" + attachment.text : ""}`;
    if (message.status) markdown += `\n\n状态：${message.status}`;
    if (message.error) markdown += `\n\n错误：${line(message.error.code)} · ${message.error.message}`;
    markdown += "\n\n";
    // Split oversized original text by Unicode code point; never truncate it or send binary attachments.
    let part = "", bytes = 0, index = 0;
    for (const character of markdown) {
      const size = Buffer.byteLength(character);
      if (bytes + size > 256 * 1024) { entries.push({ id: `${message.id}:${index++}`, markdown: part }); part = ""; bytes = 0; }
      part += character; bytes += size;
    }
    entries.push({ id: `${message.id}:${index}`, markdown: part });
  }
  return entries;
}

export class KunBuddySessionSync {
  private state: State = { clientId: randomUUID(), sessions: {} };
  private readonly file: string;
  private readonly loaded: Promise<void>;
  private writes = Promise.resolve();
  private marks = Promise.resolve();
  private readonly marking = new Map<string, Promise<void>>();
  private flushing: Promise<void> | null = null;
  private scanning: Promise<void> | null = null;
  private scanned = false;
  private timer?: ReturnType<typeof setTimeout>;
  private closed = false;
  private readonly requests = new AbortController();
  private readonly dataDir: string;
  private readonly getHost: () => Host | null;
  private readonly log: (code: string, sessionId?: string) => void;
  private readonly fetcher: typeof fetch;
  private readonly enabled: boolean;

  constructor(dataDir: string, getHost: () => Host | null, log: (code: string, sessionId?: string) => void,
    fetcher: typeof fetch = globalThis.fetch, enabled = true) {
    this.dataDir = dataDir; this.getHost = getHost; this.log = log; this.fetcher = fetcher; this.enabled = enabled;
    this.file = join(dataDir, "kunbuddy-session-sync.json");
    this.loaded = this.load();
    if (enabled) this.schedule(1000);
  }
  private async load() {
    try {
      const state = JSON.parse(await readFile(this.file, "utf8")) as State;
      if (!/^[a-zA-Z0-9_-]{1,128}$/.test(state.clientId) || !state.sessions || typeof state.sessions !== "object") throw Error("invalid state");
      this.state = state;
    } catch (error: any) { if (error?.code !== "ENOENT") { this.closed = true; this.log("sync_state_unavailable"); } }
  }
  private persist() {
    const snapshot = JSON.stringify(this.state);
    const write = this.writes.then(async () => { await mkdir(this.dataDir, { recursive: true }); await writeFile(this.file + ".tmp", snapshot, { mode: 0o600 }); await rename(this.file + ".tmp", this.file); });
    this.writes = write.catch(() => { this.log("sync_state_write_failed"); });
    return write;
  }
  private schedule(delay = 500) {
    if (this.closed || !this.enabled) return;
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => { this.timer = undefined; void this.flush().catch(() => this.log("sync_failed")).finally(() => this.schedule(30000)); }, delay);
    this.timer.unref?.();
  }
  private target(provider: ProviderPublic | undefined) {
    if (provider?.authKind !== KUNBUDDY_SUBSCRIPTION_AUTH || !provider.baseUrl || !provider.oauthAccountLabel) return null;
    const root = normalizeKunBuddyUrl(provider.baseUrl.replace(/\/api\/v1\/kunbuddy\/profiles\/[^/]+$/, ""));
    return { root, username: provider.oauthAccountLabel, providerId: provider.id };
  }
  /** Invoked after transcript persistence or durable turn settlement, never once per streamed token. */
  mark(sessionId: string): Promise<void> {
    const inFlight = this.marking.get(sessionId);
    if (inFlight) return inFlight;
    const operation = this.marks.then(async () => {
      await this.loaded;
      if (this.closed || !this.enabled || !/^[a-zA-Z0-9_-]{1,128}$/.test(sessionId)) return;
      const host = this.getHost(); if (!host) return;
      const { session } = await host.call<{ session: Session | null }>("session.get", { id: sessionId, messageLimit: 1 });
      if (!session) return;
      const { providers } = await host.call<{ providers: ProviderPublic[] }>("providers.list", {});
      const target = this.target(providers.find(provider => provider.id === session.providerId));
      if (!target) return;
      const old = Object.hasOwn(this.state.sessions, sessionId) ? this.state.sessions[sessionId] : undefined;
      if (old && (old.root !== target.root || old.username !== target.username)) { old.error = "account_changed"; await this.persist(); return; }
      const binding = old ?? { ...target, pending: false, generation: 0, acknowledged: {} };
      binding.providerId = target.providerId; binding.pending = true; binding.generation++;
      Object.defineProperty(this.state.sessions, sessionId, { value: binding, writable: true, enumerable: true, configurable: true });
      await this.persist(); this.schedule();
    });
    const observed = operation.catch(() => this.log("sync_mark_failed", sessionId)).finally(() => this.marking.delete(sessionId));
    this.marking.set(sessionId, observed); this.marks = observed;
    return observed;
  }
  async flush(): Promise<void> {
    await this.loaded;
    if (this.closed || !this.enabled) return;
    if (!this.scanned) {
      this.scanning ??= this.discoverSessions().finally(() => { this.scanning = null; });
      try { await this.scanning; } catch { this.log("sync_scan_failed"); return; }
    }
    if (this.flushing) return this.flushing;
    this.flushing = this.sendPending().finally(() => { this.flushing = null; });
    return this.flushing;
  }
  private async discoverSessions() {
    const host = this.getHost(); if (!host) return;
    const { providers } = await host.call<{ providers: ProviderPublic[] }>("providers.list", {});
    const subscriptions = new Set(providers.filter(provider => this.target(provider)).map(provider => provider.id));
    if (subscriptions.size) {
      const { sessions } = await host.call<{ sessions: Array<Pick<Session, "id" | "providerId">> }>("session.list", {});
      for (const session of sessions) if (session.providerId && subscriptions.has(session.providerId)) await this.mark(session.id);
    }
    this.scanned = true;
  }
  async refresh() { this.scanned = false; await this.flush(); }
  private async sendPending() {
    const host = this.getHost(); if (!host) return;
    for (const [sessionId, binding] of Object.entries(this.state.sessions)) {
      if (!binding.pending || this.closed) continue;
      const generation = binding.generation;
      try {
        const { providers } = await host.call<{ providers: ProviderPublic[] }>("providers.list", {});
        const provider = providers.find(item => item.id === binding.providerId);
        const target = this.target(provider);
        if (!target || target.root !== binding.root || target.username !== binding.username) { binding.error = "login_required"; continue; }
        const { value: token } = await host.call<{ value?: string }>("providers.getSecret", { id: provider!.id });
        if (!token) { binding.error = "login_required"; continue; }
        let before: number | undefined;
        let session: Session | null = null;
        const pages: UiMessage[][] = [];
        do {
          const result = await host.call<{ session: Session | null }>("session.get", { id: sessionId, messageLimit: 200, ...(before === undefined ? {} : { messageBefore: before }) });
          if (!result.session) { binding.pending = false; session = null; break; }
          session = result.session;
          const currentTarget = this.target(providers.find(item => item.id === session!.providerId));
          if (!currentTarget || currentTarget.root !== binding.root || currentTarget.username !== binding.username) throw Error("account_changed");
          pages.unshift(session.messages);
          if (!session.hasMoreBefore) break;
          if (session.messageStart === undefined || session.messageStart < 0 || before !== undefined && session.messageStart >= before) throw Error("transcript_paging_failed");
          before = session.messageStart;
        } while (true);
        if (!session) continue;
        const pending = conversationEntries(pages.flat()).filter(entry => binding.acknowledged[entry.id] !== hash(entry.markdown));
        for (let offset = 0; offset < pending.length;) {
          const batch: Entry[] = [];
          let wireBytes = 0;
          while (offset < pending.length && batch.length < 64) {
            const entry = pending[offset];
            const size = Buffer.byteLength(JSON.stringify(entry));
            if (batch.length && wireBytes + size > 2 * 1024 * 1024 - 8192) break;
            batch.push(entry); wireBytes += size; offset++;
          }
          let response: Response;
          try {
            response = await this.fetcher(binding.root + "/api/v1/kunbuddy/sessions/" + encodeURIComponent(sessionId) + "/markdown", {
              method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
              body: JSON.stringify({ account: binding.username, client_id: this.state.clientId, title: session.title.slice(0, 512), entries: batch }), redirect: "error",
              signal: AbortSignal.any([AbortSignal.timeout(10000), this.requests.signal]),
            });
          } catch { throw Error("network_error"); }
          // Do not log or persist response bodies: a proxy may include credentials or original material.
          if (!response.ok) { await response.body?.cancel(); throw Error(response.status === 401 ? "login_required" : `upload_http_${response.status}`); }
          await confirmReceipt(response, batch.length);
          for (const entry of batch) binding.acknowledged[entry.id] = hash(entry.markdown);
          await this.persist();
        }
        if (binding.generation === generation) binding.pending = false;
        binding.error = undefined; binding.lastUploaded = new Date().toISOString();
      } catch (error) {
        const code = error instanceof Error && /^(account_changed|transcript_paging_failed|network_error|login_required|upload_receipt_invalid|upload_http_\d+)$/.test(error.message) ? error.message : "sync_failed";
        binding.error = code; this.log(code, sessionId);
      }
      await this.persist();
    }
  }
  async status() {
    await this.loaded;
    return { pending: Object.values(this.state.sessions).filter(binding => binding.pending).length,
      sessions: Object.entries(this.state.sessions).map(([id, value]) => ({ id, pending: value.pending, lastUploaded: value.lastUploaded, error: value.error })) };
  }
  async close() {
    if (this.timer) clearTimeout(this.timer);
    let timer: ReturnType<typeof setTimeout> | undefined;
    try { await Promise.race([this.marks.then(() => this.flush()), new Promise<void>(resolve => { timer = setTimeout(resolve, 4000); })]); }
    finally { if (timer) clearTimeout(timer); this.closed = true; this.requests.abort(); await this.writes; }
  }
}
