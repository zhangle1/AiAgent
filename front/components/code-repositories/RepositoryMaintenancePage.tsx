"use client";

import { useEffect, useRef, useState } from "react";
import { Download, Play, Save, Wrench } from "lucide-react";
import { getCodeProjects } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";
import { cancelMaintenance, getMaintenancePlan, getMaintenanceRuns, pushMaintenance, runMaintenance, saveMaintenancePlan } from "@/lib/repository-maintenance-api";
import type { MaintenancePlan, MaintenanceRun, MaintenanceSettings } from "@/lib/repository-maintenance-types";

const statuses: Record<string, string> = { queued: "等待执行", preparing: "准备副本", analyzing: "AI 养护中", building: "编译验证中", ready: "待批准推送", push_queued: "等待推送", pushing: "推送中", delivered: "已推送", completed: "已完成", failed: "失败", cancelled: "已停止", interrupted: "执行中断" };
const active = new Set(["queued", "preparing", "analyzing", "building", "push_queued", "pushing"]);
const field = "w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-900";
const button = "inline-flex items-center justify-center gap-2 rounded-lg border border-slate-200 px-3 py-2 text-sm disabled:cursor-not-allowed disabled:opacity-50";
function time(value: string | null) { return value ? new Date(/(?:Z|[+-]\d{2}:\d{2})$/.test(value) ? value : value + "Z").toLocaleString() : "—"; }
function message(error: unknown) { return error instanceof Error ? error.message : "操作失败，请重试。"; }

export function RepositoryMaintenancePage() {
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [projectId, setProjectId] = useState<number | null>(null);
  const [plan, setPlan] = useState<MaintenancePlan | null>(null);
  const [settings, setSettings] = useState<MaintenanceSettings | null>(null);
  const [runs, setRuns] = useState<MaintenanceRun[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const generation = useRef(0);

  useEffect(() => { let disposed = false; getCodeProjects().then(rows => { if (!disposed) { setProjects(rows); setProjectId(rows[0]?.id ?? null); setLoading(false); } }).catch(e => { if (!disposed) { setError(message(e)); setLoading(false); } }); return () => { disposed = true; }; }, []);
  useEffect(() => {
    const version = ++generation.current;
    setPlan(null); setSettings(null); setRuns([]); setSelectedId(null); setError(""); setNotice("");
    if (!projectId) return;
    let disposed = false;
    let timer: ReturnType<typeof setTimeout>;
    async function refresh(initial: boolean) {
      try {
        const [nextPlan, nextRuns] = await Promise.all([getMaintenancePlan(projectId!), getMaintenanceRuns(projectId!)]);
        if (disposed || generation.current !== version) return;
        setPlan(nextPlan); setRuns(nextRuns);
        setSettings(current => initial || current === null ? { ...nextPlan.settings, auto_push: false } : current);
        setError("");
      } catch (e) { if (!disposed) setError(message(e)); }
      finally { if (!disposed) timer = setTimeout(() => void refresh(false), 5000); }
    }
    void refresh(true);
    return () => { disposed = true; clearTimeout(timer); };
  }, [projectId]);

  async function act(action: () => Promise<unknown>, success: string) {
    const version = generation.current;
    setBusy(true); setError(""); setNotice("");
    try {
      await action();
      const [nextPlan, nextRuns] = await Promise.all([getMaintenancePlan(projectId!), getMaintenanceRuns(projectId!)]);
      if (version === generation.current) { setPlan(nextPlan); setRuns(nextRuns); setNotice(success); }
    } catch (e) { if (version === generation.current) setError(message(e)); }
    finally { setBusy(false); }
  }
  function exportReport(run: MaintenanceRun) {
    const content = ["# 代码库养护报告", "", `- 运行：${run.id}`, `- 项目：${run.project_id}`, `- 状态：${statuses[run.status] ?? run.status}`, `- 创建时间：${time(run.created_at)}`, `- 完成时间：${time(run.finished_at)}`, `- 目标分支：${run.branch}`, `- 变更集：${run.change_set_id ?? "无"}`, "", "## 分析与建议", "", run.report ?? "暂无报告。", "", "## 执行日志", "", run.log ?? "暂无日志。"].join("\n");
    const url = URL.createObjectURL(new Blob(["\uFEFF", content], { type: "text/markdown;charset=utf-8" }));
    const link = document.createElement("a");
    link.href = url; link.download = `maintenance-${run.id}.md`;
    document.body.appendChild(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  const selected = runs.find(run => run.id === selectedId) ?? runs[0];
  const dirty = !!settings && JSON.stringify(settings) !== JSON.stringify(plan?.settings);
  const running = runs.some(run => active.has(run.status));
  function update(patch: Partial<MaintenanceSettings>) { setSettings(current => current ? { ...current, ...patch } : current); }

  return <main className="h-full min-h-0 overflow-y-auto bg-slate-50 p-4 md:p-8">
    <div className="mx-auto max-w-7xl space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div><h1 className="flex items-center gap-2 text-2xl font-semibold text-slate-900"><Wrench className="text-indigo-600" />代码库养护</h1><p className="mt-2 text-sm text-slate-500">按项目持续理解代码、生成建议，或完成改进并在编译通过后交付。</p></div>
        <label className="text-sm text-slate-600">选择项目<select aria-label="选择项目" className={`${field} mt-1 min-w-52`} disabled={busy} value={projectId ?? ""} onChange={e => setProjectId(Number(e.target.value))}><option value="" disabled>请选择项目</option>{projects.map(project => <option key={project.id} value={project.id}>{project.display_name}（{project.repository_count} 个仓库）</option>)}</select></label>
      </header>
      {error && <div role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</div>}
      {notice && <div role="status" className="rounded-lg bg-emerald-50 p-3 text-sm text-emerald-700">{notice}</div>}
      {!projectId && <p className="rounded-xl border bg-white p-8 text-slate-500">{loading ? "正在加载项目…" : "暂无可访问项目，请先在 Git 管理中创建项目并登记代码库。"}</p>}
      {projectId && !settings && <p className="text-slate-500">正在加载养护设置…</p>}
      {settings && <div className="grid gap-6 xl:grid-cols-[360px_minmax(0,1fr)]">
        <form className="space-y-4 rounded-xl border border-slate-200 bg-white p-5" onSubmit={e => { e.preventDefault(); void act(() => saveMaintenancePlan(projectId!, settings), "养护设置已保存。"); }}>
          <h2 className="font-semibold">养护设置</h2>
          <label className="block space-y-1 text-sm"><span>执行模式</span><select className={field} value={settings.mode} onChange={e => update({ mode: e.target.value as MaintenanceSettings["mode"], auto_push: e.target.value === "analyze" ? false : settings.auto_push })}><option value="analyze">理解代码并给出建议</option><option value="optimize">优化代码并编译验证</option></select></label>
          <label className="block space-y-1 text-sm"><span>养护目标</span><textarea className={`${field} min-h-32`} maxLength={4000} value={settings.instructions} onChange={e => update({ instructions: e.target.value })} /></label>
          <label className="block space-y-1 text-sm"><span>Codex 模型（留空使用默认）</span><input className={field} maxLength={128} value={settings.model_id ?? ""} onChange={e => update({ model_id: e.target.value || null })} /></label>
          <div className="grid grid-cols-2 gap-3"><label className="space-y-1 text-sm"><span>最长执行（分钟）</span><input className={field} type="number" min={5} max={120} required value={settings.timeout_minutes} onChange={e => update({ timeout_minutes: Number(e.target.value) })} /></label><label className="space-y-1 text-sm"><span>最多修改文件</span><input className={field} type="number" min={1} max={100} required value={settings.max_files} onChange={e => update({ max_files: Number(e.target.value) })} /></label></div>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={settings.enabled} onChange={e => update({ enabled: e.target.checked })} />启用自动定时执行</label>
          <label className="block space-y-1 text-sm"><span>执行间隔（小时）</span><input className={field} type="number" min={1} max={168} required value={settings.interval_hours} onChange={e => update({ interval_hours: Number(e.target.value) })} /></label>
          <p className="text-xs text-slate-500">下次执行：{time(plan?.next_run_at ?? null)}。关闭浏览器后，后端仍会按计划运行。</p>
          <p className="text-xs leading-5 text-slate-500">优化完成后生成待审批变更集，需逐次审阅并批准推送；交付前复核编译快照。配置和执行需要代码提交权限。</p>
          <p className="text-xs leading-5 text-amber-700">当前按执行时限和改动文件数控制范围，尚未提供 Token 预算封顶或供应商剩余额度检测。</p>
          <div className="flex flex-wrap gap-2"><button className={button} disabled={busy} type="submit"><Save size={16} />保存设置</button><button className={`${button} bg-indigo-600 text-white`} type="button" disabled={busy || dirty || running || !!plan?.active_run_id} onClick={() => void act(() => runMaintenance(projectId!), "任务已加入队列。")}><Play size={16} />立即执行</button></div>
          {dirty && <p className="text-xs text-amber-700">设置有修改，请保存后执行。</p>}
        </form>
        <section className="min-w-0 space-y-4">
          <div className="rounded-xl border border-slate-200 bg-white p-5"><h2 className="mb-3 font-semibold">运行记录 <span className="text-sm font-normal text-slate-400">最近 50 次 · 自动刷新</span></h2>
            {!runs.length ? <p className="py-8 text-center text-sm text-slate-500">暂无运行记录。保存设置后，点击“立即执行”开始首次养护。</p> : <div className="max-h-64 space-y-2 overflow-y-auto">{runs.map(run => <button key={run.id} className={`flex w-full items-center justify-between gap-3 rounded-lg border p-3 text-left text-sm ${selected?.id === run.id ? "border-indigo-300 bg-indigo-50" : "border-slate-100"}`} onClick={() => setSelectedId(run.id)}><span>{time(run.created_at)}<span className="ml-2 text-xs text-slate-500">{run.trigger === "scheduled" ? "定时" : "手动"}</span></span><span>{statuses[run.status] ?? run.status}</span></button>)}</div>}
          </div>
          {selected && <div className="min-w-0 space-y-4 rounded-xl border border-slate-200 bg-white p-5">
            <div className="flex flex-wrap items-center justify-between gap-3"><h2 className="font-semibold">{statuses[selected.status] ?? selected.status}</h2><div className="flex gap-2">{active.has(selected.status) && <button className={button} disabled={busy} onClick={() => void act(() => cancelMaintenance(projectId!, selected.id), "已请求停止任务。")}>停止任务</button>}{selected.status === "ready" && <button className={`${button} bg-indigo-600 text-white`} disabled={busy} onClick={() => void act(() => pushMaintenance(projectId!, selected.id), "已批准编译快照，等待推送。")}>批准并推送</button>}</div></div>
            <p className="break-all text-xs text-slate-500">目标分支：{selected.branch}{selected.change_set_id ? ` · 变更集 #${selected.change_set_id}` : ""}</p>
            <div className="flex items-center justify-between gap-3"><h3 className="text-sm font-medium">养护报告</h3><button className={button} disabled={!selected.report && !selected.log} onClick={() => exportReport(selected)}><Download size={16} />导出 Markdown</button></div><pre className="max-h-96 overflow-auto whitespace-pre-wrap break-words font-sans text-sm leading-6 text-slate-700">{selected.report || "执行完成后在这里显示分析结果与改进建议。"}</pre>
            <details open><summary className="cursor-pointer text-sm font-medium">执行与编译日志</summary><pre className="mt-2 max-h-80 overflow-auto whitespace-pre-wrap break-all rounded-lg bg-slate-950 p-4 text-xs leading-5 text-slate-200">{selected.log || "等待执行…"}</pre></details>
          </div>}
        </section>
      </div>}
    </div>
  </main>;
}
