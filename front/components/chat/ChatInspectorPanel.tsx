"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Globe2, Code2, FileCode2, ListTodo, Loader2, PanelRightClose, RefreshCw, Terminal } from "lucide-react";
import { getCodeProjectRuntime, getCodeRuntimeLogs } from "@/lib/code-runtime-api";
import { getCodeFile } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";
import type { CodeProjectRuntime, CodeRuntimeLog } from "@/lib/code-runtime-types";

export type ChatCodeFileReference = {
  repositoryName: string;
  filePath: string;
  line?: number;
};

export function ChatInspectorPanel({ isOpen, project, fileReference, requestedTab, onClose }: { isOpen: boolean; project: CodeProject | null; fileReference: ChatCodeFileReference | null; requestedTab?: "preview" | "file" | "tasks" | "terminal" | null; onClose: () => void }) {
  const [tab, setTab] = useState<"preview" | "file" | "tasks" | "terminal">("preview");
  const [runtime, setRuntime] = useState<CodeProjectRuntime | null>(null);
  const [activeRunId, setActiveRunId] = useState<string | null>(null);
  const [file, setFile] = useState<{ path: string; content: string; line_count: number } | null>(null);
  const [loadingFile, setLoadingFile] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [runtimeLogs, setRuntimeLogs] = useState<Record<string, CodeRuntimeLog[]>>({});
  const runtimeSequences = useRef<Record<string, number>>({});

  const previewRuns = useMemo(() => runtime?.runs.filter((run) => run.role === "frontend" && (run.status === "starting" || run.status === "running")) ?? [], [runtime]);
  const activeRun = previewRuns.find((run) => run.run_id === activeRunId) ?? previewRuns[0] ?? null;

  useEffect(() => {
    if (requestedTab) setTab(requestedTab);
  }, [requestedTab]);

  useEffect(() => {
    if (!isOpen || !project) { setRuntime(null); return; }
    let disposed = false;
    const refresh = async () => {
      try {
        const value = await getCodeProjectRuntime(project.id);
        if (disposed) return;
        setRuntime(value);
        setError(null);
        const entries = await Promise.all(value.runs.map(async (run) => {
          try {
            const after = runtimeSequences.current[run.run_id] ?? 0;
            const logs = await getCodeRuntimeLogs(run.run_id, after);
            return { runId: run.run_id, logs };
          } catch {
            return { runId: run.run_id, logs: [] };
          }
        }));
        if (disposed) return;
        setRuntimeLogs((current) => {
          const next = { ...current };
          for (const entry of entries) {
            if (!entry.logs.length) continue;
            runtimeSequences.current[entry.runId] = entry.logs[entry.logs.length - 1].sequence;
            next[entry.runId] = [...(next[entry.runId] ?? []), ...entry.logs].slice(-800);
          }
          return next;
        });
      } catch (ex) { if (!disposed) setError(ex instanceof Error ? ex.message : "无法读取项目运行状态。"); }
    };
    void refresh();
    const timer = window.setInterval(() => void refresh(), 3000);
    return () => { disposed = true; window.clearInterval(timer); };
  }, [isOpen, project]);

  useEffect(() => {
    if (!fileReference) return;
    let disposed = false;
    setTab("file");
    setLoadingFile(true);
    setError(null);
    void getCodeFile(fileReference.repositoryName, fileReference.filePath)
      .then((value) => { if (!disposed) setFile(value); })
      .catch((ex) => { if (!disposed) setError(ex instanceof Error ? ex.message : "无法读取代码文件。"); })
      .finally(() => { if (!disposed) setLoadingFile(false); });
    return () => { disposed = true; };
  }, [fileReference]);

  useEffect(() => {
    if (!fileReference?.line) return;
    const timer = window.setTimeout(() => document.getElementById(`chat-code-line-${fileReference.line}`)?.scrollIntoView({ block: "center" }), 80);
    return () => window.clearTimeout(timer);
  }, [file, fileReference]);

  if (!isOpen) return null;
  const lines = file?.content.split("\n").slice(0, 2500) ?? [];
  const line = fileReference?.line;

  return (
    <aside className="flex h-full w-[380px] max-w-[48vw] shrink-0 flex-col border-l border-slate-200 bg-white shadow-[-16px_0_40px_rgba(15,23,42,0.08)]">
      <div className="flex h-16 shrink-0 items-center justify-between border-b border-slate-200 px-4">
        <div className="flex min-w-0 items-center gap-1 rounded-lg bg-slate-100 p-1">
          <button type="button" onClick={() => setTab("file")} className={`inline-flex items-center gap-1.5 rounded-md px-2 py-1.5 text-xs font-medium ${tab === "file" ? "bg-white text-blue-700 shadow-sm" : "text-slate-500"}`}><FileCode2 size={14}/>文件</button>
          <button type="button" onClick={() => setTab("tasks")} className={`inline-flex items-center gap-1.5 rounded-md px-2 py-1.5 text-xs font-medium ${tab === "tasks" ? "bg-white text-blue-700 shadow-sm" : "text-slate-500"}`}><ListTodo size={14}/>任务</button>
          <button type="button" onClick={() => setTab("preview")} className={`inline-flex items-center gap-1.5 rounded-md px-2 py-1.5 text-xs font-medium ${tab === "preview" ? "bg-white text-blue-700 shadow-sm" : "text-slate-500"}`}><Globe2 size={14}/>浏览器</button>
          <button type="button" onClick={() => setTab("terminal")} className={`inline-flex items-center gap-1.5 rounded-md px-2 py-1.5 text-xs font-medium ${tab === "terminal" ? "bg-white text-blue-700 shadow-sm" : "text-slate-500"}`}><Terminal size={14}/>终端</button>
        </div>
        <button type="button" onClick={onClose} className="grid h-8 w-8 place-items-center rounded-lg text-slate-500 hover:bg-slate-100" aria-label="关闭右侧面板"><PanelRightClose size={17}/></button>
      </div>

      {tab === "preview" ? (
        <div className="flex min-h-0 flex-1 flex-col">
          <div className="flex items-center gap-2 border-b border-slate-100 px-3 py-2">
            <Code2 size={14} className="text-blue-600"/>
            <select value={activeRun?.run_id ?? ""} onChange={(event) => setActiveRunId(event.target.value)} className="min-w-0 flex-1 bg-transparent text-xs font-medium text-slate-700 outline-none">
              <option value="">{project ? "没有正在运行的前端程序" : "请先选择项目"}</option>
              {previewRuns.map((run) => <option key={run.run_id} value={run.run_id}>{run.repository_name} · {run.status} · :{run.port}</option>)}
            </select>
            <button type="button" onClick={() => project && void getCodeProjectRuntime(project.id).then(setRuntime)} className="grid h-7 w-7 place-items-center rounded-md text-slate-500 hover:bg-slate-100" aria-label="刷新运行状态"><RefreshCw size={14}/></button>
          </div>
          {activeRun?.preview_url ? <iframe title={`${activeRun.repository_name} preview`} src={activeRun.preview_url} className="min-h-0 flex-1 bg-white" sandbox="allow-scripts allow-forms allow-modals allow-popups" /> : <div className="flex flex-1 items-center justify-center px-8 text-center text-sm leading-6 text-slate-500">启动已配置的前端程序后，会在这里通过受控同源代理显示预览。</div>}
        </div>
      ) : tab === "file" ? (
        <div className="min-h-0 flex-1 overflow-hidden">
          <div className="border-b border-slate-100 px-4 py-3"><p className="truncate text-xs font-semibold text-slate-800">{file?.path || fileReference?.filePath || "选择代码引用以查看文件"}</p><p className="mt-1 text-[11px] text-slate-400">{file ? `${file.line_count} 行` : "代码文件仅在已注册仓库范围内读取"}</p></div>
          {loadingFile ? <div className="flex h-32 items-center justify-center gap-2 text-sm text-slate-500"><Loader2 size={15} className="animate-spin"/>读取文件中…</div> : file ? <pre className="workspace-scroll h-full overflow-auto bg-slate-950 py-3 text-[12px] leading-6 text-slate-100">{lines.map((content, index) => { const number = index + 1; const highlighted = number === line; return <div id={`chat-code-line-${number}`} key={number} className={`flex min-w-max px-4 ${highlighted ? "bg-amber-300/20 ring-1 ring-inset ring-amber-300/50" : ""}`}><span className="mr-4 w-10 select-none text-right text-slate-500">{number}</span><code className="whitespace-pre">{content || " "}</code></div>; })}{file.line_count > lines.length && <p className="px-4 pt-2 text-slate-500">为保持面板流畅，仅显示前 {lines.length} 行。</p>}</pre> : <div className="flex h-32 items-center justify-center px-8 text-center text-sm leading-6 text-slate-500">从聊天结果中的代码引用卡片打开文件。</div>}
        </div>
      ) : tab === "tasks" ? <SideTaskTab project={project} runtime={runtime} /> : <RuntimeTerminalTab runtime={runtime} logs={runtimeLogs} />}
      {error && <div className="border-t border-rose-100 bg-rose-50 px-3 py-2 text-xs text-rose-700">{error}</div>}
    </aside>
  );
}

function SideTaskTab({ project, runtime }: { project: CodeProject | null; runtime: CodeProjectRuntime | null }) {
  const activeRuns = runtime?.runs.filter((run) => ["starting", "running", "stopping"].includes(run.status)) ?? [];
  return <div className="flex min-h-0 flex-1 flex-col p-4"><div className="rounded-xl border border-slate-200 bg-slate-50 p-3"><p className="text-xs font-semibold text-slate-800">当前上下文</p><p className="mt-1 text-xs leading-5 text-slate-500">{project ? `${project.display_name} · 可从聊天、文件和终端之间切换。` : "请选择项目后，可在这里查看关联的运行任务。"}</p></div><div className="mt-4 flex min-h-0 flex-1 flex-col"><div className="flex items-center justify-between"><p className="text-xs font-semibold text-slate-700">运行任务</p><span className="text-[11px] text-slate-400">{activeRuns.length}</span></div>{activeRuns.length ? <div className="mt-2 space-y-2">{activeRuns.map((run) => <div key={run.run_id} className="rounded-lg border border-slate-200 px-3 py-2"><p className="truncate text-xs font-medium text-slate-800">{run.repository_name} · {run.role}</p><p className="mt-1 font-mono text-[11px] text-slate-500">:{run.port} · {run.status}</p></div>)}</div> : <div className="mt-2 flex flex-1 items-center justify-center rounded-xl border border-dashed border-slate-200 px-6 text-center text-xs leading-5 text-slate-500">暂无正在运行的任务。启动项目后，进程状态会显示在这里，并可切换到终端查看输出。</div>}</div></div>;
}

function RuntimeTerminalTab({ runtime, logs }: { runtime: CodeProjectRuntime | null; logs: Record<string, CodeRuntimeLog[]> }) {
  const [activeRunId, setActiveRunId] = useState<string | null>(null);
  const activeRun = runtime?.runs.find((run) => run.run_id === activeRunId) ?? runtime?.runs[0] ?? null;
  const lines = activeRun ? logs[activeRun.run_id] ?? [] : [];

  return <div className="flex min-h-0 flex-1 flex-col">
    <div className="flex shrink-0 gap-1 overflow-x-auto border-b border-slate-100 px-2 py-2">
      {runtime?.runs.map((run) => <button key={run.run_id} type="button" onClick={() => setActiveRunId(run.run_id)} className={`shrink-0 rounded-md px-2 py-1.5 text-[11px] font-medium ${activeRun?.run_id === run.run_id ? "bg-blue-50 text-blue-700" : "text-slate-500 hover:bg-slate-100"}`}><span className={`mr-1 inline-block h-1.5 w-1.5 rounded-full ${run.status === "running" ? "bg-emerald-500" : run.status === "failed" ? "bg-rose-500" : "bg-amber-400"}`}/>{run.repository_name} · {run.role}</button>)}
    </div>
    {activeRun ? <>
      <div className="flex shrink-0 items-center justify-between border-b border-slate-100 px-3 py-2 text-[11px] text-slate-500"><span className="truncate font-mono">:{activeRun.port} · {activeRun.status}</span><span>实时 Shell 输出</span></div>
      <pre className="workspace-scroll min-h-0 flex-1 overflow-auto bg-slate-950 p-3 font-mono text-[11px] leading-5 text-slate-100">{lines.length ? lines.map((item) => <span key={item.sequence} className={`block whitespace-pre-wrap break-all ${item.stream === "stderr" ? "text-amber-300" : item.stream === "system" ? "text-sky-300" : "text-emerald-300"}`}>{item.line}</span>) : <span className="text-slate-400">等待程序输出…</span>}</pre>
    </> : <div className="flex flex-1 items-center justify-center px-8 text-center text-sm leading-6 text-slate-500">从顶部“项目程序运行”启动已配置的前后端后，实时输出会显示在此处。</div>}
  </div>;
}
