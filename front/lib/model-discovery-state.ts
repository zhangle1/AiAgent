import { cloneCatalog, type Catalog, type CatalogModel, type CatalogProfile, type ServiceName } from "./settings-types";

export function modelProfileSignature(profile: CatalogProfile): string {
  return JSON.stringify([profile.id, profile.binding, profile.api_style, profile.base_url, profile.api_key, profile.extra_headers]);
}

export function createCatalogOperationQueue() {
  let tail = Promise.resolve();
  return function enqueue<T>(operation: () => Promise<T>): Promise<T> {
    const result = tail.then(operation);
    tail = result.then(() => undefined, () => undefined);
    return result;
  };
}

export function mergeDiscoveredModels(current: Catalog, service: ServiceName, profileId: string,
  models: CatalogModel[], expectedSignature: string): Catalog | null {
  const next = cloneCatalog(current);
  const profile = next.services[service].profiles.find(item => item.id === profileId);
  if (!profile || modelProfileSignature(profile) !== expectedSignature) return null;
  const automatic = (source?: string) => source === "endpoint" || source === "model.json";
  const pinned = (source?: string) => !!source && !automatic(source);
  for (const discovered of models) {
    const existing = profile.models.find(item => item.model === discovered.model);
    if (!existing) { profile.models.push({ ...discovered }); continue; }
    const keepContext = existing.context_window && pinned(existing.context_window_source);
    const keepOutput = existing.max_output_tokens && pinned(existing.max_output_tokens_source);
    Object.assign(existing, { ...discovered, id: existing.id, name: existing.name || discovered.name,
      ...(keepContext ? { context_window: existing.context_window, context_window_source: existing.context_window_source, context_window_detected_at: existing.context_window_detected_at } : {}),
      ...(keepOutput ? { max_output_tokens: existing.max_output_tokens, max_output_tokens_source: existing.max_output_tokens_source } : {}) });
  }
  const available = profile.models.filter(item => item.model.trim());
  if (!available.some(item => item.id === next.services[service].active_model_id))
    next.services[service].active_model_id = available[0]?.id ?? null;
  return next;
}
