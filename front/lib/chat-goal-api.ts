import type { ChatGoal } from "./chat-goal-types";

async function request(sessionId: string, action?: "pause" | "end", version?: number): Promise<ChatGoal | null> {
  const response = await fetch(`/api/v1/chat/sessions/${encodeURIComponent(sessionId)}/goal${action ? `/${action}` : ""}`, {
    cache: "no-store",
    ...(action ? { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ expected_version: version }) } : {}),
  });
  const payload = await response.json();
  if (!response.ok) throw new Error(payload?.message || `目标请求失败 (${response.status})`);
  return payload.goal;
}
export const getChatGoal = (sessionId: string) => request(sessionId);
export const controlChatGoal = (sessionId: string, action: "pause" | "end", version: number) => request(sessionId, action, version);
