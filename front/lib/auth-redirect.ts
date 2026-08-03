const AUTH_PATHS = new Set(["/login", "/register"]);

export function buildLoginRedirect(currentPath: string): string {
  const target = normalizeReturnPath(currentPath);
  return target ? `/login?next=${encodeURIComponent(target)}` : "/login";
}

export function resolvePostLoginPath(next: string | null): string {
  return normalizeReturnPath(next) || "/chat";
}

function normalizeReturnPath(value: string | null | undefined): string | null {
  if (!value || !value.startsWith("/") || value.startsWith("//")) return null;
  try {
    const target = new URL(value, "https://aiagent.local");
    if (target.origin !== "https://aiagent.local" || AUTH_PATHS.has(target.pathname)) return null;
    return `${target.pathname}${target.search}${target.hash}`;
  } catch {
    return null;
  }
}
