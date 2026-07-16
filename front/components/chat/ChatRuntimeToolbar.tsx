"use client";

import { useEffect, useRef, useState } from "react";
import { ChevronDown, Loader2, PanelLeftClose, PanelLeftOpen, PanelRightOpen, Play, RefreshCw, Square, Terminal } from "lucide-react";
import { getCodeProjectRuntime, startCodeProjectRuntime, stopCodeProjectRuntime } from "@/lib/code-runtime-api";
import type { CodeProject } from "@/lib/code-repository-types";
import type { CodeProjectRuntime } from "@/lib/code-runtime-types";

export function ChatRuntimeToolbar({ project, rightPanelOpen, onToggleRightPanel, onOpenRuntimePanel }: { project: CodeProject | null; rightPanelOpen: boolean; onToggleRightPanel: () => void; onOpenRuntimePanel: () => void }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [runtime, setRuntime] = useState<CodeProjectRuntime | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!menuOpen || !project) return;
    void refresh();
  // The selected project is the only runtime context for this menu.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [menuOpen, project?.id]);

  useEffect(() => {
    if (!menuOpen) return;
    const closeOutside = (event: PointerEvent) => {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) setMenuOpen(false);
    };
    document.addEventListener("pointerdown", closeOutside);
    return () => document.removeEventListener("pointerdown", closeOutside);
  }, [menuOpen]);

  async function refresh() {
    if (!project) return;
    try { setRuntime(await getCodeProjectRuntime(project.id)); setError(null); }
    catch (ex) { setError(ex instanceof Error ? ex.message : "无法读取运行状态。"); }
  }

  async function start() {
    if (!project) return;
    setBusy(true); setError(null);
    try {
      await startCodeProjectRuntime(project.id);
      await refresh();
      onOpenRuntimePanel();
    }
    catch (ex) { setError(ex instanceof Error ? ex.message : "启动失败。"); }
    finally { setBusy(false); }
  }

  async function stop(runId: string) {
    if (!project) return;
    setBusy(true); setError(null);
    try { await stopCodeProjectRuntime(project.id, runId); await refresh(); }
    catch (ex) { setError(ex instanceof Error ? ex.message : "停止失败。"); }
    finally { setBusy(false); }
  }

  return <div className="relative flex items-center gap-1.5">
    <button type="button" onClick={() => window.dispatchEvent(new Event("aiagent:sidebar-toggle"))} className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 bg-white text-slate-600 shadow-sm hover:border-blue-300 hover:text-blue-600" aria-label="显示或隐藏侧边栏"><PanelLeftClose size={15}/></button>
    <div ref={menuRef} className="relative">
      <button type="button" onClick={() => setMenuOpen((current) => !current)} className={`inline-flex h-8 items-center gap-1.5 rounded-lg border px-2.5 text-xs font-medium shadow-sm ${menuOpen ? "border-blue-300 bg-blue-50 text-blue-700" : "border-slate-200 bg-white text-slate-600 hover:border-blue-300 hover:text-blue-600"}`} aria-expanded={menuOpen}><Terminal size={14}/>项目程序运行<ChevronDown size={13} className={menuOpen ? "rotate-180 transition" : "transition"}/></button>
      {menuOpen && <div className="absolute right-0 top-10 z-50 w-[350px] overflow-hidden rounded-xl border border-slate-200 bg-white p-3 shadow-[0_18px_42px_rgba(15,23,42,0.2)]">
        <div className="mb-3 flex items-center justify-between gap-3"><div><p className="text-sm font-semibold text-slate-900">项目程序运行</p><p className="mt-0.5 text-[11px] text-slate-500">{project ? `${project.display_name} · 自动分配开发端口` : "请先在聊天底部选择项目"}</p></div><button type="button" onClick={() => void refresh()} className="grid h-7 w-7 place-items-center rounded-md text-slate-500 hover:bg-slate-100" aria-label="刷新"><RefreshCw size={14}/></button></div>
        {project && <button type="button" disabled={busy} onClick={() => void start()} className="mb-2 inline-flex h-9 w-full items-center justify-center gap-2 rounded-lg bg-blue-600 text-xs font-semibold text-white hover:bg-blue-700 disabled:bg-slate-300">{busy ? <Loader2 size={14} className="animate-spin"/> : <Play size={14}/>}启动已配置程序</button>}
        <div className="space-y-1.5">{runtime?.runs.length ? runtime.runs.map((run) => <div key={run.run_id} className="flex items-center gap-2 rounded-lg border border-slate-100 bg-slate-50 px-2.5 py-2"><span className={`h-2 w-2 rounded-full ${run.status === "running" ? "bg-emerald-500" : run.status === "failed" ? "bg-rose-500" : "bg-amber-400"}`}/><span className="min-w-0 flex-1 truncate text-xs text-slate-700">{run.repository_name} · {run.role} · :{run.port}</span>{["starting", "running", "stopping"].includes(run.status) && <button type="button" disabled={busy} onClick={() => void stop(run.run_id)} className="grid h-7 w-7 place-items-center rounded-md text-rose-600 hover:bg-rose-50" aria-label="停止"><Square size={13}/></button>}</div>) : <p className="rounded-lg bg-slate-50 px-3 py-3 text-xs leading-5 text-slate-500">尚未检测到可运行配置。C# 仓库请选择 `.sln` 或 `.csproj`，前端仓库请选择 `package.json`。</p>}</div>
        {runtime?.runs.length ? <button type="button" onClick={onOpenRuntimePanel} className="mt-2 inline-flex h-8 w-full items-center justify-center gap-1.5 rounded-lg border border-blue-200 bg-blue-50 text-xs font-medium text-blue-700 hover:bg-blue-100"><Terminal size={14}/>打开实时终端</button> : null}
        {error && <p className="mt-2 rounded-md bg-rose-50 px-2.5 py-2 text-[11px] leading-4 text-rose-700">{error}</p>}
      </div>}
    </div>
    <button type="button" onClick={onToggleRightPanel} className={`grid h-8 w-8 place-items-center rounded-lg border shadow-sm ${rightPanelOpen ? "border-blue-300 bg-blue-50 text-blue-700" : "border-slate-200 bg-white text-slate-600 hover:border-blue-300 hover:text-blue-600"}`} aria-label="打开或关闭右侧面板">{rightPanelOpen ? <PanelLeftOpen size={15}/> : <PanelRightOpen size={15}/>}</button>
  </div>;
}
