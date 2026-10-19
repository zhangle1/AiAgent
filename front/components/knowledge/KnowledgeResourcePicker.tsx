"use client";

import { useRef, useEffect, useState } from "react";
import { ChevronRight, FileText, Folder, Search, X } from "lucide-react";
import type { KnowledgeResourceNode } from "@/lib/knowledge-types";
import styles from "./knowledge-workspace.module.css";

export function isGeneratedResource(uri: string) { return uri.includes("/_parsed/") || uri.includes("/_compiled/") || /^viking:\/\/user\/[^/]+\/(summaries|wiki)(\/|$)/.test(uri); }

export function KnowledgeResourceTree({ nodes, selected, onSelect, directoriesOnly = false, multiple = false, label }: {
  nodes: KnowledgeResourceNode[]; selected: string[]; onSelect: (uri: string) => void; directoriesOnly?: boolean; multiple?: boolean; label: string;
}) {
  const [query, setQuery] = useState("");
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set(nodes.filter(n => n.kind === "directory").map(n => n.uri)));
  const visible = nodes.filter(n => !directoriesOnly || n.kind === "directory");
  const known = new Set(visible.map(n => n.uri));
  const matches = new Set(visible.filter(n => (n.name + " " + decodeURI(n.uri)).toLowerCase().includes(query.trim().toLowerCase())).map(n => n.uri));
  for (const uri of [...matches]) for (const node of visible) if (uri.startsWith(node.uri)) matches.add(node.uri);
  const children = (parent?: string) => visible.filter(n => (n.parent_uri === parent || !parent && (!n.parent_uri || !known.has(n.parent_uri))) && matches.has(n.uri));
  function row(node: KnowledgeResourceNode, depth: number) {
    const directory = node.kind === "directory", open = !!query || expanded.has(node.uri);
    return <li key={node.uri} role="none"><div className="flex min-w-0 items-center gap-1 rounded-md hover:bg-slate-50" style={{ paddingLeft: depth * 14 }}>
      {directory ? <button type="button" aria-label={`${open ? "收起" : "展开"} ${node.name}`} onClick={() => setExpanded(current => { const next = new Set(current); if (next.has(node.uri)) next.delete(node.uri); else next.add(node.uri); return next; })} className="shrink-0 p-1 text-slate-400"><ChevronRight size={12} className={open ? "rotate-90" : ""} /></button> : <span className="w-5 shrink-0" />}
      <button type="button" role="treeitem" aria-selected={selected.includes(node.uri)} aria-expanded={directory ? open : undefined} aria-level={depth + 1} disabled={multiple && directory}
        onClick={() => onSelect(node.uri)} title={decodeURI(node.uri)} className={`flex min-w-0 flex-1 items-center gap-2 rounded-md px-2 py-2 text-left text-xs disabled:opacity-70 ${selected.includes(node.uri) ? "bg-blue-50 text-blue-700" : "text-slate-700"}`}>
        {multiple && !directory && <span aria-hidden="true" className={`flex h-3.5 w-3.5 shrink-0 items-center justify-center rounded border ${selected.includes(node.uri) ? "border-blue-600 bg-blue-600 text-white" : "border-slate-300"}`}>{selected.includes(node.uri) ? "✓" : ""}</span>}
        {directory ? <Folder size={14} className="shrink-0 text-amber-500" /> : <FileText size={14} className="shrink-0 text-slate-400" />}<span className="truncate">{node.name}</span>
      </button></div>{directory && open && <ul role="group">{children(node.uri).map(child => row(child, depth + 1))}</ul>}</li>;
  }
  return <section className="min-w-0"><label className="mb-2 flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2 text-slate-400"><Search size={14} /><input aria-label={`${label}筛选`} value={query} onChange={event => setQuery(event.target.value)} placeholder="按名称或目录路径筛选" className="min-w-0 flex-1 bg-transparent text-xs text-slate-700 outline-none" /></label>
    <ul role="tree" aria-label={label} aria-multiselectable={multiple || undefined} className="max-h-64 overflow-auto rounded-lg border border-slate-200 p-2">{children().map(n => row(n, 0))}{!matches.size && <li className="p-4 text-xs text-slate-400">没有匹配的资源。</li>}</ul></section>;
}

export function KnowledgeResourcePicker({ nodes, value, onSelect, onClose, directoriesOnly, label }: {
  nodes: KnowledgeResourceNode[]; value: string; onSelect: (uri: string) => void; onClose: () => void; directoriesOnly?: boolean; label: string;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [choice, setChoice] = useState(value);
  useEffect(() => { const previous = document.activeElement; const element = dialog.current; element?.showModal(); return () => { element?.close(); if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); }; }, []);
  return <dialog ref={dialog} onCancel={onClose} aria-label={label} className={`${styles.dialog} m-auto max-h-[calc(100dvh-32px)] w-[calc(100%-32px)] max-w-xl overflow-auto rounded-2xl border-0 bg-white p-5 shadow-2xl`}>
    <header className="mb-4 flex items-center justify-between"><h2 className="font-semibold">{label}</h2><button type="button" onClick={onClose} aria-label="关闭资源选择" className="p-1 text-slate-400"><X size={18} /></button></header>
    <KnowledgeResourceTree nodes={nodes} selected={[choice]} onSelect={setChoice} directoriesOnly={directoriesOnly} label={label} />
    <p className="my-3 break-all font-mono text-[11px] text-slate-500">{decodeURI(choice)}</p><footer className="flex justify-end gap-2"><button type="button" onClick={onClose} className="rounded-lg border border-slate-200 px-3 py-2 text-xs">取消</button><button type="button" disabled={!nodes.some(n => n.uri === choice && (!directoriesOnly || n.kind === "directory"))} onClick={() => onSelect(choice)} className="rounded-lg bg-blue-600 px-3 py-2 text-xs text-white disabled:opacity-40">使用此位置</button></footer>
  </dialog>;
}
