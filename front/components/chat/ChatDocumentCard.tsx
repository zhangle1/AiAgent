"use client";

import { useState } from "react";
import { BookPlus, Download, FileText, Loader2 } from "lucide-react";
import { SaveMarkdownToKnowledgeDialog } from "./SaveMarkdownToKnowledgeDialog";
import { downloadProjectDocumentReference, saveDocumentBlob } from "@/lib/code-repository-api";
import { getKnowledgeChapterFile, type KnowledgeChapterReference } from "@/lib/knowledge-api";

type DocumentCardProps = { chapter: KnowledgeChapterReference; onOpen?: (chapter: KnowledgeChapterReference) => void } | {
  reference: string;
  projectId: number;
  onOpen: (reference: string) => void;
};

export function ChatDocumentCard(props: DocumentCardProps) {
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showSave, setShowSave] = useState(false);
  const [notice, setNotice] = useState("");
  const projectDocument = "reference" in props ? props : null;
  const chapter = "chapter" in props ? props.chapter : null;
  const reference = "reference" in props ? props.reference : props.chapter.name;
  const path = reference.replace(/(?::|#L)[1-9]\d{0,8}$/i, "").replace(/\\/g, "/");
  const name = path.split("/").pop() || path;
  const canPreview = Boolean(props.onOpen);

  async function download() {
    if (downloading) return;
    setDownloading(true);
    setError(null);
    try {
      if (projectDocument) await downloadProjectDocumentReference(projectDocument.projectId, reference);
      else if (chapter) {
        const file = await getKnowledgeChapterFile(chapter, true);
        saveDocumentBlob(await file.blob(), chapter.name);
      }
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "下载失败，请重试。");
    } finally {
      setDownloading(false);
    }
  }

  // Content-sized chips stay within the text line and shrink in narrow panels.
  return <span className="mx-0.5 inline-flex max-w-[min(calc(100%_-_4px),18rem)] flex-col overflow-hidden rounded-md border border-slate-200/80 bg-slate-50/80 align-middle text-left text-[13px] font-normal leading-5" data-document-card={name}>
    <span className="flex min-w-0 items-stretch">
      <button type="button" disabled={!canPreview} onClick={() => "chapter" in props ? props.onOpen?.(props.chapter) : props.onOpen(reference)} className="flex min-w-0 flex-1 items-center gap-1.5 px-1.5 py-0.5 text-left text-slate-700 transition hover:bg-blue-50 hover:text-blue-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-blue-500" title={chapter ? `预览资料库章节 ${name}` : `在右侧项目文档中打开 ${name}`}>
        <FileText size={14} className="shrink-0 text-slate-500"/>
        <span className="truncate" title={reference}>{name}</span>
      </button>
      <button type="button" onClick={() => void download()} disabled={downloading} aria-label={`下载 ${name}`} title={downloading ? "下载中" : chapter ? "下载章节 Markdown" : "下载原文件"} className="flex w-7 shrink-0 items-center justify-center border-l border-slate-200/70 text-slate-500 transition hover:bg-blue-50 hover:text-blue-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-blue-500 disabled:opacity-50">
        {downloading ? <Loader2 size={14} className="animate-spin"/> : <Download size={14}/>}
      </button>
      {/\.(md|markdown)$/i.test(name) && <button type="button" onClick={() => { setNotice(""); setShowSave(true); }} aria-label={`保存 ${name} 到知识库`} title="保存到知识库" className="flex w-7 shrink-0 items-center justify-center border-l border-slate-200/70 text-slate-500 hover:bg-blue-50 hover:text-blue-700 focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-blue-500"><BookPlus size={14}/></button>}
    </span>
    {showSave && <SaveMarkdownToKnowledgeDialog source={props} onClose={() => setShowSave(false)} onSaved={setNotice}/>}
    {notice && <span role="status" className="whitespace-normal break-words border-t border-slate-200 px-2 py-1 text-xs text-emerald-700">{notice}</span>}
    {error && <span role="alert" className="whitespace-normal break-words border-t border-rose-100 px-2 py-1 text-xs text-rose-700">{error}</span>}
  </span>;
}
