"use client";

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Loader2, X } from "lucide-react";
import { KnowledgeResourceTree, isGeneratedResource } from "@/components/knowledge/KnowledgeResourcePicker";
import { getKnowledgeResourceTree, uploadKnowledgeResources } from "@/lib/knowledge-api";
import { getProjectMarkdownReferenceFile } from "@/lib/code-repository-api";
import type { KnowledgeResourceNode } from "@/lib/knowledge-types";

export function SaveMarkdownToKnowledgeDialog({ projectId, reference, onClose, onSaved }: {
  projectId: number; reference: string; onClose: () => void; onSaved: (message: string) => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const active = useRef(true);
  const controller = useRef<AbortController | null>(null);
  const saving = useRef(false);
  const [nodes, setNodes] = useState<KnowledgeResourceNode[]>([]);
  const [target, setTarget] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    active.current = true;
    const previous = document.activeElement;
    const element = dialog.current;
    const abort = new AbortController();
    controller.current = abort;
    element?.showModal();
    void getKnowledgeResourceTree(abort.signal).then(tree => {
      if (active.current) setNodes(tree.filter(node => node.kind === "directory" && !isGeneratedResource(node.uri) && node.uri.startsWith("viking://projects/") && node.uri !== "viking://projects/"));
    }).catch(ex => { if (active.current && !abort.signal.aborted) setError(ex instanceof Error ? ex.message : "加载目录失败"); })
      .finally(() => { if (active.current) setLoading(false); });
    return () => { active.current = false; abort.abort(); element?.close(); if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); };
  }, []);

  async function save() {
    if (saving.current || !nodes.some(node => node.uri === target)) return;
    saving.current = true; setBusy(true); setError("");
    try {
      const file = await getProjectMarkdownReferenceFile(projectId, reference, controller.current?.signal);
      if (!active.current) return;
      const result = await uploadKnowledgeResources(target, [file], false);
      if (!active.current) return;
      const item = result.items[0];
      if (!item || item.status !== "imported") throw new Error(item?.message || "文件未保存，请重试。");
      onSaved(`已保存到知识库：${decodeURI(target)}${result.warnings.length ? `（${result.warnings.join("；")}）` : ""}`);
      onClose();
    } catch (ex) { if (active.current) setError(ex instanceof Error ? ex.message : "保存失败，请重试。"); }
    finally { saving.current = false; if (active.current) setBusy(false); }
  }

  return createPortal(<dialog ref={dialog} onCancel={event => { if (saving.current) event.preventDefault(); else onClose(); }} aria-label="保存 Markdown 到知识库" className="m-auto max-h-[calc(100dvh-32px)] w-[calc(100%-32px)] max-w-xl overflow-auto rounded-2xl border-0 bg-white p-5 text-sm text-slate-800 shadow-2xl backdrop:bg-slate-950/35">
    <header className="mb-4 flex items-center justify-between"><h2 className="font-semibold">保存 Markdown 到知识库</h2><button type="button" disabled={busy} onClick={onClose} aria-label="关闭" className="p-1"><X size={18}/></button></header>
    <p className="mb-3 text-xs text-slate-500">选择知识库项目或其下目录。保存原文件，不自动解析或编译。</p>
    {loading ? <p role="status">正在加载项目目录…</p> : <KnowledgeResourceTree nodes={nodes} selected={[target]} onSelect={uri => { if (!saving.current) setTarget(uri); }} directoriesOnly label="知识库项目目录"/>}
    {!loading && !nodes.length && !error && <p className="mt-3 text-xs">暂无项目目录，请先在知识库创建项目目录。</p>}
    <p className="my-3 break-all text-xs">{target ? decodeURI(target) : "请选择保存位置"}</p>
    {error && <p role="alert" className="my-3 text-rose-700">{error}</p>}
    <footer className="flex justify-end gap-2"><button type="button" disabled={busy} onClick={onClose} className="rounded-lg border px-3 py-2">取消</button><button type="button" disabled={loading || busy || !target} onClick={() => void save()} className="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-3 py-2 text-white disabled:opacity-40">{busy && <Loader2 size={14} className="animate-spin"/>}{busy ? "保存中…" : "保存 Markdown"}</button></footer>
  </dialog>, document.body);
}
