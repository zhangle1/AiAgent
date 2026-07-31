import type { PromptTemplate, PromptTemplateSaveRequest, PromptTemplateUseResult } from "@/lib/prompt-template-types";

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { cache: "no-store", ...init, headers: { "Content-Type": "application/json", ...init?.headers } });
  if (response.status === 401 && typeof window !== "undefined") {
    window.location.assign(`/login?next=${encodeURIComponent(window.location.pathname + window.location.search)}`);
    throw new Error("请先登录。");
  }
  const raw = await response.text();
  let payload: { message?: unknown; detail?: unknown; title?: unknown } = {};
  try { payload = raw ? JSON.parse(raw) as { message?: unknown; detail?: unknown; title?: unknown } : {}; }
  catch { /* Non-JSON proxy and server error pages use the HTTP-status fallback below. */ }
  if (!response.ok) {
    const message = [payload.message, payload.detail, payload.title].find((value): value is string => typeof value === "string" && value.trim().length > 0);
    throw new Error(message ?? `模板请求失败（HTTP ${response.status}）。`);
  }
  return payload as T;
}

export async function listPromptTemplates(filters?: { stage?: string; q?: string }): Promise<PromptTemplate[]> {
  const query = new URLSearchParams();
  if (filters?.stage) query.set("stage", filters.stage);
  if (filters?.q) query.set("q", filters.q);
  return (await request<{ templates: PromptTemplate[] }>(`/api/v1/prompt-templates/list${query.size ? `?${query}` : ""}`)).templates;
}

export function createPromptTemplate(payload: PromptTemplateSaveRequest) {
  return request<PromptTemplate>("/api/v1/prompt-templates", { method: "POST", body: JSON.stringify(payload) });
}

export function updatePromptTemplate(id: number, payload: PromptTemplateSaveRequest) {
  return request<PromptTemplate>(`/api/v1/prompt-templates/${id}`, { method: "PUT", body: JSON.stringify(payload) });
}

export function deletePromptTemplate(id: number) {
  return request<{ ok: boolean }>(`/api/v1/prompt-templates/${id}`, { method: "DELETE" });
}

export function setPromptTemplateLiked(id: number, enabled: boolean) {
  return request<PromptTemplate>(`/api/v1/prompt-templates/${id}/like`, { method: "POST", body: JSON.stringify({ enabled }) });
}

export function setPromptTemplateFavorited(id: number, enabled: boolean) {
  return request<PromptTemplate>(`/api/v1/prompt-templates/${id}/favorite`, { method: "POST", body: JSON.stringify({ enabled }) });
}

export function usePromptTemplate(id: number, payload: { project_id?: number | null; variables: Record<string, string> }) {
  return request<PromptTemplateUseResult>(`/api/v1/prompt-templates/${id}/use`, { method: "POST", body: JSON.stringify(payload) });
}
