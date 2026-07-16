export type ServiceName =
  | "llm"
  | "embedding"
  | "search"
  | "tts"
  | "stt"
  | "imagegen"
  | "videogen";

export type CatalogModel = {
  id: string;
  name: string;
  model: string;
  dimension?: string;
  send_dimensions?: boolean;
  supported_dimensions?: string;
  context_window?: string;
  context_window_source?: string;
  context_window_detected_at?: string;
  voice?: string;
  response_format?: string;
  language?: string;
  size?: string;
  quality?: string;
  style?: string;
  aspect_ratio?: string;
  duration?: string;
  resolution?: string;
};

export type CatalogProfile = {
  id: string;
  name: string;
  binding?: string | null;
  provider?: string | null;
  base_url: string;
  api_key: string;
  api_version: string;
  extra_headers?: Record<string, string>;
  proxy?: string | null;
  max_results?: number | null;
  models: CatalogModel[];
};

export type CatalogService = {
  active_profile_id: string | null;
  active_model_id?: string | null;
  profiles: CatalogProfile[];
};

export type Catalog = {
  version: number;
  services: Record<ServiceName, CatalogService>;
};

export type ProviderOption = {
  value: string;
  label: string;
  base_url?: string;
  default_model?: string;
  default_dim?: string;
};

export type UiSettings = {
  theme: string;
  language: string;
};

export type SettingsResponse = {
  ui: UiSettings;
  catalog: Catalog;
  providers: Record<ServiceName, ProviderOption[]>;
};

export type TestResult = {
  state: "success" | "failed";
  message: string;
  summary?: string;
  logs?: string[];
  profile_id?: string | null;
  model_id?: string | null;
  detected_dimension?: number | null;
  supported_dimensions?: string | null;
  catalog?: Catalog | null;
  tested_at?: string;
};

export function cloneCatalog(catalog: Catalog): Catalog {
  return JSON.parse(JSON.stringify(catalog)) as Catalog;
}

export function activeProfile(catalog: Catalog, service: ServiceName): CatalogProfile | null {
  const target = catalog.services[service];
  return target.profiles.find((profile) => profile.id === target.active_profile_id) ?? target.profiles[0] ?? null;
}

export function activeModel(catalog: Catalog, service: ServiceName): CatalogModel | null {
  if (service === "search") return null;
  const target = catalog.services[service];
  const profile = activeProfile(catalog, service);
  return profile?.models.find((model) => model.id === target.active_model_id) ?? profile?.models[0] ?? null;
}

export function serviceConfigured(catalog: Catalog, service: ServiceName): boolean {
  const profile = activeProfile(catalog, service);
  if (service === "search") {
    return Boolean(profile?.provider && profile.provider !== "none");
  }
  return Boolean(activeModel(catalog, service)?.model);
}
