export type CodexQuotaWindow = { used_percent: number; window_minutes: number | null; resets_at: number | null };
export type CodexQuota = {
  status: "available" | "not_logged_in" | "unsupported" | "unavailable";
  plan: string | null;
  account_hint: string | null;
  primary: CodexQuotaWindow | null;
  secondary: CodexQuotaWindow | null;
  updated_at: string;
  next_refresh_at: string;
};
