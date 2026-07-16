export type DashboardApplication = {
  id: string;
  name: string;
  description: string;
  root_path: string;
  repository_name?: string | null;
  template_id?: string | null;
  is_case_library: boolean;
  created_at: string;
  updated_at: string;
};
export type DashboardRepositoryOption = {
  name: string;
  display_name: string;
  root_path: string;
  status: string;
};
export type DashboardTemplate = {
  id: string;
  name: string;
  description: string;
  technology: string;
};
export type DashboardRuntime = {
  status: "stopped" | "installing" | "starting" | "running" | "failed";
  port: number | null;
  started_at?: string;
  logs: string[];
};
export type DashboardTree = {
  path: string;
  directories: Array<{ name: string; path: string }>;
  files: Array<{
    name: string;
    path: string;
    extension: string;
    size: number;
    editable: boolean;
  }>;
};
export type DashboardFile = {
  path: string;
  extension: string;
  content: string;
  line_count: number;
  sha256: string;
  updated_at: string;
};
export type DashboardWorkspaceSnapshot = {
  applicationId: string;
  rootPath: string;
  revision: string;
  framework: string;
  entryPoints: string[];
  sourceFiles: string[];
  styleFiles: string[];
  visualTargets: Array<{ file: string; role: string; detail: string }>;
};
export type DashboardGitStatus = {
  is_repository: boolean;
  branch?: string | null;
  changes: string[];
  ahead: number;
  behind: number;
  output?: string;
};
export type DashboardGitResult = {
  ok: boolean;
  action: "pull" | "push";
  output: string;
  status: DashboardGitStatus;
};

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    cache: "no-store",
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers },
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok)
    throw new Error(
      typeof payload.message === "string"
        ? payload.message
        : `Request failed with HTTP ${response.status}`,
    );
  return payload as T;
}

export function listDashboardApplications() {
  return request<DashboardApplication[]>("/api/v1/dashboard-applications/list");
}
export function listDashboardRepositories() {
  return request<DashboardRepositoryOption[]>(
    "/api/v1/dashboard-applications/repositories",
  );
}
export function createDashboardApplication(payload: {
  name: string;
  repository_name?: string;
  template_id?: string;
}) {
  return request<DashboardApplication>("/api/v1/dashboard-applications", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}
export function bindDashboardApplicationRepository(
  id: string,
  repository_name: string,
) {
  return request<DashboardApplication>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}/repository`,
    { method: "POST", body: JSON.stringify({ repository_name }) },
  );
}
export function deleteDashboardApplication(id: string) {
  return request<{ ok: boolean; id: string }>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}`,
    { method: "DELETE" },
  );
}
export function listDashboardTemplates() {
  return request<DashboardTemplate[]>(
    "/api/v1/dashboard-applications/templates",
  );
}
export function getDashboardApplication(id: string) {
  return request<DashboardApplication>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}`,
  );
}
export function getDashboardTree(id: string, path = "") {
  return request<DashboardTree>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}/tree?path=${encodeURIComponent(path)}`,
  );
}
export function getDashboardWorkspaceSnapshot(id: string) {
  return request<DashboardWorkspaceSnapshot>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}/inspect`,
  );
}
export function getDashboardFile(id: string, path: string) {
  return request<DashboardFile>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}/file?path=${encodeURIComponent(path)}`,
  );
}
export function saveDashboardFile(id: string, path: string, content: string) {
  return request<{ ok: boolean; updated_at: string }>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}/file`,
    { method: "PUT", body: JSON.stringify({ path, content }) },
  );
}
export function getDashboardRuntime(id: string) {
  return request<DashboardRuntime>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}/runtime`,
  );
}
export function startDashboardRuntime(id: string) {
  return request<DashboardRuntime>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}/runtime/start`,
    { method: "POST" },
  );
}
export function stopDashboardRuntime(id: string) {
  return request<DashboardRuntime>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}/runtime/stop`,
    { method: "POST" },
  );
}
export function getDashboardGitStatus(id: string) {
  return request<DashboardGitStatus>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}/git/status`,
  );
}
export function pullDashboardGit(id: string) {
  return request<DashboardGitResult>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}/git/pull`,
    { method: "POST" },
  );
}
export function pushDashboardGit(id: string, message?: string) {
  return request<DashboardGitResult>(
    `/api/v1/dashboard-applications/${encodeURIComponent(id)}/git/push`,
    { method: "POST", body: JSON.stringify({ message }) },
  );
}
