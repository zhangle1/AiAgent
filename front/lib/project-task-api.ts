export type ProjectTask = { id: number; project_id?: number | null; project_name?: string | null; source: "local" | "gitee" | "gitee_enterprise_csv"; external_id?: string | null; work_item_id?: string | null; work_item_type?: string | null; title: string; description?: string | null; status?: string | null; creator?: string | null; assignee?: string | null; collaborators?: string | null; priority?: string | null; labels?: string | null; external_url?: string | null; external_created_at?: string | null; external_updated_at?: string | null; updated_at?: string | null };
export type TaskImportResult = { total_rows: number; inserted: number; updated: number; skipped: number; warnings: string[] };
export type GiteeProject = { owner: string; name: string; display_name: string };
export type GiteeIssue = { id: string; number?: string | null; title?: string | null; state?: string | null; assignee?: string | null; url: string; updated_at?: string | null };
export type GiteeMember = { login: string; display_name?: string | null };

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { cache: "no-store", ...init, headers: { "Content-Type": "application/json", ...init?.headers } });
  const text = await response.text();
  let body: unknown = null;
  if (text) { try { body = JSON.parse(text); } catch { body = null; } }
  if (!response.ok) {
    const message = typeof body === "object" && body && "message" in body && typeof (body as { message?: unknown }).message === "string"
      ? (body as { message: string }).message
      : `任务服务返回 HTTP ${response.status}${text ? "，请查看后端日志。" : "。"}`;
    throw new Error(message);
  }
  return body as T;
}
export async function listProjectTasks(projectId?: number) { return (await request<{ tasks: ProjectTask[] }>(`/api/v1/project-tasks/list${projectId ? `?projectId=${projectId}` : ""}`)).tasks; }
export async function createProjectTask(payload: { project_id: number; title: string; description?: string; gitee_issue?: GiteeIssue }) { return (await request<{ task: ProjectTask }>("/api/v1/project-tasks/create", { method: "POST", body: JSON.stringify(payload) })).task; }
export async function importProjectTasks(payload: { projectId: number; file: File; mappings: Record<string, string> }): Promise<TaskImportResult> {
  const body = new FormData();
  body.set("project_id", String(payload.projectId));
  body.set("field_mappings", JSON.stringify(payload.mappings));
  body.set("file", payload.file);
  const response = await fetch("/api/v1/project-tasks/import", { method: "POST", body });
  const text = await response.text();
  let result: unknown = null;
  if (text) { try { result = JSON.parse(text); } catch { result = null; } }
  if (!response.ok) {
    const message = typeof result === "object" && result && "message" in result && typeof (result as { message?: unknown }).message === "string"
      ? (result as { message: string }).message
      : `导入服务返回 HTTP ${response.status}${text ? "，请查看后端日志。" : "。"}`;
    throw new Error(message);
  }
  return result as TaskImportResult;
}
export async function listGiteeProjects(query = "", page = 1) { return request<{ items: GiteeProject[]; page: number; has_more: boolean }>(`/api/v1/project-tasks/gitee/projects?${new URLSearchParams({ query, page: String(page), page_size: "20" })}`); }
export async function listGiteeIssues(input: { owner: string; repository: string; assignee?: string; state?: string; query?: string; page?: number }) { const query = new URLSearchParams({ owner: input.owner, repository: input.repository, state: input.state ?? "open", query: input.query ?? "", page: String(input.page ?? 1), page_size: "20" }); if (input.assignee) query.set("assignee", input.assignee); return request<{ items: GiteeIssue[]; page: number; has_more: boolean }>(`/api/v1/project-tasks/gitee/issues?${query}`); }
export async function listGiteeMembers(owner: string, repository: string) { return request<{ items: GiteeMember[] }>(`/api/v1/project-tasks/gitee/members?${new URLSearchParams({ owner, repository })}`); }
