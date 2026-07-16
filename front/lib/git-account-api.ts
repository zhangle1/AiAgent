export type GitProvider = "gitee" | "github";

export type GitAccount = {
  id: number;
  provider: GitProvider;
  display_name: string;
  username: string;
  email?: string | null;
  token_configured: boolean;
  is_active: boolean;
  updated_at?: string | null;
};

export type GitAccountPayload = {
  provider: GitProvider;
  display_name: string;
  username: string;
  email?: string;
  access_token?: string;
  is_active: boolean;
};

export type GitAccountTestResult = {
  status: "success" | "failed";
  summary: string;
  detail: string;
  tested_at: string;
};

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    cache: "no-store",
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers },
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(typeof payload.message === "string" ? payload.message : "Git account request failed.");
  return payload as T;
}

export async function listGitAccounts(): Promise<GitAccount[]> {
  return (await request<{ accounts: GitAccount[] }>("/api/v1/git-accounts/list")).accounts;
}

export async function createGitAccount(payload: GitAccountPayload): Promise<GitAccount> {
  return (await request<{ account: GitAccount }>("/api/v1/git-accounts", { method: "POST", body: JSON.stringify(payload) })).account;
}

export async function updateGitAccount(id: number, payload: GitAccountPayload): Promise<GitAccount> {
  return (await request<{ account: GitAccount }>(`/api/v1/git-accounts/${id}`, { method: "PUT", body: JSON.stringify(payload) })).account;
}

export async function activateGitAccount(id: number): Promise<GitAccount> {
  return (await request<{ account: GitAccount }>(`/api/v1/git-accounts/${id}/activate`, { method: "POST", body: "{}" })).account;
}

export async function deleteGitAccount(id: number): Promise<void> {
  await request<{ deleted: boolean }>(`/api/v1/git-accounts/${id}`, { method: "DELETE" });
}

export async function testGitAccount(id: number): Promise<GitAccountTestResult> {
  return (await request<{ result: GitAccountTestResult }>(`/api/v1/git-accounts/${id}/test`, { method: "POST", body: "{}" })).result;
}
