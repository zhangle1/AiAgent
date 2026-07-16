export type CodeRuntimeProfile = {
  id: number;
  project_id: number;
  repository_id: number;
  repository_name?: string | null;
  role: "frontend" | "backend";
  entry_path?: string | null;
  run_script?: string | null;
  health_path?: string | null;
  is_enabled: boolean;
  is_preview_enabled: boolean;
  created_at: string;
  updated_at?: string | null;
};

export type CodeRuntimeRun = {
  run_id: string;
  project_id: number;
  profile_id: number;
  repository_id: number;
  repository_name: string;
  role: "frontend" | "backend";
  status: "starting" | "running" | "stopping" | "stopped" | "exited" | "failed";
  port: number;
  preview_url?: string | null;
  command?: string | null;
  exit_code?: number | null;
  started_at: string;
  completed_at?: string | null;
};

export type CodeRuntimeLog = { sequence: number; stream: "stdout" | "stderr" | "system"; line: string; created_at: string };
export type CodeProjectRuntime = { project_id: number; profiles: CodeRuntimeProfile[]; runs: CodeRuntimeRun[] };
export type CodeRuntimeProfileSaveRequest = {
  repository_name: string;
  role: "frontend" | "backend";
  entry_path: string;
  run_script?: string;
  health_path?: string;
  is_enabled: boolean;
  is_preview_enabled: boolean;
};
