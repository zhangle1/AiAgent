export type UsageProviderSummary = {
  provider_kind: "builtin" | "third_party" | string;
  provider_id: string;
  model?: string | null;
  total_tokens: number;
  prompt_tokens: number;
  completion_tokens: number;
  turn_count: number;
  estimated_turn_count: number;
};

export type UsageActivityDay = { date: string; total_tokens: number; turn_count: number };

export type UsageSummary = {
  scope: "me" | "all";
  can_view_all: boolean;
  period_days: number;
  from: string;
  to: string;
  total_tokens: number;
  prompt_tokens: number;
  completion_tokens: number;
  turn_count: number;
  estimated_turn_count: number;
  providers: UsageProviderSummary[];
  activity: UsageActivityDay[];
};

export type UsageDayDetail = {
  scope: "me" | "all";
  can_view_all: boolean;
  date: string;
  total_tokens: number;
  prompt_tokens: number;
  completion_tokens: number;
  turn_count: number;
  providers: UsageProviderSummary[];
};

async function parseJson<T>(response: Response): Promise<T> {
  const payload = await response.json().catch(() => null) as { message?: string } | null;
  if (!response.ok) throw new Error(payload?.message || `Request failed with HTTP ${response.status}`);
  return payload as T;
}

export async function getUsageSummary(options: { scope?: "me" | "all"; days?: number } = {}): Promise<UsageSummary> {
  const params = new URLSearchParams({ scope: options.scope ?? "me", days: String(options.days ?? 365) });
  return await parseJson<UsageSummary>(await fetch(`/api/v1/usage/summary?${params}`, { cache: "no-store" }));
}

export async function getUsageDayDetail(date: string, scope: "me" | "all" = "me"): Promise<UsageDayDetail> {
  const params = new URLSearchParams({ scope });
  return await parseJson<UsageDayDetail>(await fetch(`/api/v1/usage/days/${encodeURIComponent(date)}?${params}`, { cache: "no-store" }));
}
