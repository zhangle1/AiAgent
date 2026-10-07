import type { CodeRuntimeRun } from "./code-runtime-types";

export type RuntimeSelection = { repository_name: string; entry_paths: string[] };
export type RuntimeTarget = { repository_name: string; entry_path: string; role: "frontend" | "backend"; preferred_port?: number; page_path: string; health_path: string };
export type ChatRuntimeJob = {
  request_id: string; project_id: number; manifest_repository: string; manifest_path: string;
  status: "waiting" | "starting" | "running" | "failed" | "stopped" | "expired";
  message?: string; idle_minutes: number; last_visit_at: string; runs: CodeRuntimeRun[]; targets: RuntimeTarget[];
};
