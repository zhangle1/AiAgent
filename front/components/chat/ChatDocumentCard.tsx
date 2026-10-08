"use client";

import { useState } from "react";
import { Download, FileText, Loader2, PanelRightOpen } from "lucide-react";
import { downloadProjectDocumentReference } from "@/lib/code-repository-api";

export function ChatDocumentCard({ reference, projectId, onOpen }: {
  reference: string;
  projectId: number;
  onOpen: (reference: string) => void;
}) {
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const path = reference.replace(/(?::|#L)[1-9]\d{0,8}$/i, "").replace(/\\/g, "/");
  const name = path.split("/").pop() || path;
  const format = name.split(".").pop()?.toUpperCase() || "文档";

  async function download() {
    if (downloading) return;
    setDownloading(true);
    setError(null);
    try {
      await downloadProjectDocumentReference(projectId, reference);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "下载失败，请重试。");
    } finally {
      setDownloading(false);
    }
  }

  // Spans keep this valid inside Markdown paragraphs and list items.
  return <span className="my-2 inline-flex w-full max-w-md flex-col overflow-hidden rounded-xl border border-slate-200 bg-slate-50 align-middle text-left text-sm shadow-sm" data-document-card={name}>
    <span className="flex min-w-0 items-stretch">
      <button type="button" onClick={() => onOpen(reference)} className="group flex min-w-0 flex-1 items-center gap-3 p-3 text-left transition hover:bg-blue-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-blue-500" title={`在右侧项目文档中打开 ${name}`}>
        <span className="grid h-11 w-11 shrink-0 place-items-center rounded-lg border border-blue-100 bg-white text-blue-600"><FileText size={23}/></span>
        <span className="min-w-0 flex-1"><span className="block truncate font-semibold text-slate-800">{name}</span><span className="mt-1 flex items-center gap-1 text-xs text-slate-500">{format} · 点击预览<PanelRightOpen size={12}/></span></span>
      </button>
      <button type="button" onClick={() => void download()} disabled={downloading} aria-label={`下载 ${name}`} title="下载原文件" className="my-3 mr-2 flex shrink-0 items-center gap-1 rounded-lg px-2 text-xs font-medium text-blue-700 hover:bg-blue-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 disabled:opacity-50">
        {downloading ? <Loader2 size={16} className="animate-spin"/> : <Download size={16}/>}{downloading ? "下载中" : "下载"}
      </button>
    </span>
    {error && <span role="alert" className="border-t border-rose-100 px-3 py-2 text-xs text-rose-700">{error}</span>}
  </span>;
}
