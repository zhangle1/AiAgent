export type ProjectTask = { id: number; project_id?: number | null; project_name?: string | null; source: "local" | "gitee" | "gitee_enterprise_csv" | "gitee_enterprise_api"; external_id?: string | null; work_item_id?: string | null; work_item_type?: string | null; title: string; description?: string | null; status?: string | null; creator?: string | null; assignee?: string | null; collaborators?: string | null; priority?: string | null; labels?: string | null; external_url?: string | null; external_created_at?: string | null; external_updated_at?: string | null; updated_at?: string | null };
export type TaskCreatedChatSession = { task_id: number; session: { id: string; title: string; created_at: string; updated_at: string; message_count: number; last_message: string; project_id?: number | null; project_name?: string | null; sort_order: number; priority: "high" | "normal" | "low"; is_pinned: boolean }; image_warning?: string };
export type TaskImportResult = { total_rows: number; inserted: number; updated: number; skipped: number; warnings: string[] };
export type EnterpriseIssue = { number: string; title: string; status?: string | null; work_item_type?: string | null; assignee?: string | null; creator?: string | null; updated_at?: string | null; external_url?: string | null };
export type EnterpriseIssueDetail = EnterpriseIssue & { description?: string | null; collaborators?: string | null; priority?: string | null; labels?: string | null; created_at?: string | null; attachments: Array<{ name: string; url: string; is_image: boolean }> };
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
export async function updateProjectTask(taskId: number, payload: { project_id: number; title: string; description?: string }) { return (await request<{ task: ProjectTask }>(`/api/v1/project-tasks/${taskId}`, { method: "PATCH", body: JSON.stringify(payload) })).task; }
export async function updateProjectTaskStatus(taskId: number, status: "todo" | "in_progress" | "in_review" | "done") { return (await request<{ task: ProjectTask }>(`/api/v1/project-tasks/${taskId}/status`, { method: "PATCH", body: JSON.stringify({ status }) })).task; }
export async function deleteProjectTask(taskId: number) { return request<{ deleted: boolean }>(`/api/v1/project-tasks/${taskId}`, { method: "DELETE" }); }
export async function listEnterpriseIssues(input: { page: number; state?: "open" | "progressing" | "closed" | "rejected"; query?: string }) { const params = new URLSearchParams({ page: String(input.page), page_size: "20" }); if (input.state) params.set("state", input.state); if (input.query?.trim()) params.set("query", input.query.trim()); return request<{ enterprise: string; page: number; page_size: number; has_more: boolean; items: EnterpriseIssue[] }>(`/api/v1/project-tasks/gitee/enterprise/issues?${params}`); }
export async function getEnterpriseIssue(number: string) { return request<EnterpriseIssueDetail>(`/api/v1/project-tasks/gitee/enterprise/issues/${encodeURIComponent(number)}`); }
export async function linkEnterpriseIssue(input: { projectId: number; number: string }) { return request<{ task: ProjectTask; inserted: number; updated: number }>("/api/v1/project-tasks/gitee/enterprise/link", { method: "POST", body: JSON.stringify({ project_id: input.projectId, number: input.number }) }); }
export function enterpriseAttachmentUrl(url: string) { return `/api/v1/project-tasks/gitee/enterprise/attachment?url=${encodeURIComponent(url)}`; }
export async function prepareProjectTaskChatImages(taskId: number) { return request<{ attachments: Array<{ id: string; file_name: string; content_type: string; size_bytes: number }>; warnings: string[] }>(`/api/v1/project-tasks/${taskId}/chat-images`, { method: "POST" }); }
export async function createProjectTaskChatHandoff(taskId: number) { return request<{ handoff_id: string }>(`/api/v1/project-tasks/${taskId}/chat-handoff`, { method: "POST" }); }
export async function createProjectTaskChatSessions(taskIds: number[]) { return request<{ sessions: TaskCreatedChatSession[] }>("/api/v1/project-tasks/chat-sessions", { method: "POST", body: JSON.stringify({ task_ids: taskIds }) }); }
export async function getProjectTaskChatHandoff(handoffId: string) { return request<{ handoff_id: string; project_id: number; content: string; image_attachments: Array<{ id: string; file_name: string; content_type: string; size_bytes: number }>; image_warning?: string }>(`/api/v1/project-tasks/chat-handoffs/${encodeURIComponent(handoffId)}`); }
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
