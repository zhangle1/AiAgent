import type { KnowledgeGitConfig, KnowledgeGitState, KnowledgeGitValidation, KnowledgeGitResult } from "./knowledge-git-types";

async function request<T>(suffix: string, method = "GET", body?: unknown): Promise<T> {
  const response = await fetch(`/api/v1/knowledge/team-git${suffix}`, {
    method, cache: "no-store", headers: { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const data = await response.json();
  if (!response.ok) throw new Error(data.message || data.error || "团队知识 Git 请求失败");
  return data;
}
export const getKnowledgeGit = () => request<KnowledgeGitState>("");
export const saveKnowledgeGit = (config: KnowledgeGitConfig) => request<KnowledgeGitConfig>("", "PUT", config);
export const refreshKnowledgeGit = (sessionUris: string[] = [], force = true, resourceUris?: string[], includeOwnSessions = false) => request<KnowledgeGitValidation>("/refresh", "POST", { session_uris: sessionUris, resource_uris: resourceUris, include_own_sessions: includeOwnSessions, force });
export const pushKnowledgeGit = (message: string, snapshot: string, force = true) => request<KnowledgeGitResult>("/push", "POST", { message, snapshot, force });
