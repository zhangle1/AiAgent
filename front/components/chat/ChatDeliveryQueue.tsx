"use client";

import { useEffect, useState } from "react";
import { cancelChatCommand, getChatDeliveries, type ChatDelivery } from "@/lib/chat-api";
import { removeRecoveryPointer } from "@/lib/chat-command-recovery";

export function ChatDeliveryQueue({ sessionId, sending, onResume, onRunning }: { sessionId: string; sending: boolean; onResume: (commandId: string) => void; onRunning: (row: ChatDelivery) => void }) {
  const [rows, setRows] = useState<ChatDelivery[]>([]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let disposed = false; let timer: ReturnType<typeof setTimeout>; let request: AbortController | undefined;
    async function load() {
      request?.abort(); request = new AbortController();
      try {
        const result = await getChatDeliveries(sessionId, request.signal);
        if (disposed) return;
        setRows(result);
        setError("");
        const running = result.find(row => row.status === "running");
        if (running) onRunning(running);
      }
      catch (ex) { if (!disposed && !request.signal.aborted) setError(ex instanceof Error ? ex.message : "队列读取失败"); }
      finally { if (!disposed) timer = setTimeout(load, 2000); }
    }
    void load();
    return () => { disposed = true; clearTimeout(timer); request?.abort(); };
  }, [sessionId, revision, onRunning]);
  async function remove(row: ChatDelivery) {
    setBusy(row.command_id); setError("");
    try {
      const result = await cancelChatCommand(row.command_id, row.session_id);
      if (!result.accepted) throw new Error("消息状态已变化，请刷新后重试。");
      if (row.status !== "running") {
        removeRecoveryPointer(row.command_id);
        setRows(current => current.filter(item => item.command_id !== row.command_id));
      }
      setRevision(value => value + 1);
    } catch (ex) { setError(ex instanceof Error ? ex.message : "移除失败"); }
    finally { setBusy(null); }
  }
  const pending = rows.filter(row => row.status !== "running");
  const running = sending ? undefined : rows.find(row => row.status === "running");
  if (!running && pending.length === 0) return null;
  return <section aria-label="消息投递队列" className="mb-2 shrink-0 rounded-xl border border-slate-200 bg-white/90 px-3 py-2 text-xs">
    <div className="flex items-center justify-between gap-2"><span className="font-medium text-slate-700">{pending.length > 0 ? `待处理消息 · ${pending.length}` : "正在执行的消息"}</span><button type="button" onClick={() => setRevision(value => value + 1)} className="text-blue-600">刷新队列</button></div>
    {running && <div className="mt-1 flex items-start gap-2 border-t border-slate-200/70 py-2">
      <div className="min-w-0 flex-1"><p className="line-clamp-2 whitespace-pre-wrap break-words text-slate-700">{running.message}</p><p className="mt-1 text-[11px] text-slate-500">此会话上一轮仍在运行</p></div>
      <button type="button" disabled={!!busy} onClick={() => void remove(running)} className="shrink-0 rounded border border-rose-200 px-2 py-1 text-rose-700 disabled:opacity-40">{busy === running.command_id ? "处理中" : "停止当前任务"}</button>
    </div>}
    {pending.length > 0 && <ol className="mt-1 max-h-36 space-y-1 overflow-auto">{pending.map(row => <li key={row.command_id} className="flex items-start gap-2 border-t border-slate-200/70 py-2">
      <div className="min-w-0 flex-1"><p className="line-clamp-2 whitespace-pre-wrap break-words text-slate-700">{row.message}</p><p className="mt-1 text-[11px] text-slate-500">{row.status === "uncertain" ? "结果待确认，核对后可移除记录；不会自动重发" : row.status === "steering" ? "正在确认追加指令" : row.queue_held ? "已暂停，等待恢复" : "已保存，等待前一轮完成"}</p></div>
      {row.status === "queued" && row.queue_held && <button type="button" disabled={!!busy} onClick={() => { try { onResume(row.command_id); setRevision(value => value + 1); } catch (ex) { setError(ex instanceof Error ? ex.message : "恢复失败"); } }} className="shrink-0 rounded border border-blue-200 bg-white px-2 py-1 text-blue-700 disabled:opacity-40">恢复执行</button>}
      {row.status !== "steering" && <button type="button" disabled={!!busy} onClick={() => void remove(row)} className="shrink-0 rounded px-1 py-1 text-slate-500 disabled:opacity-40">{busy === row.command_id ? "处理中" : row.status === "uncertain" ? "移除记录" : "取消"}</button>}
    </li>)}</ol>}
    {error && <p role="alert" className="mt-2 break-words text-rose-600">{error}</p>}
  </section>;
}
