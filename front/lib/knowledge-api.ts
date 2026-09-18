import type { KnowledgeBase, KnowledgeDetail, KnowledgeDocumentContent, KnowledgeDocumentImportResult, KnowledgeEnvironmentCheck, KnowledgeIndexVersion, KnowledgeJob, KnowledgeMutationResponse, KnowledgeProcessRequest, KnowledgeProcessingResult, KnowledgeProvider, KnowledgeProviderConfig, KnowledgeSearchResponse } from "@/lib/knowledge-types";

import type { KnowledgeChainCheckResult, KnowledgeCompilationJob, KnowledgeCompilerSettings, KnowledgeOrganization, KnowledgePage } from "@/lib/knowledge-types";

export async function getKnowledgeCompilations(signal?: AbortSignal): Promise<KnowledgeCompilationJob[]> {
  return parseJson(await fetch("/api/v1/knowledge/compilations", { cache: "no-store", signal }));
}
export async function cancelKnowledgeCompilation(name: string, id: number): Promise<KnowledgeCompilationJob> {
  return parseJson(await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}/compilations/${id}/cancel`, { method: "POST" }));
}
export async function getKnowledgeOfficePreview(name: string, id: number, signal?: AbortSignal): Promise<import("@/lib/knowledge-types").KnowledgeOfficePreview> {
  return parseJson(await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}/documents/${id}/office-preview`, { cache: "no-store", signal }));
}

export async function getKnowledgeCompilerSettings(): Promise<KnowledgeCompilerSettings> {
  return parseJson(await fetch("/api/v1/knowledge/compiler-settings", { cache: "no-store" }));
}
export async function saveKnowledgeCompilerSettings(config: KnowledgeCompilerSettings): Promise<KnowledgeCompilerSettings> {
  return parseJson(await fetch("/api/v1/knowledge/compiler-settings", { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(config) }));
}
export async function checkKnowledgeCompilerChain(config: KnowledgeCompilerSettings): Promise<KnowledgeChainCheckResult> {
  return parseJson(await fetch("/api/v1/knowledge/compiler-settings/check", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(config) }));
}
export async function getKnowledgePages(name: string): Promise<KnowledgePage[]> {
  return parseJson(await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}/pages`, { cache: "no-store" }));
}
export async function saveKnowledgeOrganization(name: string, organization: KnowledgeOrganization): Promise<KnowledgeOrganization> {
  return parseJson(await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}/organization`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(organization) }));
}
export async function compileKnowledgeDocument(name: string, id: number): Promise<KnowledgeCompilationJob> {
  return parseJson(await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}/documents/${id}/compile`, { method: "POST" }));
}
export async function getKnowledgeCompilation(name: string, id: number): Promise<KnowledgeCompilationJob | null> {
  return parseJson(await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}/documents/${id}/compilation`, { cache: "no-store" }));
}

export async function getKnowledgeSourceText(name: string, id: number, signal?: AbortSignal): Promise<string> {
  const response = await fetch(knowledgeDocumentFileUrl(name, id), { signal });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.text();
}

function directApi(path: string): string {
  // Keep browser requests on the address the user opened. Next.js then proxies
  // /api to the local backend, so LAN clients never resolve their own localhost.
  return path;
}

function proxiedWebSocketUrl(path: string): string {
  if (typeof window === "undefined") return path;
  const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
  return `${protocol}//${window.location.host}${path}`;
}

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

export async function getKnowledgeProviders(): Promise<KnowledgeProvider[]> {
  return parseJson<KnowledgeProvider[]>(await fetch("/api/v1/knowledge/rag-providers", { cache: "no-store" }));
}

export async function getKnowledgeBases(): Promise<KnowledgeBase[]> {
  return parseJson<KnowledgeBase[]>(await fetch("/api/v1/knowledge/list", { cache: "no-store" }));
}

export async function getKnowledgeBase(name: string): Promise<KnowledgeDetail> {
  return parseJson<KnowledgeDetail>(await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}`, { cache: "no-store" }));
}

export async function getKnowledgeIndexVersions(name: string): Promise<KnowledgeIndexVersion[]> {
  return parseJson<KnowledgeIndexVersion[]>(await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}/index-versions`, { cache: "no-store" }));
}

export async function getKnowledgeProgress(name: string): Promise<KnowledgeJob | null> {
  return parseJson<KnowledgeJob | null>(await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}/progress`, { cache: "no-store" }));
}

export async function getKnowledgeDiagnostics(): Promise<unknown> {
  return parseJson<unknown>(await fetch("/api/v1/knowledge/diagnostics", { cache: "no-store" }));
}

export async function checkKnowledgeEnvironment(provider = "llamaindex"): Promise<KnowledgeEnvironmentCheck> {
  return parseJson<KnowledgeEnvironmentCheck>(
    await fetch(`/api/v1/knowledge/rag-providers/${encodeURIComponent(provider)}/preflight`, { cache: "no-store" }),
  );
}

export async function getKnowledgeProviderConfig(provider = "llamaindex"): Promise<KnowledgeProviderConfig> {
  return parseJson<KnowledgeProviderConfig>(
    await fetch(`/api/v1/knowledge/rag-providers/${encodeURIComponent(provider)}/config`, { cache: "no-store" }),
  );
}

export async function saveKnowledgeProviderConfig(provider: string, config: KnowledgeProviderConfig): Promise<KnowledgeProviderConfig> {
  return parseJson<KnowledgeProviderConfig>(
    await fetch(`/api/v1/knowledge/rag-providers/${encodeURIComponent(provider)}/config`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(config),
    }),
  );
}

export async function createKnowledgeBase(name: string, files: File[], provider = "llamaindex"): Promise<KnowledgeMutationResponse> {
  const body = new FormData();
  body.set("Name", name);
  body.set("Provider", provider);
  for (const file of files) {
    body.append("Files", file);
  }

  return parseJson<KnowledgeMutationResponse>(
    await fetch(directApi("/api/v1/knowledge/create"), {
      method: "POST",
      body,
    }),
  );
}

export async function uploadKnowledgeDocuments(name: string, files: File[]): Promise<KnowledgeDocumentImportResult> {
  const normalizedName = name?.trim();
  if (!normalizedName) {
    throw new Error("Knowledge base name is required.");
  }

  const body = new FormData();
  for (const file of files) {
    body.append("Files", file);
  }

  return parseJson<KnowledgeDocumentImportResult>(
    await fetch(directApi(`/api/v1/knowledge/${encodeURIComponent(normalizedName)}/upload`), {
      method: "POST",
      body,
    }),
  );
}

export async function reindexKnowledgeBase(name: string): Promise<KnowledgeMutationResponse> {
  return parseJson<KnowledgeMutationResponse>(
    await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}/reindex`, {
      method: "POST",
    }),
  );
}

export async function deleteKnowledgeBase(name: string): Promise<{ ok: boolean }> {
  return parseJson<{ ok: boolean }>(
    await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}`, {
      method: "DELETE",
    }),
  );
}

export async function deleteKnowledgeDocument(kbName: string, documentId: number): Promise<{ ok: boolean }> {
  return parseJson<{ ok: boolean }>(
    await fetch(`/api/v1/knowledge/${encodeURIComponent(kbName)}/documents/${documentId}`, {
      method: "DELETE",
    }),
  );
}

export async function processKnowledgeDocument(kbName: string, documentId: number, request: KnowledgeProcessRequest): Promise<KnowledgeProcessingResult> {
  return parseJson<KnowledgeProcessingResult>(
    await fetch(`/api/v1/knowledge/${encodeURIComponent(kbName)}/documents/${documentId}/process`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }),
  );
}

export async function getKnowledgeDocumentContent(kbName: string, documentId: number): Promise<KnowledgeDocumentContent> {
  return parseJson<KnowledgeDocumentContent>(
    await fetch(`/api/v1/knowledge/${encodeURIComponent(kbName)}/documents/${documentId}/content`, { cache: "no-store" }),
  );
}

export async function setDefaultKnowledgeBase(name: string): Promise<KnowledgeBase> {
  return parseJson<KnowledgeBase>(
    await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}/default`, {
      method: "POST",
    }),
  );
}

export async function searchKnowledgeBase(name: string, query: string, topK = 5): Promise<KnowledgeSearchResponse> {
  return parseJson<KnowledgeSearchResponse>(
    await fetch(`/api/v1/knowledge/${encodeURIComponent(name)}/search`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ query, top_k: topK }),
    }),
  );
}

export function knowledgeDocumentFileUrl(kbName: string, documentId: number, download = false): string {
  const url = directApi(`/api/v1/knowledge/${encodeURIComponent(kbName)}/documents/${documentId}/file`);
  return download ? `${url}?download=1` : url;
}

export function knowledgeProgressWebSocketUrl(kbName: string): string {
  return proxiedWebSocketUrl(`/api/v1/knowledge/ws?kbName=${encodeURIComponent(kbName)}`);
}
