"use client";

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ChevronDown, FilePenLine, Loader2, PackageOpen, PanelLeftOpen, PanelRightOpen, Play, RefreshCw, Save, Square, Terminal, X } from "lucide-react";
import { getCodeProjectRuntime, startCodeProjectRuntime, stopCodeProjectRuntime } from "@/lib/code-runtime-api";
import { packageCodeRepositoryViaWebSocket, readChatConfiguredCodeFile, writeChatConfiguredCodeFile } from "@/lib/code-repository-api";
import type { CodeProject, CodeRepository, ConfiguredCodeFile } from "@/lib/code-repository-types";
import type { CodeProjectRuntime, CodeRuntimeProfile, CodeRuntimeRun } from "@/lib/code-runtime-types";

type ChatConfigDraft = ConfiguredCodeFile & { repositoryName: string; repositoryDisplayName: string };

export function ChatRuntimeToolbar({ project, rightPanelOpen, onToggleRightPanel, onOpenRuntimePanel }: { project: CodeProject | null; rightPanelOpen: boolean; onToggleRightPanel: () => void; onOpenRuntimePanel: () => void }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [runtime, setRuntime] = useState<CodeProjectRuntime | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [packageStatus, setPackageStatus] = useState<Record<string, string>>({});
  const [configDraft, setConfigDraft] = useState<ChatConfigDraft | null>(null);
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
    try {
      setRuntime(await getCodeProjectRuntime(project.id));
      setError(null);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "无法读取运行状态。");
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

  const activeRuns = runtime?.runs.filter(isActiveRun) ?? [];

  return <>
    <div className="relative flex items-center gap-1.5">
      <div ref={menuRef} className="relative">
        <button type="button" onClick={() => setMenuOpen((current) => !current)} className={`inline-flex h-8 items-center gap-1.5 rounded-lg border px-2.5 text-xs font-medium shadow-sm ${menuOpen ? "border-blue-300 bg-blue-50 text-blue-700" : "border-slate-200 bg-white text-slate-600 hover:border-blue-300 hover:text-blue-600"}`} aria-expanded={menuOpen}>
          <Terminal size={14}/>项目程序运行<ChevronDown size={13} className={menuOpen ? "rotate-180 transition" : "transition"}/>
        </button>
        {menuOpen && <div className="absolute right-0 top-10 z-[90] w-[390px] overflow-hidden rounded-xl border border-slate-200 bg-white p-3 shadow-[0_18px_42px_rgba(15,23,42,0.2)]">
          <div className="mb-3 flex items-center justify-between gap-3">
            <div>
              <p className="text-sm font-semibold text-slate-900">项目程序运行</p>
              <p className="mt-0.5 text-[11px] text-slate-500">{project ? `${project.display_name} · 可按代码库单独启动` : "请先在聊天底部选择项目"}</p>
            </div>
            <button type="button" onClick={() => void refresh()} className="grid h-7 w-7 place-items-center rounded-md text-slate-500 hover:bg-slate-100" aria-label="刷新"><RefreshCw size={14}/></button>
          </div>

          {project && <button type="button" disabled={busy} onClick={() => void startProfiles(runtime?.profiles ?? [], "项目")} className="mb-3 inline-flex h-9 w-full items-center justify-center gap-2 rounded-lg bg-blue-600 text-xs font-semibold text-white hover:bg-blue-700 disabled:bg-slate-300">
            {busy ? <Loader2 size={14} className="animate-spin"/> : <Play size={14}/>}启动全部已配置程序
          </button>}

          {project?.repositories.length ? <div className="mb-3 space-y-2">
            <p className="px-0.5 text-[11px] font-semibold text-slate-500">代码库</p>
            {project.repositories.map((repository) => <RepositoryCard key={repository.id} repository={repository} profiles={runtime?.profiles ?? []} busy={busy} packageStatus={packageStatus[repository.name]} onStart={(profiles) => void startProfiles(profiles, repository.display_name)} onPackage={() => void packageRepository(repository.name)} onOpenConfiguration={(path) => void openConfiguration(repository.name, repository.display_name, path)}/>) }
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
        </div>}
      </div>
      <button type="button" onClick={onToggleRightPanel} className={`grid h-8 w-8 place-items-center rounded-lg border shadow-sm ${rightPanelOpen ? "border-blue-300 bg-blue-50 text-blue-700" : "border-slate-200 bg-white text-slate-600 hover:border-blue-300 hover:text-blue-600"}`} aria-label="打开或关闭右侧面板">{rightPanelOpen ? <PanelLeftOpen size={15}/> : <PanelRightOpen size={15}/>}</button>
    </div>
    {configDraft && <ChatConfigurationEditor draft={configDraft} busy={busy} onChange={setConfigDraft} onClose={() => !busy && setConfigDraft(null)} onSave={() => void saveConfiguration()} />}
  </>;
}

function RepositoryCard({ repository, profiles, busy, packageStatus, onStart, onPackage, onOpenConfiguration }: { repository: CodeRepository; profiles: CodeRuntimeProfile[]; busy: boolean; packageStatus?: string; onStart: (profiles: CodeRuntimeProfile[]) => void; onPackage: () => void; onOpenConfiguration: (path: string) => void }) {
  const repositoryProfiles = profiles.filter((profile) => profile.repository_id === repository.id && profile.is_enabled);
  return <section className="rounded-lg border border-slate-100 bg-slate-50/70 p-2.5">
    <div className="flex items-center gap-1.5">
      <span className="min-w-0 flex-1 truncate text-xs font-semibold text-slate-800">{repository.display_name}</span>
      <button type="button" disabled={busy || !repositoryProfiles.length} onClick={() => onStart(repositoryProfiles)} title={repositoryProfiles.length ? "只运行此代码库的已启用配置" : "请先为代码库保存运行配置"} className="inline-flex h-7 shrink-0 items-center gap-1 rounded-md bg-blue-600 px-2 text-[11px] font-medium text-white hover:bg-blue-700 disabled:bg-slate-300"><Play size={13}/>运行</button>
      <button type="button" disabled={busy} onClick={onPackage} className="inline-flex h-7 shrink-0 items-center gap-1 rounded-md border border-blue-200 bg-white px-2 text-[11px] font-medium text-blue-700 hover:bg-blue-50 disabled:opacity-50"><PackageOpen size={13}/>打包</button>
    </div>
    {(repository.chat_editable_configuration_files ?? []).length ? <div className="mt-2 flex flex-wrap gap-1.5">{repository.chat_editable_configuration_files.map((path) => <button type="button" key={path} disabled={busy} onClick={() => onOpenConfiguration(path)} className="inline-flex max-w-full items-center gap-1 rounded-md border border-slate-200 bg-white px-2 py-1 font-mono text-[10px] text-slate-600 hover:border-blue-200 hover:bg-blue-50 hover:text-blue-700 disabled:opacity-50" title={`在聊天中编辑 ${path}`}><FilePenLine size={12}/><span className="truncate">{path}</span></button>)}</div> : <p className="mt-1.5 text-[10px] text-slate-400">未开放聊天可修改的配置文件</p>}
    {packageStatus && <p className="mt-1.5 truncate text-[10px] text-slate-500" title={packageStatus}>{packageStatus}</p>}
  </section>;
}

function RunCard({ run, busy, onForceStop }: { run: CodeRuntimeRun; busy: boolean; onForceStop: () => void }) {
  return <div className="rounded-lg border border-slate-100 bg-slate-50 px-2.5 py-2">
    <div className="flex items-center gap-2"><span className={`h-2 w-2 rounded-full ${run.status === "running" ? "bg-emerald-500" : "bg-amber-400"}`}/><span className="min-w-0 flex-1 truncate text-xs text-slate-700">{run.repository_name} · {run.role} · :{run.port}</span><button type="button" disabled={busy || run.status === "stopping"} onClick={onForceStop} title="强制关闭该 Shell 及其子进程" className="inline-flex h-7 shrink-0 items-center gap-1 rounded-md px-1.5 text-[10px] font-medium text-rose-600 hover:bg-rose-50 disabled:opacity-40"><Square size={12}/>强制结束</button></div>
  </div>;
}

function isActiveRun(run: CodeRuntimeRun) {
  return run.status === "starting" || run.status === "running" || run.status === "stopping";
}

function ChatConfigurationEditor({ draft, busy, onChange, onClose, onSave }: { draft: ChatConfigDraft; busy: boolean; onChange: (draft: ChatConfigDraft | null) => void; onClose: () => void; onSave: () => void }) {
  if (typeof document === "undefined") return null;
  return createPortal(<div className="fixed inset-0 z-[120] grid place-items-center bg-slate-950/30 p-4"><section className="flex max-h-[82vh] w-full max-w-3xl flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl"><header className="flex items-start gap-3 border-b border-slate-100 px-5 py-4"><FilePenLine size={18} className="mt-0.5 text-blue-600"/><div className="min-w-0 flex-1"><h2 className="truncate text-sm font-semibold text-slate-900">{draft.repositoryDisplayName} · {draft.path}</h2><p className="mt-1 text-[11px] text-slate-500">仅此代码库明确开放给聊天修改的配置文件可以保存。</p></div><button type="button" onClick={onClose} disabled={busy} className="grid h-7 w-7 place-items-center rounded-md text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="关闭"><X size={16}/></button></header><textarea value={draft.content} onChange={(event) => onChange({ ...draft, content: event.target.value })} spellCheck={false} className="min-h-[360px] flex-1 resize-none bg-slate-950 p-4 font-mono text-xs leading-6 text-slate-100 outline-none"/><footer className="flex items-center justify-between gap-3 border-t border-slate-100 px-5 py-3"><span className="text-[11px] text-slate-400">保存时会检查文件是否已在磁盘上被修改。</span><button type="button" onClick={onSave} disabled={busy} className="inline-flex h-8 items-center gap-1.5 rounded-md bg-blue-600 px-3 text-xs font-medium text-white hover:bg-blue-700 disabled:bg-slate-300">{busy ? <Loader2 size={14} className="animate-spin"/> : <Save size={14}/>}保存配置</button></footer></section></div>, document.body);
}
