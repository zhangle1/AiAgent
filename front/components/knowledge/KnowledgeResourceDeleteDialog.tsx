"use client";

import { useEffect, useRef, useState } from "react";
import { Loader2, Trash2, X } from "lucide-react";
import { deleteKnowledgeResource } from "@/lib/knowledge-api";
import type { KnowledgeResourceNode } from "@/lib/knowledge-types";
import styles from "./knowledge-workspace.module.css";

export function KnowledgeResourceDeleteDialog({ node, onClose, onDeleted }: { node: KnowledgeResourceNode; onClose: () => void; onDeleted: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const cancelButton = useRef<HTMLButtonElement>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    const element = dialog.current;
    const previous = document.activeElement;
    element?.showModal();
    cancelButton.current?.focus();
    return () => { element?.close(); if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); };
  }, []);
  async function remove() {
    if (saving || !node.document_id) return;
    setSaving(true); setError("");
    try {
      const result = await deleteKnowledgeResource(node.uri, node.document_id);
      if (!result.deleted || result.document_id !== node.document_id || result.uri !== node.uri) throw new Error("删除结果与当前文件不一致，请刷新目录后重试。");
      onDeleted();
    } catch (ex) { setError(ex instanceof Error ? ex.message : "删除失败，请重试。"); setSaving(false); }
  }
  return <dialog ref={dialog} onCancel={event => { event.preventDefault(); if (!saving) onClose(); }} aria-labelledby="knowledge-delete-title" className={`${styles.dialog} m-auto max-h-[calc(100dvh-32px)] w-[calc(100%-32px)] max-w-md overflow-y-auto rounded-2xl border-0 bg-white p-0 text-slate-900 shadow-2xl`}>
    <header className="flex items-center gap-3 border-b border-slate-100 p-5"><h2 id="knowledge-delete-title" className="flex-1 text-base font-semibold">删除资料</h2><button type="button" aria-label="关闭删除资料" disabled={saving} onClick={onClose} className="rounded p-1 text-slate-500 disabled:opacity-40"><X size={18} /></button></header>
    <div className="space-y-3 p-5"><p className="text-sm text-slate-600">确认从资料库移除以下文件？</p><p className="break-all rounded-lg bg-slate-50 p-3 text-sm font-medium">{node.name}</p><p className="text-xs leading-5 text-slate-500">文件及其解析章节、图片将从目录隐藏。磁盘原文件、聊天和任务历史保留，不会删除原始会话。</p>{error && <p role="alert" className="rounded-lg bg-rose-50 p-3 text-xs leading-5 text-rose-700">{error}</p>}</div>
    <footer className="flex justify-end gap-2 border-t border-slate-100 p-4"><button ref={cancelButton} type="button" disabled={saving} onClick={onClose} className="rounded-lg border border-slate-200 px-3 py-2 text-xs disabled:opacity-40">取消</button><button type="button" disabled={saving} onClick={() => void remove()} className="inline-flex items-center gap-1.5 rounded-lg bg-rose-600 px-3 py-2 text-xs text-white disabled:opacity-40">{saving ? <Loader2 size={14} className="animate-spin" /> : <Trash2 size={14} />}确认删除</button></footer>
  </dialog>;
}
