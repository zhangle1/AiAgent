"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, FolderGit2, GitBranch, History, Loader2, Map as MapIcon, Network, RefreshCw, Users, X } from "lucide-react";
import { getCodeProjects } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";
import { getCodexModelPolicy, type CodexModelPolicy } from "@/lib/agent-provider-api";
import { analyzeAtlas, createAtlasSnapshot, readAtlasEvidence } from "@/lib/change-atlas-api";
import { atlasDay, atlasDays, atlasModuleStats, selectAtlasCommits } from "@/lib/change-atlas";
import type { AtlasAnalysis, AtlasCommit, AtlasEvidence, AtlasFile, AtlasSnapshot } from "@/lib/change-atlas-types";
import { AtlasViews, type AtlasView } from "./AtlasViews";
import { AtlasChangeGraph } from "./AtlasChangeGraph";
import { AtlasAssistant } from "./AtlasAssistant";

const field = "min-w-0 rounded-lg border border-slate-200 bg-white px-2 py-2 text-xs text-slate-800";
const tabs = [{ id: "business", name: "业务地图", icon: MapIcon }, { id: "structure", name: "代码结构", icon: Network }, { id: "timeline", name: "变化回放", icon: History }, { id: "people", name: "协作交集", icon: Users }] as const;
const message = (error: unknown) => error instanceof Error ? error.message : "操作失败，请重试。";
const aborted = (error: unknown) => error instanceof Error && error.name === "AbortError";

export function ChangeAtlasPage({ initialProjectId }: { initialProjectId?: number }) {
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [projectId, setProjectId] = useState<number | null>(null);
  const [repositoryId, setRepositoryId] = useState("");
  const [days, setDays] = useState(7);
  const [ref, setRef] = useState("HEAD");
  const [refs, setRefs] = useState<string[]>([]);
  const [remote, setRemote] = useState(false);
  const [reload, setReload] = useState(0);
  const [snapshot, setSnapshot] = useState<AtlasSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [view, setView] = useState<AtlasView>("business");
  const [selected, setSelected] = useState("");
  const [until, setUntil] = useState("");
  const [daily, setDaily] = useState(false);
  const [authorId, setAuthorId] = useState("");
  const [search, setSearch] = useState("");
  const [perspective, setPerspective] = useState<"delivery" | "developer">("delivery");
  const [evidence, setEvidence] = useState<AtlasEvidence | null>(null);
  const [evidenceBusy, setEvidenceBusy] = useState(false);
  const [evidenceError, setEvidenceError] = useState("");
  const [policy, setPolicy] = useState<CodexModelPolicy | null>(null);
  const [modelId, setModelId] = useState("");
  const [effort, setEffort] = useState("");
  const [question, setQuestion] = useState("解释当前模块的近期变化，并提出需要验证的业务场景。");
  const [analysis, setAnalysis] = useState<AtlasAnalysis | null>(null);
  const [analysisBusy, setAnalysisBusy] = useState(false);
  const [analysisStatus, setAnalysisStatus] = useState("");
  const [analysisError, setAnalysisError] = useState("");
  const [labels, setLabels] = useState<Record<string, string>>({});
  const analysisAbort = useRef<AbortController | null>(null);
  const evidenceAbort = useRef<AbortController | null>(null);
  const evidenceSequence = useRef(0);
  const scopeKey = [projectId, repositoryId, days, ref, remote, snapshot?.id, selected, until, daily, authorId, perspective, modelId, effort].join(":");
  const scope = useRef(scopeKey); scope.current = scopeKey;

  useEffect(() => {
    let disposed = false;
    getCodeProjects().then(rows => { if (disposed) return; setProjects(rows); setProjectId(rows.find(project => project.id === initialProjectId)?.id ?? rows[0]?.id ?? null); if (!rows.length) setLoading(false); }).catch(reason => { if (!disposed) { setError(message(reason)); setLoading(false); } });
    getCodexModelPolicy().then(value => { if (disposed) return; setPolicy(value); setModelId(value.default_model_id); setEffort(value.default_reasoning_effort); }).catch(() => { /* Default model is still resolved by the server when analysis is requested. */ });
    return () => { disposed = true; };
  }, [initialProjectId]);

  useEffect(() => {
    if (!projectId) return;
    const controller = new AbortController(); let disposed = false;
    setLoading(true); setError(""); setSnapshot(null); setSelected(""); setAuthorId(""); setEvidence(null); setAnalysis(null); setLabels({}); setSearch("");
    createAtlasSnapshot(projectId, { repository_id: repositoryId ? Number(repositoryId) : undefined, days, ref, refresh_remote: remote }, controller.signal).then(value => {
      if (disposed) return;
      setSnapshot(value); setUntil(atlasDay(value.created_at)); setSelected(atlasModuleStats(value, value.commits)[0]?.id || "");
      setRefs(Array.from(new Set(value.repositories.flatMap(repository => repository.refs))).sort());
    }).catch(reason => { if (!disposed && !aborted(reason)) setError(message(reason)); }).finally(() => { if (!disposed) setLoading(false); });
    return () => { disposed = true; controller.abort(); };
  }, [projectId, repositoryId, days, ref, remote, reload]);

  useEffect(() => {
    analysisAbort.current?.abort(); evidenceAbort.current?.abort(); evidenceSequence.current++;
    setAnalysis(null); setAnalysisError(""); setAnalysisBusy(false); setEvidence(null); setEvidenceError(""); setEvidenceBusy(false);
    return () => { analysisAbort.current?.abort(); evidenceAbort.current?.abort(); };
  }, [scopeKey]);

  const commits = useMemo(() => snapshot ? selectAtlasCommits(snapshot, { until, daily, authorId }) : [], [snapshot, until, daily, authorId]);
  const timeCommits = useMemo(() => snapshot ? selectAtlasCommits(snapshot, { until, daily, authorId: "" }) : [], [snapshot, until, daily]);
  const stats = useMemo(() => snapshot ? atlasModuleStats(snapshot, commits) : [], [snapshot, commits]);
  const chosen = stats.find(module => module.id === selected);
  const fileMap = useMemo(() => new Map(snapshot?.files.map(file => [file.id, file]) || []), [snapshot]);
  const authors = useMemo(() => Array.from(new Map(snapshot?.commits.map(commit => [commit.author_id, commit.author]) || []).entries()).sort((a, b) => a[1].localeCompare(b[1])), [snapshot]);
  const dates = snapshot ? atlasDays(snapshot) : [];
  const project = projects.find(item => item.id === projectId);
  const changedFileCount = new Set(commits.flatMap(commit => commit.files.map(file => file.file_id))).size;

  function selectProject(id: string) { setProjectId(Number(id)); setRepositoryId(""); setRef("HEAD"); setRefs([]); }
  async function showEvidence(commit: AtlasCommit, file: AtlasFile) {
    if (!snapshot || !projectId) return;
    evidenceAbort.current?.abort(); const controller = new AbortController(); evidenceAbort.current = controller;
    const version = ++evidenceSequence.current, key = scope.current;
    setEvidence(null); setEvidenceBusy(true); setEvidenceError("");
    try { const value = await readAtlasEvidence(projectId, snapshot.id, commit.id, file.id, controller.signal); if (scope.current === key && evidenceSequence.current === version) setEvidence(value); }
    catch (reason) { if (!aborted(reason) && scope.current === key && evidenceSequence.current === version) setEvidenceError(message(reason)); }
    finally { if (scope.current === key && evidenceSequence.current === version) setEvidenceBusy(false); }
  }
  function selectFile(file: AtlasFile) {
    const commit = commits.find(item => item.files.some(change => change.file_id === file.id));
    if (commit) void showEvidence(commit, file);
    else { evidenceAbort.current?.abort(); evidenceSequence.current++; setEvidence(null); setEvidenceBusy(false); setEvidenceError("该文件在当前人员和时间范围内没有改动，可调整范围后查看历史差异。"); }
  }
  async function explain() {
    if (!snapshot || !projectId || !chosen) return;
    const controller = new AbortController(); analysisAbort.current?.abort(); analysisAbort.current = controller;
    const key = scope.current;
    setAnalysisBusy(true); setAnalysisError(""); setAnalysis(null); setAnalysisStatus("正在连接 AI 解读服务…");
    try {
      const result = await analyzeAtlas(projectId, snapshot.id, { module_id: selected, until, daily, author_id: authorId || undefined, perspective, question, model_id: modelId || undefined, reasoning_effort: effort || undefined }, controller.signal, status => {
        if (scope.current === key && !controller.signal.aborted) setAnalysisStatus(status);
      });
      if (scope.current !== key || controller.signal.aborted) return;
      setAnalysis(result); if (result.module_label) setLabels(current => ({ ...current, [selected]: result.module_label! }));
    } catch (reason) { if (!aborted(reason) && scope.current === key) setAnalysisError(message(reason)); }
    finally { if (scope.current === key && analysisAbort.current === controller) setAnalysisBusy(false); }
  }

  return <main className="h-full min-h-0 overflow-y-auto bg-white p-3 text-slate-900 md:p-6">
    <div className="mx-auto max-w-[1600px] space-y-4">
      <header className="flex min-h-12 flex-wrap items-center justify-between gap-4 pl-16 lg:pl-0"><div><h1 className="flex items-center gap-2 text-xl font-semibold"><MapIcon size={22} className="text-blue-600"/>变化地图</h1><p className="mt-1 text-xs leading-6 text-slate-500">从业务模块、代码目录、时间和协作交集理解近期改动。</p></div><label className="flex min-w-0 items-center gap-2 text-xs text-slate-500">项目<select aria-label="变化地图项目" value={projectId || ""} onChange={event => selectProject(event.target.value)} className={`${field} max-w-64`}><option value="" disabled>选择项目</option>{projects.map(item => <option key={item.id} value={item.id}>{item.display_name}</option>)}</select></label></header>
      <section className="flex flex-wrap items-center gap-3 rounded-xl border border-slate-200 bg-white px-4 py-3" aria-label="地图范围">
        <label className="flex min-w-0 items-center gap-1.5 text-xs text-slate-500"><FolderGit2 size={15}/><select aria-label="变化地图代码库" className={`${field} max-w-52`} value={repositoryId} onChange={event => { setRepositoryId(event.target.value); setRef("HEAD"); setRefs([]); }}><option value="">所有代码库</option>{project?.repositories.map(repository => <option key={repository.id} value={repository.id}>{repository.display_name}</option>)}</select></label>
        <label className="flex min-w-0 items-center gap-1.5 text-xs text-slate-500"><GitBranch size={15}/><select aria-label="变化地图分支" className={`${field} max-w-48`} value={ref} onChange={event => setRef(event.target.value)}><option value="HEAD">当前分支</option>{refs.map(value => <option key={value} value={value}>{value.replace("refs/heads/", "本地 / ").replace("refs/remotes/", "远端 / ")}</option>)}</select></label>
        <label className="text-xs text-slate-500"><select aria-label="变化地图时间范围" className={field} value={days} onChange={event => setDays(Number(event.target.value))}>{[3, 7, 14, 30].map(value => <option key={value} value={value}>近 {value} 天</option>)}</select></label>
        <label className="flex items-center gap-1.5 text-xs text-slate-600"><input type="checkbox" checked={remote} onChange={event => setRemote(event.target.checked)} className="accent-blue-600"/>同步远端后读取</label>
        <button type="button" disabled={!projectId || loading} onClick={() => setReload(value => value + 1)} className="ml-auto inline-flex items-center justify-center gap-2 rounded-lg border border-slate-200 px-3 py-2 text-xs hover:bg-slate-50 disabled:opacity-50">{loading ? <Loader2 size={14} className="animate-spin"/> : <RefreshCw size={14}/>}刷新地图</button>
      </section>
      {error && <p role="alert" className="rounded-lg bg-red-50 px-4 py-3 text-sm leading-6 text-red-700">{error}</p>}
      {loading && <div role="status" className="flex items-center justify-center gap-3 py-24 text-sm text-slate-500"><Loader2 className="animate-spin" size={20}/>正在固定 Git 版本并读取近期改动…</div>}
      {!loading && !snapshot && !error && <p className="rounded-xl border border-dashed border-slate-300 px-5 py-20 text-center text-sm text-slate-500">尚无可访问项目，请先在代码库设置中登记项目并分配访问权限。</p>}
      {snapshot && <>
        {snapshot.warnings.length > 0 && <details className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-2 text-xs leading-6 text-amber-800"><summary className="cursor-pointer"><AlertTriangle size={14} className="mr-1 inline"/>{snapshot.warnings.length} 项读取提示</summary><ul className="list-disc pl-4">{snapshot.warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul></details>}
        <div className="flex flex-wrap items-center gap-x-5 gap-y-2 text-xs text-slate-500"><span>{commits.length} 次已载入提交</span><span>{changedFileCount} 个改动文件</span><span>{new Set(commits.map(commit => commit.author_id)).size} 个 Git 署名</span><span className="rounded bg-blue-50 px-2 py-1 text-blue-700">目录级结构</span><span className="ml-auto">活动统计 · UTC · {new Date(snapshot.created_at).toLocaleString()} 生成</span></div>
        <div className="grid min-w-0 items-start gap-4 xl:grid-cols-[minmax(0,1fr)_320px]">
          <div className="min-w-0 space-y-4">
            <section className="min-w-0 overflow-hidden rounded-xl border border-slate-200 bg-white">
              <div className="flex flex-wrap gap-x-5 border-b border-slate-200 px-4" role="tablist" aria-label="变化维度">{tabs.map(tab => <button type="button" id={`atlas-tab-${tab.id}`} key={tab.id} role="tab" aria-selected={view === tab.id} aria-controls="atlas-view" onClick={() => setView(tab.id)} className={`inline-flex items-center gap-1.5 border-b-2 py-3 text-xs ${view === tab.id ? "border-blue-600 font-medium text-blue-600" : "border-transparent text-slate-500"}`}><tab.icon size={15}/>{tab.name}</button>)}</div>
              <div className="flex flex-wrap items-center gap-2 px-4 pt-4">{(view === "business" || view === "structure") && <input aria-label="查找模块" placeholder="查找模块 / 目录" value={search} onChange={event => setSearch(event.target.value)} className={`${field} flex-1`}/>}<select aria-label="聚焦模块" value={selected} onChange={event => setSelected(event.target.value)} className={`${field} max-w-full sm:max-w-[230px]`}>{stats.map(module => <option key={module.id} value={module.id}>{labels[module.id] || module.name} · {module.commits.length} 次 · {module.directory}</option>)}</select><div className="flex rounded-lg bg-slate-100 p-1" aria-label="阅读视角">{(["delivery", "developer"] as const).map(value => <button type="button" key={value} aria-pressed={perspective === value} onClick={() => { setPerspective(value); setView(value === "developer" ? "structure" : "business"); setSearch(""); }} className={`rounded-md px-2 py-1 text-[11px] ${perspective === value ? "bg-white text-blue-600 shadow-sm" : "text-slate-500"}`}>{value === "delivery" ? "交付视角" : "开发视角"}</button>)}</div></div>
              <p className="px-4 pt-3 text-xs leading-6 text-slate-500" aria-live="polite">{perspective === "delivery" ? "交付视角：按模块查看变化规模与历史同改，聚焦联动场景和验收建议。" : "开发视角：展开目录定位文件，通过提交关系链追溯增删行与差异。"}</p>
              <div id="atlas-view" role="tabpanel" aria-labelledby={`atlas-tab-${view}`} className="min-w-0 p-4">{snapshot.modules.length > 0 ? <AtlasViews perspective={perspective} snapshot={snapshot} commits={commits} timeCommits={timeCommits} stats={stats} view={view} selected={selected} until={until} authorId={authorId} search={search} labels={labels} onModule={setSelected} onFile={selectFile} onTime={(module, day) => { setSelected(module); setUntil(day); }} onPerson={(module, author) => { setSelected(module); setAuthorId(authorId === author && module === selected ? "" : author); }}/> : <p className="py-12 text-center text-sm text-slate-500">当前仓库没有可展示的已提交文件。</p>}</div>
              <div className="space-y-3 border-t border-slate-200 bg-slate-50 px-4 py-3"><div className="flex flex-wrap items-center justify-between gap-2 text-xs"><span>{daily ? until : `${atlasDay(snapshot.start_at)} — ${until}`}</span><label className="flex items-center gap-1.5 text-slate-500"><input type="checkbox" className="accent-blue-600" checked={daily} onChange={event => setDaily(event.target.checked)}/>仅看选中当天</label></div><label className="block text-[11px] text-slate-500">活动时间截面<input type="range" aria-label="活动时间截面" min={0} max={Math.max(0, dates.length - 1)} value={Math.max(0, dates.indexOf(until))} onChange={event => setUntil(dates[Number(event.target.value)])} className="mt-2 block w-full accent-blue-600"/></label><div className="flex flex-wrap items-center gap-2"><label className="min-w-0 flex-1 text-xs text-slate-500">人员 <select aria-label="Git 署名筛选" value={authorId} onChange={event => setAuthorId(event.target.value)} className={`${field} max-w-full`}><option value="">所有 Git 署名</option>{authors.map(([id, author]) => <option key={id} value={id}>{author} · {id.slice(0, 4)}</option>)}</select></label>{authorId && <button type="button" onClick={() => setAuthorId("")} className="inline-flex items-center gap-1 text-xs text-blue-600"><X size={12}/>清除筛选</button>}<span className="text-[11px] text-slate-500">提交活动包含合并提交；不表示最终净变化。</span></div></div>
            </section>
            <section className="min-w-0 rounded-xl border border-slate-200 bg-white p-4">
              <AtlasChangeGraph key={`${snapshot.id}:${selected}`} commits={chosen?.commits || []} files={snapshot.files.filter(file => file.module_id === selected)} onEvidence={(commit, file) => void showEvidence(commit, file)}/>
            </section>
            <section className="min-w-0 rounded-xl border border-slate-200 bg-white p-4" aria-label="模块改动证据"><header><h2 className="break-all text-sm font-semibold">{chosen ? labels[chosen.id] || chosen.name : "选择模块"} · 相关改动</h2><p className="mt-1 break-all text-xs text-slate-500">{chosen?.directory}</p></header><div className="mt-3 divide-y divide-slate-100">{chosen?.commits.slice(0, 20).map(commit => { const files = commit.files.map(change => ({ change, file: fileMap.get(change.file_id) })).filter((row): row is { change: typeof commit.files[number]; file: AtlasFile } => !!row.file && row.file.module_id === selected); return <div key={commit.id} className="py-3"><button type="button" onClick={() => { if (files[0]) void showEvidence(commit, files[0].file); }} className="break-words text-left text-sm hover:text-blue-600">{commit.subject}</button><p className="mt-1 text-[11px] text-slate-500">{atlasDay(commit.committed_at)} · {commit.author} · <code>{commit.sha.slice(0, 9)}</code>{commit.is_merge ? " · 合并提交，按第一父版本比较" : ""}{commit.truncated ? " · 文件列表已截断" : ""}</p><div className="mt-2 flex flex-wrap gap-1.5">{files.slice(0, 6).map(({ file, change }) => <button type="button" key={file.id} onClick={() => void showEvidence(commit, file)} className="max-w-full break-all rounded-md bg-slate-50 px-2 py-1 text-left text-[11px] text-blue-600 hover:bg-blue-50">{file.path}<span className="ml-2 text-slate-400">{change.added === null ? "二进制" : `+${change.added} −${change.deleted}`}</span></button>)}{files.length > 6 && <span className="text-[11px] text-slate-400">另 {files.length - 6} 个文件</span>}</div></div>; })}{!chosen?.commits.length && <p className="py-8 text-center text-xs text-slate-500">当前模块在这个范围没有提交，可调整日期或人员。</p>}</div>{(chosen?.commits.length || 0) > 20 && <p className="text-xs text-slate-500">显示最近 20 次，可通过时间和人员继续缩小范围。</p>}
              {evidenceBusy && <p role="status" className="mt-4 flex items-center gap-2 text-xs text-slate-500"><Loader2 className="animate-spin" size={14}/>读取指定提交的差异…</p>}{evidenceError && <p role="alert" className="mt-3 rounded bg-amber-50 p-3 text-xs leading-6 text-amber-800">{evidenceError}</p>}{evidence && <div className="mt-4 rounded-lg border border-slate-200" aria-label="Git 差异证据"><header className="flex items-start justify-between gap-2 border-b border-slate-200 bg-slate-50 p-3"><p className="min-w-0 break-all font-mono text-xs">{evidence.path}<br/><span className="text-slate-500">{evidence.sha}</span></p><button type="button" onClick={() => setEvidence(null)} aria-label="关闭差异"><X size={14}/></button></header><pre className="max-h-[460px] overflow-auto whitespace-pre-wrap break-all p-3 font-mono text-xs leading-6">{evidence.patch || "没有可显示的文本差异。"}</pre>{evidence.truncated && <p className="px-3 pb-3 text-xs text-amber-700">差异已截断，仅展示部分证据。</p>}</div>}
            </section>
          </div>
          <AtlasAssistant module={chosen} scope={`${perspective === "delivery" ? "交付视角" : "开发视角"} · ${project?.display_name || ""} · ${daily ? until : `${atlasDay(snapshot.start_at)}—${until}`} · ${authors.find(([id]) => id === authorId)?.[1] || "所有署名"}`} policy={policy} modelId={modelId} effort={effort} question={question} result={analysis} busy={analysisBusy} status={analysisStatus} error={analysisError} canAnalyze={!!chosen?.commits.length} onQuestion={setQuestion} onModel={value => { setModelId(value); setEffort(""); }} onEffort={setEffort} onAnalyze={() => void explain()} onCancel={() => { analysisAbort.current?.abort(); setAnalysisBusy(false); }} onEvidence={value => { evidenceAbort.current?.abort(); evidenceSequence.current++; setEvidence(value); setEvidenceBusy(false); setEvidenceError(""); }}/>
        </div>
        <footer className="space-y-1 text-[11px] leading-6 text-slate-500">{snapshot.repositories.map(repository => <p key={repository.id} className="break-all">{repository.name} · {repository.source === "remote" ? "已同步远端" : "本地已提交版本"} · {repository.ref} · {repository.head_sha.slice(0, 12) || "无提交"}</p>)}<p>地图快照保留 24 小时访问有效期。读取不含未提交文件；重命名当前按删除与新增展示。</p></footer>
      </>}
    </div>
  </main>;
}
