"use client";

import Link from "next/link";
import { Folder } from "lucide-react";
import { useEffect, useId, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import type { SessionSummary } from "@/lib/session-api";

function updatedLabel(timestamp: string) {
  const utc = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(timestamp) ? timestamp : timestamp + "Z";
  const minutes = Math.floor((Date.now() - Date.parse(utc)) / 60000);
  if (!Number.isFinite(minutes)) return "";
  if (minutes < 1) return "刚刚";
  if (minutes < 60) return `${minutes} 分钟前`;
  if (minutes < 1440) return `${Math.floor(minutes / 60)} 小时前`;
  return `${Math.floor(minutes / 1440)} 天前`;
}

export function SidebarSessionLink({ session, projectName, active, className, children }: {
  session: SessionSummary;
  projectName: string;
  active: boolean;
  className: string;
  children: ReactNode;
}) {
  const id = useId();
  const anchor = useRef<HTMLAnchorElement>(null);
  const tooltip = useRef<HTMLDivElement>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const [open, setOpen] = useState(false);
  function dismiss() {
    clearTimeout(timer.current);
    setOpen(false);
  }
  function schedule(visible: boolean) {
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setOpen(visible), 180);
  }
  function show() {
    if (!window.matchMedia("(min-width: 64rem)").matches) return;
    clearTimeout(timer.current);
    setOpen(true);
  }

  useEffect(() => () => clearTimeout(timer.current), []);
  useLayoutEffect(() => {
    if (!open || !anchor.current || !tooltip.current) return;
    const row = anchor.current.getBoundingClientRect();
    const sidebar = anchor.current.closest("aside");
    const side = sidebar?.getBoundingClientRect().right ?? row.right;
    const card = tooltip.current;
    card.style.left = Math.max(8, Math.min(side + 6, window.innerWidth - card.offsetWidth - 8)) + "px";
    card.style.top = Math.max(8, Math.min(row.top, window.innerHeight - card.offsetHeight - 8)) + "px";
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") dismiss(); };
    document.addEventListener("keydown", escape);
    sidebar?.addEventListener("scroll", dismiss, true);
    window.addEventListener("resize", dismiss);
    return () => {
      document.removeEventListener("keydown", escape);
      sidebar?.removeEventListener("scroll", dismiss, true);
      window.removeEventListener("resize", dismiss);
    };
  }, [open, session.title, projectName]);

  return <>
    <Link ref={anchor} href={`/chat?session=${encodeURIComponent(session.id)}`} className={className}
      aria-current={active ? "page" : undefined}
      aria-describedby={open ? id : undefined}
      onPointerEnter={event => { if (event.pointerType === "mouse" && window.matchMedia("(min-width: 64rem)").matches) schedule(true); }}
      onPointerLeave={() => schedule(false)}
      onFocus={event => { if (event.currentTarget.matches(":focus-visible")) show(); }}
      onBlur={dismiss}
      onClick={event => {
        if (event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) show();
      }}>
      {children}
    </Link>
    {open && createPortal(<div ref={tooltip} role="tooltip" id={id}
      onPointerEnter={() => clearTimeout(timer.current)} onPointerLeave={() => schedule(false)}
      className="fixed z-[65] hidden w-80 max-w-[calc(100vw-16px)] rounded-xl border border-slate-200 bg-white px-3 py-2 text-slate-800 shadow-lg lg:block">
      <div className="flex items-baseline justify-between gap-3">
        <span className="min-w-0 break-words text-sm font-semibold">{session.title}</span>
        <time dateTime={session.updated_at} className="shrink-0 text-xs text-slate-500">{updatedLabel(session.updated_at)}</time>
      </div>
      <div className="mt-1 flex items-center gap-1.5 text-xs text-slate-500"><Folder size={13} className="shrink-0"/><span className="break-all">{projectName}</span></div>
    </div>, document.body)}
  </>;
}
