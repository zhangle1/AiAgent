import type { AtlasAnalysis, AtlasAnalysisRequest, AtlasCreateRequest, AtlasEvidence, AtlasSnapshot } from "./change-atlas-types";

async function request<T>(projectId: number, path: string, signal?: AbortSignal, body?: unknown): Promise<T> {
  const response = await fetch(`/api/v1/projects/${projectId}/change-atlas/${path}`, {
    method: body === undefined ? "GET" : "POST", cache: "no-store", signal,
    headers: { "Content-Type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body),
  });
  const raw = await response.text();
  let value: unknown = null;
  if (raw.trim()) {
    try { value = JSON.parse(raw); }
    catch { throw new Error(`变化地图服务返回了无法识别的响应（HTTP ${response.status}）。`); }
  }
  if (!response.ok) {
    const payload = value && typeof value === "object" ? value as { message?: unknown; detail?: unknown; title?: unknown } : {};
    const detail = payload.message ?? payload.detail ?? payload.title;
    throw new Error(typeof detail === "string" && detail.trim() ? detail : `变化地图服务返回 HTTP ${response.status}`);
  }
  if (value === null) throw new Error("变化地图服务返回了空响应，请重试。");
  return value as T;
}
export const createAtlasSnapshot = (projectId: number, body: AtlasCreateRequest, signal?: AbortSignal) => request<AtlasSnapshot>(projectId, "snapshots", signal, body);
export const readAtlasEvidence = (projectId: number, snapshotId: string, commitId: string, fileId: string, signal?: AbortSignal) =>
  request<AtlasEvidence>(projectId, `snapshots/${encodeURIComponent(snapshotId)}/evidence?${new URLSearchParams({ commit_id: commitId, file_id: fileId })}`, signal);
export async function readAtlasAnalysisStream(response: Response, onStatus?: (message: string) => void): Promise<AtlasAnalysis> {
  if (!response.ok || !response.headers.get("content-type")?.includes("text/event-stream")) {
    const value = await response.json().catch(() => null);
    if (!response.ok) throw new Error(value?.message || `AI 解读服务返回 HTTP ${response.status}`);
    throw new Error("AI 解读服务未返回事件流，请更新前后端后重试。");
  }
  const reader = response.body?.getReader();
  if (!reader) throw new Error("AI 解读连接没有返回内容。");
  const decoder = new TextDecoder();
  let pending = "";
  try {
    while (true) {
      const { value, done } = await reader.read();
      pending += decoder.decode(value, { stream: !done });
      pending = pending.replace(/\r\n/g, "\n");
      if (pending.length > 1_000_000) throw new Error("AI 解读响应过大，请缩小范围。");
      let boundary: number;
      while ((boundary = pending.indexOf("\n\n")) >= 0) {
        const frame = pending.slice(0, boundary); pending = pending.slice(boundary + 2);
        const event = frame.split("\n").find(line => line.startsWith("event:"))?.slice(6).trim();
        const data = frame.split("\n").filter(line => line.startsWith("data:")).map(line => line.slice(5).trimStart()).join("\n");
        if (!data) continue;
        const payload = JSON.parse(data);
        if (event === "status" && typeof payload.message === "string") onStatus?.(payload.message);
        if (event === "error") throw new Error(payload.message || "AI 解读失败，请重试。");
        if (event === "result") return payload as AtlasAnalysis;
      }
      if (done) throw new Error("AI 解读连接已中断，尚未收到完整结果，请重试。");
    }
  } finally { await reader.cancel().catch(() => {}); reader.releaseLock(); }
}
export async function analyzeAtlas(projectId: number, snapshotId: string, body: AtlasAnalysisRequest, signal?: AbortSignal, onStatus?: (message: string) => void): Promise<AtlasAnalysis> {
  const response = await fetch(`/api/v1/projects/${projectId}/change-atlas/snapshots/${encodeURIComponent(snapshotId)}/analysis/stream`, {
    method: "POST", cache: "no-store", signal,
    headers: { "Content-Type": "application/json", Accept: "text/event-stream" }, body: JSON.stringify(body),
  });
  return readAtlasAnalysisStream(response, onStatus);
}
