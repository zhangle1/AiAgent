import type { ProjectPushBinding, PushAudit, PushChannel, PushChannelInput, PushChannelTestResult } from "@/lib/push-types";

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { cache: "no-store", ...init, headers: { "Content-Type": "application/json", ...init?.headers } });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(typeof payload.message === "string" ? payload.message : `请求失败（HTTP ${response.status}）`);
  return payload as T;
}

export function getPushChannels() { return request<PushChannel[]>("/api/v1/admin/push/channels"); }
export function createPushChannel(payload: PushChannelInput) { return request<PushChannel>("/api/v1/admin/push/channels", { method: "POST", body: JSON.stringify(payload) }); }
export function updatePushChannel(id: number, payload: PushChannelInput) { return request<PushChannel>(`/api/v1/admin/push/channels/${id}`, { method: "PUT", body: JSON.stringify(payload) }); }
export function deletePushChannel(id: number) { return request<{ deleted: boolean }>(`/api/v1/admin/push/channels/${id}`, { method: "DELETE" }); }
export function testPushChannel(id: number) { return request<PushChannelTestResult>(`/api/v1/admin/push/channels/${id}/test`, { method: "POST" }); }
export function testPushChannelStream(id: number) { return request<PushChannelTestResult>(`/api/v1/admin/push/channels/${id}/stream-test`, { method: "POST" }); }
export function getProjectPushBindings() { return request<ProjectPushBinding[]>("/api/v1/admin/push/project-bindings"); }
export function createProjectPushBinding(payload: { project_id: number; push_channel_id: number; enabled: boolean }) { return request<ProjectPushBinding>("/api/v1/admin/push/project-bindings", { method: "POST", body: JSON.stringify(payload) }); }
export function updateProjectPushBinding(id: number, enabled: boolean) { return request<ProjectPushBinding>(`/api/v1/admin/push/project-bindings/${id}`, { method: "PUT", body: JSON.stringify({ enabled }) }); }
export function deleteProjectPushBinding(id: number) { return request<{ deleted: boolean }>(`/api/v1/admin/push/project-bindings/${id}`, { method: "DELETE" }); }
export function getPushAudit() { return request<PushAudit[]>("/api/v1/admin/push/audit?limit=100"); }
