import type {
  CodeRepository,
  CodeRepositoryDirectoryBrowser,
  CodeRepositoryInspection,
  CodeRepositorySaveRequest,
} from "@/lib/code-repository-types";

async function parseJson<T>(response: Response): Promise<T> {
  const text = await response.text();
  let payload: unknown = null;
  if (text) {
    try {
      payload = JSON.parse(text) as unknown;
    } catch {
      throw new Error(`Request returned non-JSON response: HTTP ${response.status} ${text.slice(0, 160)}`);
    }
  }

  if (!response.ok) {
    const message = typeof payload === "object" && payload && "message" in payload
      ? String((payload as { message?: string }).message)
      : `Request failed with HTTP ${response.status}`;
    throw new Error(message);
  }

  return payload as T;
}

export async function getCodeRepositories(): Promise<CodeRepository[]> {
  return parseJson<CodeRepository[]>(await fetch("/api/v1/code-repositories/list", { cache: "no-store" }));
}

export async function browseCodeRepositoryDirectories(path?: string): Promise<CodeRepositoryDirectoryBrowser> {
  const query = path ? `?path=${encodeURIComponent(path)}` : "";
  return parseJson<CodeRepositoryDirectoryBrowser>(
    await fetch(`/api/v1/code-repositories/browse${query}`, { cache: "no-store" }),
  );
}

export async function inspectCodeRepository(rootPath: string): Promise<CodeRepositoryInspection> {
  return parseJson<CodeRepositoryInspection>(
    await fetch("/api/v1/code-repositories/inspect", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ root_path: rootPath }),
    }),
  );
}

export async function createCodeRepository(payload: CodeRepositorySaveRequest): Promise<CodeRepository> {
  return parseJson<CodeRepository>(
    await fetch("/api/v1/code-repositories/create", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    }),
  );
}

export async function updateCodeRepository(name: string, payload: CodeRepositorySaveRequest): Promise<CodeRepository> {
  return parseJson<CodeRepository>(
    await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    }),
  );
}

export async function deleteCodeRepository(name: string): Promise<void> {
  await parseJson<{ ok: boolean }>(
    await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}`, { method: "DELETE" }),
  );
}

export async function indexCodeRepository(name: string): Promise<{ ok: boolean; status: string }> {
  return parseJson<{ ok: boolean; status: string }>(
    await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/index`, { method: "POST" }),
  );
}
export type CodeRepositoryCloneEvent = { type: "connected" | "started" | "output" | "completed"; message?: string; line?: string; stream?: "stdout" | "stderr"; success?: boolean; exit_code?: number; destination_path?: string };

export function cloneCodeRepositoryViaWebSocket(request: { repository_url: string; destination_parent_path: string; git_account_id: number }, onEvent: (event: CodeRepositoryCloneEvent) => void): Promise<CodeRepositoryCloneEvent> {
  return new Promise((resolve, reject) => {
    const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    // Use the same frontend origin. Next.js forwards the upgrade to the backend,
    // where the local server-side git process is started.
    const socket = new WebSocket(`${protocol}//${window.location.host}/api/v1/code-repositories/clone/ws`);
    let completed = false;
    socket.onopen = () => socket.send(JSON.stringify(request));
    socket.onmessage = (message) => {
      try {
        const event = JSON.parse(String(message.data)) as CodeRepositoryCloneEvent;
        onEvent(event);
        if (event.type === "completed") { completed = true; resolve(event); }
      } catch { reject(new Error("Invalid clone terminal response.")); }
    };
    socket.onerror = () => { if (!completed) reject(new Error("Unable to connect to the server-side clone terminal.")); };
    socket.onclose = () => { if (!completed) reject(new Error("Clone terminal disconnected before completion.")); };
  });
}
export type CodeIndexProgress = { repositoryName?: string; repository_name?: string; status: string; stage: string; currentPath?: string | null; current_path?: string | null; totalFiles?: number; total_files?: number; scannedFiles?: number; scanned_files?: number; indexedFiles?: number; indexed_files?: number; skippedFiles?: number; skipped_files?: number; percent: number; error?: string | null };
export async function getCodeIndexProgress(name: string): Promise<CodeIndexProgress> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/index-progress`, { cache: "no-store" })); }

export type CodeTree = { path: string; directories: Array<{ name: string; path: string }>; files: Array<{ name: string; path: string; extension: string; size: number }> };
export type CodeFile = { path: string; extension: string; content: string; line_count: number };
export async function getCodeTree(name: string, path = ""): Promise<CodeTree> { return parseJson<CodeTree>(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/tree?path=${encodeURIComponent(path)}`)); }
export async function getCodeFile(name: string, path: string): Promise<CodeFile> { return parseJson<CodeFile>(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/file?path=${encodeURIComponent(path)}`)); }
export type CodeGrepResult = { path: string; line: number; preview: string };
export async function grepCodeRepository(name: string, query: string): Promise<{ query: string; matches: CodeGrepResult[]; truncated: boolean }> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/grep?query=${encodeURIComponent(query)}`)); }
