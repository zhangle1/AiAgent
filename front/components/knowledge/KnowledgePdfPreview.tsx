"use client";

import { useEffect, useRef, useState } from "react";
import { Document, Page, pdfjs } from "react-pdf";

pdfjs.GlobalWorkerOptions.workerSrc = new URL("pdfjs-dist/build/pdf.worker.min.mjs", import.meta.url).toString();
const documentOptions = { wasmUrl: "/pdfjs/wasm/" };

export default function KnowledgePdfPreview({ url, name }: { url: string; name: string }) {
  const container = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(640);
  const [pages, setPages] = useState(0);
  const [page, setPage] = useState(1);
  const [error, setError] = useState("");
  useEffect(() => {
    setPage(1); setPages(0); setError("");
  }, [url]);
  useEffect(() => {
    const element = container.current;
    if (!element) return;
    const observer = new ResizeObserver(() => setWidth(Math.max(120, Math.min(1200, element.clientWidth - 24))));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  return <section aria-label={`${name} PDF 预览`} className="min-w-0 rounded-lg border border-slate-200 bg-white">
    <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 p-3 text-xs text-slate-600">
      <span>PDF 原文 · {pages ? `共 ${pages} 页` : "正在读取"}</span>
      <div className="flex items-center gap-2"><button type="button" disabled={page <= 1} onClick={() => setPage(p => p - 1)} className="rounded border px-2 py-1 disabled:opacity-40">上一页</button><label className="flex items-center gap-1">第 <input aria-label="PDF 页码" type="number" min={1} max={pages || 1} value={page} onChange={event => setPage(Math.max(1, Math.min(pages || 1, Number(event.target.value) || 1)))} className="w-16 rounded border px-2 py-1" /> 页</label><button type="button" disabled={!pages || page >= pages} onClick={() => setPage(p => p + 1)} className="rounded border px-2 py-1 disabled:opacity-40">下一页</button></div>
    </div>
    {error && <div role="alert" className="p-4 text-sm text-rose-700">PDF 预览失败：{error}<a href={url} target="_blank" rel="noreferrer" className="ml-2 underline">打开原文件</a></div>}
    <div ref={container} className="min-h-[420px] overflow-hidden p-3">
      <Document file={url} options={documentOptions} loading={<p role="status" className="p-4 text-sm text-slate-400">正在加载 PDF…</p>} onLoadSuccess={({ numPages }) => { setPages(numPages); setError(""); }} onLoadError={reason => setError(reason.message)} error={null}>
        {!error && <Page pageNumber={page} width={width} renderAnnotationLayer={false} renderTextLayer={false} onRenderError={reason => setError(reason.message)} loading={<p className="p-4 text-sm text-slate-400">正在渲染第 {page} 页…</p>} />}
      </Document>
    </div>
  </section>;
}
