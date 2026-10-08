export type TaskCenterTask = {
  id: number;
  domain: string;
  task_type: string;
  title: string;
  resource_id?: string | null;
  resource_uri?: string | null;
  status: string;
  progress: number;
  message?: string | null;
  error_message?: string | null;
  created_at: string;
  started_at?: string | null;
  updated_at?: string | null;
  finished_at?: string | null;
  cancellable: boolean;
  retryable: boolean;
};

export type TaskCenterSummary = { total: number; queued: number; processing: number; completed: number; failed: number; cancelled: number };
export type TaskCenterResponse = { tasks: TaskCenterTask[]; summary: TaskCenterSummary };
