import type { OntologyConnection, OntologyGraph, OntologyModel, OntologyPlugin } from "./ontology-types";
import type { OntologySource, OntologySummary } from "./ontology-types";
async function request<T>(project: number, path: string, method = "GET", body?: unknown, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`/api/v1/ontology/${project}/${path}`, { method, signal, cache: "no-store", headers: { "Content-Type": "application/json" }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  if (!response.ok) { const error = await response.json().catch(() => null); throw new Error(error?.message ?? `请求失败 (${response.status})`); }
  return response.json();
}
export function formatOntologyContext(parts: { text: string }[]): string {
  if (parts.length === 1) return parts[0].text;
  return `[${parts.map(({ text }) => text).join(",")}]`;
}

export const ontologyApi = {
  removeSource: (p: number, m: OntologyModel, sourceId: string) => request<OntologyModel>(p, `models/${m.id}/sources/${sourceId}?version=${m.version}`, "DELETE"),
  testSource: (p: number, m: OntologyModel, source: OntologySource, password: string) => request<{checkedAt:string}>(p, `models/${m.id}/sources/test`, "POST", {version:m.version, source, password}),
  saveSource: (p: number, m: OntologyModel, source: OntologySource, password: string) => request<OntologyModel>(p, `models/${m.id}/sources`, "PUT", {version:m.version, source, password}),
  lookup: (p: number, id: string, version: number, sourceIds: string[], key: string, options?: { includeDatabasePasswords?: boolean; signal?: AbortSignal }) => request<{text:string;version:number}>(p, `models/${id}/lookup`, "POST", {version, sourceIds, key, includeDatabasePasswords: options?.includeDatabasePasswords}, options?.signal),
  plugins: (p: number) => request<OntologyPlugin[]>(p, "plugins"),
  list: (p: number) => request<OntologySummary[]>(p, "models"),
  rename: (p: number, m: OntologySummary, name: string) => request<OntologyModel>(p, `models/${m.id}/name`, "PUT", {version:m.version,name}),
  delete: (p: number, m: OntologySummary) => request<{id:string}>(p, `models/${m.id}?version=${m.version}`, "DELETE"),
  create: (p: number, name: string) => request<OntologyModel>(p, "models", "POST", { name }),
  get: (p: number, id: string) => request<OntologyModel>(p, `models/${id}`),
  graph: (p: number, m: OntologyModel, graph: OntologyGraph) => request<OntologyModel>(p, `models/${m.id}/graph`, "PUT", { version: m.version, graph }),
  connection: (p: number, m: OntologyModel, connection: OntologyConnection, password: string) => request<OntologyModel>(p, `models/${m.id}/connection`, "PUT", { version: m.version, graph: m.graph, connection, password }),
  context: (p: number, id: string) => request<{ text: string; version: number }>(p, `models/${id}/context`),
};
