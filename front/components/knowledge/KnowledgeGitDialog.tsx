"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { X } from "lucide-react";
import { KnowledgeGitSettings } from "@/components/settings/KnowledgeGitSettings";

export function KnowledgeGitDialog({ initialSessionUri, initialSessionUris, initialResourceUris, onClose }: { initialSessionUri?: string; initialSessionUris?: string[]; initialResourceUris?: string[]; onClose: () => void }) {
  const ref = useRef<HTMLDialogElement>(null);
  const selection = useRef({ initialSessionUri, initialSessionUris, initialResourceUris });
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    const dialog = ref.current;
    const previous = document.activeElement;
    dialog?.showModal();
    return () => { dialog?.close(); if (previous instanceof HTMLElement) previous.focus(); };
  }, []);
  return <dialog ref={ref} aria-label="知识 Git 提交推送" onCancel={event => { event.preventDefault(); if (!busy) onClose(); }}
    className="m-auto max-h-[calc(100dvh-2rem)] w-[calc(100%-2rem)] max-w-3xl overflow-y-auto rounded-xl bg-white p-0 text-slate-900 shadow-2xl backdrop:bg-slate-950/35">
    <header className="sticky top-0 z-10 flex items-center justify-between gap-3 border-b border-slate-200 bg-white px-4 py-3">
      <h2 className="text-sm font-semibold">知识 Git 提交推送</h2>
      <div className="flex items-center gap-3"><Link href="/settings/knowledge#team-git" aria-disabled={busy} onClick={event => { if (busy) event.preventDefault(); }} className="text-xs text-blue-600">同步设置</Link>
        <button type="button" aria-label="关闭推送弹窗" disabled={busy} onClick={onClose} className="rounded p-1.5 hover:bg-slate-100 disabled:opacity-40"><X size={17} /></button></div>
    </header>
    <KnowledgeGitSettings initialSessionUri={selection.current.initialSessionUri} initialSessionUris={selection.current.initialSessionUris} initialResourceUris={selection.current.initialResourceUris} onBusyChange={setBusy} compact />
  </dialog>;
}
