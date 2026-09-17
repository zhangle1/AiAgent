"use client";

import { useEffect, useRef, useState } from "react";
import { cancelKnowledgeCompilation, compileKnowledgeDocument, getKnowledgeCompilations } from "@/lib/knowledge-api";
import type { KnowledgeCompilationJob } from "@/lib/knowledge-types";

export const knowledgeTasksChanged = "knowledge-tasks-changed";
const active = (job: KnowledgeCompilationJob) => ["queued", "processing", "cancelling"].includes(job.status);
const labels: Record<string, string> = { queued: "排队中", processing: "提炼中", cancelling: "正在取消", cancelled: "已取消", success: "已完成", error: "失败" };
function elapsed(value?: string | null) {
  if (!value) return 0;
  const date = /(?:Z|[+-]\d\d:\d\d)$/i.test(value) ? value : value + "Z";
  return Math.max(0, Math.floor((Date.now() - new Date(date).getTime()) / 1000));
}

export function KnowledgeTaskQueue({ onCompleted }: { onCompleted: () => void }) {
  const [jobs, setJobs] = useState<KnowledgeCompilationJob[]>([]);
  const [error, setError] = useState("");
  const [open, setOpen] = useState(true);
  const [busy, setBusy] = useState<number | null>(null);
  const [revision, setRevision] = useState(0);
  const previous = useRef<KnowledgeCompilationJob[]>([]);
  const completed = useRef(onCompleted);
  completed.current = onCompleted;
  useEffect(() => {
    const update = () => setRevision(value => value + 1);
    window.addEventListener(knowledgeTasksChanged, update);
    return () => window.removeEventListener(knowledgeTasksChanged, update);
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout>;
    async function poll() {
      try {
        const rows = await getKnowledgeCompilations(controller.signal);
        if (controller.signal.aborted) return;
        const ended = rows.some(row => !active(row) && previous.current.some(old => old.id === row.id && active(old)));
        previous.current = rows;
        setJobs(rows); setError("");
        if (ended) completed.current();
      } catch (ex) {
        if (!controller.signal.aborted) setError(`任务状态刷新失败，正在重连：${ex instanceof Error ? ex.message : String(ex)}`);
      } finally {
        if (!controller.signal.aborted) timer = setTimeout(() => void poll(), 2000);
      }
    }
    void poll();
    return () => { controller.abort(); clearTimeout(timer); };
  }, [revision]);

  async function action(job: KnowledgeCompilationJob, retry: boolean) {
    setBusy(job.id); setError("");
    try {
      if (retry && job.document_id) await compileKnowledgeDocument(job.knowledge_base_name, job.document_id);
      else await cancelKnowledgeCompilation(job.knowledge_base_name, job.id);
      window.dispatchEvent(new Event(knowledgeTasksChanged));
    } catch (ex) { setError(ex instanceof Error ? ex.message : String(ex)); }
    finally { setBusy(null); }
  }
  const running = jobs.filter(active).length;
  return <section className="border-b border-blue-100 bg-blue-50/40 px-6 py-3" aria-label="知识提炼任务队列">
    <button type="button" onClick={() => setOpen(!open)} aria-expanded={open} className="flex w-full items-center justify-between text-sm font-medium">
      <span>提炼任务队列 · {running ? `${running} 个进行中` : "暂无进行中的任务"}</span><span>{open ? "收起" : "展开"}</span>
    </button>
    {error && <p role="alert" className="mt-2 text-xs text-red-700">{error}</p>}
    {open && jobs.length > 0 && <div className="mt-3 max-h-60 space-y-2 overflow-auto">
      {jobs.map(job => <div key={job.id} className="rounded-md border border-slate-200 bg-white p-3">
        <div className="flex items-center gap-3 text-xs">
          <span className="min-w-0 flex-1 truncate font-medium" title={job.document_name || ""}>{job.knowledge_base_name} / {job.document_name || `文档 ${job.document_id}`} <span className="text-slate-500">#{job.id}</span></span>
          <span>{labels[job.status] || job.status}</span>
          {active(job) ? <button disabled={busy === job.id || job.status === "cancelling"} onClick={() => void action(job, false)} className="rounded border px-2 py-1 disabled:opacity-50">取消</button> :
            ["error", "cancelled"].includes(job.status) && <button disabled={busy !== null || jobs.some(other => active(other) && other.document_id === job.document_id)} onClick={() => void action(job, true)} className="rounded border px-2 py-1 disabled:opacity-50">重试</button>}
        </div>
        <div className="mt-2 text-xs text-slate-600">{job.message}</div>
        <progress aria-label={`${job.document_name || "文档"}提炼进度`} value={job.progress} max={100} className="mt-2 h-2 w-full accent-blue-600" />
        <div className="mt-1 flex justify-between text-[11px] text-slate-500"><span>{active(job) ? `已${job.status === "queued" ? "排队" : "运行"} ${elapsed(job.started_at || job.created_at)} 秒` : "任务已结束"}</span><span>{job.progress}% · 按原文覆盖与处理阶段计算</span></div>
        {job.status === "processing" && elapsed(job.updated_at || job.started_at) > 90 && <p className="mt-1 text-xs text-amber-700">当前阶段超过 90 秒没有新进展，可继续等待或取消后检查模型连接。</p>}
      </div>)}
    </div>}
  </section>;
}
