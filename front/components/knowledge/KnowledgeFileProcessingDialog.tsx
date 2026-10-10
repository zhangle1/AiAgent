"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { ChevronDown, FileText, RefreshCw, Search, X } from "lucide-react";
import { getKnowledgeResourceProcessing } from "@/lib/knowledge-api";
import type { KnowledgeResourceProcessing } from "@/lib/knowledge-types";
import styles from "./knowledge-workspace.module.css";

const stages: Record<string, string> = { queued: "等待执行", validating: "配置校验", parsing: "正文解析 L2", semantic: "语义生成 L0/L1", compiling: "知识整理", completed: "处理完成" };
const active = new Set(["queued", "processing", "cancelling"]);

function status(row: KnowledgeResourceProcessing) {
  if (row.task_status === "error") return { label: "处理失败", color: "bg-rose-50 text-rose-700" };
  if (row.task_status === "queued") return { label: "排队中", color: "bg-amber-50 text-amber-700" };
  if (row.task_status === "processing") return { label: "处理中", color: "bg-blue-50 text-blue-700" };
  if (row.task_status === "cancelling") return { label: "正在取消", color: "bg-amber-50 text-amber-700" };
  if (row.task_status === "cancelled") return { label: "已取消", color: "bg-slate-100 text-slate-600" };
  if (row.task_status === "success") return { label: row.stage === "completed" ? "处理成功" : "历史任务完成", color: "bg-emerald-50 text-emerald-700" };
  if (row.node.status === "processed") return { label: "正文已解析", color: "bg-emerald-50 text-emerald-700" };
  if (row.node.status === "error") return { label: "解析失败", color: "bg-rose-50 text-rose-700" };
  if (row.node.status === "parsing" || row.node.status === "processing") return { label: "正文解析中", color: "bg-blue-50 text-blue-700" };
  return { label: "待处理", color: "bg-slate-100 text-slate-600" };
}

function size(bytes?: number | null) {
  if (bytes == null) return "—";
  if (bytes < 1024) return `${bytes} B`;
  return bytes < 1048576 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1048576).toFixed(1)} MB`;
}

function readableUri(uri: string) { try { return decodeURIComponent(uri); } catch { return uri; } }

export function KnowledgeFileProcessingDialog({ onClose, onOpen }: { onClose: () => void; onOpen: (uri: string) => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [rows, setRows] = useState<KnowledgeResourceProcessing[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [query, setQuery] = useState("");
  const [revision, setRevision] = useState(0);
  const [updated, setUpdated] = useState("");
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  useEffect(() => {
    const element = dialog.current;
    const previous = document.activeElement;
    element?.showModal();
    return () => { element?.close(); if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    async function load() {
      setLoading(true);
      try {
        const next = await getKnowledgeResourceProcessing(controller.signal);
        if (controller.signal.aborted) return;
        setRows(next); setError("");
        setUpdated(new Date().toLocaleTimeString("zh-CN", { hour12: false }));
        if (next.some(row => active.has(row.task_status ?? "") || ["parsing", "processing"].includes(row.node.status ?? ""))) timer = setTimeout(() => void load(), 3000);
      } catch (ex) { if (!controller.signal.aborted) setError(ex instanceof Error ? ex.message : "文件处理状态读取失败"); }
      finally { if (!controller.signal.aborted) setLoading(false); }
    }
    void load();
    return () => { controller.abort(); clearTimeout(timer); };
  }, [revision]);

  const visible = useMemo(() => rows.filter(row => `${row.node.name} ${readableUri(row.node.uri)}`.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase())), [rows, query]);
  const failed = rows.filter(row => row.task_status === "error" || (!row.task_status && row.node.status === "error")).length;

  return <dialog ref={dialog} onCancel={onClose} aria-labelledby="knowledge-file-processing-title" className={`${styles.dialog} m-auto max-h-[calc(100dvh-32px)] w-[calc(100%-32px)] max-w-4xl overflow-hidden rounded-2xl border-0 bg-white p-0 text-slate-900 shadow-2xl`}>
    <div className="flex max-h-[calc(100dvh-32px)] flex-col">
      <header className="flex shrink-0 items-center gap-3 border-b border-slate-100 px-5 py-4">
        <div className="min-w-0 flex-1"><h2 id="knowledge-file-processing-title" className="text-lg font-semibold">文件处理任务</h2><p className="mt-1 text-xs text-slate-500">查看当前可见目录中的文件及最新处理结果</p></div>
        <button type="button" onClick={() => setRevision(value => value + 1)} disabled={loading} className="inline-flex h-8 items-center gap-1.5 rounded-lg border border-slate-200 px-3 text-xs hover:bg-slate-50 disabled:opacity-50"><RefreshCw size={14} className={loading ? "animate-spin" : ""} />刷新</button>
        <button type="button" aria-label="关闭文件处理任务" onClick={onClose} className="rounded-lg p-2 text-slate-400 hover:bg-slate-100"><X size={18} /></button>
      </header>
      <div className="flex shrink-0 flex-wrap items-center justify-between gap-3 px-5 py-4">
        <p className="text-xs text-slate-500">共 {rows.length} 个文件{failed > 0 && <span className="ml-2 text-rose-600">{failed} 个失败</span>}</p>
        <label className="flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-2 text-slate-400"><Search size={14} /><input aria-label="搜索处理文件" value={query} onChange={event => setQuery(event.target.value)} placeholder="搜索文件名或目录" className="w-44 bg-transparent text-xs text-slate-700 outline-none" /></label>
      </div>
      {error && <p role="alert" className="mx-5 mb-3 rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700">{error}。请点击刷新重试。</p>}
      <div aria-busy={loading} className="mx-5 min-h-0 overflow-auto rounded-xl border border-slate-200">
        <table className="w-full text-left text-sm">
          <thead className="sticky top-0 z-10 bg-slate-50 text-xs font-normal text-slate-500"><tr><th scope="col" className="px-4 py-3 font-medium">文件名</th><th scope="col" className="px-3 py-3 font-medium">状态</th><th scope="col" className="px-3 py-3 font-medium">大小</th><th scope="col" className="px-3 py-3 font-medium"><span className="sr-only">操作</span></th></tr></thead>
          <tbody>{visible.map(row => {
            const result = status(row); const isExpanded = expanded.has(row.node.uri);
            return <ProcessingRow key={row.node.uri} row={row} label={result.label} color={result.color} expanded={isExpanded} onToggle={() => setExpanded(current => { const next = new Set(current); next.has(row.node.uri) ? next.delete(row.node.uri) : next.add(row.node.uri); return next; })} onOpen={onOpen} />;
          })}</tbody>
        </table>
        {!visible.length && <p className="py-14 text-center text-sm text-slate-400">{loading ? "正在读取文件处理状态…" : error ? "暂时无法读取文件状态" : rows.length ? "没有匹配的文件" : "还没有上传文件"}</p>}
      </div>
      <footer className="flex shrink-0 flex-wrap items-center justify-between gap-2 px-5 py-4 text-xs text-slate-500"><span role="status">{updated ? `更新于 ${updated} · 处理中自动刷新` : "处理状态来自后台任务队列"}</span><a href="/task-center?domain=knowledge" className="text-blue-600 hover:underline">打开任务中心</a></footer>
    </div>
  </dialog>;
}

function ProcessingRow({ row, label, color, expanded, onToggle, onOpen }: { row: KnowledgeResourceProcessing; label: string; color: string; expanded: boolean; onToggle: () => void; onOpen: (uri: string) => void }) {
  const failed = row.task_status === "error" || (!row.task_status && row.node.status === "error");
  return <>
    <tr className={`border-t border-slate-100 ${failed ? "bg-rose-50/50" : "hover:bg-slate-50"}`}>
      <td className="max-w-0 px-4 py-3"><div className="flex min-w-0 items-center gap-2"><FileText size={15} className="shrink-0 text-slate-400" /><span title={row.node.name} className="truncate font-medium">{row.node.name}</span></div><p title={readableUri(row.node.parent_uri ?? "")} className="mt-1 truncate text-[11px] text-slate-400">{readableUri(row.node.parent_uri ?? "")}</p></td>
      <td className="whitespace-nowrap px-3 py-3"><button type="button" onClick={onToggle} aria-expanded={expanded} aria-label={`查看 ${row.node.name} 处理详情`} className="inline-flex items-center gap-1"><span className={`rounded-full px-2 py-1 text-[11px] ${color}`}>{label}</span><ChevronDown size={12} className={`text-slate-400 transition ${expanded ? "rotate-180" : ""}`} /></button>{active.has(row.task_status ?? "") && <p className="mt-1 text-[11px] text-slate-500">{stages[row.stage ?? ""] ?? "等待阶段信息"} · {row.progress ?? 0}%</p>}</td>
      <td className="whitespace-nowrap px-3 py-3 text-xs text-slate-500">{size(row.node.size)}</td>
      <td className="whitespace-nowrap px-3 py-3"><button type="button" onClick={() => onOpen(row.node.uri)} className="text-xs text-blue-600 hover:underline" aria-label={`预览 ${row.node.name}`}>预览</button></td>
    </tr>
    {expanded && <tr className="border-t border-slate-100 bg-slate-50"><td colSpan={4} className="space-y-2 px-4 py-3 text-xs leading-5 text-slate-600">
      <p>正文状态：{row.node.status === "processed" ? "正文已解析并保存" : row.node.status === "error" ? "正文解析失败" : row.node.status === "parsing" ? "正在解析正文" : "尚未确认解析完成"}；任务阶段：{row.stage ? stages[row.stage] ?? row.stage : "未记录，历史任务不能确认语义是否生成"}</p>
      <p className={failed ? "break-words text-rose-700" : "break-words"}>{failed ? `错误：${row.error_message || row.message || "未返回详细原因，请在任务中心重新处理"}` : row.message || "尚无处理任务，可在文件预览中点击重新解析"}</p>
    </td></tr>}
  </>;
}
