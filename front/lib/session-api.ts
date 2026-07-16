import type { KnowledgeCitation } from "@/lib/knowledge-types";

export type SessionMessage = { id: number; role: "user" | "assistant"; content: string; thinking?: string | null; citations?: KnowledgeCitation[] | null; metadata?: { model_id?: string; model?: string } | null; created_at: string };
export type SessionSummary = { id: string; title: string; created_at: string; updated_at: string; message_count: number; last_message: string };
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

export async function listSessions(): Promise<SessionSummary[]> { return (await request<{ sessions: SessionSummary[] }>("/api/v1/sessions/list?limit=12")).sessions; }
export function getSession(id: string) { return request<SessionDetail>(`/api/v1/sessions/${encodeURIComponent(id)}`); }
export function deleteSession(id: string) { return request<{ deleted: boolean }>(`/api/v1/sessions/${encodeURIComponent(id)}`, { method: "DELETE" }); }
