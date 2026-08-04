export type AgentProviderEnvironment = {
  id: "codex" | "codebuddy";
  name: string;
  command: string;
  installed: boolean;
  version?: string | null;
  protocol: string;
  chat_supported: boolean;
  message: string;
};

export type CodexModelOption = {
  id: string;
  name: string;
  description: string;
  model_id?: string | null;
  profile_name?: string | null;
  supports_reasoning_effort: boolean;
  is_builtin: boolean;
  image_input: "native" | "ocr" | "none";
};

export type CodexProfileModel = {
  display_name: string;
  profile_name: string;
  model_id?: string | null;
  description: string;
  supports_reasoning_effort: boolean;
  supports_image_ocr: boolean;
};

export type CodexModelPolicy = {
  models: CodexModelOption[];
  allowed_model_ids: string[];
  default_model_id: string;
  allow_chat_model_override: boolean;
  allowed_reasoning_efforts: string[];
  default_reasoning_effort: string;
  allow_chat_reasoning_effort_override: boolean;
  profile_models: CodexProfileModel[];
};

export type ImageOcrPolicy = {
  enabled: boolean;
  native_image_input_enabled: boolean;
  auto_process_images: boolean;
  language: "ch" | "en" | "japan" | "korean";
  max_image_bytes: number;
  max_prompt_characters: number;
  timeout_seconds: number;
};

export type ImageOcrDiagnostic = {
  ready: boolean;
  python_configured: boolean;
  worker_configured: boolean;
  paddle_version?: string | null;
  paddleocr_version?: string | null;
  error?: string | null;
  result?: {
    attachment_id: string;
    engine: string;
    language: string;
    text: string;
    confidence?: number | null;
    elapsed_ms: number;
    from_cache: boolean;
    truncated: boolean;
  } | null;
};

async function parseJson<T>(response: Response): Promise<T> {
  const payload = await response.json().catch(() => null) as { message?: string } | null;
  if (!response.ok) throw new Error(payload?.message || `Request failed with HTTP ${response.status}`);
  return payload as T;
}

export async function getAgentProviderEnvironments(): Promise<AgentProviderEnvironment[]> {
  return parseJson<AgentProviderEnvironment[]>(await fetch("/api/v1/agent-providers/environments", { cache: "no-store" }));
}

export async function getCodexModelPolicy(): Promise<CodexModelPolicy> {
  return parseJson<CodexModelPolicy>(await fetch("/api/v1/agent-providers/codex-model-policy", { cache: "no-store" }));
}

export async function updateCodexModelPolicy(payload: Pick<CodexModelPolicy, "allowed_model_ids" | "default_model_id" | "allow_chat_model_override" | "allowed_reasoning_efforts" | "default_reasoning_effort" | "allow_chat_reasoning_effort_override" | "profile_models">): Promise<CodexModelPolicy> {
  return parseJson<CodexModelPolicy>(await fetch("/api/v1/agent-providers/codex-model-policy", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  }));
}

export async function getImageOcrPolicy(): Promise<ImageOcrPolicy> {
  return parseJson<ImageOcrPolicy>(await fetch("/api/v1/agent-providers/image-ocr-policy", { cache: "no-store" }));
}

export async function updateImageOcrPolicy(payload: ImageOcrPolicy): Promise<ImageOcrPolicy> {
  return parseJson<ImageOcrPolicy>(await fetch("/api/v1/agent-providers/image-ocr-policy", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  }));
}

export async function diagnoseImageOcr(attachmentId?: string): Promise<ImageOcrDiagnostic> {
  return parseJson<ImageOcrDiagnostic>(await fetch("/api/v1/agent-providers/image-ocr-diagnostics", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ attachment_id: attachmentId || null }),
  }));
}
