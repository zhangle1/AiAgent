import type { KnowledgeCitation } from "@/lib/knowledge-types";

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
  session_id?: string;
  message: string;
  knowledge_base_name?: string;
  knowledge_base_names?: string[];
  code_repository_names?: string[];
  dashboard_application_id?: string;
  dashboard_file_path?: string;
  dashboard_workspace_revision?: string;
  model_id?: string;
  top_k?: number;
  mode?: string;
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

export async function completeChat(payload: ChatCompleteRequest): Promise<ChatCompleteResponse> {
  return parseJson<ChatCompleteResponse>(
    await fetch("/api/v1/chat/complete", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    }),
  );
}

export type ChatStreamEvent = {
  type: "label" | "loop" | "thinking" | "content" | "tool" | "tool_request" | "tool_result" | "sources" | "done" | "error";
  label?: string | null;
  content?: string;
  model_id?: string | null;
  model?: string | null;
  knowledge_base_name?: string | null;
  citations?: KnowledgeCitation[] | null;
  metadata?: Record<string, unknown>;
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

  return await new Promise<void>((resolve, reject) => {
    const cleanup = () => {
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

    socket.onopen = () => {
      opened = true;
      socket.send(JSON.stringify(payload));
    };

    socket.onmessage = (message) => {
      try {
        const event = JSON.parse(String(message.data)) as ChatStreamEvent;
        if (event.type === "error") {
          try {
            onEvent(event);
          } catch {
            // The terminal WebSocket error below is the authoritative result.
          }
          const error = new Error(event.content || "Chat WebSocket returned an error.");
          error.name = "ChatStreamError";
          fail(error);
          return;
        }
        onEvent(event);
      } catch (ex) {
        fail(ex instanceof Error ? ex : new Error("Invalid WebSocket event."));
      }
    };

    socket.onerror = () => {
      fail(new Error("Chat WebSocket connection failed."));
    };

    socket.onclose = () => {
      if (!opened) {
        fail(new Error("Chat WebSocket closed before opening."));
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
  const response = await fetch("/api/v1/chat/complete/stream", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
    signal,
  });

  if (!response.ok || !response.body) {
    throw new Error(`Request failed with HTTP ${response.status}`);
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const frames = buffer.split("\n\n");
    buffer = frames.pop() ?? "";
    for (const frame of frames) {
      const event = parseSseFrame(frame);
      if (event) onEvent(event);
    }
  }

  if (buffer.trim()) {
    const event = parseSseFrame(buffer);
    if (event) onEvent(event);
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
