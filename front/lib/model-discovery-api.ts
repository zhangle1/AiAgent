import type { CatalogProfile } from "./settings-types";

export type DiscoveredModel = {
  model: string;
  name: string;
  context_window?: number | null;
  max_output_tokens?: number | null;
  max_output_tokens_source?: string | null;
  supports_images?: boolean | null;
  supports_tools?: boolean | null;
  context_window_source?: string | null;
};

export async function discoverModels(profile: CatalogProfile, service: "llm" | "embedding", signal?: AbortSignal) {
  const response = await fetch("/api/v1/settings/model-discovery", {
    method: "POST", signal,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ service, profile_id: profile.id, binding: profile.binding, api_style: profile.api_style, base_url: profile.base_url,
      api_key: profile.api_key, extra_headers: profile.extra_headers }),
  });
  if (!response.ok) throw new Error(`模型列表获取失败（HTTP ${response.status}），请检查地址、密钥和权限。`);
  return response.json() as Promise<{ models: DiscoveredModel[]; catalog_path: string }>;
}
