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
export const refreshKnowledgeGit = () => request<KnowledgeGitValidation>("/refresh", "POST");
export const pushKnowledgeGit = (message: string, snapshot: string) => request<KnowledgeGitResult>("/push", "POST", { message, snapshot });
