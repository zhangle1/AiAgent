export type OntologyGraph = { nodes: { id: string; kind: string; label: string }[]; edges: { source: string; target: string }[] };
export type OntologyConnection = { type: string; host: string; port: number; database: string; username: string; trustServerCertificate?: boolean };
export type OntologySource = { id: string; label: string; connection: OntologyConnection; relation: string; keyColumn: string; valueColumn: string; probeKey?: string; note?: string };
export type OntologySummary = { id: string; name: string; version: number };
export type OntologyModel = { id: string; name: string; version: number; connection: OntologyConnection; has_password: boolean; graph: OntologyGraph; sources?: { definition: OntologySource; checkedAt: string }[] };
export type OntologyPlugin = { id: string; label: string; version: number };
export type OntologyAttachment = { projectId: number; key: string; includeDatabasePasswords?: boolean; models: { id: string; name: string; version: number; ids: string[] }[] };
