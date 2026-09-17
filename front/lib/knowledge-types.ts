export type KnowledgeProvider = {
  id: string;
  name: string;
  description: string;
  configured: boolean;
  status: string;
  modes: string[];
  default_mode: string;
};

export type KnowledgeJob = {
  id: number;
  knowledge_base_id: number;
  index_version_id?: number | null;
  job_type: string;
  status: string;
  progress: number;
  message?: string | null;
  error_message?: string | null;
  created_at: string;
  started_at?: string | null;
  finished_at?: string | null;
};

export type KnowledgeBase = {
  organization?: KnowledgeOrganization;
  id: number;
  name: string;
  display_name: string;
  description?: string | null;
  engine_type: string;
  status: string;
  is_default: boolean;
  document_count: number;
  active_version_id?: number | null;
  created_at: string;
  updated_at?: string | null;
  latest_job?: KnowledgeJob | null;
};

export type KnowledgeDocument = {
  id: number;
  has_artifact?: boolean;
  file_name: string;
  original_file_name: string;
  file_size: number;
  content_type?: string | null;
  extension?: string | null;
  file_hash?: string | null;
  status: string;
  created_at: string;
};

export type KnowledgeProcessRequest = {
  generator: "llm_api" | "codex";
  model_id?: string | null;
  reasoning_effort?: string | null;
};

export type KnowledgeOrganization = { company?: string | null; project?: string | null };
export type KnowledgeCompilerSettings = {
  retrieval_mode: "wiki" | "rag";
  generator: "codex" | "llm_api";
  model_id?: string | null;
  reasoning_effort?: string | null;
  max_steps: number;
  timeout_minutes: number;
};
export type KnowledgeCompilationJob = {
  knowledge_base_name: string;
  document_name?: string | null;
  created_at: string;
  started_at?: string | null;
  finished_at?: string | null;
  updated_at?: string | null;
  id: number;
  document_id?: number | null;
  status: string;
  progress: number;
  message?: string | null;
};
export type KnowledgeOfficePreview = {
  sections: { name: string; html: string }[];
  truncated: boolean;
};
export type KnowledgePage = {
  id: number;
  page_index: number;
  document_id?: number | null;
  title: string;
  content?: string | null;
  source_name?: string | null;
  review_status?: string | null;
  generator?: string | null;
  model?: string | null;
  created_at?: string | null;
};

export type KnowledgeProcessingResult = {
  document_id: number;
  parsed_document_id: number;
  artifact_id: number;
  status: string;
  generator: string;
  parser: string;
};

export type KnowledgeDocumentContent = {
  document_id: number;
  original_file_name: string;
  status: string;
  parsed_document_id?: number | null;
  parsed_content?: string | null;
  parser?: string | null;
  artifact_id?: number | null;
  artifact_content?: string | null;
  generator?: string | null;
  provider?: string | null;
  model?: string | null;
  review_status?: string | null;
};

export type KnowledgeDetail = KnowledgeBase & {
  documents: KnowledgeDocument[];
};

export type KnowledgeIndexVersion = {
  id: number;
  knowledge_base_id: number;
  version_no: number;
  status: string;
  engine_type: string;
  storage_path?: string | null;
  document_count: number;
  chunk_count: number;
  active: boolean;
  created_at: string;
  activated_at?: string | null;
};

export type KnowledgeMutationResponse = {
  knowledge_base: KnowledgeBase;
  task_id?: number | null;
  message: string;
};

export type KnowledgeDocumentImportItem = {
  file_name: string;
  status: "imported" | "skipped" | "failed";
  message: string;
  document_id?: number | null;
};

export type KnowledgeDocumentImportResult = {
  knowledge_base: KnowledgeBase;
  items: KnowledgeDocumentImportItem[];
  task_id?: number | null;
  message: string;
};

export type KnowledgeEnvironmentCheck = {
  ok?: boolean;
  provider?: string;
  action?: string;
  document_count?: number;
  chunk_count?: number;
  error_code?: string | null;
  error_message?: string | null;
  documentCount?: number;
  chunkCount?: number;
  errorCode?: string | null;
  errorMessage?: string | null;
  Ok?: boolean;
  ErrorCode?: string | null;
  ErrorMessage?: string | null;
  details?: Record<string, unknown>;
  Details?: Record<string, unknown>;
};

export type KnowledgeProviderConfig = {
  provider: string;
  retrieval_profile: "hybrid" | "vector";
  top_k: number;
  vector_candidate_multiplier: number;
  keyword_candidate_multiplier: number;
  chunk_size: number;
  chunk_overlap: number;
  updated_at?: string | null;
};

export type KnowledgeCitation = {
  score?: number | null;
  text: string;
  metadata?: Record<string, unknown> | null;
};

export type KnowledgeSearchResponse = {
  query: string;
  provider: string;
  answer: string;
  content: string;
  citations: KnowledgeCitation[];
};
