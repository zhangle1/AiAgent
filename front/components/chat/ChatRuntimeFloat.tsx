"use client";

import { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { Activity, ExternalLink, RefreshCw, Square, X } from "lucide-react";
import { listChatRuntimeJobs, stopChatRuntime } from "@/lib/chat-runtime-api";
import { getCodeProjectRuntime, stopCodeProjectRuntime } from "@/lib/code-runtime-api";
import type { CodeRuntimeRun } from "@/lib/code-runtime-types";
import { runtimeTestHref, runtimeJobAccessUrl, runtimeAccessUrl } from "@/lib/chat-runtime";
import type { ChatRuntimeJob } from "@/lib/chat-runtime-types";

const labels = { waiting: "等待 AI 清单", starting: "启动 / 就绪检查中", running: "可测试", failed: "运行失败", stopped: "已关闭", expired: "请求已过期" };
export function ChatRuntimeFloat({ projectId, onLogs }: { projectId: number; onLogs: () => void }) {
  const [open, setOpen] = useState(false);
  const [jobs, setJobs] = useState<ChatRuntimeJob[]>([]);
  const [runs, setRuns] = useState<CodeRuntimeRun[]>([]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState("");
  const [revision, setRevision] = useState(0);
  useEffect(() => { setJobs([]); setRuns([]); setError(""); }, [projectId]);
  useEffect(() => {
    let active = true; let timer: ReturnType<typeof setTimeout>;
    async function refresh() {
      try {
        const [groups, processes] = await Promise.allSettled([listChatRuntimeJobs(projectId), getCodeProjectRuntime(projectId)]);
        if (!active) return;
        const errors: string[] = [];
        if (groups.status === "fulfilled") setJobs(groups.value);
        else errors.push(groups.reason instanceof Error ? groups.reason.message : "运行请求读取失败");
        if (processes.status === "fulfilled" && Array.isArray(processes.value?.runs)) setRuns(processes.value.runs);
        else errors.push(processes.status === "rejected" && processes.reason instanceof Error ? processes.reason.message : "运行进程列表无效");
        setError(errors.join("；"));
      }
      catch (reason) { if (active) setError(reason instanceof Error ? reason.message : "进程探测失败"); }
      finally { if (active) timer = setTimeout(refresh, 3000); }
    }
    void refresh();
    const changed = () => setRevision(value => value + 1);
    window.addEventListener("aiagent:runtime-refresh", changed);
    return () => { active = false; clearTimeout(timer); window.removeEventListener("aiagent:runtime-refresh", changed); };
  }, [projectId, revision]);
  async function stop(id: string, individual = false) {
    setBusy(id);
    try { if (individual) await stopCodeProjectRuntime(projectId, id); else await stopChatRuntime(projectId, id); setRevision(value => value + 1); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "关闭失败"); }
    finally { setBusy(""); }
  }
  if (typeof document === "undefined") return null;
  const groupedIds = new Set(jobs.flatMap(job => job.runs.map(run => run.run_id)));
  const otherRuns = runs.filter(run => !groupedIds.has(run.run_id));
  const activeRuns = otherRuns.filter(run => ["starting", "running", "stopping"].includes(run.status));
  const canStopJob = (job: ChatRuntimeJob) => ["waiting", "starting", "running"].includes(job.status) || job.runs.some(run => ["starting", "running", "stopping"].includes(run.status));
  const active = jobs.filter(canStopJob);
  return createPortal(<div className="fixed bottom-24 right-4 z-[80] flex max-w-[calc(100vw-2rem)] flex-col items-end gap-2 sm:bottom-5">
    {open && <section aria-label="AI 运行进程" className="flex max-h-[65dvh] w-96 max-w-full flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white text-slate-800 shadow-2xl dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100">
      <header className="flex items-center gap-2 border-b p-3"><Activity size={17}/><strong className="flex-1 text-sm">AI 运行进程</strong><button aria-label="刷新运行进程" onClick={() => setRevision(value => value + 1)}><RefreshCw size={15}/></button><button aria-label="收起运行进程" onClick={() => setOpen(false)}><X size={17}/></button></header>
      <div className="space-y-3 overflow-y-auto p-3">{error && <p role="alert" className="text-xs text-rose-600">{error}</p>}{!error && !jobs.length && !otherRuns.length && <p className="text-xs text-slate-500">当前项目暂无托管运行进程。选择“配置 AI 运行”并发送聊天后，启动状态和对应进程会显示在这里。</p>}{jobs.map(job => <article key={job.request_id} className="space-y-2 rounded-xl border border-slate-200 p-3 dark:border-slate-700">
        <div className="flex items-center justify-between gap-2"><span className="text-xs font-semibold">{labels[job.status]}</span>{canStopJob(job) && <button disabled={busy === job.request_id} onClick={() => void stop(job.request_id)} className="inline-flex items-center gap-1 text-xs text-rose-600 disabled:opacity-50"><Square size={12}/>关闭整组</button>}</div>
        <p className="break-words text-xs text-slate-500">{job.message || "等待聊天 AI 生成启动清单"}</p>
        {job.runs.map(run => <div key={run.run_id} className="break-all rounded-lg bg-slate-50 p-2 text-[11px] dark:bg-slate-800"><strong>{run.repository_name} · {run.role === "frontend" ? "前端" : "后端"}</strong><p>{run.entry_path}</p><p>PID {run.process_id ?? "—"} · :{run.port} · {run.status}</p></div>)}
        <p className="text-[11px] text-slate-500">测试窗口停止续期 {job.idle_minutes} 分钟后关闭</p>
        {job.status === "running" && <a href={runtimeJobAccessUrl(window.location.origin, job) || runtimeTestHref(projectId, job.request_id)} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 text-xs font-semibold text-blue-600"><ExternalLink size={13}/>新窗口测试</a>}
        <a href={`${runtimeTestHref(projectId, job.request_id)}&mode=manage`} target="_blank" rel="noopener noreferrer" className="ml-2 text-xs text-blue-600">管理 / 续期</a>
      </article>)}{otherRuns.map(run => <article key={run.run_id} className="space-y-2 rounded-xl border p-3 text-xs">
        <strong>{run.repository_name} · {run.role === "frontend" ? "前端" : "后端"}</strong>
        <p className="break-all">{run.entry_path}</p><p>PID {run.process_id ?? "—"} · :{run.port} · {run.status}</p>
        {["starting", "running", "stopping"].includes(run.status) && <button disabled={busy === run.run_id} onClick={() => void stop(run.run_id, true)} className="text-rose-600 disabled:opacity-50">结束对应进程</button>}
        {run.status === "running" && <a href={runtimeAccessUrl(window.location.origin, run.port)} target="_blank" rel="noopener noreferrer" className="ml-3 text-blue-600">打开应用 :{run.port} ↗</a>}
      </article>)}</div><footer className="border-t p-3"><button onClick={onLogs} className="text-xs text-blue-600">打开实时终端与日志</button><p className="mt-1 text-[10px] text-slate-500">每 3 秒探测。直接打开应用和查看浮窗均不续期；需要续期请打开“管理 / 续期”。</p></footer>
    </section>}
    <button onClick={() => setOpen(value => !value)} aria-expanded={open} aria-label="AI 进程浮窗" className="flex items-center gap-2 rounded-full bg-slate-900 px-4 py-3 text-xs font-medium text-white shadow-xl ring-1 ring-white/20"><Activity size={16} className={(active.length + activeRuns.length) ? "text-emerald-400" : "text-slate-400"}/>运行进程{(active.length + activeRuns.length) ? ` · ${active.length + activeRuns.length}` : ""}</button>
  </div>, document.body);
}
