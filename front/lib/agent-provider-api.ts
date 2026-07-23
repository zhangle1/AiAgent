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

async function parseJson<T>(response: Response): Promise<T> {
  const payload = await response.json().catch(() => null) as { message?: string } | null;
  if (!response.ok) throw new Error(payload?.message || `Request failed with HTTP ${response.status}`);
  return payload as T;
}

export async function getAgentProviderEnvironments(): Promise<AgentProviderEnvironment[]> {
  return parseJson<AgentProviderEnvironment[]>(await fetch("/api/v1/agent-providers/environments", { cache: "no-store" }));
}
