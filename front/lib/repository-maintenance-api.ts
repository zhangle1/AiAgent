import type { MaintenancePlan, MaintenanceRun, MaintenanceSettings } from "./repository-maintenance-types";

async function request<T>(projectId: number, path = "", method = "GET", body?: unknown): Promise<T> {
  const response = await fetch(`/api/v1/projects/${projectId}/maintenance${path}`, {
    method, cache: "no-store", headers: { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new Error(data?.message || `养护服务返回 HTTP ${response.status}`);
  return data as T;
}
export const getMaintenancePlan = (id: number) => request<MaintenancePlan>(id);
export const saveMaintenancePlan = (id: number, settings: MaintenanceSettings) => request<MaintenancePlan>(id, "", "PUT", settings);
export const getMaintenanceRuns = (id: number) => request<MaintenanceRun[]>(id, "/runs");
export const runMaintenance = (id: number) => request<MaintenanceRun>(id, "/runs", "POST");
export const cancelMaintenance = (id: number, runId: string) => request(id, `/runs/${encodeURIComponent(runId)}/cancel`, "POST");
export const pushMaintenance = (id: number, runId: string) => request<MaintenanceRun>(id, `/runs/${encodeURIComponent(runId)}/push`, "POST");
