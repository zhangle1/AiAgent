export type AuthStatus = { authenticated: boolean; user_id?: string; username?: string; is_admin?: boolean; registration_enabled?: boolean };

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { ...init, headers: { "Content-Type": "application/json", ...init?.headers } });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(typeof payload.message === "string" ? payload.message : "请求失败，请稍后重试。");
  return payload as T;
}

export function getAuthStatus() { return request<AuthStatus>("/api/v1/auth/status"); }
export function login(username: string, password: string) { return request<{ ok: true }>("/api/v1/auth/login", { method: "POST", body: JSON.stringify({ username, password }) }); }
export function register(username: string, password: string) { return request<{ ok: true }>("/api/v1/auth/register", { method: "POST", body: JSON.stringify({ username, password }) }); }
export function logout() { return request<{ ok: true }>("/api/v1/auth/logout", { method: "POST", body: "{}" }); }
