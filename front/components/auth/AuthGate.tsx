"use client";

import { useEffect, useState, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { Menu } from "lucide-react";
import { getAuthStatus } from "@/lib/auth-api";
import { buildLoginRedirect } from "@/lib/auth-redirect";
import { AppSidebar } from "@/components/layout/AppSidebar";
import { FrontendOnboarding } from "@/components/onboarding/FrontendOnboarding";

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
      if (!status.authenticated) router.replace(buildLoginRedirect(pathname));
      else setReady(true);
    }).catch(() => router.replace(buildLoginRedirect(pathname)));
  }, [isAuthPage, pathname, router]);

  useEffect(() => {
    const toggleSidebar = () => setSidebarCompact((current) => !current);
    window.addEventListener("aiagent:sidebar-toggle", toggleSidebar);
    return () => window.removeEventListener("aiagent:sidebar-toggle", toggleSidebar);
  }, []);

  if (isAuthPage) return <>{children}</>;
  if (!ready) return <main className="flex min-h-screen items-center justify-center text-sm text-zinc-500">正在验证登录状态…</main>;
  if (isDashboardWorkspace) return <>{children}</>;
  const contentHeight = pathname === "/chat" ? "chat-viewport" : "min-h-screen";
  return <><AppSidebar compact={sidebarCompact} />{pathname !== "/chat" && <MobileWorkspaceLauncher />}<div className={`${contentHeight} pl-0 transition-[padding] duration-200 ${sidebarCompact ? "lg:pl-[72px]" : "lg:pl-[240px]"}`}>{children}</div><FrontendOnboarding /></>;
}

function MobileWorkspaceLauncher() {
  return <button type="button" onClick={() => window.dispatchEvent(new Event("aiagent:mobile-drawer-toggle"))} className="fixed left-4 top-4 z-40 grid h-12 w-12 place-items-center rounded-2xl border border-slate-200/90 bg-white/95 text-slate-700 shadow-[0_10px_28px_rgba(15,23,42,0.16)] backdrop-blur transition hover:border-blue-200 hover:bg-blue-50 hover:text-blue-700 active:scale-95 lg:hidden" aria-label="打开工作台菜单" title="工作台菜单"><Menu size={22}/></button>;
}
