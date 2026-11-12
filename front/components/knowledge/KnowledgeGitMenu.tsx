"use client";

import { useEffect, useId, useRef, useState } from "react";
import { createPortal } from "react-dom";
import Link from "next/link";
import { ChevronDown, GitBranch, Loader2, X } from "lucide-react";
import { KnowledgeGitSync } from "./KnowledgeGitSync";
import { getKnowledgeGit } from "@/lib/knowledge-git-api";
import type { KnowledgeGitState } from "@/lib/knowledge-git-types";

export function KnowledgeGitMenu() {
  const id = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState<number | null>(null);
  const [state, setState] = useState<KnowledgeGitState | null>(null);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  function close() { if (!busy) { setOpen(false); trigger.current?.focus(); } }
  useEffect(() => {
    if (!open) return;
    let disposed = false; setState(null); setError(""); setPending(null);
    getKnowledgeGit().then(value => { if (!disposed) setState(value); }).catch(ex => { if (!disposed) setError(ex instanceof Error ? ex.message : "读取同步设置失败"); });
    return () => { disposed = true; };
  }, [open, revision]);
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => { if (!panel.current?.contains(event.target as Node) && !trigger.current?.contains(event.target as Node)) close(); };
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") { event.preventDefault(); close(); } };
    document.addEventListener("pointerdown", outside); document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("pointerdown", outside); document.removeEventListener("keydown", escape); };
  }, [open, busy]);
  useEffect(() => { if (open) panel.current?.focus(); }, [open]);
  return <>
    <button ref={trigger} type="button" aria-label="知识 Git 提交推送" aria-haspopup="dialog" aria-expanded={open} aria-controls={open ? id : undefined} disabled={busy} onClick={() => { if (open) close(); else { setState(null); setError(""); setPending(null); setOpen(true); } }} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-emerald-200 bg-emerald-50 px-2.5 text-xs text-emerald-700 hover:border-emerald-400 disabled:opacity-60">
      {busy ? <Loader2 size={13} className="animate-spin" /> : <GitBranch size={13} />}Git{pending !== null && <span className="rounded bg-emerald-100 px-1 tabular-nums">{pending}</span>}<ChevronDown size={12} className={open ? "rotate-180" : ""} />
    </button>
    {open && createPortal(<>
      <button type="button" aria-label="关闭知识 Git 面板背景" disabled={busy} onClick={close} className="fixed inset-0 z-[85] bg-slate-950/35 lg:hidden" />
      <div ref={panel} id={id} role="dialog" aria-label="知识 Git 提交推送" tabIndex={-1} className="fixed inset-x-0 bottom-0 z-[90] box-border max-h-[84dvh] w-full overflow-y-auto overscroll-contain rounded-t-2xl border border-slate-200 bg-white p-4 pb-[calc(env(safe-area-inset-bottom)+1rem)] text-slate-900 shadow-xl outline-none lg:inset-x-auto lg:bottom-auto lg:right-5 lg:top-20 lg:max-h-[calc(100dvh-6rem)] lg:w-[390px] lg:rounded-xl lg:p-4">
        <header className="mb-3 flex items-center justify-between gap-3"><h2 className="text-sm font-semibold">知识 Git</h2><div className="flex items-center gap-2"><Link href="/settings/knowledge#team-git" aria-disabled={busy} onClick={event => { if (busy) event.preventDefault(); }} className="text-[11px] text-blue-600">同步设置</Link><button type="button" aria-label="关闭知识 Git 面板" disabled={busy} onClick={close} className="rounded p-1 text-slate-400 hover:bg-slate-100 disabled:opacity-40"><X size={15} /></button></div></header>
        {error ? <div role="alert" className="text-xs text-red-700">{error}<button type="button" onClick={() => setRevision(value => value + 1)} className="ml-2 underline">重新加载</button></div>
          : !state ? <p role="status" className="text-xs text-slate-500">正在读取同步设置…</p>
          : <KnowledgeGitSync state={state} autoDetect onBusyChange={setBusy} onPendingChange={setPending} />}
      </div>
    </>, document.body)}
  </>;
}
