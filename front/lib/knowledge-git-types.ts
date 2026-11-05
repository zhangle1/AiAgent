export type KnowledgeGitConfig = { remote_url: string; branch: string };
export type KnowledgeGitState = {
  root_path: string; workspace_path: string; config: KnowledgeGitConfig;
  status: { branch?: string; changes: string[]; ahead: number; behind: number; remote_refresh_error?: string } | null;
};
export type KnowledgeGitValidation = { snapshot_sha256: string; is_valid: boolean; files: string[]; checks: { name: string; passed: boolean; message: string }[] };
export type KnowledgeGitResult = { ok: boolean; output: string; commit_sha?: string };
