import type { SessionDetail, SessionSummary } from "@/lib/session-api";

export type AdminUser = { id: string; username: string; role: string; is_disabled: boolean; created_at: string; project_ids: number[] };
export type AdminSession = SessionSummary & { user_id: string; username: string };
export type AdminUsageBucket = { key: string; label: string; total_tokens: number; turn_count: number };
export type AdminUsageUser = { user_id: string; username: string; total_tokens: number; prompt_tokens: number; completion_tokens: number; turn_count: number };
export type AdminUsageReport = { period: "day" | "week" | "month" | "year"; from: string; to: string; total_tokens: number; turn_count: number; buckets: AdminUsageBucket[]; users: AdminUsageUser[] };

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { cache: "no-store", ...init, headers: { "Content-Type": "application/json", ...init?.headers } });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(typeof payload.message === "string" ? payload.message : `请求失败（HTTP ${response.status}）`);
  return payload as T;
}

export function getAdminUsers() { return request<AdminUser[]>("/api/v1/admin/users"); }
export function createAdminUser(payload: { username: string; password: string; project_ids: number[] }) { return request<AdminUser>("/api/v1/admin/users", { method: "POST", body: JSON.stringify(payload) }); }
export function updateAdminUserProjects(userId: string, projectIds: number[]) { return request<{ ok: boolean }>(`/api/v1/admin/users/${encodeURIComponent(userId)}/projects`, { method: "PUT", body: JSON.stringify({ project_ids: projectIds }) }); }
export function getAdminSessions(userId?: string) { return request<AdminSession[]>(`/api/v1/admin/sessions?${new URLSearchParams({ limit: "100", ...(userId ? { user_id: userId } : {}) })}`); }
export function getAdminSession(userId: string, sessionId: string) { return request<SessionDetail>(`/api/v1/admin/users/${encodeURIComponent(userId)}/sessions/${encodeURIComponent(sessionId)}`); }
export function getAdminUsage(period: "day" | "week" | "month" | "year", days: number, userId?: string) { return request<AdminUsageReport>(`/api/v1/admin/usage?${new URLSearchParams({ period, days: String(days), ...(userId ? { user_id: userId } : {}) })}`); }
