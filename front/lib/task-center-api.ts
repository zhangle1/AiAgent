import type { TaskCenterResponse, TaskCenterTask } from "@/lib/task-center-types";

async function parse<T>(response: Response): Promise<T> {
  const text = await response.text();
  let payload: unknown = null;
  try {
    payload = text ? JSON.parse(text) as unknown : null;
  } catch {
    if (!response.ok) throw new Error(text || `请求失败：HTTP ${response.status}`);
  }
  if (!response.ok) throw new Error(typeof payload === "object" && payload && "message" in payload ? String((payload as { message?: string }).message) : `请求失败：HTTP ${response.status}`);
  return payload as T;
}

export async function listTaskCenter(input: { domain?: string; status?: string; signal?: AbortSignal } = {}) {
  const params = new URLSearchParams();
  if (input.domain) params.set("domain", input.domain);
  if (input.status) params.set("status", input.status);
  params.set("limit", "200");
  return parse<TaskCenterResponse>(await fetch(`/api/v1/task-center/list?${params}`, { cache: "no-store", signal: input.signal }));
}

export async function cancelTaskCenterTask(domain: string, id: number) {
  return parse<TaskCenterTask>(await fetch(`/api/v1/task-center/${encodeURIComponent(domain)}/${id}/cancel`, { method: "POST" }));
}

export async function retryTaskCenterTask(domain: string, id: number) {
  return parse<TaskCenterTask>(await fetch(`/api/v1/task-center/${encodeURIComponent(domain)}/${id}/retry`, { method: "POST" }));
}
