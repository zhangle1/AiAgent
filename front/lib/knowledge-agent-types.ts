import type { KnowledgeCompilerSettings, KnowledgeResourceImport, KnowledgeResourceNode } from "./knowledge-types";

export type KnowledgeAgentModel = { id: string; name: string; profile: string; context_window: number; native_tools: boolean; is_default: boolean };
export type KnowledgeAgentOptions = {
  llm_models: KnowledgeAgentModel[];
  cli_policy: { models: { id: string; name: string; profile_name?: string | null; reasoning_efforts: string[] }[]; allowed_model_ids: string[]; default_model_id: string; default_reasoning_effort: string; allow_chat_model_override?: boolean; allow_chat_reasoning_effort_override?: boolean };
  compiler: KnowledgeCompilerSettings;
};
export type KnowledgeAgentContext = {
  estimated_input_tokens: number; input_limit: number; context_window: number; output_reserve: number; reserve: number;
  estimation: string; compression_before: number; compression_after: number; compression_ratio: number;
  compactions: number; model_calls: number; model_call_limit: number; native_tools: boolean;
};
export type KnowledgeAgentEvent = { type: string; content?: string; model_id?: string; model?: string; metadata: Record<string, unknown> };
export type KnowledgeAgentHistory = { role: "user" | "assistant"; content: string };
export type KnowledgeAgentCommand = "ls" | "read" | "search" | "status";
export type KnowledgeAgentCommandResult = {
  uri?: string; nodes?: KnowledgeResourceNode[]; total?: number; content?: string; offset?: number;
  total_characters?: number; next_offset?: number | null; query?: string; hits?: { uri: string; name: string; offset: number; excerpt: string }[];
  total_files?: number; scan_limited?: boolean; processing?: unknown;
};
export type KnowledgeAgentCompileResult = KnowledgeResourceImport;
