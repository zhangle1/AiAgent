import type { KnowledgeCitation } from "@/lib/knowledge-types";
import type { ChatImageAttachment } from "@/lib/chat-api";

export type SessionMessage = { id: number; role: "user" | "assistant"; content: string; thinking?: string | null; citations?: KnowledgeCitation[] | null; metadata?: { model_id?: string; model?: string; attachments?: ChatImageAttachment[] } | null; created_at: string };
export type SessionPriority = "high" | "normal" | "low";
export type ProjectSessionSortMode = "updated" | "priority" | "manual";
export type ProjectSessionPreference = { project_id: number; is_pinned: boolean; sort_mode: ProjectSessionSortMode };
export type SessionSummary = { id: string; title: string; created_at: string; updated_at: string; message_count: number; last_message: string; project_id?: number | null; project_name?: string | null; sort_order: number; priority: SessionPriority; is_pinned: boolean };
export type SessionDetail = SessionSummary & { messages: SessionMessage[]; preferences: Record<string, unknown> };

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { cache: "no-store", ...init, headers: { "Content-Type": "application/json", ...init?.headers } });
  if (response.status === 401 && typeof window !== "undefined") {
    window.location.assign(`/login?next=${encodeURIComponent(window.location.pathname + window.location.search)}`);
    throw new Error("请先登录。");
  }
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(typeof payload.message === "string" ? payload.message : "会话请求失败。");
  return payload as T;
}

export async function listSessions(): Promise<SessionSummary[]> { return (await request<{ sessions: SessionSummary[] }>("/api/v1/sessions/list?limit=100")).sessions; }
export function getSession(id: string) { return request<SessionDetail>(`/api/v1/sessions/${encodeURIComponent(id)}`); }
export function deleteSession(id: string) { return request<{ deleted: boolean }>(`/api/v1/sessions/${encodeURIComponent(id)}`, { method: "DELETE" }); }
export function renameSession(id: string, title: string) { return request<{ ok: boolean }>(`/api/v1/sessions/${encodeURIComponent(id)}`, { method: "PATCH", body: JSON.stringify({ title }) }); }
export function reorderSessions(sessionIds: string[]) { return request<{ ok: boolean }>("/api/v1/sessions/reorder", { method: "PUT", body: JSON.stringify({ session_ids: sessionIds }) }); }
export function updateSessionMetadata(id: string, metadata: { priority?: SessionPriority; is_pinned?: boolean }) { return request<{ ok: boolean }>(`/api/v1/sessions/${encodeURIComponent(id)}/meta`, { method: "PATCH", body: JSON.stringify(metadata) }); }
export async function listProjectSessionPreferences(): Promise<ProjectSessionPreference[]> { return (await request<{ preferences: ProjectSessionPreference[] }>("/api/v1/sessions/project-preferences")).preferences; }
export function updateProjectSessionPreference(projectId: number, preference: { is_pinned?: boolean; sort_mode?: ProjectSessionSortMode }) { return request<{ ok: boolean }>(`/api/v1/sessions/projects/${projectId}/preference`, { method: "PATCH", body: JSON.stringify(preference) }); }
