"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { CheckCircle2, CircleAlert, Clock3, ListTodo, RefreshCw, RotateCcw, Square } from "lucide-react";
import { cancelTaskCenterTask, listTaskCenter, retryTaskCenterTask } from "@/lib/task-center-api";
import type { TaskCenterTask } from "@/lib/task-center-types";

const labels: Record<string, string> = { queued: "排队中", processing: "处理中", cancelling: "正在取消", completed: "已完成", failed: "失败", cancelled: "已取消" };

export function TaskCenterPage() {
  const [tasks, setTasks] = useState<TaskCenterTask[]>([]);
  const [summary, setSummary] = useState({ total: 0, queued: 0, processing: 0, completed: 0, failed: 0, cancelled: 0 });
  const [status, setStatus] = useState("");
  const [busy, setBusy] = useState<number | null>(null);
  const [error, setError] = useState("");

  async function load(signal?: AbortSignal) {
    try {
      const result = await listTaskCenter({ domain: "knowledge", status, signal });
      if (!signal?.aborted) { setTasks(result.tasks); setSummary(result.summary); setError(""); }
    } catch (ex) {
      if (!signal?.aborted) setError(ex instanceof Error ? ex.message : "任务中心加载失败");
    }
  }

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    const timer = setInterval(() => void load(controller.signal), 2500);
    return () => { controller.abort(); clearInterval(timer); };
  }, [status]);

  async function action(task: TaskCenterTask, kind: "cancel" | "retry") {
    setBusy(task.id); setError("");
    try {
      if (kind === "cancel") await cancelTaskCenterTask(task.domain, task.id);
      else await retryTaskCenterTask(task.domain, task.id);
      await load();
    } catch (ex) { setError(ex instanceof Error ? ex.message : "任务操作失败"); }
    finally { setBusy(null); }
  }

  return <main className="mx-auto min-h-screen max-w-7xl bg-slate-50 px-6 py-8">
    <header className="flex flex-wrap items-start justify-between gap-4"><div><p className="text-xs font-semibold tracking-[.16em] text-blue-600">TASK CENTER</p><h1 className="mt-1 text-2xl font-semibold text-slate-950">任务中心</h1><p className="mt-2 text-sm text-slate-500">集中查看文档解析、语义处理和索引任务；后续任务域共用这条队列。</p></div><button type="button" onClick={() => void load()} className="inline-flex h-9 items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 text-sm hover:border-blue-300"><RefreshCw size={15} />刷新</button></header>
    {error && <div className="mt-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>}
    <section className="mt-6 grid gap-3 sm:grid-cols-2 lg:grid-cols-5">{[
      { label: "全部", value: summary.total, icon: ListTodo, filter: "" },
      { label: "排队中", value: summary.queued, icon: Clock3, filter: "queued" },
      { label: "处理中", value: summary.processing, icon: RefreshCw, filter: "processing" },
      { label: "已完成", value: summary.completed, icon: CheckCircle2, filter: "completed" },
      { label: "失败", value: summary.failed, icon: CircleAlert, filter: "failed" },
    ].map(({ label, value, icon: Icon, filter }) => <button key={label} type="button" onClick={() => setStatus(filter)} className={`rounded-xl border bg-white p-4 text-left transition hover:border-blue-300 ${status === filter ? "border-blue-400 ring-2 ring-blue-50" : "border-slate-200"}`}><Icon size={17} className="text-slate-500" /><p className="mt-3 text-xs text-slate-500">{label}</p><p className="mt-1 text-2xl font-semibold text-slate-950">{value}</p></button>)}</section>
    <section className="mt-6 overflow-hidden rounded-xl border border-slate-200 bg-white"><div className="flex items-center justify-between border-b border-slate-100 px-4 py-3"><div><h2 className="font-semibold text-slate-900">知识任务队列</h2><p className="mt-1 text-xs text-slate-500">当前先接入 knowledge 任务域，任务执行仍由知识模块 worker 负责。</p></div><Link href="/knowledge" className="text-xs text-blue-600 hover:underline">返回知识中心</Link></div>{tasks.length === 0 ? <div className="grid min-h-52 place-items-center text-sm text-slate-400">暂无匹配的后台任务</div> : <div className="divide-y divide-slate-100">{tasks.map(task => <article key={`${task.domain}:${task.id}`} className="p-4"><div className="flex flex-wrap items-center gap-3"><span className="rounded bg-blue-50 px-2 py-1 text-[11px] font-medium text-blue-700">{task.domain}</span><span className="min-w-0 flex-1 truncate text-sm font-medium text-slate-900">{task.title}</span><span className="text-xs text-slate-500">{labels[task.status] || task.status}</span>{task.cancellable && <button type="button" disabled={busy === task.id} onClick={() => void action(task, "cancel")} className="inline-flex items-center gap-1 rounded border border-slate-200 px-2 py-1 text-xs hover:border-red-300 hover:text-red-600 disabled:opacity-50"><Square size={12} />取消</button>}{task.retryable && <button type="button" disabled={busy === task.id} onClick={() => void action(task, "retry")} className="inline-flex items-center gap-1 rounded border border-slate-200 px-2 py-1 text-xs hover:border-blue-300 hover:text-blue-600 disabled:opacity-50"><RotateCcw size={12} />重试</button>}</div><div className="mt-2 flex items-center gap-3"><progress value={task.progress} max={100} className="h-2 flex-1 accent-blue-600" /><span className="w-10 text-right text-xs text-slate-500">{task.progress}%</span></div><p className={`mt-2 rounded-md px-2 py-1 text-xs ${task.status === "failed" ? "border border-red-100 bg-red-50 text-red-700" : "text-slate-500"}`}>{task.status === "failed" ? `错误：${task.error_message || task.message || "任务失败，未返回错误详情"}` : task.message || task.resource_uri || "等待任务信息"}</p></article>)}</div>}</section>
  </main>;
}
