import type { KnowledgeCompilerSettings } from "./knowledge-types";
import type { KnowledgeAgentCommand, KnowledgeAgentCommandResult, KnowledgeAgentCompileResult, KnowledgeAgentEvent, KnowledgeAgentHistory, KnowledgeAgentOptions } from "./knowledge-agent-types";

async function json<T>(response: Response): Promise<T> {
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new Error(body?.message ?? `知识 Agent 请求失败（HTTP ${response.status}）`);
  return body as T;
}

export function getKnowledgeAgentOptions(signal?: AbortSignal) {
  return fetch("/api/v1/knowledge-agent/options", { cache: "no-store", signal }).then(json<KnowledgeAgentOptions>);
}
export function runKnowledgeAgentCommand(input: { command: KnowledgeAgentCommand; uri: string; target_uri?: string; query?: string; offset?: number; limit?: number }, signal?: AbortSignal) {
  return fetch("/api/v1/knowledge-agent/command", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(input), signal }).then(json<KnowledgeAgentCommandResult>);
}
export function compileKnowledgeAgent(uris: string[], configuration: KnowledgeCompilerSettings, signal?: AbortSignal) {
  return fetch("/api/v1/knowledge-agent/compile", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ uris, configuration }), signal }).then(json<KnowledgeAgentCompileResult>);
}

/** A real SSE response; EOF without a terminal event is a failed turn, not a successful answer. */
export async function streamKnowledgeAgent(input: { uri: string; model_id: string; message: string; history: KnowledgeAgentHistory[] }, onEvent: (event: KnowledgeAgentEvent) => void, signal: AbortSignal) {
  const response = await fetch("/api/v1/knowledge-agent/stream", { method: "POST", headers: { "Content-Type": "application/json", Accept: "text/event-stream" }, body: JSON.stringify(input), signal });
  if (!response.ok) { await json(response); return; }
  if (!response.headers.get("Content-Type")?.includes("text/event-stream") || !response.body) throw new Error("服务端没有返回流式响应。");
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let done = false;
  function packet(text: string) {
    const data = text.split("\n").filter(line => line.startsWith("data:")).map(line => line.slice(5).trimStart()).join("\n");
    if (!data) return;
    const event = JSON.parse(data) as KnowledgeAgentEvent;
    if (typeof event.type !== "string") throw new Error("知识 Agent 返回无效事件。");
    onEvent(event);
    if (event.type === "error") throw new Error(event.content || "Agent 处理失败");
    if (event.type === "done") done = true;
  }
  try {
    while (!done) {
      const chunk = await reader.read();
      buffer = (buffer + decoder.decode(chunk.value, { stream: !chunk.done })).replace(/\r\n/g, "\n");
      if (buffer.length > 1_000_000) throw new Error("流式事件过大。");
      let boundary: number;
      while (!done && (boundary = buffer.indexOf("\n\n")) >= 0) { packet(buffer.slice(0, boundary)); buffer = buffer.slice(boundary + 2); }
      if (chunk.done) { if (buffer.trim() && !done) packet(buffer); break; }
    }
    if (!done && !signal.aborted) throw new Error("连接在任务完成前中断，已保留收到的内容。");
  } finally { await reader.cancel().catch(() => undefined); reader.releaseLock(); }
}
