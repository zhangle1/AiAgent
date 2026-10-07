import type { ChatRuntimeJob, RuntimeSelection } from "./chat-runtime-types";

async function request<T>(projectId: number, suffix = "", body?: unknown): Promise<T> {
  const response = await fetch(`/api/v1/code-runtime/projects/${projectId}/chat-runs${suffix}`, {
    method: body === undefined ? "GET" : "POST", cache: "no-store",
    ...(body === undefined ? {} : { headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }),
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) throw new Error(payload?.message || `运行请求失败（${response.status}）`);
  if (payload === null) throw new Error("运行服务返回了无效响应");
  return payload;
}
export const listChatRuntimeJobs = (projectId: number) => request<ChatRuntimeJob[]>(projectId);
export const prepareChatRuntime = (projectId: number, selections: RuntimeSelection[], idleMinutes: number) => request<ChatRuntimeJob>(projectId, "", { selections, idle_minutes: idleMinutes });
export const stopChatRuntime = (projectId: number, id: string) => request(projectId, `/${encodeURIComponent(id)}/stop`, {});
export const visitChatRuntime = (projectId: number, id: string) => request(projectId, `/${encodeURIComponent(id)}/visit`, {});
