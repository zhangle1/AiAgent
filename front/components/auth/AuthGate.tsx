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
  const [sidebarCompact, setSidebarCompact] = useState(false);

  useEffect(() => {
    if (isAuthPage) { setReady(true); return; }
    setReady(false);
    void getAuthStatus().then((status) => {
      if (!status.authenticated) router.replace(`/login?next=${encodeURIComponent(pathname)}`);
      else setReady(true);
    }).catch(() => router.replace(`/login?next=${encodeURIComponent(pathname)}`));
  }, [isAuthPage, pathname, router]);

  useEffect(() => {
    const toggleSidebar = () => setSidebarCompact((current) => !current);
    window.addEventListener("aiagent:sidebar-toggle", toggleSidebar);
    return () => window.removeEventListener("aiagent:sidebar-toggle", toggleSidebar);
  }, []);

  if (isAuthPage) return <>{children}</>;
  if (!ready) return <main className="flex min-h-screen items-center justify-center text-sm text-zinc-500">正在验证登录状态…</main>;
  if (isDashboardWorkspace) return <>{children}</>;
  return <><AppSidebar compact={sidebarCompact} /><div className={`min-h-screen transition-[padding] duration-200 ${sidebarCompact ? "lg:pl-[72px]" : "lg:pl-[240px]"}`}>{children}</div></>;
}
