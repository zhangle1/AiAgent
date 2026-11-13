import type { OntologyConnection, OntologyGraph, OntologyModel, OntologyPlugin } from "./ontology-types";
async function request<T>(project: number, path: string, method = "GET", body?: unknown): Promise<T> {
  const response = await fetch(`/api/v1/ontology/${project}/${path}`, { method, cache: "no-store", headers: { "Content-Type": "application/json" }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  if (!response.ok) { const error = await response.json().catch(() => null); throw new Error(error?.message ?? `请求失败 (${response.status})`); }
  return response.json();
}
export const ontologyApi = {
  plugins: (p: number) => request<OntologyPlugin[]>(p, "plugins"),
  list: (p: number) => request<{ id: string; name: string }[]>(p, "models"),
  create: (p: number, name: string) => request<OntologyModel>(p, "models", "POST", { name }),
  get: (p: number, id: string) => request<OntologyModel>(p, `models/${id}`),
  graph: (p: number, m: OntologyModel, graph: OntologyGraph) => request<OntologyModel>(p, `models/${m.id}/graph`, "PUT", { version: m.version, graph }),
  connection: (p: number, m: OntologyModel, connection: OntologyConnection, password: string) => request<OntologyModel>(p, `models/${m.id}/connection`, "PUT", { version: m.version, graph: m.graph, connection, password }),
  context: (p: number, id: string) => request<{ text: string; version: number }>(p, `models/${id}/context`),
};
