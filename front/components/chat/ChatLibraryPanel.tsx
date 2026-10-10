"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Download, FileText, FolderOpen, Link2, Loader2, RefreshCw, Search } from "lucide-react";
import { getMyChatUploads, myChatUploadContentUrl } from "@/lib/chat-api";
import { getProjectMarkdownDocuments } from "@/lib/code-repository-api";
import type { CodeProjectMarkdownDocument } from "@/lib/code-repository-types";
import { buildChatLibrary } from "@/lib/chat-library";
import { readChatLibraryFile, saveChatLibraryFile } from "@/lib/chat-library-api";
import type { ChatLibraryItem } from "@/lib/chat-library-types";

export function ChatLibraryPanel({ projectId, refreshToken, onInsert, onAttach, onPreview }: {
  projectId: number | null;
  refreshToken?: number;
  onInsert?: (document: CodeProjectMarkdownDocument) => void;
  onAttach?: (file: File) => Promise<void>;
  onPreview: (document: CodeProjectMarkdownDocument) => void;
}) {
  const [items, setItems] = useState<ChatLibraryItem[]>([]);
  const [keyword, setKeyword] = useState("");
  const [source, setSource] = useState("all");
  const [revision, setRevision] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const operation = useRef<AbortController | null>(null);
  const attach = useRef(onAttach);
  attach.current = onAttach;
  useEffect(() => () => operation.current?.abort(), []);

  useEffect(() => {
    let disposed = false;
    async function load() {
      setLoading(true);
      setError("");
      const [uploads, documents] = await Promise.allSettled([
        getMyChatUploads(), projectId ? getProjectMarkdownDocuments(projectId) : Promise.resolve([]),
      ]);
      if (disposed) return;
      setItems(buildChatLibrary(uploads.status === "fulfilled" ? uploads.value : [], documents.status === "fulfilled" ? documents.value : []));
      const failures = [uploads.status === "rejected" ? "上传记录读取失败" : "", documents.status === "rejected" ? "项目资料读取失败" : ""].filter(Boolean);
      setError(failures.length ? `${failures.join("；")}，请刷新重试。` : "");
      setLoading(false);
    }
    void load();
    return () => { disposed = true; };
  }, [projectId, refreshToken, revision]);

  useEffect(() => {
    const refresh = () => { if (document.visibilityState === "visible") setRevision((value) => value + 1); };
    window.addEventListener("focus", refresh);
    return () => window.removeEventListener("focus", refresh);
  }, []);

  const visible = useMemo(() => {
    const query = keyword.trim().toLowerCase();
    return items.filter((item) => (source === "all" || item.source === source) && `${item.name} ${item.location}`.toLowerCase().includes(query));
  }, [items, keyword, source]);

  async function act(item: ChatLibraryItem, action: "download" | "attach") {
    if (operation.current) return;
    const controller = new AbortController();
    operation.current = controller;
    setBusy(item.id);
    setError("");
    setNotice("");
    try {
      if (action === "attach" && item.document) {
        onInsert?.(item.document);
      } else {
        const file = await readChatLibraryFile(item, projectId, controller.signal);
        if (controller.signal.aborted) return;
        if (action === "download") saveChatLibraryFile(file);
        else await attach.current?.(file);
      }
      if (!controller.signal.aborted) setNotice(action === "attach" ? `已关联：${item.name}` : `已下载：${item.name}`);
    } catch (ex) {
      if (!controller.signal.aborted) setError(ex instanceof Error ? ex.message : "操作失败，请重试。");
    } finally {
      operation.current = null;
      if (!controller.signal.aborted) setBusy(null);
    }
  }

  return <section className="flex min-h-0 flex-1 flex-col overflow-hidden">
    <div className="shrink-0 space-y-3 border-b border-slate-200 p-4">
      <div className="flex items-center justify-between"><div><h2 className="text-sm font-semibold text-slate-900">资料库</h2><p className="mt-1 text-[11px] leading-5 text-slate-500">我的上传与当前项目资料，最近更新优先</p></div><button type="button" disabled={loading} onClick={() => setRevision((value) => value + 1)} aria-label="刷新资料库" className="rounded-lg p-2 text-slate-500 hover:bg-slate-100 disabled:opacity-40"><RefreshCw size={15} className={loading ? "animate-spin" : ""}/></button></div>
      <label className="flex items-center gap-2 rounded-lg border border-slate-200 px-3"><Search size={14} className="text-slate-400"/><input aria-label="搜索资料库" value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder="搜索文件名或路径" className="h-9 min-w-0 flex-1 bg-transparent text-xs outline-none"/></label>
      <div className="flex gap-2">{[["all", "全部"], ["upload", "我的上传"], ["project", "项目文件"]].map(([value, label]) => <button key={value} type="button" aria-pressed={source === value} onClick={() => setSource(value)} className={`rounded-full px-3 py-1.5 text-xs ${source === value ? "bg-blue-50 font-medium text-blue-700" : "text-slate-500 hover:bg-slate-50"}`}>{label}</button>)}</div>
      <p className="text-[10px] leading-4 text-slate-400">包含项目中已保存的 HTML、文档等产物，排除代码及构建目录。上传显示最近 200 项已发送附件；聊天完成后自动刷新。</p>
    </div>
    {error && <p role="alert" className="shrink-0 bg-rose-50 px-4 py-2 text-xs text-rose-700">{error}</p>}
    {notice && <p role="status" className="shrink-0 bg-blue-50 px-4 py-2 text-xs text-blue-700">{notice}</p>}
    <div className="workspace-scroll min-h-0 flex-1 overflow-auto p-3">
      {loading ? <div className="flex items-center justify-center gap-2 py-12 text-xs text-slate-500"><Loader2 size={16} className="animate-spin"/>正在发现资料…</div> : visible.length ? <div className="grid grid-cols-[repeat(auto-fit,minmax(220px,1fr))] gap-3">{visible.map((item) => <article key={item.id} className="min-w-0 rounded-xl border border-slate-200 bg-slate-50/60 p-3 transition hover:border-blue-200 hover:bg-white">
        <div className="mb-3 flex items-center justify-between"><span className="rounded-lg bg-white p-2 text-blue-600"><FileText size={21}/></span><span className="text-[10px] text-slate-400">{item.source === "upload" ? "上传资料" : "项目文件"}</span></div>
        {item.upload?.kind === "image" && <img src={myChatUploadContentUrl(item.upload.id)} alt={item.name} loading="lazy" className="mb-3 h-32 w-full rounded-lg bg-slate-100 object-contain"/>}
        <h3 title={item.name} className="break-words text-xs font-semibold leading-5 text-slate-800">{item.name}</h3><p title={item.location} className="mt-1 truncate text-[10px] text-slate-400">{item.location}</p><p className="mt-2 text-[10px] text-slate-400">{item.updatedAt ? new Date(item.updatedAt).toLocaleString() : "更新时间未知"}</p>
        <div className="mt-3 flex flex-wrap gap-2 border-t border-slate-200/70 pt-3">
          {item.document && <button type="button" onClick={() => onPreview(item.document!)} className="text-xs text-slate-500 hover:text-blue-700">预览</button>}
          <button type="button" disabled={busy !== null} onClick={() => void act(item, "download")} className="inline-flex items-center gap-1 text-xs text-slate-600 hover:text-blue-700 disabled:opacity-40"><Download size={12}/>下载</button>
          <button type="button" disabled={busy !== null || (item.document ? !onInsert : !onAttach)} onClick={() => void act(item, "attach")} className="ml-auto inline-flex items-center gap-1 text-xs font-medium text-blue-600 disabled:opacity-40">{busy === item.id ? <Loader2 size={12} className="animate-spin"/> : <Link2 size={12}/>}关联到聊天</button>
        </div>
      </article>)}</div> : <div className="py-14 text-center text-xs leading-6 text-slate-400"><FolderOpen size={30} className="mx-auto mb-3"/>{keyword || source !== "all" ? "没有匹配的资料" : "暂无资料。发送附件或在项目中保存文档后，可在这里找到。"}</div>}
    </div>
  </section>;
}
