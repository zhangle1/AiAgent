"use client";

import { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { Check, Copy, QrCode, Smartphone, X } from "lucide-react";

export function ClientScanDialog() {
  const [open, setOpen] = useState(false);
  const [url, setUrl] = useState("");
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (open) setUrl(`${window.location.origin}/login`);
  }, [open]);

  async function copyUrl() {
    if (!url) return;
    try {
      await navigator.clipboard.writeText(url);
    } catch {
      const fallback = document.createElement("textarea");
      fallback.value = url;
      fallback.setAttribute("readonly", "");
      fallback.style.cssText = "position:fixed;opacity:0;pointer-events:none";
      document.body.appendChild(fallback);
      fallback.select();
      document.execCommand("copy");
      document.body.removeChild(fallback);
    }
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1800);
  }

  const qrUrl = url ? `https://api.qrserver.com/v1/create-qr-code/?format=svg&size=256x256&data=${encodeURIComponent(url)}` : "";

  return <>
    <button type="button" onClick={() => setOpen(true)} className="hidden h-8 items-center gap-1.5 rounded-lg border border-blue-200 bg-blue-50 px-2.5 text-xs font-semibold text-blue-700 shadow-sm transition hover:bg-blue-100 lg:inline-flex" aria-label="客户端扫码访问" title="客户端扫码访问">
      <Smartphone size={15}/><span className="hidden xl:inline">客户端扫码</span><QrCode size={14}/>
    </button>
    {open && typeof document !== "undefined" && createPortal(<div className="fixed inset-0 z-[130] grid place-items-center bg-slate-950/65 p-4 backdrop-blur-sm" role="presentation" onMouseDown={() => setOpen(false)}>
      <section className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-5 shadow-2xl" role="dialog" aria-modal="true" aria-labelledby="client-scan-title" onMouseDown={(event) => event.stopPropagation()}>
        <header className="flex items-start gap-3"><span className="grid h-10 w-10 place-items-center rounded-xl bg-blue-50 text-blue-600"><Smartphone size={20}/></span><div className="min-w-0 flex-1"><h2 id="client-scan-title" className="text-base font-semibold text-slate-900">客户端扫码访问</h2><p className="mt-1 text-xs leading-5 text-slate-500">使用手机扫描二维码，继续当前 AiAgent 工作台。</p></div><button type="button" onClick={() => setOpen(false)} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="关闭客户端扫码"><X size={17}/></button></header>
        <div className="mx-auto mt-5 grid w-[220px] place-items-center rounded-2xl border border-slate-100 bg-white p-3 shadow-sm">{qrUrl ? <img src={qrUrl} alt="当前访问地址二维码" width={196} height={196} referrerPolicy="no-referrer" /> : <QrCode size={160} className="text-slate-200"/>}</div>
        <div className="mt-5 rounded-xl border border-slate-200 bg-slate-50 p-3"><p className="text-[11px] font-medium text-slate-500">当前访问地址</p><p className="mt-1 break-all font-mono text-[11px] leading-5 text-slate-700">{url || "正在读取地址…"}</p><button type="button" disabled={!url} onClick={() => void copyUrl()} className="mt-3 inline-flex h-9 w-full items-center justify-center gap-2 rounded-lg border border-blue-200 bg-white text-xs font-semibold text-blue-700 hover:bg-blue-50 disabled:opacity-50">{copied ? <Check size={15}/> : <Copy size={15}/>} {copied ? "已复制" : "复制网址"}</button></div>
      </section>
    </div>, document.body)}
  </>;
}
