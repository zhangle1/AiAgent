"use client";

import { useRef, useState } from "react";
import { Download, Layers, Loader2 } from "lucide-react";
import { downloadPsd, generatePrototypePsd, PSD_VIEWPORTS, type PsdViewport } from "@/lib/prototype-psd";

// Serialize exports across folders to avoid several large canvases at once.
let exportInProgress = false;

export function PsdFiles({ name, html }: { name: string; html: string }) {
  const [busy, setBusy] = useState<PsdViewport | null>(null);
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const [ready, setReady] = useState<Partial<Record<PsdViewport, string>>>({});
  const cache = useRef<Partial<Record<PsdViewport, { html: string; blob: Blob }>>>({});

  async function exportFile(viewport: PsdViewport) {
    if (exportInProgress) { setError("已有 PSD 正在生成，请稍后重试。"); return; }
    exportInProgress = true;
    setBusy(viewport);
    setError("");
    try {
      const cached = cache.current[viewport];
      const blob = cached?.html === html ? cached.blob : await generatePrototypePsd(html, viewport, setStatus);
      cache.current[viewport] = { html, blob };
      setReady((value) => ({ ...value, [viewport]: html }));
      downloadPsd(blob, `${name}-${PSD_VIEWPORTS[viewport].label}.psd`);
    } catch (value) { setError(value instanceof Error ? value.message : "PSD 生成失败，请重试。"); }
    finally { exportInProgress = false; setBusy(null); setStatus(""); }
  }

  return <div className="ml-7 border-l border-slate-200 pl-2">
    {(Object.keys(PSD_VIEWPORTS) as PsdViewport[]).map((viewport) => <button key={viewport} type="button" disabled={busy !== null} onClick={() => void exportFile(viewport)} title="生成并下载带分组的像素图层 PSD" className="flex w-full items-center gap-2 rounded px-2 py-2 text-left text-xs text-slate-600 hover:bg-violet-50 disabled:opacity-50">
      {busy === viewport ? <Loader2 size={14} className="shrink-0 animate-spin" /> : <Layers size={14} className="shrink-0 text-blue-500" />}
      <span className="min-w-0 flex-1 truncate">{name}-{PSD_VIEWPORTS[viewport].label}.psd<span className="block text-[10px] text-slate-400">{busy === viewport ? status : ready[viewport] === html ? "已生成 · 点击下载" : "待生成 · 点击生成并下载"}</span></span><Download size={12} />
    </button>)}
    {error && <p role="alert" className="px-2 py-1 text-xs text-rose-600">{error}</p>}
    <p className="px-2 pb-2 text-[10px] leading-4 text-slate-400">分组像素图层；文字已栅格化。按当前 HTML 生成，刷新后可重新导出。外链图片需允许跨域。</p>
  </div>;
}
