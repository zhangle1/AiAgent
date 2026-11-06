"use client";

import { useEffect, useRef, useState } from "react";
import { Loader2, RefreshCw, Trash2, X } from "lucide-react";
import { cleanupKnowledgeResources, previewKnowledgeCleanup } from "@/lib/knowledge-api";
import type { KnowledgeCleanupPreview } from "@/lib/knowledge-cleanup-types";
import styles from "./knowledge-workspace.module.css";

export function KnowledgeCleanupDialog({ onClose, onCleaned }: { onClose: () => void; onCleaned: (removed: number) => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [preview, setPreview] = useState<KnowledgeCleanupPreview | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [confirmed, setConfirmed] = useState(false);

  useEffect(() => {
    const element = dialog.current;
    const previous = document.activeElement;
    element?.showModal();
    return () => { element?.close(); if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError(""); setSelected(new Set()); setConfirmed(false);
    previewKnowledgeCleanup(controller.signal).then(value => {
      if (!controller.signal.aborted) setPreview(value);
    }).catch(ex => {
      if (!controller.signal.aborted) setError(ex instanceof Error ? ex.message : "扫描失败，请重试。");
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [revision]);

  const eligible = preview?.items.filter(item => item.can_cleanup).slice(0, 500) ?? [];
  const disabled = loading || saving || !!error;

  async function clean() {
    if (disabled || !preview || !confirmed || selected.size === 0) return;
    setSaving(true); setError("");
    try {
      const result = await cleanupKnowledgeResources([...selected], preview.snapshot_sha256);
      onCleaned(result.removed_count);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "清理失败，请重新扫描后重试。");
      setConfirmed(false);
    } finally { setSaving(false); }
  }

  return <dialog ref={dialog} onCancel={event => { event.preventDefault(); if (!saving) onClose(); }} aria-labelledby="knowledge-cleanup-title" className={`${styles.dialog} m-auto max-h-[calc(100dvh-32px)] w-[calc(100%-32px)] max-w-2xl overflow-hidden rounded-2xl border-0 bg-white p-0 text-slate-900 shadow-2xl`}>
    <div className="flex max-h-[calc(100dvh-32px)] flex-col">
      <header className="flex shrink-0 items-center gap-2 border-b border-slate-100 p-4 sm:p-5">
        <div className="min-w-0 flex-1"><h2 id="knowledge-cleanup-title" className="text-base font-semibold">清理失效记录</h2><p className="mt-1 text-xs text-slate-500">{preview?.includes_shared ? "扫描本人资料及共享资料，不包含其他人员的个人记录。" : "仅扫描本人的知识资料记录。"}</p></div>
        <button type="button" disabled={loading || saving} onClick={() => setRevision(value => value + 1)} className="inline-flex shrink-0 items-center gap-1 rounded-md border border-slate-200 px-2 py-1.5 text-xs disabled:opacity-50"><RefreshCw size={13} />重新扫描</button>
        <button type="button" aria-label="关闭清理失效记录" disabled={saving} onClick={onClose} className="shrink-0 rounded-md p-1.5 text-slate-500 disabled:opacity-50"><X size={17} /></button>
      </header>
      <div className="min-h-0 overflow-auto p-4 sm:p-5">
        <p className="rounded-lg bg-amber-50 p-3 text-xs leading-5 text-amber-900">仅移除勾选的失效知识库记录及其目录入口，保留磁盘文件、聊天历史和任务记录。迁移过存储目录时，也可先迁移资料并修正路径，再重新扫描。</p>
        {error && <p role="alert" className="mt-3 rounded-lg bg-rose-50 p-3 text-xs text-rose-700">{error}</p>}
        {loading ? <p role="status" className="flex items-center justify-center gap-2 py-12 text-sm text-slate-500"><Loader2 size={16} className="animate-spin" />正在扫描失效记录…</p> : preview && <>
          <div className="my-4 flex flex-wrap items-center justify-between gap-2 text-xs text-slate-500"><span>发现 {preview.items.length} 条失效记录，可清理 {preview.items.filter(item => item.can_cleanup).length} 条</span><label className="flex items-center gap-2"><input type="checkbox" aria-label="选择全部可清理记录" disabled={disabled || eligible.length === 0} checked={eligible.length > 0 && eligible.every(item => selected.has(item.document_id))} onChange={event => { setSelected(new Set(event.target.checked ? eligible.map(item => item.document_id) : [])); setConfirmed(false); }} />全选（每次最多 500 条）</label></div>
          <div className="divide-y divide-slate-100 rounded-lg border border-slate-200">{preview.items.map(item => <label key={item.document_id} className="flex items-start gap-3 p-3 text-xs">
            <input type="checkbox" aria-label={`清理 ${item.name}`} checked={selected.has(item.document_id)} disabled={disabled || !item.can_cleanup || selected.size >= 500 && !selected.has(item.document_id)} onChange={event => { setSelected(current => { const next = new Set(current); event.target.checked ? next.add(item.document_id) : next.delete(item.document_id); return next; }); setConfirmed(false); }} className="mt-0.5 shrink-0" />
            <span className="min-w-0 flex-1"><span className="block break-words font-medium text-slate-800">{item.name}</span><span className="mt-1 block break-all text-[10px] text-slate-400">{readableUri(item.uri)}</span><span className="mt-1 block leading-5 text-slate-600">{item.message}</span>{item.blocked_reason && <span className="mt-1 block text-amber-700">{item.blocked_reason}</span>}</span>
          </label>)}{!preview.items.length && <p className="py-12 text-center text-sm text-slate-500">没有需要清理的失效记录</p>}</div>
        </>}
      </div>
      <footer className="flex shrink-0 flex-wrap items-center justify-between gap-3 border-t border-slate-100 p-4 sm:p-5">
        <label className="flex items-center gap-2 text-xs text-slate-600"><input type="checkbox" aria-label="确认移除所选失效记录" checked={confirmed} disabled={disabled || selected.size === 0} onChange={event => setConfirmed(event.target.checked)} />我确认移除所选 {selected.size} 条失效记录</label>
        <button type="button" disabled={disabled || !confirmed || selected.size === 0} onClick={() => void clean()} className="inline-flex items-center gap-1.5 rounded-lg bg-rose-600 px-3 py-2 text-xs text-white disabled:opacity-40">{saving ? <Loader2 size={14} className="animate-spin" /> : <Trash2 size={14} />}确认清理</button>
      </footer>
    </div>
  </dialog>;
}

function readableUri(uri: string) { try { return decodeURIComponent(uri); } catch { return uri; } }
