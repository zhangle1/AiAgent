"use client";

import { useState } from "react";
import { Download, Loader2, PackageOpen } from "lucide-react";
import { downloadProjectDocumentReference, downloadProjectPackage } from "@/lib/code-repository-api";

export function ChatPackageDownload({ projectId, reference, href, name }: { projectId: number; reference?: string; href?: string; name: string }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function download() {
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      if (href) await downloadProjectPackage(projectId, href);
      else await downloadProjectDocumentReference(projectId, reference!);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "下载失败，请重试。");
    } finally { setBusy(false); }
  }
  return <span className="my-2 inline-flex max-w-full flex-col align-middle">
    <button type="button" disabled={busy} onClick={() => void download()} className="inline-flex max-w-full items-center gap-3 rounded-xl border border-blue-200 bg-blue-50 px-4 py-3 text-left text-blue-800 hover:bg-blue-100 disabled:opacity-60">
      <PackageOpen size={24} className="shrink-0"/><span className="min-w-0"><span className="block truncate font-semibold">{name}</span><span className="block text-xs">ZIP 交付包 · {busy ? "下载中" : "点击下载"}</span></span>{busy ? <Loader2 size={18} className="animate-spin"/> : <Download size={18}/>}
    </button>
    {error && <span role="alert" className="text-xs text-rose-700">{error}</span>}
  </span>;
}
