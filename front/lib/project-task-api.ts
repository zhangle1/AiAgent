export type ProjectTask = { id: number; project_id?: number | null; project_name?: string | null; source: "local" | "gitee"; external_id?: string | null; title: string; description?: string | null; status?: string | null; assignee?: string | null; external_url?: string | null; external_updated_at?: string | null; updated_at?: string | null };

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { cache: "no-store", ...init, headers: { "Content-Type": "application/json", ...init?.headers } });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(typeof body.message === "string" ? body.message : "任务请求失败。");
  return body as T;
}
export async function listProjectTasks(projectId?: number) { return (await request<{ tasks: ProjectTask[] }>(`/api/v1/project-tasks${projectId ? `?projectId=${projectId}` : ""}`)).tasks; }
export async function createProjectTask(payload: { project_id?: number; title: string; description?: string }) { return (await request<{ task: ProjectTask }>("/api/v1/project-tasks", { method: "POST", body: JSON.stringify(payload) })).task; }
export async function syncGiteeTasks(projectId: number) { return request<{ synced: number }>(`/api/v1/project-tasks/sync/gitee?projectId=${projectId}`, { method: "POST", body: "{}" }); }
