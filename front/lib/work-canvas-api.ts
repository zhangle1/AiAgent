import { buildLoginRedirect } from "@/lib/auth-redirect";
import type { CanvasDelivery, WorkCanvasEdge, WorkCanvasNode, WorkCanvasSnapshot, WorkCanvasSummary } from "@/lib/work-canvas-types";

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { cache: "no-store", ...init, headers: { "Content-Type": "application/json", ...init?.headers } });
  if (response.status === 401 && typeof window !== "undefined") window.location.assign(buildLoginRedirect(window.location.pathname + window.location.search));
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(typeof payload.message === "string" ? payload.message : `工作画布请求失败（HTTP ${response.status}）。`);
  return payload as T;
}

export async function listWorkCanvases() { return (await request<{ canvases: WorkCanvasSummary[] }>("/api/v1/work-canvases")).canvases; }
export function createWorkCanvas(name: string) { return request<WorkCanvasSnapshot>("/api/v1/work-canvases", { method: "POST", body: JSON.stringify({ name }) }); }
export function getWorkCanvas(id: string) { return request<WorkCanvasSnapshot>(`/api/v1/work-canvases/${encodeURIComponent(id)}/snapshot`); }
export function renameWorkCanvas(id: string, name: string) { return request<{ ok: boolean }>(`/api/v1/work-canvases/${encodeURIComponent(id)}`, { method: "PATCH", body: JSON.stringify({ name }) }); }
export function archiveWorkCanvas(id: string) { return request<{ ok: boolean }>(`/api/v1/work-canvases/${encodeURIComponent(id)}`, { method: "PATCH", body: JSON.stringify({ is_archived: true }) }); }
export function addWorkCanvasNode(id: string, sessionId: string, x: number, y: number) { return request<WorkCanvasNode>(`/api/v1/work-canvases/${encodeURIComponent(id)}/nodes`, { method: "POST", body: JSON.stringify({ session_id: sessionId, position_x: x, position_y: y }) }); }
export function removeWorkCanvasNode(id: string, nodeId: string) { return request<{ removed: boolean }>(`/api/v1/work-canvases/${encodeURIComponent(id)}/nodes/${encodeURIComponent(nodeId)}`, { method: "DELETE" }); }
export function saveWorkCanvasLayout(id: string, version: number, nodes: Array<{ id: string; position_x: number; position_y: number }>) { return request<{ version: number }>(`/api/v1/work-canvases/${encodeURIComponent(id)}/layout`, { method: "PATCH", body: JSON.stringify({ expected_version: version, nodes }) }); }
export function createDeliveryLink(canvasId: string, sourceNodeId: string, targetNodeId: string, label: string) { return request<WorkCanvasEdge>(`/api/v1/work-canvases/${encodeURIComponent(canvasId)}/delivery-links`, { method: "POST", body: JSON.stringify({ source_node_id: sourceNodeId, target_node_id: targetNodeId, label }) }); }
export function deleteDeliveryLink(canvasId: string, linkId: string) { return request<{ removed: boolean }>(`/api/v1/work-canvases/${encodeURIComponent(canvasId)}/delivery-links/${encodeURIComponent(linkId)}`, { method: "DELETE" }); }
export function sendCanvasDelivery(linkId: string, content: string, senderNote: string, runSuggested: boolean) { return request<CanvasDelivery>("/api/v1/work-canvases/deliveries", { method: "POST", body: JSON.stringify({ link_id: linkId, content, sender_note: senderNote, run_suggested: runSuggested }) }); }
export async function getCanvasInbox(sessionId: string) { return (await request<{ deliveries: CanvasDelivery[] }>(`/api/v1/work-canvases/sessions/${encodeURIComponent(sessionId)}/inbox`)).deliveries; }
export function decideCanvasDelivery(deliveryId: string, decision: "accepted" | "ignored") { return request<{ ok: boolean }>(`/api/v1/work-canvases/deliveries/${encodeURIComponent(deliveryId)}/decision`, { method: "POST", body: JSON.stringify({ decision }) }); }
