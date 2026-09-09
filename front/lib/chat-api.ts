import type { KnowledgeCitation } from "@/lib/knowledge-types";

export type CodexSandboxMode = "full-access" | "workspace-write" | "read-only";

const CHAT_RUNTIME_STORAGE_KEY = "aiagent:chat-runtime-id";
export const CHAT_STREAM_CONNECT_TIMEOUT_MS = 15_000;
export const CHAT_STREAM_QUIET_AFTER_MS = 45_000;
export const CHAT_STREAM_IDLE_TIMEOUT_MS = 360_000;

function chatStreamError(message: string): Error {
  const error = new Error(message);
  error.name = "ChatStreamError";
  return error;
}

function directWebSocket(path: string): string {
  const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
  return `${protocol}//${window.location.host}${path}`;
}

async function parseJson<T>(response: Response): Promise<T> {
  const text = await response.text();
  let payload: unknown = null;
  if (text) {
    try {
      payload = JSON.parse(text) as unknown;
    } catch {
      throw new Error(`Request returned non-JSON response: HTTP ${response.status} ${text.slice(0, 160)}`);
    }
  }

  if (!response.ok) {
    const message = typeof payload === "object" && payload && "message" in payload
      ? String((payload as { message?: string }).message)
      : `Request failed with HTTP ${response.status}`;
    throw new Error(message);
  }

  return payload as T;
}

export type ChatCompleteRequest = {
  debug_trace?: boolean;
  trace_id?: string;
  session_id?: string;
  message: string;
  knowledge_base_name?: string;
  knowledge_base_names?: string[];
  code_repository_names?: string[];
  code_project_id?: number;
  project_references?: Array<{ project_id: number }>;
  markdown_document_references?: Array<{ repository_name: string; path: string }>;
  dashboard_application_id?: string;
  dashboard_file_path?: string;
  dashboard_workspace_revision?: string;
  model_id?: string;
  codex_model_id?: string;
  codex_reasoning_effort?: string;
  codex_sandbox_mode?: CodexSandboxMode;
  top_k?: number;
  mode?: string;
  agent?: "codex" | "deepseek-harness" | "codebuddy";
  attachment_ids?: string[];
  document_attachment_ids?: string[];
  client_runtime_id?: string;
};

export type ChatImageAttachment = {
  id: string;
  file_name: string;
  content_type: string;
  size_bytes: number;
};

export type ChatFileAttachment = {
  id: string;
  file_name: string;
  content_type: string;
  size_bytes: number;
  kind: "document";
  extraction_status: "ready" | "unsupported";
  extraction_id?: string | null;
};

export type ChatUploadFile = {
  id: string;
  file_name: string;
  content_type: string;
  size_bytes: number;
  kind: "image" | "document" | "extracted_text";
  extraction_status?: string | null;
  created_at: string;
  session_id?: string | null;
  source_attachment_id?: string | null;
  uploader_id?: string | null;
  uploader_name?: string | null;
};

export type ChatFileExtractionPreview = {
  attachment_id: string;
  file_name: string;
  content: string;
  truncated: boolean;
};

export type ChatCompleteResponse = {
  query: string;
  answer: string;
  content: string;
  model_id?: string | null;
  model?: string | null;
  knowledge_base_name?: string | null;
  citations: KnowledgeCitation[];
};

export function getChatRuntimeId(): string {
  const existing = sessionStorage.getItem(CHAT_RUNTIME_STORAGE_KEY);
  if (existing) return existing;
  const created = globalThis.crypto?.randomUUID?.().replaceAll("-", "") ?? `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`;
  sessionStorage.setItem(CHAT_RUNTIME_STORAGE_KEY, created);
  return created;
}

export async function heartbeatCodexRuntime(codeProjectId?: number, codexModelId?: string, codexReasoningEffort?: string, codexSandboxMode: CodexSandboxMode = "full-access"): Promise<void> {
  if (!codeProjectId) return;
  await parseJson<{ ok: boolean }>(
    await fetch("/api/v1/chat/codex/heartbeat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ client_runtime_id: getChatRuntimeId(), code_project_id: codeProjectId, codex_model_id: codexModelId, codex_reasoning_effort: codexReasoningEffort, codex_sandbox_mode: codexSandboxMode }),
    }),
  );
}

export async function completeChat(payload: ChatCompleteRequest): Promise<ChatCompleteResponse> {
  return parseJson<ChatCompleteResponse>(
    await fetch("/api/v1/chat/complete", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    }),
  );
}

export async function uploadChatImage(file: File): Promise<ChatImageAttachment> {
  const body = new FormData();
  body.set("file", file);
  return parseJson<ChatImageAttachment>(await fetch("/api/v1/chat/attachments/images", { method: "POST", body }));
}

export async function uploadChatFile(file: File): Promise<ChatFileAttachment> {
  const body = new FormData();
  body.set("file", file);
  return parseJson<ChatFileAttachment>(await fetch("/api/v1/chat/attachments/files", { method: "POST", body }));
}

export async function deleteChatImage(attachmentId: string): Promise<void> {
  await parseJson<{ ok: boolean }>(await fetch(`/api/v1/chat/attachments/${encodeURIComponent(attachmentId)}`, { method: "DELETE" }));
}

export async function deleteChatFile(attachmentId: string): Promise<void> {
  await parseJson<{ ok: boolean }>(await fetch(`/api/v1/chat/attachments/files/${encodeURIComponent(attachmentId)}`, { method: "DELETE" }));
}

export async function getChatFileExtraction(attachmentId: string, sessionId?: string): Promise<ChatFileExtractionPreview> {
  const query = sessionId ? `?session_id=${encodeURIComponent(sessionId)}` : "";
  return parseJson<ChatFileExtractionPreview>(await fetch(`/api/v1/chat/attachments/files/${encodeURIComponent(attachmentId)}/extraction${query}`, { cache: "no-store" }));
}

export function persistedChatImageUrl(sessionId: string, attachmentId: string): string {
  return `/api/v1/chat/attachments/${encodeURIComponent(sessionId)}/${encodeURIComponent(attachmentId)}`;
}

export function chatImagePreviewUrl(attachmentId: string): string {
  return `/api/v1/chat/attachments/${encodeURIComponent(attachmentId)}/preview`;
}

export async function getMyChatUploads(filters: { keyword?: string; kind?: string; sessionId?: string } = {}): Promise<ChatUploadFile[]> {
  const query = new URLSearchParams({ limit: "200" });
  if (filters.keyword) query.set("keyword", filters.keyword);
  if (filters.kind) query.set("kind", filters.kind);
  if (filters.sessionId) query.set("session_id", filters.sessionId);
  return parseJson<ChatUploadFile[]>(await fetch(`/api/v1/chat/uploads/mine?${query}`, { cache: "no-store" }));
}

export function myChatUploadContentUrl(attachmentId: string): string {
  return `/api/v1/chat/uploads/${encodeURIComponent(attachmentId)}/content`;
}

export async function getChatUploadText(attachmentId: string, adminUserId?: string): Promise<string> {
  const response = await fetch(adminUserId ? `/api/v1/admin/uploads/${encodeURIComponent(attachmentId)}/content?user_id=${encodeURIComponent(adminUserId)}` : myChatUploadContentUrl(attachmentId), { cache: "no-store" });
  if (!response.ok) throw new Error(`读取附件失败（HTTP ${response.status}）`);
  return response.text();
}

export type ChatStreamEvent = {
  type: "debug_trace" | "session_ready" | "label" | "loop" | "thinking" | "content" | "tool" | "tool_request" | "tool_result" | "sources" | "done" | "completed" | "error";
  debug_trace?: ChatDebugTraceEvent | null;
  label?: string | null;
  content?: string;
  model_id?: string | null;
  model?: string | null;
  knowledge_base_name?: string | null;
  citations?: KnowledgeCitation[] | null;
  metadata?: Record<string, unknown>;
};

export type ChatDebugTraceEvent = {
  trace_id: string;
  stage: string;
  status: "started" | "completed" | "failed" | "cancelled";
  elapsed_ms: number;
  duration_ms?: number | null;
  provider: "codex" | "openai_compatible" | "unknown";
  transport: "codex_app_server" | "http_stream" | "websocket" | "sse" | "unknown";
  error_code?: "cancelled" | "request_failed" | string | null;
};

export async function streamCompleteChat(
  payload: ChatCompleteRequest,
  onEvent: (event: ChatStreamEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  try {
    await streamCompleteChatWs(payload, onEvent, signal);
  } catch (error) {
    if (signal?.aborted || (error instanceof DOMException && error.name === "AbortError") || (error instanceof Error && error.name === "ChatStreamError")) throw error;
    await streamCompleteChatSse(payload, onEvent, signal);
  }
}

async function streamCompleteChatWs(
  payload: ChatCompleteRequest,
  onEvent: (event: ChatStreamEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  if (typeof WebSocket === "undefined") {
    throw new Error("WebSocket is not available.");
  }

  const socket = new WebSocket(directWebSocket("/api/v1/chat/ws"));
  let settled = false;
  let opened = false;
  let completed = false;
  let connectTimer: number | null = null;
  let legacyDoneTimer: number | null = null;
  let idleTimer: number | null = null;

  return await new Promise<void>((resolve, reject) => {
    const cleanup = () => {
      if (legacyDoneTimer !== null) window.clearTimeout(legacyDoneTimer);
      if (idleTimer !== null) window.clearTimeout(idleTimer);
      if (connectTimer !== null) window.clearTimeout(connectTimer);
      signal?.removeEventListener("abort", abort);
      socket.onopen = null;
      socket.onmessage = null;
      socket.onerror = null;
      socket.onclose = null;
    };

    const finish = () => {
      if (settled) return;
      settled = true;
      cleanup();
      resolve();
    };

    const scheduleLegacyDoneCompletion = () => {
      if (legacyDoneTimer !== null || settled) return;
      legacyDoneTimer = window.setTimeout(() => {
        legacyDoneTimer = null;
        finish();
      }, 800);
    };

    const fail = (error: Error) => {
      if (settled) return;
      settled = true;
      cleanup();
      try {
        socket.close();
      } catch {
        // Ignore close errors while falling back to SSE.
      }
      reject(error);
    };

    const armIdleTimeout = () => {
      if (idleTimer !== null) window.clearTimeout(idleTimer);
      idleTimer = window.setTimeout(() => {
        idleTimer = null;
        fail(chatStreamError("连接已连续 6 分钟没有收到服务端事件，已停止本次执行。请查看运行日志后重试。"));
      }, CHAT_STREAM_IDLE_TIMEOUT_MS);
    };

    const abort = () => {
      try {
        socket.close(1000, "aborted");
      } catch {
        // Ignore abort close errors.
      }
      fail(new DOMException("The operation was aborted.", "AbortError"));
    };

    signal?.addEventListener("abort", abort, { once: true });
    if (signal?.aborted) {
      abort();
      return;
    }

    connectTimer = window.setTimeout(() => fail(new Error("Chat WebSocket connection timed out.")), CHAT_STREAM_CONNECT_TIMEOUT_MS);

    socket.onopen = () => {
      opened = true;
      if (connectTimer !== null) {
        window.clearTimeout(connectTimer);
        connectTimer = null;
      }
      armIdleTimeout();
      try {
        socket.send(JSON.stringify(payload));
      } catch {
        fail(chatStreamError("聊天请求发送失败，未进入执行状态。"));
      }
    };

    socket.onmessage = (message) => {
      try {
        armIdleTimeout();
        const event = JSON.parse(String(message.data)) as ChatStreamEvent;
        if (event.type === "error") {
          try {
            onEvent(event);
          } catch {
            // The terminal WebSocket error below is the authoritative result.
          }
          fail(chatStreamError(event.content || "Chat WebSocket returned an error."));
          return;
        }
        onEvent(event);
        if (event.type === "completed") {
          completed = true;
          finish();
          return;
        }
        if (event.type === "done") scheduleLegacyDoneCompletion();
      } catch (ex) {
        fail(chatStreamError(ex instanceof SyntaxError ? "聊天服务返回了无法识别的流事件，本次执行已停止。" : "聊天流事件处理失败，本次执行已停止。"));
      }
    };

    socket.onerror = () => {
      if (legacyDoneTimer !== null) return;
      fail(opened
        ? chatStreamError("聊天连接在执行过程中断开，任务已停止。请查看运行日志后重试。")
        : new Error("Chat WebSocket connection failed."));
    };

    socket.onclose = () => {
      if (!opened) {
        fail(new Error("Chat WebSocket closed before opening."));
        return;
      }
      if (!completed) {
        if (legacyDoneTimer !== null) return;
        fail(chatStreamError("聊天连接在收到完成状态前关闭，任务可能已中断。请查看运行日志后重试。"));
        return;
      }
      finish();
    };
  });
}

async function streamCompleteChatSse(
  payload: ChatCompleteRequest,
  onEvent: (event: ChatStreamEvent) => void,
  signal?: AbortSignal,
): Promise<void> {
  const response = await openSseResponse(payload, signal);

  if (!response.ok || !response.body) {
    throw new Error(`Request failed with HTTP ${response.status}`);
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let completed = false;
  let sawDone = false;
  const handleEvent = (event: ChatStreamEvent) => {
    onEvent(event);
    if (event.type === "completed") completed = true;
    if (event.type === "done") sawDone = true;
  };

  while (true) {
    const { value, done } = await readSseChunk(reader);
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const frames = buffer.split("\n\n");
    buffer = frames.pop() ?? "";
    for (const frame of frames) {
      const event = parseSseFrame(frame);
      if (event) handleEvent(event);
    }
  }

  if (buffer.trim()) {
    const event = parseSseFrame(buffer);
    if (event) handleEvent(event);
  }

  if (!completed && !sawDone) throw new Error("Chat stream ended before completion.");
}

async function openSseResponse(payload: ChatCompleteRequest, signal?: AbortSignal): Promise<Response> {
  if (signal?.aborted) throw new DOMException("The operation was aborted.", "AbortError");
  const controller = new AbortController();
  const abort = () => controller.abort();
  signal?.addEventListener("abort", abort, { once: true });
  let connectTimer: number | null = null;
  try {
    return await Promise.race([
      fetch("/api/v1/chat/complete/stream", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
        signal: controller.signal,
      }),
      new Promise<never>((_, reject) => {
        connectTimer = window.setTimeout(() => {
          controller.abort();
          reject(chatStreamError("连接聊天服务超时，本次执行尚未开始。请稍后重试。"));
        }, CHAT_STREAM_CONNECT_TIMEOUT_MS);
      }),
    ]);
  } finally {
    if (connectTimer !== null) window.clearTimeout(connectTimer);
    signal?.removeEventListener("abort", abort);
  }
}

async function readSseChunk(reader: ReadableStreamDefaultReader<Uint8Array>): Promise<ReadableStreamReadResult<Uint8Array>> {
  let idleTimer: number | null = null;
  try {
    return await Promise.race([
      reader.read(),
      new Promise<never>((_, reject) => {
        idleTimer = window.setTimeout(() => {
          void reader.cancel("chat_stream_idle_timeout");
          reject(chatStreamError("连接已连续 6 分钟没有收到服务端事件，已停止本次执行。请查看运行日志后重试。"));
        }, CHAT_STREAM_IDLE_TIMEOUT_MS);
      }),
    ]);
  } finally {
    if (idleTimer !== null) window.clearTimeout(idleTimer);
  }
}

function parseSseFrame(frame: string): ChatStreamEvent | null {
  const dataLines = frame
    .split(/\r?\n/)
    .filter((line) => line.startsWith("data:"))
    .map((line) => line.slice("data:".length).trimStart());
  if (dataLines.length === 0) return null;
  try {
    return JSON.parse(dataLines.join("\n")) as ChatStreamEvent;
  } catch {
    return null;
  }
}
