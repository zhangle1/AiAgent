"use client";

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ChevronDown, ChevronRight, Download, File, FileDiff, FilePenLine, Folder, FolderOpen, GitBranch, Info, Loader2, PackageOpen, PanelLeftOpen, PanelRightOpen, Play, RefreshCw, RotateCcw, Save, Square, Terminal, Upload, X } from "lucide-react";
import { getCodeProjectRuntime, startCodeProjectRuntime, stopCodeProjectRuntime } from "@/lib/code-runtime-api";
import { discardCodeRepositoryChangesAndPull, discardProjectGitChangesAndPull, getCodeRepositoryGitDiff, getProjectGitStatus, packageCodeRepositoryViaWebSocket, pushCodeRepositoryGit, pushProjectGit, readChatConfiguredCodeFile, writeChatConfiguredCodeFile } from "@/lib/code-repository-api";
import type { CodeProject, CodeRepository, ConfiguredCodeFile, GitDiffComparison, GitWorkspaceDiff, GitWorkspaceDiffFile, GitWorkspaceStatus, ProjectGitBatchOperationResult, ProjectGitRepositoryStatus, ProjectGitStatus } from "@/lib/code-repository-types";
import type { CodeProjectRuntime, CodeRuntimeProfile, CodeRuntimeRun } from "@/lib/code-runtime-types";

type ChatConfigDraft = ConfiguredCodeFile & { repositoryName: string; repositoryDisplayName: string };
type ProjectGitBatchAction = "discard-and-pull" | "commit-and-push";

export function ChatRuntimeToolbar({ project, rightPanelOpen, onToggleRightPanel, onOpenRuntimePanel }: { project: CodeProject | null; rightPanelOpen: boolean; onToggleRightPanel: () => void; onOpenRuntimePanel: () => void }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [runtime, setRuntime] = useState<CodeProjectRuntime | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [gitStatuses, setGitStatuses] = useState<Record<number, GitWorkspaceStatus>>({});
  const [projectGitStatus, setProjectGitStatus] = useState<ProjectGitStatus | null>(null);
  const [packageStatus, setPackageStatus] = useState<Record<string, string>>({});
  const [configDraft, setConfigDraft] = useState<ChatConfigDraft | null>(null);
  const [pushTarget, setPushTarget] = useState<CodeRepository | null>(null);
  const [diffTarget, setDiffTarget] = useState<CodeRepository | null>(null);
  const [helpOpen, setHelpOpen] = useState(false);
  const [batchAction, setBatchAction] = useState<ProjectGitBatchAction | null>(null);
  const [batchResult, setBatchResult] = useState<ProjectGitBatchOperationResult | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);
  const panelRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!project) {
      setProjectGitStatus(null);
      setGitStatuses({});
      return;
    }
    setProjectGitStatus(null);
    setGitStatuses({});
    void refresh();
  // The selected project is the only runtime context for this menu and is checked immediately.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [project?.id]);

  useEffect(() => {
    if (!menuOpen) return;
    const closeOutside = (event: PointerEvent) => {
      const target = event.target as Node;
      if (!menuRef.current?.contains(target) && !panelRef.current?.contains(target)) setMenuOpen(false);
    };
    document.addEventListener("pointerdown", closeOutside);
    return () => document.removeEventListener("pointerdown", closeOutside);
  }, [menuOpen]);

  useEffect(() => {
    const refreshGitAfterChat = (event: Event) => {
      const projectId = (event as CustomEvent<{ projectId?: number }>).detail?.projectId;
      if (!project || projectId !== project.id) return;
      window.setTimeout(() => void refresh(), 300);
    };
    window.addEventListener("aiagent:chat-stream-complete", refreshGitAfterChat);
    return () => window.removeEventListener("aiagent:chat-stream-complete", refreshGitAfterChat);
  // The selected project determines whether the finished turn can affect this Git panel.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [project?.id]);

  async function refresh() {
    if (!project) return;
    setRefreshing(true);
    try {
      const [runtimeState, gitState] = await Promise.all([getCodeProjectRuntime(project.id), getProjectGitStatus(project.id)]);
      setRuntime(runtimeState);
      setProjectGitStatus(gitState);
      setGitStatuses(Object.fromEntries(gitState.repositories.flatMap((repository) => repository.status ? [[repository.repository_id, repository.status] as const] : [])));
      setError(null);
    } catch (ex) {
      setProjectGitStatus({ project_id: project.id, state: "attention", message: "Git 状态检查失败，请手动刷新重试。", repositories: [] });
      setError(ex instanceof Error ? ex.message : "无法读取运行状态。");
    } finally {
      setRefreshing(false);
    }
  }

  async function startProfiles(profiles: CodeRuntimeProfile[], description: string) {
    if (!project) return;
    const activeProfileIds = new Set(runtime?.runs.filter((run) => isActiveRun(run)).map((run) => run.profile_id));
    const pendingProfileIds = profiles.filter((profile) => profile.is_enabled && !activeProfileIds.has(profile.id)).map((profile) => profile.id);
    if (!pendingProfileIds.length) {
      setError(`${description}没有可启动的配置；正在运行的 Shell 可在下方强制结束。`);
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await startCodeProjectRuntime(project.id, pendingProfileIds);
      await refresh();
      onOpenRuntimePanel();
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "启动失败。");
    } finally {
      setBusy(false);
    }
  }

  async function forceStop(runId: string) {
    if (!project) return;
    setBusy(true);
    setError(null);
    try {
      await stopCodeProjectRuntime(project.id, runId);
      await refresh();
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "强制结束失败。");
    } finally {
      setBusy(false);
    }
  }

  async function openConfiguration(repositoryName: string, repositoryDisplayName: string, path: string) {
    setBusy(true);
    setError(null);
    try {
      const file = await readChatConfiguredCodeFile(repositoryName, path);
      setConfigDraft({ ...file, repositoryName, repositoryDisplayName });
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "无法读取聊天配置文件。");
    } finally {
      setBusy(false);
    }
  }

  async function saveConfiguration() {
    if (!configDraft) return;
    setBusy(true);
    setError(null);
    try {
      const saved = await writeChatConfiguredCodeFile(configDraft.repositoryName, { path: configDraft.path, content: configDraft.content, expected_sha256: configDraft.sha256 });
      setConfigDraft((current) => current ? { ...current, sha256: saved.sha256 } : null);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "保存配置文件失败。");
    } finally {
      setBusy(false);
    }
  }

  async function packageRepository(repositoryName: string) {
    setBusy(true);
    setError(null);
    setPackageStatus((current) => ({ ...current, [repositoryName]: "正在打包…" }));
    try {
      const completed = await packageCodeRepositoryViaWebSocket(repositoryName, (event) => {
        if (event.line) setPackageStatus((current) => ({ ...current, [repositoryName]: event.line! }));
      });
      setPackageStatus((current) => ({ ...current, [repositoryName]: completed.success ? "打包完成" : completed.message || "打包失败" }));
    } catch (ex) {
      const message = ex instanceof Error ? ex.message : "打包失败。";
      setPackageStatus((current) => ({ ...current, [repositoryName]: message }));
    } finally {
      setBusy(false);
    }
  }

  async function discardRepositoryChangesAndPull(repository: CodeRepository) {
    if (!window.confirm(`重置更新“${repository.display_name}”会用服务器上的最新代码替换本机尚未保存的修改。您额外新建的文件和已提交的版本不会删除。确认继续吗？`)) return;
    setBusy(true);
    setError(null);
    try {
      const result = await discardCodeRepositoryChangesAndPull(repository.name);
      if (!result.ok) throw new Error(result.output || "重置更新失败。");
      await refresh();
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "重置更新失败。");
    } finally {
      setBusy(false);
    }
  }

  async function pushRepository(repository: CodeRepository, message: string) {
    setBusy(true);
    setError(null);
    try {
      const result = await pushCodeRepositoryGit(repository.name, message);
      if (!result.ok) throw new Error(result.output || "提交推送失败。");
      setPushTarget(null);
      await refresh();
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "提交推送失败。");
    } finally {
      setBusy(false);
    }
  }

  async function runProjectGitBatch(action: ProjectGitBatchAction, message?: string) {
    if (!project) return;
    const repositoryNames = projectGitStatus?.repositories.filter((repository) => repository.status?.is_repository).map((repository) => repository.repository_name) ?? [];
    if (!repositoryNames.length) {
      setError("当前项目没有可批量操作的 Git 代码库。");
      return;
    }
    setBusy(true);
    setError(null);
    setBatchResult(null);
    try {
      const result = action === "discard-and-pull"
        ? await discardProjectGitChangesAndPull(project.id, repositoryNames)
        : await pushProjectGit(project.id, repositoryNames, message ?? "");
      setBatchResult(result);
      await refresh();
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "项目 Git 批量操作失败。");
    } finally {
      setBusy(false);
    }
  }

  const activeRuns = runtime?.runs.filter(isActiveRun) ?? [];
  const visibleGitRows = projectGitStatus?.repositories.filter((repository) => repository.status?.is_repository) ?? [];
  const topGitState = refreshing ? "checking" : project ? projectGitStatus?.state ?? "neutral" : "neutral";
  const topGitClass = topGitState === "synced" ? "border-emerald-300 bg-emerald-50 text-emerald-700" : topGitState === "attention" ? "border-amber-300 bg-amber-50 text-amber-800" : topGitState === "checking" ? "border-blue-300 bg-blue-50 text-blue-700" : "border-slate-200 bg-white text-slate-600 hover:border-blue-300 hover:text-blue-600";
  const topGitLabel = topGitState === "checking" ? "正在检查 Git 状态" : topGitState === "synced" ? "代码已同步" : topGitState === "attention" ? "需要处理 Git 状态" : "项目程序运行";

  return <>
    <div className="relative flex items-center gap-1.5">
      {menuOpen && typeof document !== "undefined" && createPortal(<button type="button" onClick={() => setMenuOpen(false)} className="fixed inset-0 z-[85] bg-slate-950/35 lg:hidden" aria-label="关闭项目程序运行" />, document.body)}
      <div ref={menuRef} className="relative">
        <button type="button" onClick={() => setMenuOpen((current) => !current)} className={`inline-flex h-9 items-center gap-1 rounded-xl border px-2 text-[11px] font-medium shadow-sm lg:h-8 lg:gap-1.5 lg:rounded-lg lg:px-2.5 lg:text-xs ${topGitClass}`} aria-expanded={menuOpen} aria-label={`${topGitLabel}：${projectGitStatus?.message ?? "请选择项目后检查"}`} title={projectGitStatus?.message ?? "请选择项目后检查"}>
          {refreshing ? <Loader2 size={14} className="animate-spin"/> : <Terminal size={14}/>}项目程序运行<ChevronDown size={13} className={menuOpen ? "rotate-180 transition" : "transition"}/>
        </button>
        {menuOpen && typeof document !== "undefined" && createPortal(<div ref={panelRef} className="fixed inset-x-0 bottom-0 z-[90] box-border w-full max-w-full max-h-[84dvh] overflow-x-hidden overflow-y-auto overscroll-contain rounded-t-2xl border border-slate-200 bg-white p-4 pb-[calc(env(safe-area-inset-bottom)+1rem)] shadow-[0_-12px_42px_rgba(15,23,42,0.2)] lg:inset-x-auto lg:right-5 lg:top-16 lg:w-[390px] lg:max-h-[calc(100dvh-5rem)] lg:rounded-xl lg:p-3 lg:shadow-[0_18px_42px_rgba(15,23,42,0.2)]">
          <div className="mb-3 flex items-center justify-between gap-3">
            <div>
              <p className="text-sm font-semibold text-slate-900">项目程序运行</p>
              <p className="mt-0.5 text-[11px] text-slate-500">{project ? `${project.display_name} · 可按代码库单独启动` : "请先在聊天底部选择项目"}</p>
            </div>
            <div className="relative flex items-center gap-1"><button type="button" onClick={() => setHelpOpen((current) => !current)} className={`grid h-7 w-7 place-items-center rounded-md transition ${helpOpen ? "bg-blue-50 text-blue-700" : "text-slate-500 hover:bg-slate-100"}`} aria-label="查看操作说明" aria-expanded={helpOpen} title="查看操作说明"><Info size={15}/></button><button type="button" disabled={refreshing} onClick={() => void refresh()} className="grid h-7 w-7 place-items-center rounded-md text-slate-500 hover:bg-slate-100 disabled:opacity-50" aria-label="刷新"><RefreshCw size={14} className={refreshing ? "animate-spin" : undefined}/></button>{helpOpen && <RuntimeActionHelp onClose={() => setHelpOpen(false)}/>}</div>
          </div>

          {project && <ProjectGitOverview state={topGitState} summary={projectGitStatus?.message} rows={projectGitStatus?.repositories ?? []} busy={busy} onDiscard={() => { setBatchResult(null); setBatchAction("discard-and-pull"); }} onPush={() => { setBatchResult(null); setBatchAction("commit-and-push"); }} />}

          {project?.repositories.length ? <div className="mb-3 space-y-2">
            <p className="px-0.5 text-[11px] font-semibold text-slate-500">代码库</p>
            {project.repositories.map((repository) => <RepositoryCard key={repository.id} repository={repository} gitStatus={gitStatuses[repository.id]} profiles={runtime?.profiles ?? []} busy={busy} packageStatus={packageStatus[repository.name]} onStart={(profiles) => void startProfiles(profiles, repository.display_name)} onPackage={() => void packageRepository(repository.name)} onOpenDiff={() => setDiffTarget(repository)} onDiscardAndPull={() => void discardRepositoryChangesAndPull(repository)} onCommitPush={() => { setError(null); setPushTarget(repository); }} onOpenConfiguration={(path) => void openConfiguration(repository.name, repository.display_name, path)}/>) }
          </div> : null}

          {runtime && runtime.profiles.length ? <div className="mb-2 space-y-1.5">
            <p className="px-0.5 text-[11px] font-semibold text-slate-500">已保存的调试配置</p>
            {runtime.profiles.map((profile) => <div key={profile.id} className="rounded-lg border border-violet-100 bg-violet-50/50 px-2.5 py-2 text-[11px] leading-5 text-slate-600">
              <div className="flex items-center justify-between gap-2"><span className="truncate font-semibold text-slate-800">{profile.repository_name} · {profile.role === "frontend" ? "前端" : "C# 后端"}</span><span className="shrink-0 text-violet-700">默认 :{profile.preferred_port ?? (profile.role === "frontend" ? 4300 : 5100)}</span></div>
              <code className="block truncate text-slate-500">{profile.role === "frontend" ? `启动 npm run ${profile.run_script || "dev"}` : `启动 dotnet run --project ${profile.entry_path ?? ""}`}</code>
            </div>)}
          </div> : <p className="mb-2 rounded-lg bg-slate-50 px-3 py-3 text-xs leading-5 text-slate-500">尚未保存调试配置。请在“项目与代码库”的代码库详情中选择 `.csproj` 或 `package.json`，并填写启动脚本与默认端口。</p>}

          {activeRuns.length ? <div className="space-y-1.5">
            <p className="px-0.5 text-[11px] font-semibold text-slate-500">正在运行的 Shell</p>
            {activeRuns.map((run) => <RunCard key={run.run_id} run={run} busy={busy} onForceStop={() => void forceStop(run.run_id)}/>) }
            <button type="button" onClick={onOpenRuntimePanel} className="mt-1 inline-flex h-8 w-full items-center justify-center gap-1.5 rounded-lg border border-blue-200 bg-blue-50 text-xs font-medium text-blue-700 hover:bg-blue-100"><Terminal size={14}/>打开实时终端</button>
          </div> : runtime && runtime.profiles.length ? <p className="rounded-lg bg-slate-50 px-3 py-2 text-[11px] leading-5 text-slate-500">尚未启动；可点击代码库右侧的“运行”，只启动该代码库的配置。</p> : null}
          {error && <p className="mt-2 rounded-md bg-rose-50 px-2.5 py-2 text-[11px] leading-4 text-rose-700">{error}</p>}
        </div>, document.body)}
      </div>
      <button type="button" onClick={onToggleRightPanel} className={`hidden h-8 w-8 place-items-center rounded-lg border shadow-sm lg:grid ${rightPanelOpen ? "border-blue-300 bg-blue-50 text-blue-700" : "border-slate-200 bg-white text-slate-600 hover:border-blue-300 hover:text-blue-600"}`} aria-label="打开或关闭右侧面板">{rightPanelOpen ? <PanelLeftOpen size={15}/> : <PanelRightOpen size={15}/>}</button>
    </div>
    {configDraft && <ChatConfigurationEditor draft={configDraft} busy={busy} onChange={setConfigDraft} onClose={() => !busy && setConfigDraft(null)} onSave={() => void saveConfiguration()} />}
    {pushTarget && <CommitPushDialog repository={pushTarget} busy={busy} error={error} onClose={() => !busy && setPushTarget(null)} onSubmit={(message) => void pushRepository(pushTarget, message)} />}
    {batchAction && project && <ProjectGitBatchDialog action={batchAction} rows={visibleGitRows} busy={busy} error={error} result={batchResult} onClose={() => !busy && setBatchAction(null)} onSubmit={(message) => void runProjectGitBatch(batchAction, message)} />}
    {diffTarget && <GitDiffDialog repository={diffTarget} onClose={() => setDiffTarget(null)}/>}
  </>;
}

function RepositoryCard({ repository, gitStatus, profiles, busy, packageStatus, onStart, onPackage, onOpenDiff, onDiscardAndPull, onCommitPush, onOpenConfiguration }: { repository: CodeRepository; gitStatus?: GitWorkspaceStatus; profiles: CodeRuntimeProfile[]; busy: boolean; packageStatus?: string; onStart: (profiles: CodeRuntimeProfile[]) => void; onPackage: () => void; onOpenDiff: () => void; onDiscardAndPull: () => void; onCommitPush: () => void; onOpenConfiguration: (path: string) => void }) {
  const repositoryProfiles = profiles.filter((profile) => profile.repository_id === repository.id && profile.is_enabled);
  return <section className="min-w-0 max-w-full overflow-hidden rounded-lg border border-slate-100 bg-slate-50/70 p-2.5">
    <p className="mb-2 break-words text-xs font-semibold leading-5 text-slate-800" title={repository.display_name}>{repository.display_name}</p>
    <div className="flex flex-wrap items-center gap-1.5">
      <button type="button" disabled={busy || !repositoryProfiles.length} onClick={() => onStart(repositoryProfiles)} title={repositoryProfiles.length ? "只运行此代码库的已启用配置" : "请先为代码库保存运行配置"} className="inline-flex h-7 shrink-0 items-center gap-1 rounded-md bg-blue-600 px-2 text-[11px] font-medium text-white hover:bg-blue-700 disabled:bg-slate-300"><Play size={13}/>运行</button>
      <button type="button" disabled={busy} onClick={onPackage} className="inline-flex h-7 shrink-0 items-center gap-1 rounded-md border border-blue-200 bg-white px-2 text-[11px] font-medium text-blue-700 hover:bg-blue-50 disabled:opacity-50"><PackageOpen size={13}/>打包</button>
      {(gitStatus?.is_repository || repository.is_git_repository) && <><button type="button" disabled={busy} onClick={onOpenDiff} title="按文件查看工作区、待推送或待拉取的代码差异" className="inline-flex h-7 shrink-0 items-center gap-1 rounded-md border border-violet-200 bg-white px-2 text-[11px] font-medium text-violet-700 hover:bg-violet-50 disabled:opacity-50"><FileDiff size={12}/>差异</button><button type="button" disabled={busy} onClick={onDiscardAndPull} title="重置已跟踪文件的本地修改并拉取服务器最新代码；不会删除额外新建的文件" className="inline-flex h-7 shrink-0 items-center gap-1 rounded-md border border-amber-200 bg-amber-50 px-2 text-[11px] font-medium text-amber-800 hover:bg-amber-100 disabled:opacity-50"><RotateCcw size={12}/>重置更新</button><button type="button" disabled={busy} onClick={onCommitPush} title="填写 Conventional Commit 信息后提交并推送当前代码库" className="inline-flex h-7 shrink-0 items-center gap-1 rounded-md border border-emerald-200 bg-emerald-50 px-2 text-[11px] font-medium text-emerald-700 hover:bg-emerald-100 disabled:opacity-50"><Upload size={12}/>提交推送</button></>}
    </div>
    {gitStatus?.is_repository ? <GitSyncSummary status={gitStatus}/> : repository.is_git_repository ? <p className="mt-1.5 text-[10px] text-slate-400">Git 状态暂时不可用，点击面板刷新重试。</p> : null}
    {(repository.chat_editable_configuration_files ?? []).length ? <div className="mt-2 flex flex-wrap gap-1.5">{repository.chat_editable_configuration_files.map((path) => <button type="button" key={path} disabled={busy} onClick={() => onOpenConfiguration(path)} className="inline-flex max-w-full items-center gap-1 rounded-md border border-slate-200 bg-white px-2 py-1 font-mono text-[10px] text-slate-600 hover:border-blue-200 hover:bg-blue-50 hover:text-blue-700 disabled:opacity-50" title={`在聊天中编辑 ${path}`}><FilePenLine size={12}/><span className="truncate">{path}</span></button>)}</div> : <p className="mt-1.5 text-[10px] text-slate-400">未开放聊天可修改的配置文件</p>}
    {packageStatus && <p className="mt-1.5 truncate text-[10px] text-slate-500" title={packageStatus}>{packageStatus}</p>}
  </section>;
}

function GitSyncSummary({ status }: { status: GitWorkspaceStatus }) {
  const branch = status.branch || "detached";
  const remoteBranch = status.remote_branch || "未设置上游";
  return <div className="mt-2 max-w-full overflow-hidden rounded-md border border-slate-200 bg-white px-2 py-1.5 text-[10px] leading-4 text-slate-500">
    <div className="flex min-w-0 items-center gap-1 text-slate-600" title={`本地分支 ${branch}，远程跟踪分支 ${remoteBranch}`}><GitBranch size={12} className="shrink-0 text-slate-400"/><span className="truncate font-mono">{branch}</span><span className="text-slate-300">→</span><span className="truncate font-mono">{remoteBranch}</span></div>
    {status.remote_branch ? <><div className="mt-1 flex flex-wrap gap-x-3 gap-y-0.5"><span className={status.behind ? "text-amber-700" : "text-slate-400"}><Download size={11} className="mr-0.5 inline"/>远端领先 {status.behind} 提交 · 拉取 {status.behind_files} 文件</span><span className={status.ahead ? "text-blue-700" : "text-slate-400"}><Upload size={11} className="mr-0.5 inline"/>本地领先 {status.ahead} 提交 · 推送 {status.ahead_files} 文件</span>{status.changes.length ? <span className="text-rose-600">待提交 {status.changes.length} 文件</span> : null}</div>{status.remote_refresh_error ? <p className="mt-1 truncate text-amber-700" title={status.remote_refresh_error}>远程刷新失败，当前显示本地缓存状态。</p> : null}</> : <p className="mt-1 text-slate-400">未设置远程跟踪分支，无法计算拉取/推送差异。</p>}
  </div>;
}

function RuntimeActionHelp({ onClose }: { onClose: () => void }) {
  return <section className="absolute right-0 top-9 z-20 w-72 rounded-xl border border-slate-200 bg-white p-3 shadow-[0_14px_32px_rgba(15,23,42,0.18)]" role="dialog" aria-label="代码库操作说明">
    <div className="flex items-center justify-between gap-3"><p className="text-xs font-semibold text-slate-800">代码库操作说明</p><button type="button" onClick={onClose} className="grid h-6 w-6 place-items-center rounded text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="关闭说明"><X size={13}/></button></div>
    <div className="mt-2.5 space-y-2 text-[11px] leading-5 text-slate-600"><p><strong className="text-slate-800">运行：</strong>只启动当前代码库已启用的运行配置。</p><p><strong className="text-slate-800">打包：</strong>按该代码库的发布配置构建并输出交付文件。</p><p><strong className="text-slate-800">差异：</strong>按文件查看本地工作区或远程分支差异。</p><p><strong className="text-amber-800">重置更新：</strong>撤回已跟踪文件的未提交修改，再拉取远程最新代码；额外新建的文件不会删除。</p><p><strong className="text-emerald-800">提交推送：</strong>填写提交说明后，提交并推送当前代码库。</p></div>
  </section>;
}

function ProjectGitOverview({ state, summary, rows, busy, onDiscard, onPush }: { state: string; summary?: string; rows: ProjectGitRepositoryStatus[]; busy: boolean; onDiscard: () => void; onPush: () => void }) {
  const discardable = rows.filter((row) => row.status?.is_repository && (row.status.changes.length > 0 || row.status.behind > 0));
  const pushable = rows.filter((row) => row.status?.is_repository && (row.status.changes.length > 0 || row.status.ahead > 0));
  const tone = state === "synced" ? "border-emerald-200 bg-emerald-50 text-emerald-800" : state === "attention" ? "border-amber-200 bg-amber-50 text-amber-900" : "border-slate-200 bg-slate-50 text-slate-600";
  return <section className={`mb-3 min-w-0 max-w-full overflow-hidden rounded-lg border px-3 py-2.5 text-[11px] leading-5 ${tone}`} aria-live="polite">
    <p className="font-semibold">Git 前置检查</p>
    <p className="mt-0.5 break-words">{summary ?? (state === "checking" ? "正在刷新远端引用并检查项目代码库…" : "请选择项目后检查 Git 状态。")}</p>
    {rows.length > 0 && <div className="mt-2 space-y-1">{rows.map((row) => <p key={row.repository_id} className="flex gap-1.5 break-words"><span className={`mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full ${row.state === "synced" ? "bg-emerald-500" : row.state === "not-repository" ? "bg-slate-400" : "bg-amber-500"}`}/><span><strong>{row.display_name}</strong>：{row.message}</span></p>)}</div>}
    {(discardable.length > 0 || pushable.length > 0) && <div className="mt-3 grid min-w-0 gap-2 sm:grid-cols-2">
      <button type="button" disabled={busy || state === "checking" || discardable.length === 0} onClick={onDiscard} className="inline-flex min-h-9 items-center justify-center gap-1.5 rounded-lg border border-amber-300 bg-white px-3 text-xs font-semibold text-amber-800 hover:bg-amber-100 disabled:cursor-not-allowed disabled:opacity-50"><RotateCcw size={14}/>一键重置更新</button>
      <button type="button" disabled={busy || state === "checking" || pushable.length === 0} onClick={onPush} className="inline-flex min-h-9 items-center justify-center gap-1.5 rounded-lg border border-emerald-300 bg-white px-3 text-xs font-semibold text-emerald-700 hover:bg-emerald-100 disabled:cursor-not-allowed disabled:opacity-50"><Upload size={14}/>一键提交推送</button>
    </div>}
    {!discardable.length && !pushable.length && state !== "checking" && <p className="mt-2 text-slate-500">没有待拉取或待推送的 Git 代码库。</p>}
  </section>;
}

function ProjectGitBatchDialog({ action, rows, busy, error, result, onClose, onSubmit }: { action: ProjectGitBatchAction; rows: ProjectGitRepositoryStatus[]; busy: boolean; error: string | null; result: ProjectGitBatchOperationResult | null; onClose: () => void; onSubmit: (message?: string) => void }) {
  const [type, setType] = useState<(typeof commitTypes)[number]["value"]>("fix");
  const [summary, setSummary] = useState("");
  const message = summary.trim() ? `${type}: ${summary.trim()}` : "";
  const isDiscard = action === "discard-and-pull";
  const title = isDiscard ? "一键重置更新" : "一键提交并推送";
  if (typeof document === "undefined") return null;
  return createPortal(<div className="fixed inset-0 z-[145] grid items-end overflow-y-auto bg-slate-950/60 p-3 backdrop-blur-sm sm:place-items-center sm:p-4" role="presentation" onMouseDown={() => !busy && onClose()}>
    <section className="max-h-[calc(100dvh-1.5rem)] w-full max-w-lg overflow-y-auto rounded-2xl border border-slate-200 bg-white p-4 shadow-2xl sm:max-h-[calc(100dvh-2rem)] sm:p-5" role="dialog" aria-modal="true" aria-labelledby="project-git-batch-title" onMouseDown={(event) => event.stopPropagation()}>
      <header className="flex items-start gap-3"><span className={`grid h-10 w-10 place-items-center rounded-xl ${isDiscard ? "bg-amber-50 text-amber-700" : "bg-emerald-50 text-emerald-600"}`}>{isDiscard ? <RotateCcw size={19}/> : <Upload size={19}/>}</span><div className="min-w-0 flex-1"><h2 id="project-git-batch-title" className="text-base font-semibold text-slate-900">{title}</h2><p className="mt-1 text-xs leading-5 text-slate-500">将按仓库依次执行，结果会逐项展示。</p></div><button type="button" onClick={onClose} disabled={busy} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100" aria-label="关闭"><X size={17}/></button></header>
      {!result && <div className="mt-4 space-y-3">
        <div className={`rounded-lg px-3 py-2 text-xs leading-5 ${isDiscard ? "bg-amber-50 text-amber-900" : "bg-emerald-50 text-emerald-800"}`}>{isDiscard ? "此操作会丢弃下列仓库中已跟踪文件的未提交修改，再以 fast-forward 拉取远端代码；未跟踪文件不会删除。无法更新或认证失败会按仓库报告。" : "使用同一提交说明提交并推送。远端领先、未设置上游或远端刷新失败的仓库会被跳过，请先一键重置更新。"}</div>
        <div className="max-h-40 space-y-1 overflow-y-auto rounded-lg border border-slate-200 bg-slate-50 p-2">{rows.map((row) => <p key={row.repository_id} className="break-words px-1 text-xs leading-5 text-slate-700"><strong>{row.display_name}</strong>：未提交 {row.status?.changes.length ?? 0} 个文件，远端领先 {row.status?.behind ?? 0} 个提交</p>)}</div>
        {!isDiscard && <><label className="grid gap-1.5 text-xs font-medium text-slate-700">提交类型<select value={type} onChange={(event) => setType(event.target.value as (typeof commitTypes)[number]["value"])} disabled={busy} className="h-10 rounded-lg border border-slate-200 bg-white px-3 text-sm text-slate-800 outline-none focus:border-blue-500">{commitTypes.map((item) => <option key={item.value} value={item.value}>{item.value} · {item.label}</option>)}</select></label><label className="grid gap-1.5 text-xs font-medium text-slate-700">统一提交说明<textarea value={summary} onChange={(event) => setSummary(event.target.value)} disabled={busy} autoFocus rows={3} className="resize-y rounded-lg border border-slate-200 px-3 py-2 text-sm leading-5 text-slate-800 outline-none focus:border-blue-500" placeholder="例如：修复项目批量 Git 状态检查"/></label><p className="rounded-lg bg-slate-50 px-3 py-2 font-mono text-xs text-slate-600">{message || `${type}: 请填写提交说明`}</p></>}
      </div>}
      {result && <div className="mt-4"><p className="mb-2 text-xs font-medium text-slate-700">操作结果</p><div className="max-h-72 space-y-2 overflow-y-auto">{result.repositories.map((row) => <div key={row.repository_id} className={`rounded-lg border px-3 py-2 text-xs leading-5 ${row.outcome === "succeeded" ? "border-emerald-200 bg-emerald-50 text-emerald-800" : row.outcome === "skipped" ? "border-amber-200 bg-amber-50 text-amber-900" : "border-rose-200 bg-rose-50 text-rose-700"}`}><strong>{row.display_name}</strong>：{row.outcome === "succeeded" ? "成功" : row.outcome === "skipped" ? "已跳过" : "失败"}<p className="break-words">{row.message}</p></div>)}</div></div>}
      {error && <p className="mt-3 break-words rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700">{error}</p>}
      <footer className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end"><button type="button" onClick={onClose} disabled={busy} className="h-9 w-full rounded-lg border border-slate-200 px-3 text-xs font-medium text-slate-600 hover:bg-slate-50 sm:w-auto">{result ? "关闭" : "取消"}</button>{!result && <button type="button" disabled={busy || (!isDiscard && !message)} onClick={() => onSubmit(message)} className={`inline-flex h-9 w-full items-center justify-center gap-1.5 rounded-lg px-3 text-xs font-semibold text-white disabled:bg-slate-300 sm:w-auto ${isDiscard ? "bg-amber-600 hover:bg-amber-700" : "bg-emerald-600 hover:bg-emerald-700"}`}>{busy ? <Loader2 size={14} className="animate-spin"/> : isDiscard ? <RotateCcw size={14}/> : <Upload size={14}/>}确认{title}</button>}</footer>
    </section>
  </div>, document.body);
}

type GitDiffLine = { kind: "context" | "add" | "remove" | "meta"; content: string; oldLine?: number; newLine?: number };
type GitDiffFile = GitWorkspaceDiffFile & { lines: GitDiffLine[] };

function GitDiffDialog({ repository, onClose }: { repository: CodeRepository; onClose: () => void }) {
  const [comparison, setComparison] = useState<GitDiffComparison>("working");
  const [diff, setDiff] = useState<GitWorkspaceDiff | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [selectedPath, setSelectedPath] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError("");
    setSelectedPath(null);
    void getCodeRepositoryGitDiff(repository.name, comparison).then((result) => {
      if (!cancelled) setDiff(result);
    }).catch((value) => {
      if (!cancelled) setError(value instanceof Error ? value.message : "无法读取代码差异。");
    }).finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [repository.name, comparison]);

  const parsedFiles = parseGitDiffFiles(diff?.content || "");
  const files = (diff?.files?.length ? diff.files.map((file) => ({ ...file, lines: parsedFiles.find((parsed) => parsed.path === file.path)?.lines ?? [] })) : parsedFiles);
  const activeFile = files.find((item) => item.path === selectedPath) || files[0] || null;
  if (typeof document === "undefined") return null;
  return createPortal(<div className="fixed inset-0 z-[150] grid place-items-center bg-slate-950/60 p-3 backdrop-blur-sm" role="presentation" onMouseDown={onClose}>
    <section className="flex h-[min(82vh,760px)] w-full max-w-6xl flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl" role="dialog" aria-modal="true" aria-labelledby="git-diff-title" onMouseDown={(event) => event.stopPropagation()}>
      <header className="flex shrink-0 items-start gap-3 border-b border-slate-200 px-4 py-3"><span className="grid h-9 w-9 place-items-center rounded-lg bg-violet-50 text-violet-700"><FileDiff size={18}/></span><div className="min-w-0 flex-1"><h2 id="git-diff-title" className="truncate text-sm font-semibold text-slate-900">代码文件差异</h2><p className="mt-0.5 truncate text-[11px] text-slate-500">{repository.display_name} · VS Code 风格的行级高亮</p></div><button type="button" onClick={onClose} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="关闭代码差异"><X size={17}/></button></header>
      <div className="flex shrink-0 gap-1 border-b border-slate-100 px-4 py-2">{(["working", "push", "pull"] as const).map((mode) => <button key={mode} type="button" onClick={() => setComparison(mode)} className={`rounded-md px-2.5 py-1.5 text-xs font-medium transition ${comparison === mode ? "bg-violet-600 text-white" : "text-slate-600 hover:bg-slate-100"}`}>{mode === "working" ? "工作区" : mode === "push" ? "待推送" : "待拉取"}</button>)}</div>
      {loading ? <div className="grid min-h-0 flex-1 place-items-center text-sm text-slate-500"><span><Loader2 size={16} className="mr-2 inline animate-spin"/>正在读取 Git Diff…</span></div> : error ? <p className="m-4 rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-xs text-rose-700">{error}</p> : files.length ? <div className="grid min-h-0 flex-1 md:grid-cols-[260px_minmax(0,1fr)]"><aside className="min-h-0 overflow-auto border-b border-slate-200 bg-slate-50 p-2 md:border-b-0 md:border-r"><p className="px-2 py-1 text-[11px] font-semibold text-slate-500">文件变更</p><GitDiffFileTree files={files} activePath={activeFile?.path} onSelect={setSelectedPath}/></aside><div className="min-h-0 overflow-auto bg-[#1e1e1e] p-3"><p className="mb-2 truncate font-mono text-[11px] text-slate-400">{activeFile?.path}</p>{activeFile?.lines.length ? <pre className="min-w-max overflow-visible font-mono text-[12px] leading-5 text-slate-100">{activeFile.lines.map((line, index) => <DiffCodeLine key={`${line.kind}-${index}`} line={line}/>)}</pre> : <p className="rounded-lg border border-white/10 bg-white/5 px-4 py-6 text-center text-xs leading-5 text-slate-400">该文件是未跟踪的新增文件，Git 尚无可对比的基线；它已在左侧目录树中标记为 U。</p>}</div></div> : <div className="grid min-h-0 flex-1 place-items-center px-8 text-center text-sm leading-6 text-slate-500">{diff?.message || "当前比较范围没有代码差异。"}</div>}
      {diff?.is_truncated && <p className="shrink-0 border-t border-amber-100 bg-amber-50 px-4 py-2 text-[11px] text-amber-800">差异内容较长，当前仅显示前 240,000 个字符。</p>}
    </section>
  </div>, document.body);
}

function DiffCodeLine({ line }: { line: GitDiffLine }) {
  const colors = line.kind === "add" ? "bg-emerald-500/20 text-emerald-100" : line.kind === "remove" ? "bg-rose-500/20 text-rose-100" : line.kind === "meta" ? "bg-slate-800 text-slate-400" : "text-slate-200";
  const prefix = line.kind === "add" ? "+" : line.kind === "remove" ? "-" : line.kind === "context" ? " " : "";
  return <span className={`flex min-w-max ${colors}`}><span className="w-12 select-none border-r border-white/5 px-2 text-right text-slate-500">{line.oldLine ?? ""}</span><span className="w-12 select-none border-r border-white/5 px-2 text-right text-slate-500">{line.newLine ?? ""}</span><span className="w-5 select-none text-center text-slate-400">{prefix}</span><code className="whitespace-pre pr-4">{line.content || " "}</code></span>;
}

type GitDiffFileTreeNode = { children: Map<string, GitDiffFileTreeNode>; file?: GitDiffFile };

function GitDiffFileTree({ files, activePath, onSelect }: { files: GitDiffFile[]; activePath?: string; onSelect: (path: string) => void }) {
  const root: GitDiffFileTreeNode = { children: new Map() };
  for (const file of files) {
    let node = root;
    for (const segment of file.path.split("/").filter(Boolean)) {
      let child = node.children.get(segment);
      if (!child) { child = { children: new Map() }; node.children.set(segment, child); }
      node = child;
    }
    node.file = file;
  }
  return <GitDiffFileTreeNodeView node={root} activePath={activePath} onSelect={onSelect}/>;
}

function GitDiffFileTreeNodeView({ node, activePath, onSelect, depth = 0 }: { node: GitDiffFileTreeNode; activePath?: string; onSelect: (path: string) => void; depth?: number }) {
  return <div>{[...node.children.entries()].sort(([leftName, left], [rightName, right]) => Number(Boolean(left.file)) - Number(Boolean(right.file)) || leftName.localeCompare(rightName)).map(([name, child]) => <GitDiffFileTreeItem key={name} name={name} node={child} activePath={activePath} onSelect={onSelect} depth={depth}/>)}</div>;
}

function GitDiffFileTreeItem({ name, node, activePath, onSelect, depth }: { name: string; node: GitDiffFileTreeNode; activePath?: string; onSelect: (path: string) => void; depth: number }) {
  const [expanded, setExpanded] = useState(depth < 2);
  if (node.file && node.children.size === 0) {
    const active = activePath === node.file.path;
    return <button type="button" onClick={() => onSelect(node.file!.path)} title={node.file.old_path ? `${node.file.old_path} → ${node.file.path}` : node.file.path} className={`flex min-h-8 w-full items-center gap-1.5 rounded-md py-1 pr-2 text-left text-xs transition ${active ? "bg-violet-100 text-violet-800" : "text-slate-600 hover:bg-white"}`} style={{ paddingLeft: `${depth * 12 + 8}px` }}><File size={13} className="shrink-0"/><span className="min-w-0 flex-1 truncate font-mono">{name}</span><GitDiffFileStatus status={node.file.status}/></button>;
  }
  return <div><button type="button" onClick={() => setExpanded((current) => !current)} className="flex min-h-8 w-full items-center gap-1 rounded-md py-1 pr-2 text-left text-xs font-medium text-slate-600 hover:bg-white" style={{ paddingLeft: `${depth * 12 + 4}px` }}><span className="grid h-4 w-4 place-items-center">{expanded ? <ChevronDown size={13}/> : <ChevronRight size={13}/>}</span>{expanded ? <FolderOpen size={14} className="shrink-0 text-amber-500"/> : <Folder size={14} className="shrink-0 text-amber-500"/>}<span className="min-w-0 truncate">{name}</span></button>{expanded && <GitDiffFileTreeNodeView node={node} activePath={activePath} onSelect={onSelect} depth={depth + 1}/>}</div>;
}

function GitDiffFileStatus({ status }: { status: string }) {
  const code = status === "??" ? "U" : status[0]?.toUpperCase() || "M";
  const color = code === "A" || code === "U" ? "bg-emerald-100 text-emerald-700" : code === "D" ? "bg-rose-100 text-rose-700" : code === "R" ? "bg-violet-100 text-violet-700" : "bg-amber-100 text-amber-700";
  return <span className={`rounded px-1.5 py-0.5 text-[10px] font-semibold ${color}`}>{code}</span>;
}

function parseGitDiffFiles(content: string): GitDiffFile[] {
  const files: GitDiffFile[] = [];
  let current: GitDiffFile | null = null;
  let oldLine = 0;
  let newLine = 0;
  for (const rawLine of content.split(/\r?\n/)) {
    const fileMatch = /^diff --git a\/(.+?) b\/(.+)$/.exec(rawLine);
    if (fileMatch) { current = { path: fileMatch[2], status: "M", lines: [] }; files.push(current); oldLine = 0; newLine = 0; continue; }
    if (!current) continue;
    const hunkMatch = /^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@/.exec(rawLine);
    if (hunkMatch) { oldLine = Number(hunkMatch[1]); newLine = Number(hunkMatch[2]); current.lines.push({ kind: "meta", content: rawLine }); continue; }
    if (rawLine.startsWith("+++") || rawLine.startsWith("---") || rawLine.startsWith("index ") || rawLine.startsWith("new file") || rawLine.startsWith("deleted file")) { current.lines.push({ kind: "meta", content: rawLine }); continue; }
    if (rawLine.startsWith("+")) { current.lines.push({ kind: "add", content: rawLine.slice(1), newLine: newLine || undefined }); newLine += 1; continue; }
    if (rawLine.startsWith("-")) { current.lines.push({ kind: "remove", content: rawLine.slice(1), oldLine: oldLine || undefined }); oldLine += 1; continue; }
    if (rawLine.startsWith(" ")) { current.lines.push({ kind: "context", content: rawLine.slice(1), oldLine: oldLine || undefined, newLine: newLine || undefined }); oldLine += 1; newLine += 1; continue; }
    if (rawLine) current.lines.push({ kind: "meta", content: rawLine });
  }
  return files;
}

function RunCard({ run, busy, onForceStop }: { run: CodeRuntimeRun; busy: boolean; onForceStop: () => void }) {
  return <div className="rounded-lg border border-slate-100 bg-slate-50 px-2.5 py-2">
    <div className="flex items-center gap-2"><span className={`h-2 w-2 rounded-full ${run.status === "running" ? "bg-emerald-500" : "bg-amber-400"}`}/><span className="min-w-0 flex-1 truncate text-xs text-slate-700">{run.repository_name} · {run.role} · :{run.port}</span><button type="button" disabled={busy || run.status === "stopping"} onClick={onForceStop} title="强制关闭该 Shell 及其子进程" className="inline-flex h-7 shrink-0 items-center gap-1 rounded-md px-1.5 text-[10px] font-medium text-rose-600 hover:bg-rose-50 disabled:opacity-40"><Square size={12}/>强制结束</button></div>
  </div>;
}

function isActiveRun(run: CodeRuntimeRun) {
  return run.status === "starting" || run.status === "running" || run.status === "stopping";
}

const commitTypes = [
  { value: "feat", label: "新功能、新能力" },
  { value: "fix", label: "修复 bug、纠正错误逻辑" },
  { value: "refactor", label: "重构代码，不改变功能" },
  { value: "perf", label: "性能优化" },
  { value: "docs", label: "文档修改" },
  { value: "style", label: "格式、代码风格调整" },
  { value: "test", label: "测试相关" },
  { value: "chore", label: "构建、工具、依赖等杂项" },
] as const;

function CommitPushDialog({ repository, busy, error, onClose, onSubmit }: { repository: CodeRepository; busy: boolean; error: string | null; onClose: () => void; onSubmit: (message: string) => void }) {
  const [type, setType] = useState<(typeof commitTypes)[number]["value"]>("fix");
  const [summary, setSummary] = useState("");
  const message = summary.trim() ? `${type}: ${summary.trim()}` : "";
  if (typeof document === "undefined") return null;
  return createPortal(<div className="fixed inset-0 z-[140] grid items-end overflow-y-auto bg-slate-950/60 p-3 backdrop-blur-sm sm:place-items-center sm:p-4" role="presentation" onMouseDown={onClose}>
    <section className="max-h-[calc(100dvh-1.5rem)] w-full max-w-md overflow-y-auto rounded-2xl border border-slate-200 bg-white p-4 shadow-2xl sm:max-h-[calc(100dvh-2rem)] sm:p-5" role="dialog" aria-modal="true" aria-labelledby="commit-push-title" onMouseDown={(event) => event.stopPropagation()}>
      <header className="flex items-start gap-3"><span className="grid h-10 w-10 place-items-center rounded-xl bg-emerald-50 text-emerald-600"><Upload size={19}/></span><div className="min-w-0 flex-1"><h2 id="commit-push-title" className="text-base font-semibold text-slate-900">提交并推送</h2><p className="mt-1 truncate text-xs text-slate-500">{repository.display_name}</p></div><button type="button" onClick={onClose} disabled={busy} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100" aria-label="关闭提交推送"><X size={17}/></button></header>
      <div className="mt-5 grid gap-3"><label className="grid gap-1.5 text-xs font-medium text-slate-700">提交类型<select value={type} onChange={(event) => setType(event.target.value as (typeof commitTypes)[number]["value"])} disabled={busy} className="h-10 rounded-lg border border-slate-200 bg-white px-3 text-sm text-slate-800 outline-none focus:border-blue-500">{commitTypes.map((item) => <option key={item.value} value={item.value}>{item.value} · {item.label}</option>)}</select></label><label className="grid gap-1.5 text-xs font-medium text-slate-700">提交说明<textarea value={summary} onChange={(event) => setSummary(event.target.value)} onKeyDown={(event) => { if ((event.ctrlKey || event.metaKey) && event.key === "Enter" && message && !busy) { event.preventDefault(); onSubmit(message); } }} disabled={busy} autoFocus rows={4} className="min-h-24 max-h-48 resize-y rounded-lg border border-slate-200 px-3 py-2 text-sm leading-5 text-slate-800 outline-none focus:border-blue-500" placeholder={"例如：修复库存扣减数量计算错误\n\n补充说明：库存为 0 时不再继续扣减。"}/><span className="text-[11px] font-normal text-slate-400">支持多行；按 Ctrl / ⌘ + Enter 可直接提交。</span></label><div className="max-h-32 overflow-y-auto whitespace-pre-wrap break-words rounded-lg bg-slate-50 px-3 py-2 font-mono text-xs leading-5 text-slate-600">{message || `${type}: 请填写提交说明`}</div>{error && <p className="break-words rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700">{error}</p>}</div>
      <footer className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end"><button type="button" onClick={onClose} disabled={busy} className="h-9 w-full rounded-lg border border-slate-200 px-3 text-xs font-medium text-slate-600 hover:bg-slate-50 sm:w-auto">取消</button><button type="button" disabled={!message || busy} onClick={() => onSubmit(message)} className="inline-flex h-9 w-full items-center justify-center gap-1.5 rounded-lg bg-emerald-600 px-3 text-xs font-semibold text-white hover:bg-emerald-700 disabled:bg-slate-300 sm:w-auto">{busy ? <Loader2 size={14} className="animate-spin"/> : <Upload size={14}/>}提交并推送</button></footer>
    </section>
  </div>, document.body);
}

function ChatConfigurationEditor({ draft, busy, onChange, onClose, onSave }: { draft: ChatConfigDraft; busy: boolean; onChange: (draft: ChatConfigDraft | null) => void; onClose: () => void; onSave: () => void }) {
  if (typeof document === "undefined") return null;
  return createPortal(<div className="fixed inset-0 z-[120] grid place-items-center bg-slate-950/30 p-4"><section className="flex max-h-[82vh] w-full max-w-3xl flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl"><header className="flex items-start gap-3 border-b border-slate-100 px-5 py-4"><FilePenLine size={18} className="mt-0.5 text-blue-600"/><div className="min-w-0 flex-1"><h2 className="truncate text-sm font-semibold text-slate-900">{draft.repositoryDisplayName} · {draft.path}</h2><p className="mt-1 text-[11px] text-slate-500">仅此代码库明确开放给聊天修改的配置文件可以保存。</p></div><button type="button" onClick={onClose} disabled={busy} className="grid h-7 w-7 place-items-center rounded-md text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="关闭"><X size={16}/></button></header><textarea value={draft.content} onChange={(event) => onChange({ ...draft, content: event.target.value })} spellCheck={false} className="min-h-[360px] flex-1 resize-none bg-slate-950 p-4 font-mono text-xs leading-6 text-slate-100 outline-none"/><footer className="flex items-center justify-between gap-3 border-t border-slate-100 px-5 py-3"><span className="text-[11px] text-slate-400">保存时会检查文件是否已在磁盘上被修改。</span><button type="button" onClick={onSave} disabled={busy} className="inline-flex h-8 items-center gap-1.5 rounded-md bg-blue-600 px-3 text-xs font-medium text-white hover:bg-blue-700 disabled:bg-slate-300">{busy ? <Loader2 size={14} className="animate-spin"/> : <Save size={14}/>}保存配置</button></footer></section></div>, document.body);
}
