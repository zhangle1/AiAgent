import type { CodeChangeSet } from "@/lib/code-delivery-types";

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { cache: "no-store", ...init, headers: { "Content-Type": "application/json", ...init?.headers } });
  const text = await response.text();
  let body: unknown;
  try { body = text ? JSON.parse(text) : null; } catch { body = null; }
  if (!response.ok) throw new Error(typeof body === "object" && body && "message" in body ? String((body as { message: unknown }).message) : `交付服务返回 HTTP ${response.status}`);
  return body as T;
}
export async function listCodeChangeSets(projectId?: number, taskId?: number) { const query = new URLSearchParams(); if (projectId) query.set("projectId", String(projectId)); if (taskId) query.set("taskId", String(taskId)); return (await request<{ change_sets: CodeChangeSet[] }>(`/api/v1/code-deliveries?${query}`)).change_sets; }
export function createCodeChangeSet(input: { project_id: number; task_id?: number; title?: string; commit_message?: string }) { return request<CodeChangeSet>("/api/v1/code-deliveries", { method: "POST", body: JSON.stringify(input) }); }
export function validateCodeChangeSet(id: number) { return request<CodeChangeSet>(`/api/v1/code-deliveries/${id}/validate`, { method: "POST" }); }
export function approveCodeChangeSet(id: number, approved: boolean, comment?: string) { return request<CodeChangeSet>(`/api/v1/code-deliveries/${id}/approve`, { method: "POST", body: JSON.stringify({ approved, comment }) }); }
export function deliverCodeChangeSet(id: number) { return request<CodeChangeSet>(`/api/v1/code-deliveries/${id}/deliver`, { method: "POST" }); }
