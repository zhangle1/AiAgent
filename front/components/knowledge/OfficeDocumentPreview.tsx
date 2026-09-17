"use client";

import { useEffect, useState } from "react";
import { getKnowledgeOfficePreview } from "@/lib/knowledge-api";
import type { KnowledgeOfficePreview } from "@/lib/knowledge-types";

export function OfficeDocumentPreview({ kbName, documentId }: { kbName: string; documentId: number }) {
  const [preview, setPreview] = useState<KnowledgeOfficePreview | null>(null);
  const [error, setError] = useState("");
  const [section, setSection] = useState(0);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setPreview(null); setError(""); setSection(0);
    getKnowledgeOfficePreview(kbName, documentId, controller.signal)
      .then(value => { if (!controller.signal.aborted) setPreview(value); })
      .catch(ex => { if (!controller.signal.aborted) setError(ex instanceof Error ? ex.message : String(ex)); });
    return () => controller.abort();
  }, [kbName, documentId, revision]);
  if (error) return <div role="alert" className="rounded border border-red-200 p-4 text-sm text-red-700">{error}<button onClick={() => setRevision(value => value + 1)} className="ml-3 underline">重试预览</button></div>;
  if (!preview) return <div role="status" className="p-8 text-center text-sm text-slate-500">正在读取 Office 文件…</div>;
  const html = `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'"><style>body{font:14px/1.7 system-ui,sans-serif;color:#1e293b;padding:24px;margin:0;background:white}p{white-space:pre-wrap;overflow-wrap:anywhere}table{border-collapse:collapse;margin:12px 0}td,th{border:1px solid #dbe3ee;padding:6px 10px;white-space:pre-wrap;min-width:60px;max-width:480px;overflow-wrap:anywhere}th{background:#f1f5f9}img{display:block;max-width:100%;height:auto;margin:16px 0}</style></head><body>${preview.sections[section]?.html || "<p>暂无可预览内容</p>"}</body></html>`;
  return <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-lg border">
    <div className="flex shrink-0 gap-2 overflow-x-auto border-b bg-slate-50 p-2">{preview.sections.map((item, index) => <button key={index} onClick={() => setSection(index)} aria-pressed={section === index} className={`shrink-0 rounded px-3 py-1 text-xs ${section === index ? "bg-blue-600 text-white" : "bg-white"}`}>{item.name}</button>)}</div>
    <p className="px-3 py-2 text-xs text-slate-500">内容预览，复杂排版请下载原文件。{preview.truncated && "已达到预览上限，仅显示部分内容（最多 20 个工作表，每表 200 行 × 50 列）。"}</p>
    <iframe title="Office 原始文件内容预览" sandbox="" srcDoc={html} className="min-h-[480px] w-full flex-1 border-0 bg-white" />
  </div>;
}
