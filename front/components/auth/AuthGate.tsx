"use client";

import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { Menu } from "lucide-react";
import { getAuthStatus, type AuthStatus } from "@/lib/auth-api";
import { buildLoginRedirect } from "@/lib/auth-redirect";
import { ChatStreamProvider } from "@/components/chat/ChatStreamProvider";
import { AppSidebar } from "@/components/layout/AppSidebar";

const DEFAULT_SIDEBAR_WIDTH = 296;
const MIN_SIDEBAR_WIDTH = 240;
const MAX_SIDEBAR_WIDTH = 520;
const SIDEBAR_WIDTH_KEY = "aiagent:workspace-sidebar-width";

function clampSidebarWidth(width: number) {
  return Math.min(MAX_SIDEBAR_WIDTH, Math.max(MIN_SIDEBAR_WIDTH, Math.round(width)));
}

export function AuthGate({ children }: { children: ReactNode }) {
  const pathname = usePathname() ?? "/";
  const router = useRouter();
  const isAuthPage = pathname === "/login" || pathname === "/register";
  const isPublicPrototypePage = pathname === "/prototype-share";
  const isDashboardWorkspace = /^\/dashboard-applications\/[^/]+$/.test(pathname);
  const [ready, setReady] = useState(false);
  const [authStatus, setAuthStatus] = useState<AuthStatus | null>(null);
  const [sidebarCompact, setSidebarCompact] = useState(false);
  const [sidebarWidth, setSidebarWidth] = useState(DEFAULT_SIDEBAR_WIDTH);

  useEffect(() => {
    setReady(false);
    if (isAuthPage || isPublicPrototypePage) { setAuthStatus(null); return; }
    let current = true;
    const requireLogin = () => {
      if (!current) return;
      setAuthStatus(null);
      router.replace(buildLoginRedirect(pathname));
    };
    void getAuthStatus().then((status) => {
      if (!current) return;
      if (!status.authenticated) { requireLogin(); return; }
      setAuthStatus(status);
      setReady(true);
    }).catch(requireLogin);
    return () => { current = false; };
  }, [isAuthPage, isPublicPrototypePage, pathname, router]);

  useEffect(() => {
    const toggleSidebar = () => setSidebarCompact((current) => !current);
    window.addEventListener("aiagent:sidebar-toggle", toggleSidebar);
    return () => window.removeEventListener("aiagent:sidebar-toggle", toggleSidebar);
  }, []);

  useEffect(() => {
    try {
      const saved = Number(localStorage.getItem(SIDEBAR_WIDTH_KEY));
      if (Number.isFinite(saved) && saved > 0) setSidebarWidth(clampSidebarWidth(saved));
    } catch {
      // Resizing still works when browser storage is unavailable.
    }
  }, []);

  if (isAuthPage || isPublicPrototypePage) return <>{children}</>;
  const loading = <main className="flex min-h-screen items-center justify-center text-sm text-zinc-500">正在验证登录状态…</main>;
  if (!authStatus?.authenticated) return loading;
  const contentHeight = pathname === "/chat" ? "chat-viewport" : "min-h-screen";
  const layoutStyle = { "--workspace-sidebar-width": `${sidebarWidth}px` } as CSSProperties;
  // Keep active streams mounted during protected-page navigation and begin recovery only after login.
  let content: ReactNode = loading;
  if (ready) {
    content = isDashboardWorkspace || pathname === "/runtime-test" ? children : <div className="contents" style={layoutStyle}>
      <AppSidebar compact={sidebarCompact} authStatus={authStatus}
        resizeHandle={!sidebarCompact && <SidebarResizeHandle width={sidebarWidth} onResize={setSidebarWidth} />} />
      {pathname !== "/chat" && <MobileWorkspaceLauncher />}
      <div className={`${contentHeight} pl-0 ${sidebarCompact ? "lg:pl-14" : "lg:pl-[var(--workspace-sidebar-width)]"}`}>{children}</div>
    </div>;
  }
  return <ChatStreamProvider key={authStatus.user_id ?? authStatus.username}>{content}</ChatStreamProvider>;
}

function SidebarResizeHandle({ width, onResize }: { width: number; onResize: (width: number) => void }) {
  const drag = useRef<{ pointerId: number; startX: number; startWidth: number; width: number } | null>(null);
  const [dragging, setDragging] = useState(false);

  function save(next: number) {
    try { localStorage.setItem(SIDEBAR_WIDTH_KEY, String(next)); } catch { /* Browser storage is optional. */ }
  }

  return <div role="separator" aria-orientation="vertical" aria-label="调整侧边栏宽度" aria-controls="workspace-sidebar-panel"
    aria-valuemin={MIN_SIDEBAR_WIDTH} aria-valuemax={MAX_SIDEBAR_WIDTH} aria-valuenow={width} tabIndex={0}
    className={`absolute inset-y-0 -right-1 z-10 hidden w-2 touch-none cursor-col-resize select-none outline-none focus-visible:bg-blue-300/50 hover:bg-blue-300/40 lg:block ${dragging ? "bg-blue-300/50" : ""}`}
    onPointerDown={(event) => {
      if (event.button !== 0 || drag.current) return;
      event.stopPropagation();
      event.currentTarget.setPointerCapture(event.pointerId);
      drag.current = { pointerId: event.pointerId, startX: event.clientX, startWidth: width, width };
      setDragging(true);
    }}
    onPointerMove={(event) => {
      const current = drag.current;
      if (!current || event.pointerId !== current.pointerId) return;
      current.width = clampSidebarWidth(current.startWidth + event.clientX - current.startX);
      onResize(current.width);
    }}
    onLostPointerCapture={(event) => {
      const current = drag.current;
      if (!current || event.pointerId !== current.pointerId) return;
      drag.current = null;
      setDragging(false);
      save(current.width);
    }}
    onKeyDown={(event) => {
      const step = event.shiftKey ? 48 : 16;
      const widths: Partial<Record<string, number>> = { ArrowLeft: width - step, ArrowRight: width + step, Home: MIN_SIDEBAR_WIDTH, End: MAX_SIDEBAR_WIDTH };
      const target = widths[event.key];
      if (target === undefined) return;
      event.preventDefault();
      const next = clampSidebarWidth(target);
      onResize(next);
      save(next);
    }}
  />;
}

function MobileWorkspaceLauncher() {
  return <button type="button" onClick={() => window.dispatchEvent(new Event("aiagent:mobile-drawer-toggle"))} className="fixed left-4 top-4 z-40 grid h-12 w-12 place-items-center rounded-2xl border border-slate-200/90 bg-white/95 text-slate-700 shadow-[0_10px_28px_rgba(15,23,42,0.16)] backdrop-blur transition hover:border-blue-200 hover:bg-blue-50 hover:text-blue-700 active:scale-95 lg:hidden" aria-label="打开工作台菜单" title="工作台菜单"><Menu size={22}/></button>;
}
