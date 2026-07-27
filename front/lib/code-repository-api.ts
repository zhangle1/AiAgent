import type {
  CodeRepository,
  CodeRepositoryDirectoryBrowser,
  CodeRepositoryInspection,
  CodeProject,
  CodeProjectSaveRequest,
  CodeRepositorySaveRequest,
  GitOperationResult,
  GitWorkspaceStatus,
  GitWorkspaceBranches,
  GitWorkspaceDiff,
  GitDiffComparison,
  CodeRepositoryHealth,
  ConfiguredCodeFile,
} from "@/lib/code-repository-types";

async function parseJson<T>(response: Response): Promise<T> {
  const text = await response.text();
  let payload: unknown = null;
  if (text) {
    try {
      payload = JSON.parse(text) as unknown;
    } catch {
      const detail = text.replace(/<[^>]*>/g, " ").replace(/\s+/g, " ").trim();
      const suffix = detail && !/^internal server error$/i.test(detail) ? ` 原因：${detail.slice(0, 160)}` : " 服务端未返回详细错误，请查看后端日志。";
      throw new Error(`请求失败（HTTP ${response.status}）。${suffix}`);
    }
  }

  if (!response.ok) {
    const message = typeof payload === "object" && payload && "message" in payload
      ? String((payload as { message?: string }).message)
      : `请求失败（HTTP ${response.status}）。服务端未返回详细错误，请查看后端日志。`;
    throw new Error(message);
  }

  return payload as T;
}

export async function getCodeRepositories(): Promise<CodeRepository[]> {
  return parseJson<CodeRepository[]>(await fetch("/api/v1/code-repositories/list", { cache: "no-store" }));
}

export async function getCodeProjects(): Promise<CodeProject[]> {
  return parseJson<CodeProject[]>(await fetch("/api/v1/code-repositories/projects", { cache: "no-store" }));
}

export type ResolvedCodeFileReference = {
  repository_name: string;
  file_path: string;
  line?: number | null;
};

export async function resolveProjectCodeFileReference(projectId: number, reference: string): Promise<ResolvedCodeFileReference> {
  return parseJson<ResolvedCodeFileReference>(
    await fetch(`/api/v1/code-repositories/projects/${projectId}/resolve-file-reference`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ reference }),
    }),
  );
}

export async function createCodeProject(payload: CodeProjectSaveRequest): Promise<CodeProject> {
  return parseJson<CodeProject>(await fetch("/api/v1/code-repositories/projects", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload) }));
}

export async function updateCodeProject(id: number, payload: CodeProjectSaveRequest): Promise<CodeProject> {
  return parseJson<CodeProject>(await fetch(`/api/v1/code-repositories/projects/${id}`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload) }));
}

export async function deleteCodeProject(id: number): Promise<void> {
  await parseJson<{ ok: boolean }>(await fetch(`/api/v1/code-repositories/projects/${id}`, { method: "DELETE" }));
}

export async function browseCodeRepositoryDirectories(path?: string): Promise<CodeRepositoryDirectoryBrowser> {
  const query = path ? `?path=${encodeURIComponent(path)}` : "";
  return parseJson<CodeRepositoryDirectoryBrowser>(
    await fetch(`/api/v1/code-repositories/browse${query}`, { cache: "no-store" }),
  );
}

export async function browseCodeRepositoryFiles(rootPath: string, kind: "solution" | "configuration", path?: string): Promise<CodeRepositoryDirectoryBrowser> {
  const query = new URLSearchParams({ root_path: rootPath, kind });
  if (path) query.set("path", path);
  return parseJson<CodeRepositoryDirectoryBrowser>(
    await fetch(`/api/v1/code-repositories/browse/files?${query.toString()}`, { cache: "no-store" }),
  );
}

export async function uploadCodeRepositoryFile(rootPath: string, directoryPath: string, file: File, overwrite: boolean): Promise<{ name: string; path: string }> {
  const body = new FormData();
  body.set("root_path", rootPath);
  body.set("path", directoryPath);
  body.set("file", file);
  body.set("overwrite", String(overwrite));
  return parseJson<{ name: string; path: string }>(
    await fetch("/api/v1/code-repositories/browse/files/upload", { method: "POST", body }),
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
export type CodeRepositoryCloneEvent = { type: "connected" | "started" | "output" | "completed"; message?: string; line?: string; stream?: "stdout" | "stderr"; success?: boolean; exit_code?: number; destination_path?: string; repository?: CodeRepository };
export type CodeRepositoryPackageEvent = { type: "connected" | "started" | "output" | "completed"; message?: string; line?: string; stream?: "stdout" | "stderr"; success?: boolean; exit_code?: number; target_path?: string; output_path?: string; archive_name?: string | null };

export function cloneCodeRepositoryViaWebSocket(request: { project_id?: number; repository_url: string; destination_parent_path?: string; git_account_id: number }, onEvent: (event: CodeRepositoryCloneEvent) => void): Promise<CodeRepositoryCloneEvent> {
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
export function packageCodeRepositoryViaWebSocket(repositoryName: string, onEvent: (event: CodeRepositoryPackageEvent) => void): Promise<CodeRepositoryPackageEvent> {
  return new Promise((resolve, reject) => {
    const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    const socket = new WebSocket(`${protocol}//${window.location.host}/api/v1/code-repositories/package/ws`);
    let completed = false;
    socket.onopen = () => socket.send(JSON.stringify({ repository_name: repositoryName }));
    socket.onmessage = (message) => {
      try {
        const event = JSON.parse(String(message.data)) as CodeRepositoryPackageEvent;
        onEvent(event);
        if (event.type === "completed") { completed = true; resolve(event); }
      } catch { reject(new Error("Invalid package terminal response.")); }
    };
    socket.onerror = () => { if (!completed) reject(new Error("Unable to connect to the server-side package terminal.")); };
    socket.onclose = () => { if (!completed) reject(new Error("Package terminal disconnected before completion.")); };
  });
}
export type CodeIndexProgress = { repositoryName?: string; repository_name?: string; status: string; stage: string; currentPath?: string | null; current_path?: string | null; totalFiles?: number; total_files?: number; scannedFiles?: number; scanned_files?: number; indexedFiles?: number; indexed_files?: number; skippedFiles?: number; skipped_files?: number; percent: number; error?: string | null };
export async function getCodeIndexProgress(name: string): Promise<CodeIndexProgress> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/index-progress`, { cache: "no-store" })); }
export async function getCodeRepositoryHealth(name: string): Promise<CodeRepositoryHealth> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/health`, { cache: "no-store" })); }
export async function readConfiguredCodeFile(name: string, path: string): Promise<ConfiguredCodeFile> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/configured-file?path=${encodeURIComponent(path)}`, { cache: "no-store" })); }
export async function writeConfiguredCodeFile(name: string, payload: { path: string; content: string; expected_sha256: string }): Promise<{ ok: boolean; path: string; sha256: string }> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/configured-file`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload) })); }
export async function readChatConfiguredCodeFile(name: string, path: string): Promise<ConfiguredCodeFile> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/chat-configured-file?path=${encodeURIComponent(path)}`, { cache: "no-store" })); }
export async function writeChatConfiguredCodeFile(name: string, payload: { path: string; content: string; expected_sha256: string }): Promise<{ ok: boolean; path: string; sha256: string }> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/chat-configured-file`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload) })); }

export async function getCodeRepositoryGitStatus(name: string): Promise<GitWorkspaceStatus> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/git/status`, { cache: "no-store" })); }
export async function getCodeRepositoryGitBranches(name: string): Promise<GitWorkspaceBranches> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/git/branches`, { cache: "no-store" })); }
export async function getCodeRepositoryGitDiff(name: string, comparison: GitDiffComparison): Promise<GitWorkspaceDiff> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/git/diff?comparison=${comparison}`, { cache: "no-store" })); }
export async function checkoutCodeRepositoryGitBranch(name: string, branch: string): Promise<GitOperationResult> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/git/checkout`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ branch }) })); }
export async function discardCodeRepositoryChangesAndPull(name: string): Promise<GitOperationResult> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/git/discard-and-pull`, { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" })); }
export async function pullCodeRepositoryGit(name: string): Promise<GitOperationResult> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/git/pull`, { method: "POST", body: "{}", headers: { "Content-Type": "application/json" } })); }
export async function pushCodeRepositoryGit(name: string, message: string): Promise<GitOperationResult> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/git/push`, { method: "POST", body: JSON.stringify({ message }), headers: { "Content-Type": "application/json" } })); }

export type CodeTree = { path: string; directories: Array<{ name: string; path: string }>; files: Array<{ name: string; path: string; extension: string; size: number }> };
export type CodeFile = { path: string; extension: string; content: string; line_count: number };
export async function getCodeTree(name: string, path = ""): Promise<CodeTree> { return parseJson<CodeTree>(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/tree?path=${encodeURIComponent(path)}`)); }
export async function getCodeFile(name: string, path: string): Promise<CodeFile> { return parseJson<CodeFile>(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/file?path=${encodeURIComponent(path)}`)); }
export type CodeGrepResult = { path: string; line: number; preview: string };
export async function grepCodeRepository(name: string, query: string): Promise<{ query: string; matches: CodeGrepResult[]; truncated: boolean }> { return parseJson(await fetch(`/api/v1/code-repositories/${encodeURIComponent(name)}/grep?query=${encodeURIComponent(query)}`)); }
