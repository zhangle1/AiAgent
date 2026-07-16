import type { CodeProjectRuntime, CodeRuntimeLog, CodeRuntimeProfile, CodeRuntimeProfileSaveRequest, CodeRuntimeRun } from "@/lib/code-runtime-types";

async function parseJson<T>(response: Response): Promise<T> {
  const body = await response.text();
  let payload: unknown;
  try { payload = body ? JSON.parse(body) : null; } catch { payload = null; }
  if (!response.ok) {
    const message = payload && typeof payload === "object" && "message" in payload ? String((payload as { message?: string }).message) : `运行请求失败（HTTP ${response.status}）。`;
    throw new Error(message);
  }
  return payload as T;
}

export async function getCodeProjectRuntime(projectId: number): Promise<CodeProjectRuntime> {
  return parseJson(await fetch(`/api/v1/code-runtime/projects/${projectId}`, { cache: "no-store" }));
}

export async function saveCodeRuntimeProfile(projectId: number, payload: CodeRuntimeProfileSaveRequest, profileId?: number): Promise<CodeRuntimeProfile> {
  const suffix = profileId ? `/profiles/${profileId}` : "/profiles";
  return parseJson(await fetch(`/api/v1/code-runtime/projects/${projectId}${suffix}`, {
    method: profileId ? "PUT" : "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload),
  }));
}

export async function deleteCodeRuntimeProfile(projectId: number, profileId: number): Promise<void> {
  await parseJson(await fetch(`/api/v1/code-runtime/projects/${projectId}/profiles/${profileId}`, { method: "DELETE" }));
}

export async function startCodeProjectRuntime(projectId: number, profileIds?: number[]): Promise<CodeRuntimeRun[]> {
  return parseJson(await fetch(`/api/v1/code-runtime/projects/${projectId}/start`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ profile_ids: profileIds }),
  }));
}

export async function stopCodeProjectRuntime(projectId: number, runId: string): Promise<void> {
  await parseJson(await fetch(`/api/v1/code-runtime/projects/${projectId}/runs/${encodeURIComponent(runId)}/stop`, { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" }));
}

export async function getCodeRuntimeLogs(runId: string, afterSequence = 0): Promise<CodeRuntimeLog[]> {
  return parseJson(await fetch(`/api/v1/code-runtime/runs/${encodeURIComponent(runId)}/logs?after_sequence=${afterSequence}`, { cache: "no-store" }));
}
