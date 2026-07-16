export type CodeRepository = {
  id: number;
  name: string;
  display_name: string;
  root_path: string;
  source_type: string;
  description?: string | null;
  status: string;
  languages: string[];
  build_systems: string[];
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
};

export type CodeRepositoryDirectoryBrowser = {
  path: string;
  parent_path?: string | null;
  allowed_roots: string[];
  directories: string[];
};

export type CodeRepositorySaveRequest = {
  name?: string;
  display_name?: string;
  root_path: string;
  description?: string;
};
