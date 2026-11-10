"use client";

import { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { Gauge, X } from "lucide-react";
import { getCodexQuota } from "@/lib/codex-quota-api";
import type { CodexQuota, CodexQuotaWindow } from "@/lib/codex-quota-types";

const messages = {
  not_logged_in: "后端本地 Codex 尚未登录。",
  unsupported: "当前 Codex 登录方式不支持订阅额度查询。",
  unavailable: "暂时无法读取额度，请稍后重试。",
};

export function CodexQuotaButton() {
  const [open, setOpen] = useState(false);
  const [quota, setQuota] = useState<CodexQuota | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout>;
    const load = async () => {
      try {
        const value = await getCodexQuota(controller.signal);
        if (controller.signal.aborted) return;
        setQuota(value); setFailed(false);
        timer = setTimeout(() => void load(), Math.max(60_000, Date.parse(value.next_refresh_at) - Date.now()));
      } catch {
        if (!controller.signal.aborted) { setFailed(true); timer = setTimeout(() => void load(), 60_000); }
      }
    };
    setQuota(null); setFailed(false);
    void load();
    const keydown = (event: KeyboardEvent) => { if (event.key === "Escape") setOpen(false); };
    window.addEventListener("keydown", keydown);
    return () => { controller.abort(); clearTimeout(timer); window.removeEventListener("keydown", keydown); };
  }, [open]);

  return <>
    <button type="button" onClick={() => setOpen(true)} className="grid h-10 w-full place-items-center rounded-xl text-slate-600 transition hover:bg-white" title="Codex 额度" aria-label="Codex 额度"><Gauge size={18}/></button>
    {open && createPortal(<div className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-950/30 p-4" onMouseDown={() => setOpen(false)}>
      <div role="dialog" aria-modal="true" aria-labelledby="codex-quota-title" className="max-h-[80vh] w-full max-w-sm overflow-y-auto rounded-2xl border border-slate-200 bg-white p-5 text-slate-900 shadow-xl" onMouseDown={(event) => event.stopPropagation()}>
        <div className="flex items-center justify-between"><h2 id="codex-quota-title" className="font-semibold">Codex 额度</h2><button type="button" autoFocus onClick={() => setOpen(false)} aria-label="关闭" className="rounded p-1 hover:bg-slate-100"><X size={18}/></button></div>
        <p className="mt-2 text-xs text-slate-500">账号来源：后端本地 Codex 登录账号。所有使用此后端的用户共享该额度。</p>
        {failed ? <p className="mt-4 text-sm text-red-600">额度读取失败，请稍后重新打开。</p> : !quota ? <p className="mt-4 text-sm">正在读取…</p> : <>
          {quota.status !== "available" ? <p className="mt-4 text-sm">{messages[quota.status]}</p> : <div className="mt-4 space-y-4">
            {quota.account_hint && <p className="text-sm">当前账号：{quota.account_hint}</p>}
            {quota.plan && <p className="text-sm">订阅：{quota.plan}</p>}
            <QuotaWindow label="主要额度窗口" value={quota.primary}/>
            <QuotaWindow label="次要额度窗口" value={quota.secondary}/>
            {!quota.primary && !quota.secondary && <p className="text-sm">当前账号未返回额度窗口。</p>}
          </div>}
          <p className="mt-5 text-xs text-slate-500">查询时间：{new Date(quota.updated_at).toLocaleString()}</p>
          <p className="mt-1 text-xs text-slate-500">下次更新：{new Date(quota.next_refresh_at).toLocaleString()}（成功结果缓存 30 分钟）</p>
        </>}
      </div>
    </div>, document.body)}
  </>;
}

function QuotaWindow({ label, value }: { label: string; value: CodexQuotaWindow | null }) {
  if (!value) return null;
  return <div><div className="flex justify-between text-sm"><span>{label}{value.window_minutes ? ` · ${value.window_minutes >= 60 ? `${value.window_minutes / 60} 小时` : `${value.window_minutes} 分钟`}` : ""}</span><span>已用 {value.used_percent}%</span></div>
    <progress value={value.used_percent} max={100} className="mt-2 h-2 w-full accent-blue-600" aria-label={label}/>
    {value.resets_at != null && <p className="mt-1 text-xs text-slate-500">重置：{new Date(value.resets_at * 1000).toLocaleString()}</p>}
  </div>;
}
