export type MaintenanceSettings = {
  enabled: boolean; interval_hours: number; mode: "analyze" | "optimize";
  auto_push: boolean; instructions: string; model_id: string | null;
  timeout_minutes: number; max_files: number;
};
export type MaintenancePlan = { project_id: number; settings: MaintenanceSettings; next_run_at: string | null; active_run_id: string | null };
export type MaintenanceRun = {
  id: string; project_id: number; status: string; trigger: string; report: string | null;
  log: string | null; change_set_id: number | null; branch: string;
  created_at: string; finished_at: string | null;
};
