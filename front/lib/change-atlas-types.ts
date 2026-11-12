export type AtlasRepository = { id: number; name: string; head_sha: string; ref: string; source: "local" | "remote"; refs: string[] };
export type AtlasModule = { id: string; repository_id: number; name: string; directory: string };
export type AtlasFile = { id: string; repository_id: number; module_id: string; path: string; at_head: boolean };
export type AtlasFileChange = { file_id: string; added: number | null; deleted: number | null };
export type AtlasCommit = { id: string; repository_id: number; sha: string; parent_sha: string | null; is_merge: boolean; author_id: string; author: string; committed_at: string; subject: string; files: AtlasFileChange[]; truncated: boolean };
export type AtlasSnapshot = { id: string; project_id: number; created_at: string; start_at: string; days: number; coverage: "directory"; repositories: AtlasRepository[]; modules: AtlasModule[]; files: AtlasFile[]; commits: AtlasCommit[]; warnings: string[] };
export type AtlasCreateRequest = { repository_id?: number; days: number; ref: string; refresh_remote: boolean };
export type AtlasEvidence = { id: string; file_id: string; commit_id: string; path: string; sha: string; patch: string; truncated: boolean; suppressed: boolean };
export type AtlasAnalysisRequest = { module_id: string; author_id?: string; until: string; daily: boolean; perspective: "delivery" | "developer"; question: string; model_id?: string; reasoning_effort?: string };
export type AtlasClaim = { kind: "code_observation" | "inference" | "suggestion"; text: string; evidence_ids: string[] };
export type AtlasAnalysis = { summary: string; module_label: string | null; claims: AtlasClaim[]; checks: string[]; unknowns: string[]; evidence: AtlasEvidence[]; model: string; created_at: string };
export type AtlasFilters = { until: string; daily: boolean; authorId: string };

export type AtlasImportEdge = { id: string; source: string; target: string; line: number; text: string };
export type AtlasImportGraph = { snapshot_id: string; head_sha: string; files: AtlasFile[]; edges: AtlasImportEdge[]; scanned: number; skipped: number; unresolved: number; warnings: string[] };
