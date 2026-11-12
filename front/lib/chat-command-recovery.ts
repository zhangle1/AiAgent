import type { ChatCompleteRequest } from "./chat-api";

const KEY = "aiagent:pending-chat-commands:v1";
type Pointer = Pick<ChatCompleteRequest, "agent" | "code_project_id"> & { command_id: string; session_id: string; saved_at: number };

export function readRecoveryPointers(): Pointer[] {
  try {
    const data: unknown = JSON.parse(sessionStorage.getItem(KEY) ?? "[]");
    if (!Array.isArray(data)) return [];
    return data.filter((value): value is Pointer => value && typeof value.command_id === "string"
      && typeof value.session_id === "string" && typeof value.saved_at === "number"
      && Date.now() - value.saved_at < 24 * 60 * 60 * 1000).slice(-10);
  } catch { return []; }
}

export function saveRecoveryPointer(id: string, request: ChatCompleteRequest): void {
  if (!request.session_id || !request.command_id) return;
  try {
    const pointers = readRecoveryPointers().filter((item) => item.command_id !== id && item.session_id !== request.session_id);
    pointers.push({ command_id: request.command_id, session_id: request.session_id, agent: request.agent,
      code_project_id: request.code_project_id, saved_at: Date.now() });
    sessionStorage.setItem(KEY, JSON.stringify(pointers.slice(-10)));
  } catch { /* Storage restrictions must not prevent sending. */ }
}

export function removeRecoveryPointer(id: string): void {
  try { sessionStorage.setItem(KEY, JSON.stringify(readRecoveryPointers().filter((item) => item.command_id !== id))); }
  catch { /* Storage is optional. */ }
}
