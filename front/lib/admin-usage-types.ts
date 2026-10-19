export type AdminUsageFilters = { start_date?: string; end_date?: string; project_id?: string };
export type UsageMetrics = { session_count: number; active_days: number; daily_turns: number };
export type AdminUsageBucket = { key: string; label: string; total_tokens: number; turn_count: number; metrics: UsageMetrics };
export type AdminUsageUser = { user_id: string; username: string; total_tokens: number; prompt_tokens: number; completion_tokens: number; turn_count: number };
export type AdminUsageUserProject = { user_id: string; username: string; project_id: number | null; project_name: string; total_tokens: number; turn_count: number; metrics: UsageMetrics };
export type AdminUsageReport = {
  period: "day" | "week" | "month" | "year"; from: string; to: string; total_tokens: number; turn_count: number;
  metrics: UsageMetrics; buckets: AdminUsageBucket[]; users: AdminUsageUser[];
  projects: { id: number; name: string }[]; user_projects: AdminUsageUserProject[];
};
