export type AgentProviderEnvironment = {
  id: "codex" | "codebuddy";
  name: string;
  command: string;
  installed: boolean;
  version?: string | null;
  protocol: string;
  chat_supported: boolean;
  message: string;
};

export type CodexModelOption = {
  id: string;
  name: string;
  description: string;
};

export type CodexModelPolicy = {
  models: CodexModelOption[];
  allowed_model_ids: string[];
  default_model_id: string;
  allow_chat_model_override: boolean;
};

async function parseJson<T>(response: Response): Promise<T> {
  const payload = await response.json().catch(() => null) as { message?: string } | null;
  if (!response.ok) throw new Error(payload?.message || `Request failed with HTTP ${response.status}`);
  return payload as T;
}

export async function getAgentProviderEnvironments(): Promise<AgentProviderEnvironment[]> {
  return parseJson<AgentProviderEnvironment[]>(await fetch("/api/v1/agent-providers/environments", { cache: "no-store" }));
}

export async function getCodexModelPolicy(): Promise<CodexModelPolicy> {
  return parseJson<CodexModelPolicy>(await fetch("/api/v1/agent-providers/codex-model-policy", { cache: "no-store" }));
}

export async function updateCodexModelPolicy(payload: Pick<CodexModelPolicy, "allowed_model_ids" | "default_model_id" | "allow_chat_model_override">): Promise<CodexModelPolicy> {
  return parseJson<CodexModelPolicy>(await fetch("/api/v1/agent-providers/codex-model-policy", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  }));
}
