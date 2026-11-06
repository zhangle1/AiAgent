import type { ChatSkill } from "./chat-skill-types";
export async function getChatSkills(signal?: AbortSignal): Promise<ChatSkill[]> {
  const response = await fetch("/api/v1/chat/skills", { signal, cache: "no-store" });
  if (!response.ok) throw new Error(`技能列表读取失败（${response.status}），请重试。`);
  return response.json();
}
