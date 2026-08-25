import type { KnowledgeCitation } from "@/lib/knowledge-types";
import type { ChatDebugTraceEvent, ChatFileAttachment, ChatImageAttachment } from "@/lib/chat-api";
import { buildLoginRedirect } from "@/lib/auth-redirect";

export type SessionMessage = { id: number; role: "user" | "assistant"; content: string; thinking?: string | null; citations?: KnowledgeCitation[] | null; metadata?: { model_id?: string; model?: string; attachments?: ChatImageAttachment[]; document_attachments?: ChatFileAttachment[] } | null; created_at: string };
export type SessionPriority = "high" | "normal" | "low";
export type ProjectSessionSortMode = "updated" | "priority" | "manual";
export type ProjectListSortMode = "name" | "recent";
export type ProjectSessionPreference = { project_id: number; is_pinned: boolean; is_archived: boolean; sort_mode: ProjectSessionSortMode };
export type ChatSidebarPreference = { project_sort_mode: ProjectListSortMode };
export type SessionSummary = { id: string; title: string; created_at: string; updated_at: string; message_count: number; last_message: string; project_id?: number | null; project_name?: string | null; sort_order: number; priority: SessionPriority; is_pinned: boolean; agent?: string | null; model_id?: string | null; model?: string | null };
export type SessionDetail = SessionSummary & { messages: SessionMessage[]; preferences: Record<string, unknown> };
export type ChatDebugTraceRecord = { trace_id: string; provider: string; transport: string; created_at: string; expires_at: string; events: ChatDebugTraceEvent[] };
export type AgentRunSummary = { run_id: string; session_id: string; runtime_kind: string; runtime_version: string; protocol_version: string; status: string; model_id?: string | null; prompt_tokens: number; completion_tokens: number; total_tokens: number; tool_calls: number; file_changes: number; error_code?: string | null; created_at: string; updated_at: string; completed_at?: string | null };
export type AgentRunEvent = { sequence: number; event_type: string; item_id?: string | null; created_at: string; status?: string | null; content_preview?: string | null; metadata: Record<string, unknown> };
export type AgentRunDetail = { run: AgentRunSummary; events: AgentRunEvent[] };

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { cache: "no-store", ...init, headers: { "Content-Type": "application/json", ...init?.headers } });
  if (response.status === 401 && typeof window !== "undefined") {
    window.location.assign(buildLoginRedirect(window.location.pathname + window.location.search));
    throw new Error("请先登录。");
  }
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(typeof payload.message === "string" ? payload.message : `会话请求失败（HTTP ${response.status}）。`);
  return payload as T;
}

export async function listSessions(): Promise<SessionSummary[]> { return (await request<{ sessions: SessionSummary[] }>("/api/v1/sessions/list?limit=100")).sessions; }
export function createSession(payload: { project_id: number; title?: string }) { return request<SessionSummary>("/api/v1/sessions/create", { method: "POST", body: JSON.stringify(payload) }); }
export function getSession(id: string) { return request<SessionDetail>(`/api/v1/sessions/${encodeURIComponent(id)}`); }
export async function getSessionDiagnostics(id: string) { return (await request<{ traces: ChatDebugTraceRecord[] }>(`/api/v1/sessions/${encodeURIComponent(id)}/diagnostics`)).traces; }
export async function listAgentRuns(id: string) { return (await request<{ runs: AgentRunSummary[] }>(`/api/v1/agent-runs/session/${encodeURIComponent(id)}?limit=30`)).runs; }
export function getAgentRun(id: string) { return request<AgentRunDetail>(`/api/v1/agent-runs/${encodeURIComponent(id)}`); }
export function cancelAgentRun(id: string) { return request<{ accepted: boolean }>(`/api/v1/agent-runs/${encodeURIComponent(id)}/cancel`, { method: "POST" }); }
export function deleteSession(id: string) { return request<{ deleted: boolean }>(`/api/v1/sessions/${encodeURIComponent(id)}`, { method: "DELETE" }); }
export function archiveSession(id: string) { return request<{ archived: boolean }>(`/api/v1/sessions/${encodeURIComponent(id)}/archive`, { method: "POST" }); }
export function renameSession(id: string, title: string) { return request<{ ok: boolean }>(`/api/v1/sessions/${encodeURIComponent(id)}`, { method: "PATCH", body: JSON.stringify({ title }) }); }
export function reorderSessions(sessionIds: string[]) { return request<{ ok: boolean }>("/api/v1/sessions/reorder", { method: "PUT", body: JSON.stringify({ session_ids: sessionIds }) }); }
export function updateSessionMetadata(id: string, metadata: { priority?: SessionPriority; is_pinned?: boolean }) { return request<{ ok: boolean }>(`/api/v1/sessions/${encodeURIComponent(id)}/meta`, { method: "PATCH", body: JSON.stringify(metadata) }); }
export async function listProjectSessionPreferences(): Promise<ProjectSessionPreference[]> { return (await request<{ preferences: ProjectSessionPreference[] }>("/api/v1/sessions/project-preferences")).preferences; }
export function updateProjectSessionPreference(projectId: number, preference: { is_pinned?: boolean; is_archived?: boolean; sort_mode?: ProjectSessionSortMode }) { return request<{ ok: boolean }>(`/api/v1/sessions/projects/${projectId}/preference`, { method: "PATCH", body: JSON.stringify(preference) }); }
export function getChatSidebarPreference() { return request<ChatSidebarPreference>("/api/v1/sessions/sidebar-preference"); }
export function updateChatSidebarPreference(preference: { project_sort_mode: ProjectListSortMode }) { return request<{ ok: boolean }>("/api/v1/sessions/sidebar-preference", { method: "PATCH", body: JSON.stringify(preference) }); }
