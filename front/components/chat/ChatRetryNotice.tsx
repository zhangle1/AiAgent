"use client";

import { useEffect, useState } from "react";
import { CircleAlert, RefreshCw } from "lucide-react";
import type { ChatStreamRecord } from "./ChatStreamProvider";

export function ChatRetryNotice({ stream, onRetry, onCancel }: {
  stream: ChatStreamRecord;
  onRetry: () => void;
  onCancel: () => void;
}) {
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    setNow(Date.now());
    if (stream.retryAt === undefined) return;
    const timer = window.setInterval(() => setNow(Date.now()), 250);
    return () => window.clearInterval(timer);
  }, [stream.retryAt]);
  const seconds = Math.max(0, Math.ceil(((stream.retryAt ?? now) - now) / 1000));

  return (
    <div className="mb-3 flex shrink-0 flex-wrap items-center gap-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-[12px] text-rose-700">
      <CircleAlert size={17} className="shrink-0" aria-hidden="true" />
      <p role="alert" className="min-w-0 flex-1 break-words">{stream.errorMessage || "本次执行失败，请重试。"}</p>
      <div className="flex shrink-0 items-center gap-2">
        <button type="button" onClick={onRetry} className="inline-flex min-h-8 items-center gap-1.5 rounded-lg border border-rose-200 bg-white px-3 font-medium tabular-nums hover:bg-rose-100" title="立即重试本轮消息">
          <RefreshCw size={13} aria-hidden="true" />
          {stream.retryAt === undefined ? "点击重试" : `${seconds} 秒后重试 · 立即重试`}
        </button>
        {stream.retryAt !== undefined && <button type="button" onClick={onCancel} className="min-h-8 rounded-lg px-2 hover:bg-rose-100">取消自动重试</button>}
      </div>
    </div>
  );
}
