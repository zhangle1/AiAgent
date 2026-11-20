"use client";

import { useEffect, useState } from "react";
import { CircleAlert, RefreshCw, X } from "lucide-react";
import type { ChatStreamRecord } from "./ChatStreamProvider";

export function ChatRetryNotice({ stream, onRetry, onCancel }: {
  stream: ChatStreamRecord;
  onRetry: () => void;
  onCancel: () => void;
}) {
  const [now, setNow] = useState(Date.now);
  const [dismissedAttempt, setDismissedAttempt] = useState<string | null>(null);
  const attempt = `${stream.id}:${stream.startedAt}:${stream.observedAt}`;
  useEffect(() => {
    setNow(Date.now());
    if (stream.retryAt === undefined) return;
    const timer = window.setInterval(() => setNow(Date.now()), 250);
    return () => window.clearInterval(timer);
  }, [stream.retryAt]);
  const seconds = Math.max(0, Math.ceil(((stream.retryAt ?? now) - now) / 1000));
  if (dismissedAttempt === attempt) return null;

  return (
    <div className="mb-3 flex shrink-0 flex-wrap items-center gap-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-[12px] text-rose-700">
      <CircleAlert size={17} className="shrink-0" aria-hidden="true" />
      <p role="alert" className="min-w-0 flex-1 break-words">{stream.errorMessage || "本次执行失败，请重试。"}
        <span className="mt-1 block">{stream.serverRejected ? "服务端已返回失败。重新执行可能重复已完成的操作；若再次报错，请复制诊断编号联系管理员检查。" : "连接中断，执行结果待确认。查询只恢复原命令的结果，不会重新执行工具。"}</span>
      </p>
      <div className="flex shrink-0 items-center gap-2">
        <button type="button" onClick={onRetry} className="inline-flex min-h-8 items-center gap-1.5 rounded-lg border border-rose-200 bg-white px-3 font-medium tabular-nums hover:bg-rose-100" title={stream.serverRejected ? "重新执行前将确认，可能重复之前的操作" : "查询原命令的执行结果"}>
          <RefreshCw size={13} aria-hidden="true" />
          {stream.serverRejected ? "重新执行本轮" : stream.retryAt === undefined ? "查询执行结果" : `${seconds} 秒后查询 · 立即查询`}
        </button>
        {stream.retryAt !== undefined && <button type="button" onClick={onCancel} className="min-h-8 rounded-lg px-2 hover:bg-rose-100">取消自动查询</button>}
        <button type="button" onClick={() => { onCancel(); setDismissedAttempt(attempt); }} className="inline-flex h-8 w-8 items-center justify-center rounded-lg hover:bg-rose-100 focus-visible:outline focus-visible:outline-2" aria-label="关闭错误提示" title="关闭提示并取消自动查询，保留回复和诊断记录">
          <X size={15} aria-hidden="true" />
        </button>
      </div>
    </div>
  );
}
