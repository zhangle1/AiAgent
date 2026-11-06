export type KnowledgeCleanupItem = {
  document_id: number;
  name: string;
  uri: string;
  reason: string;
  message: string;
  can_cleanup: boolean;
  blocked_reason?: string | null;
};

export type KnowledgeCleanupPreview = {
  items: KnowledgeCleanupItem[];
  snapshot_sha256: string;
  includes_shared: boolean;
};

export type KnowledgeCleanupResult = { removed_count: number };
