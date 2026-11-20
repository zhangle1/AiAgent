"use client";

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Loader2, X } from "lucide-react";
import { KnowledgeResourceTree, isGeneratedResource } from "@/components/knowledge/KnowledgeResourcePicker";
import { getKnowledgeResourceTree, uploadKnowledgeResources, type KnowledgeChapterReference } from "@/lib/knowledge-api";
import { createKnowledgeChapterCopy } from "@/lib/knowledge-chapter-copy";
import { getAuthStatus } from "@/lib/auth-api";
import { getProjectMarkdownReferenceFile } from "@/lib/code-repository-api";
import type { KnowledgeResourceNode } from "@/lib/knowledge-types";

export function SaveMarkdownToKnowledgeDialog({ source, onClose, onSaved }: {
  source: { projectId: number; reference: string } | { chapter: KnowledgeChapterReference };
  onClose: () => void; onSaved: (message: string) => void;
}) {
  const isChapter = "chapter" in source;
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
    void Promise.all([getKnowledgeResourceTree(abort.signal), isChapter ? getAuthStatus() : null]).then(([tree, account]) => {
      if (abort.signal.aborted) return;
      const projects = "viking://projects/";
      const people = "viking://user/";
      const owner = account?.username?.trim() || account?.user_id;
      setNodes(tree.filter(({ kind, uri }) => {
        if (kind !== "directory" || isGeneratedResource(uri)) return false;
        if (uri.startsWith(projects)) return uri !== projects;
        if (!isChapter) return false;
        if (uri.startsWith("viking://resources/")) return true;
        return Boolean(account?.authenticated && owner && uri.startsWith(people) &&
          decodeURIComponent(uri.slice(people.length).split("/")[0]) === owner);
      }));
    }).catch(ex => { if (!abort.signal.aborted) setError(ex instanceof Error ? ex.message : "加载目录失败"); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => { active.current = false; abort.abort(); element?.close(); if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); };
  }, [isChapter]);

  async function save() {
    if (saving.current || !nodes.some(node => node.uri === target)) return;
    saving.current = true; setBusy(true); setError("");
    try {
      const signal = controller.current?.signal;
      const file = "chapter" in source ? await createKnowledgeChapterCopy(source.chapter, signal)
        : await getProjectMarkdownReferenceFile(source.projectId, source.reference, signal);
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
    <p className="mb-3 text-xs text-slate-500">{isChapter ? "选择个人、项目或共享资料目录。章节和插图将保存到同一个 Markdown 文件。" : "选择知识库项目或其下目录。"}</p>
    {loading ? <p role="status">正在加载目录…</p> : <KnowledgeResourceTree nodes={nodes} selected={[target]} onSelect={uri => { if (!saving.current) setTarget(uri); }} directoriesOnly label="知识库目录"/>}
    {!loading && !nodes.length && !error && <p className="mt-3 text-xs">暂无可选目录。</p>}
    <p className="my-3 break-all text-xs">{target ? decodeURI(target) : "请选择保存位置"}</p>
    {error && <p role="alert" className="my-3 text-rose-700">{error}</p>}
    <footer className="flex justify-end gap-2"><button type="button" disabled={busy} onClick={onClose} className="rounded-lg border px-3 py-2">取消</button><button type="button" disabled={loading || busy || !target} onClick={() => void save()} className="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-3 py-2 text-white disabled:opacity-40">{busy && <Loader2 size={14} className="animate-spin"/>}{busy ? "保存中…" : "保存 Markdown"}</button></footer>
  </dialog>, document.body);
}
