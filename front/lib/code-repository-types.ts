export type CodeRepository = {
  id: number;
  project_id?: number | null;
  project_name?: string | null;
  name: string;
  display_name: string;
  root_path: string;
  source_type: string;
  description?: string | null;
  status: string;
  languages: string[];
  build_systems: string[];
  solution_files: string[];
  configuration_files: string[];
  chat_editable_configuration_files: string[];
  publish_target?: string | null;
  publish_configuration: string;
  publish_runtime?: string | null;
  publish_output_path: string;
  publish_command?: string | null;
  is_git_repository: boolean;
  branch?: string | null;
  last_scanned_at?: string | null;
  last_indexed_at?: string | null;
  created_at: string;
  updated_at?: string | null;
};

export type CodeRepositoryInspection = {
  root_path: string;
  suggested_name: string;
  suggested_display_name: string;
  languages: string[];
  build_systems: string[];
  is_git_repository: boolean;
  branch?: string | null;
  marker_files: string[];
  solution_files: string[];
  configuration_files: string[];
};

export type CodeProject = {
  id: number;
  name: string;
  display_name: string;
  root_path: string;
  description?: string | null;
  auto_git_update_enabled: boolean;
  auto_git_update_interval_hours: number;
  auto_git_update_last_attempted_at?: string | null;
  auto_git_update_last_succeeded_at?: string | null;
  auto_git_update_last_result?: string | null;
  repositories: CodeRepository[];
  repository_count: number;
  created_at: string;
  updated_at?: string | null;
};

export type CodeProjectAutoGitUpdate = { project_id: number; enabled: boolean; interval_hours: number; last_attempted_at?: string | null; last_succeeded_at?: string | null; last_result?: string | null };

export type CodeProjectReference = {
  id: number;
  display_name: string;
  description?: string | null;
};

export type CodeProjectMarkdownDocument = {
  repository_name: string;
  path: string;
  name: string;
  source?: "repository" | "upload" | "agent_index";
  uploader_id?: string | null;
  directory_path?: string | null;
  updated_at?: string | null;
};

export type CodeProjectMarkdownDirectory = {
  repository_name: string;
  path: string;
};

export type CodeProjectMarkdownDocumentContent = {
  repository_name: string;
  path: string;
  content: string;
  is_truncated: boolean;
};

export type CodeProjectAgentMarkdownIndex = {
  available: boolean;
  is_stale: boolean;
  content: string;
  updated_at?: string | null;
};

export type CodeRepositoryDirectoryBrowser = {
  path: string;
  parent_path?: string | null;
  allowed_roots: string[];
  directories: string[];
  directory_entries?: Array<{ name: string; path: string; modified_at?: string | null }>;
  files?: Array<{ name: string; path: string }>;
};

export type CodeRepositorySaveRequest = {
  name?: string;
  project_id?: number;
  display_name?: string;
  root_path: string;
  description?: string;
  languages?: string[];
  solution_files?: string[];
  configuration_files?: string[];
  chat_editable_configuration_files?: string[];
  publish_target?: string;
  publish_configuration?: string;
  publish_runtime?: string;
  publish_output_path?: string;
  publish_command?: string;
};

export type CodeProjectSaveRequest = {
  name?: string;
  display_name?: string;
  root_path: string;
  description?: string;
};

export type GitWorkspaceStatus = { is_repository: boolean; branch?: string | null; remote_branch?: string | null; remote_name?: string | null; changes: string[]; ahead: number; behind: number; ahead_files: number; behind_files: number; remote_refresh_error?: string | null; output: string };
export type GitOperationResult = { ok: boolean; action: string; output: string; status: GitWorkspaceStatus };
export type ProjectGitRepositoryStatus = { repository_id: number; repository_name: string; display_name: string; state: "synced" | "changes" | "ahead" | "behind" | "no-upstream" | "not-repository" | "failed"; message: string; status?: GitWorkspaceStatus | null };
export type ProjectGitStatus = { project_id: number; state: "synced" | "attention" | "neutral"; message: string; repositories: ProjectGitRepositoryStatus[] };
export type ProjectGitBatchRepositoryResult = { repository_id: number; repository_name: string; display_name: string; outcome: "succeeded" | "skipped" | "failed"; message: string; result?: GitOperationResult | null };
export type ProjectGitBatchOperationResult = { project_id: number; action: "discard-and-pull" | "commit-and-push" | "automatic-discard-and-pull"; repositories: ProjectGitBatchRepositoryResult[] };
export type GitWorkspaceBranches = { current_branch?: string | null; local_branches: string[]; remote_branches: string[] };
export type GitDiffComparison = "working" | "push" | "pull";
export type GitWorkspaceDiffFile = { path: string; status: string; old_path?: string | null };
export type GitWorkspaceDiff = { comparison: GitDiffComparison; remote_branch?: string | null; file_count: number; is_truncated: boolean; files: GitWorkspaceDiffFile[]; content: string; message?: string | null };
export type CodeRepositoryHealth = { root_exists: boolean; project_match: boolean; is_git_repository: boolean; branch?: string | null; solution_files: Array<{ path: string; exists: boolean }>; configuration_files: Array<{ path: string; exists: boolean }>; messages: string[] };
export type ConfiguredCodeFile = { path: string; content: string; sha256: string; updated_at: string };
