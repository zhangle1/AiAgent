import type { CodexQuota } from "./codex-quota-types";

export async function getCodexQuota(signal?: AbortSignal): Promise<CodexQuota> {
  const response = await fetch("/api/v1/chat/codex/quota", { cache: "no-store", signal });
  if (!response.ok) throw new Error("无法读取 Codex 额度");
  return response.json() as Promise<CodexQuota>;
}
