"use client";

import { useState } from "react";
import { Download, FileText, Loader2 } from "lucide-react";
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

  // Content-sized chips stay within the text line and shrink in narrow panels.
  return <span className="mx-0.5 inline-flex max-w-[min(calc(100%_-_4px),18rem)] flex-col overflow-hidden rounded-md border border-slate-200/80 bg-slate-50/80 align-middle text-left text-[13px] font-normal leading-5" data-document-card={name}>
    <span className="flex min-w-0 items-stretch">
      <button type="button" onClick={() => onOpen(reference)} className="flex min-w-0 flex-1 items-center gap-1.5 px-1.5 py-0.5 text-left text-slate-700 transition hover:bg-blue-50 hover:text-blue-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-blue-500" title={`在右侧项目文档中打开 ${name}`}>
        <FileText size={14} className="shrink-0 text-slate-500"/>
        <span className="truncate" title={reference}>{name}</span>
      </button>
      <button type="button" onClick={() => void download()} disabled={downloading} aria-label={`下载 ${name}`} title={downloading ? "下载中" : "下载原文件"} className="flex w-7 shrink-0 items-center justify-center border-l border-slate-200/70 text-slate-500 transition hover:bg-blue-50 hover:text-blue-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-blue-500 disabled:opacity-50">
        {downloading ? <Loader2 size={14} className="animate-spin"/> : <Download size={14}/>}
      </button>
    </span>
    {error && <span role="alert" className="whitespace-normal break-words border-t border-rose-100 px-2 py-1 text-xs text-rose-700">{error}</span>}
  </span>;
}
