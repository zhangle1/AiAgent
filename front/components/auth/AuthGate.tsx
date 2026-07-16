"use client";

import { useEffect, useState, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { getAuthStatus } from "@/lib/auth-api";
import { AppSidebar } from "@/components/layout/AppSidebar";

export function AuthGate({ children }: { children: ReactNode }) {
  const pathname = usePathname() ?? "/";
  const router = useRouter();
  const isAuthPage = pathname === "/login" || pathname === "/register";
  const isDashboardWorkspace = /^\/dashboard-applications\/[^/]+$/.test(pathname);
  const [ready, setReady] = useState(isAuthPage);

  useEffect(() => {
    if (isAuthPage) { setReady(true); return; }
    setReady(false);
    void getAuthStatus().then((status) => {
      if (!status.authenticated) router.replace(`/login?next=${encodeURIComponent(pathname)}`);
      else setReady(true);
    }).catch(() => router.replace(`/login?next=${encodeURIComponent(pathname)}`));
  }, [isAuthPage, pathname, router]);

  if (isAuthPage) return <>{children}</>;
  if (!ready) return <main className="flex min-h-screen items-center justify-center text-sm text-zinc-500">正在验证登录状态…</main>;
  if (isDashboardWorkspace) return <>{children}</>;
  return <><AppSidebar /><div className="min-h-screen lg:pl-[220px]">{children}</div></>;
}
