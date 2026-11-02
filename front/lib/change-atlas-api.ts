import type { AtlasAnalysis, AtlasAnalysisRequest, AtlasCreateRequest, AtlasEvidence, AtlasSnapshot } from "./change-atlas-types";

async function request<T>(projectId: number, path: string, signal?: AbortSignal, body?: unknown): Promise<T> {
  const response = await fetch(`/api/v1/projects/${projectId}/change-atlas/${path}`, {
    method: body === undefined ? "GET" : "POST", cache: "no-store", signal,
    headers: { "Content-Type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body),
  });
  const value = await response.json().catch(() => null);
  if (!response.ok) throw new Error(value?.message || `变化地图服务返回 HTTP ${response.status}`);
  return value as T;
}
export const createAtlasSnapshot = (projectId: number, body: AtlasCreateRequest, signal?: AbortSignal) => request<AtlasSnapshot>(projectId, "snapshots", signal, body);
export const readAtlasEvidence = (projectId: number, snapshotId: string, commitId: string, fileId: string, signal?: AbortSignal) =>
  request<AtlasEvidence>(projectId, `snapshots/${encodeURIComponent(snapshotId)}/evidence?${new URLSearchParams({ commit_id: commitId, file_id: fileId })}`, signal);
export const analyzeAtlas = (projectId: number, snapshotId: string, body: AtlasAnalysisRequest, signal?: AbortSignal) =>
  request<AtlasAnalysis>(projectId, `snapshots/${encodeURIComponent(snapshotId)}/analysis`, signal, body);
