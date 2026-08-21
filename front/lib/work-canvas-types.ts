import type { SessionSummary } from "@/lib/session-api";

export type WorkCanvasSummary = { id: string; name: string; scope_project_id?: number | null; node_count: number; version: number; updated_at: string };
export type WorkCanvasNode = { id: string; node_type: "session"; session_id: string; position_x: number; position_y: number; session: SessionSummary };
export type WorkCanvasEdge = { id: string; source_node_id: string; target_node_id: string; relation_type: "depends_on" | "produces" | "informs" | "related_to" | "delivery"; label?: string | null; pending_count: number };
export type CanvasDelivery = { id: string; link_id?: string | null; source_session_id?: string | null; source_session_title: string; target_session_id?: string | null; sender_note?: string | null; content: string; run_suggested: boolean; status: string; created_at?: string | null };
export type WorkCanvasSnapshot = WorkCanvasSummary & { viewport?: { x?: number; y?: number; zoom?: number } | null; nodes: WorkCanvasNode[]; edges: WorkCanvasEdge[] };
