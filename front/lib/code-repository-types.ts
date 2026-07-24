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
  repositories: CodeRepository[];
  repository_count: number;
  created_at: string;
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

export type GitWorkspaceStatus = { is_repository: boolean; branch?: string | null; changes: string[]; ahead: number; behind: number; output: string };
export type GitOperationResult = { ok: boolean; action: string; output: string; status: GitWorkspaceStatus };
export type CodeRepositoryHealth = { root_exists: boolean; project_match: boolean; is_git_repository: boolean; branch?: string | null; solution_files: Array<{ path: string; exists: boolean }>; configuration_files: Array<{ path: string; exists: boolean }>; messages: string[] };
export type ConfiguredCodeFile = { path: string; content: string; sha256: string; updated_at: string };
