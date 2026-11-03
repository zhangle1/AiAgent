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
export const analyzeAtlas = (projectId: number, snapshotId: string, body: AtlasAnalysisRequest, signal?: AbortSignal) =>
  request<AtlasAnalysis>(projectId, `snapshots/${encodeURIComponent(snapshotId)}/analysis`, signal, body);
