export type OntologyGraph = { nodes: { id: string; kind: string; label: string }[]; edges: { source: string; target: string }[] };
export type OntologyConnection = { type: string; host: string; port: number; database: string; username: string };
export type OntologyModel = { id: string; name: string; version: number; connection: OntologyConnection; has_password: boolean; graph: OntologyGraph };
export type OntologyPlugin = { id: string; label: string; version: number };
