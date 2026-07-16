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
  file_name: string;
  original_file_name: string;
  file_size: number;
  content_type?: string | null;
  extension?: string | null;
  file_hash?: string | null;
  status: string;
  created_at: string;
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
