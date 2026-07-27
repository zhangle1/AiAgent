"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { ChevronRight, Download, FileDiff, FolderGit2, GitBranch, Loader2, RefreshCw, RotateCcw, Upload } from "lucide-react";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";
import { checkoutCodeRepositoryGitBranch, discardCodeRepositoryChangesAndPull, getCodeProjects, getCodeRepositories, getCodeRepositoryGitBranches, getCodeRepositoryGitDiff, getCodeRepositoryGitStatus, pullCodeRepositoryGit, pushCodeRepositoryGit } from "@/lib/code-repository-api";
import type { CodeProject, CodeRepository, GitDiffComparison, GitWorkspaceBranches, GitWorkspaceDiff, GitWorkspaceStatus } from "@/lib/code-repository-types";

type BusyAction = "" | "load" | "pull" | "push" | "checkout" | "discard-pull" | "diff";

export function GitWorkspacePage() {
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [repositories, setRepositories] = useState<CodeRepository[]>([]);
  const [selected, setSelected] = useState<CodeRepository | null>(null);
  const [status, setStatus] = useState<GitWorkspaceStatus | null>(null);
  const [branches, setBranches] = useState<GitWorkspaceBranches | null>(null);
  const [diff, setDiff] = useState<GitWorkspaceDiff | null>(null);
  const [comparison, setComparison] = useState<GitDiffComparison>("working");
  const [branchTarget, setBranchTarget] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState<BusyAction>("");
  const [error, setError] = useState("");

  const projectRepositoryIds = useMemo(() => new Set(projects.flatMap((project) => project.repositories.map((repository) => repository.id))), [projects]);
  const unassigned = useMemo(() => repositories.filter((repository) => !projectRepositoryIds.has(repository.id)), [projectRepositoryIds, repositories]);

  useEffect(() => { void load(); }, []);

  async function load() {
    try {
      const [projectRows, repositoryRows] = await Promise.all([getCodeProjects(), getCodeRepositories()]);
      setProjects(projectRows);
      setRepositories(repositoryRows);
      const current = selected && repositoryRows.find((repository) => repository.id === selected.id);
      const first = current || projectRows.flatMap((project) => project.repositories)[0] || repositoryRows[0];
      if (first) await choose(first);
      else setSelected(null);
    } catch (value) {
      setError(text(value));
    }
  }

  async function choose(repository: CodeRepository) {
    setSelected(repository);
    setStatus(null);
    setBranches(null);
    setDiff(null);
    setError("");
    setBusy("load");
    try {
      const [nextStatus, nextBranches] = await Promise.all([getCodeRepositoryGitStatus(repository.name), getCodeRepositoryGitBranches(repository.name)]);
      setStatus(nextStatus);
      setBranches(nextBranches);
      setBranchTarget(nextStatus.branch || nextBranches.current_branch || "");
      await loadDiff(repository.name, "working");
    } catch (value) {
      setError(text(value));
    } finally {
      setBusy("");
    }
  }

  async function loadDiff(repositoryName = selected?.name, mode = comparison) {
    if (!repositoryName) return;
    setComparison(mode);
    setBusy("diff");
    try {
      setDiff(await getCodeRepositoryGitDiff(repositoryName, mode));
    } catch (value) {
      setError(text(value));
    } finally {
      setBusy("");
    }
  }

  async function refreshSelected() {
    if (selected) await choose(selected);
  }

  async function checkout() {
    if (!selected || !branchTarget) return;
    setBusy("checkout");
    setError("");
    try {
      const result = await checkoutCodeRepositoryGitBranch(selected.name, branchTarget);
      if (!result.ok) throw new Error(result.output || "切换分支失败。");
      await choose(selected);
    } catch (value) {
      setError(text(value));
    } finally {
      setBusy("");
    }
  }

  async function pull() {
    if (!selected) return;
    setBusy("pull");
    setError("");
    try {
      const result = await pullCodeRepositoryGit(selected.name);
      setStatus(result.status);
      if (!result.ok) throw new Error(result.output || "拉取失败。");
      await Promise.all([loadDiff(selected.name, comparison), refreshBranches(selected.name)]);
    } catch (value) {
      setError(text(value));
    } finally {
      setBusy("");
    }
  }

  async function discardAndPull() {
    if (!selected || !window.confirm(`更新“${selected.display_name}”会用服务器上的最新代码替换本机尚未保存的修改。您额外新建的文件和已提交的版本不会删除。确认更新代码库吗？`)) return;
    setBusy("discard-pull");
    setError("");
    try {
      const result = await discardCodeRepositoryChangesAndPull(selected.name);
      setStatus(result.status);
      if (!result.ok) throw new Error(result.output || "更新代码库失败。");
      await Promise.all([loadDiff(selected.name, comparison), refreshBranches(selected.name)]);
    } catch (value) {
      setError(text(value));
    } finally {
      setBusy("");
    }
  }

  async function push() {
    if (!selected) return;
    setBusy("push");
    setError("");
    try {
      const result = await pushCodeRepositoryGit(selected.name, message);
      setStatus(result.status);
      if (!result.ok) throw new Error(result.output || "提交或推送失败。");
      setMessage("");
      await Promise.all([loadDiff(selected.name, comparison), refreshBranches(selected.name)]);
    } catch (value) {
      setError(text(value));
    } finally {
      setBusy("");
    }
  }

  async function refreshBranches(repositoryName: string) {
    const nextBranches = await getCodeRepositoryGitBranches(repositoryName);
    setBranches(nextBranches);
  }

  return <section>
    <SettingsPageHeader title="Git 管理" description="按项目浏览代码库，查看与服务器的差异、切换分支，并安全地拉取或提交推送。" action={<Link href="/settings/git/accounts" className="inline-flex h-9 items-center rounded-lg border border-slate-200 bg-white px-3 text-xs font-medium text-slate-700 hover:bg-slate-50">Git 账号与令牌</Link>}/>
    <div className="grid gap-5 xl:grid-cols-[300px_minmax(0,1fr)]">
      <aside className="max-h-[calc(100vh-220px)] overflow-auto rounded-xl border border-slate-200 bg-white p-3">
        <div className="mb-2 flex items-center justify-between px-1"><span className="text-xs font-semibold text-slate-700">项目与代码库</span><button type="button" onClick={() => void load()} disabled={Boolean(busy)} className="grid h-7 w-7 place-items-center rounded-md text-slate-400 hover:bg-slate-100 disabled:opacity-40" aria-label="刷新项目"><RefreshCw size={14} className={busy === "load" ? "animate-spin" : undefined}/></button></div>
        <ProjectTree projects={projects} unassigned={unassigned} selectedId={selected?.id} onSelect={(repository) => void choose(repository)}/>
      </aside>
      <main className="min-w-0 rounded-xl border border-slate-200 bg-white p-5">
        {selected ? <GitWorkspaceDetail selected={selected} status={status} branches={branches} diff={diff} comparison={comparison} branchTarget={branchTarget} message={message} busy={busy} onRefresh={() => void refreshSelected()} onComparison={(mode) => void loadDiff(undefined, mode)} onBranchChange={setBranchTarget} onCheckout={() => void checkout()} onMessageChange={setMessage} onPull={() => void pull()} onDiscardAndPull={() => void discardAndPull()} onPush={() => void push()}/> : <p className="py-12 text-center text-sm text-slate-400">没有可查看的代码库。</p>}
        {error && <p className="mt-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>}
      </main>
    </div>
  </section>;
}

function ProjectTree({ projects, unassigned, selectedId, onSelect }: { projects: CodeProject[]; unassigned: CodeRepository[]; selectedId?: number; onSelect: (repository: CodeRepository) => void }) {
  return <div className="space-y-3">
    {projects.map((project) => <section key={project.id}><div className="flex items-center gap-2 px-2 py-1.5 text-xs font-semibold text-slate-700"><FolderGit2 size={15} className="text-slate-500"/><span className="min-w-0 flex-1 truncate">{project.display_name}</span><span className="text-[10px] font-normal text-slate-400">{project.repositories.length}</span></div><div className="ml-3 border-l border-slate-100 pl-2">{project.repositories.length ? project.repositories.map((repository) => <RepositoryTreeItem key={repository.id} repository={repository} selected={selectedId === repository.id} onSelect={onSelect}/>) : <p className="px-2 py-2 text-[11px] text-slate-400">未挂载代码库</p>}</div></section>)}
    {unassigned.length ? <section><div className="flex items-center gap-2 px-2 py-1.5 text-xs font-semibold text-slate-500"><FolderGit2 size={15}/><span>未归类代码库</span></div><div className="ml-3 border-l border-slate-100 pl-2">{unassigned.map((repository) => <RepositoryTreeItem key={repository.id} repository={repository} selected={selectedId === repository.id} onSelect={onSelect}/>)}</div></section> : null}
  </div>;
}

function RepositoryTreeItem({ repository, selected, onSelect }: { repository: CodeRepository; selected: boolean; onSelect: (repository: CodeRepository) => void }) {
  return <button type="button" onClick={() => onSelect(repository)} className={`mb-1 flex w-full items-center gap-2 rounded-lg px-2 py-2 text-left ${selected ? "bg-blue-50 text-blue-700" : "text-slate-600 hover:bg-slate-50"}`}><GitBranch size={14} className={selected ? "text-blue-500" : "text-slate-400"}/><span className="min-w-0 flex-1 truncate text-xs font-medium">{repository.display_name}</span><ChevronRight size={13} className={selected ? "text-blue-400" : "text-slate-300"}/></button>;
}

function GitWorkspaceDetail({ selected, status, branches, diff, comparison, branchTarget, message, busy, onRefresh, onComparison, onBranchChange, onCheckout, onMessageChange, onPull, onDiscardAndPull, onPush }: { selected: CodeRepository; status: GitWorkspaceStatus | null; branches: GitWorkspaceBranches | null; diff: GitWorkspaceDiff | null; comparison: GitDiffComparison; branchTarget: string; message: string; busy: BusyAction; onRefresh: () => void; onComparison: (mode: GitDiffComparison) => void; onBranchChange: (branch: string) => void; onCheckout: () => void; onMessageChange: (message: string) => void; onPull: () => void; onDiscardAndPull: () => void; onPush: () => void }) {
  const isLoading = busy === "load";
  if (isLoading) return <div className="grid min-h-80 place-items-center text-sm text-slate-400"><Loader2 size={18} className="mr-2 inline animate-spin"/>正在读取 Git 状态…</div>;
  return <><div className="flex flex-wrap items-start justify-between gap-3"><div><div className="flex items-center gap-2 text-sm font-semibold text-slate-900"><GitBranch size={17} className="text-blue-600"/>{selected.display_name}</div><p className="mt-1 font-mono text-xs text-slate-500">{selected.root_path}</p></div><button type="button" onClick={onRefresh} disabled={Boolean(busy)} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-slate-200 px-2.5 text-xs text-slate-600 hover:bg-slate-50 disabled:opacity-50"><RefreshCw size={13} className={busy ? "animate-spin" : undefined}/>刷新状态</button></div>{status?.is_repository ? <><div className="mt-6 grid gap-3 sm:grid-cols-2 xl:grid-cols-5"><Stat label="本地分支" value={status.branch || "detached"}/><Stat label="远程分支" value={status.remote_branch || "未设置上游"}/><Stat label="远端领先 / 拉取" value={`${status.behind} 提交 · ${status.behind_files} 文件`}/><Stat label="本地领先 / 推送" value={`${status.ahead} 提交 · ${status.ahead_files} 文件`}/><Stat label="待提交" value={`${status.changes.length} 文件`}/></div>{status.remote_refresh_error && <p className="mt-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800" title={status.remote_refresh_error}>远程刷新失败，领先和文件数当前来自本地缓存。</p>}<section className="mt-5 rounded-xl border border-slate-200 bg-slate-50/60 p-4"><div className="flex flex-wrap items-center justify-between gap-3"><div><h3 className="text-sm font-semibold text-slate-800">切换分支</h3><p className="mt-1 text-[11px] text-slate-500">若工作区有未提交修改，服务端会拒绝切换以保护本地文件。</p></div><div className="flex min-w-[280px] flex-1 justify-end gap-2"><select value={branchTarget} onChange={(event) => onBranchChange(event.target.value)} disabled={Boolean(busy)} className="h-9 min-w-0 flex-1 rounded-lg border border-slate-200 bg-white px-2 text-xs text-slate-700 outline-none focus:border-blue-500"><optgroup label="本地分支">{branches?.local_branches.map((branch) => <option key={branch} value={branch}>{branch}</option>)}</optgroup><optgroup label="远程分支">{branches?.remote_branches.map((branch) => <option key={branch} value={branch}>{branch}</option>)}</optgroup></select><button type="button" onClick={onCheckout} disabled={Boolean(busy) || !branchTarget} className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-blue-200 bg-white px-3 text-xs font-medium text-blue-700 hover:bg-blue-50 disabled:opacity-50">{busy === "checkout" ? <Loader2 size={13} className="animate-spin"/> : <GitBranch size={13}/>}切换</button></div></div></section><section className="mt-5 overflow-hidden rounded-xl border border-slate-200"><div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 bg-slate-50 px-4 py-3"><div><div className="flex items-center gap-2 text-sm font-semibold text-slate-800"><FileDiff size={16} className="text-blue-600"/>Git Diff</div><p className="mt-0.5 text-[11px] text-slate-500">查看工作区，或与服务器分支比较的变更。</p></div><span className="rounded-full bg-white px-2 py-1 text-[11px] text-slate-500">{diff?.file_count ?? 0} 个文件</span></div><div className="flex flex-wrap gap-2 border-b border-slate-100 px-4 py-2"><DiffTab active={comparison === "working"} onClick={() => onComparison("working")}>工作区</DiffTab><DiffTab active={comparison === "push"} onClick={() => onComparison("push")}>待推送差异</DiffTab><DiffTab active={comparison === "pull"} onClick={() => onComparison("pull")}>待拉取差异</DiffTab></div><div className="p-3">{busy === "diff" ? <div className="grid h-52 place-items-center text-xs text-slate-400"><Loader2 size={16} className="mr-2 inline animate-spin"/>正在生成 Diff…</div> : diff?.content ? <pre className="workspace-scroll max-h-[420px] overflow-auto rounded-lg bg-slate-950 p-4 font-mono text-xs leading-5 text-slate-100">{diff.content}</pre> : <p className="rounded-lg bg-slate-50 px-4 py-8 text-center text-xs text-slate-500">{diff?.message || "暂无差异。"}</p>}{diff?.is_truncated ? <p className="mt-2 text-[11px] text-amber-700">Diff 内容较长，当前仅显示前 240,000 个字符。</p> : null}</div></section><div className="mt-5 grid gap-3 border-t border-slate-100 pt-5 md:grid-cols-[minmax(0,1fr)_auto_auto_auto]"><input value={message} onChange={(event) => onMessageChange(event.target.value)} className="h-10 rounded-lg border border-slate-200 px-3 text-sm outline-none focus:border-blue-500" placeholder={`chore: update ${selected.display_name}`}/><button type="button" onClick={onPull} disabled={Boolean(busy)} className="inline-flex h-10 items-center justify-center gap-2 rounded-lg border border-slate-200 px-4 text-xs font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50">{busy === "pull" ? <Loader2 size={14} className="animate-spin"/> : <Download size={14}/>}拉取</button><button type="button" onClick={onDiscardAndPull} disabled={Boolean(busy)} className="inline-flex h-10 items-center justify-center gap-2 rounded-lg border border-amber-200 bg-amber-50 px-4 text-xs font-medium text-amber-800 hover:bg-amber-100 disabled:opacity-50" title="撤回已跟踪文件的未提交修改后拉取；未跟踪文件和已提交历史会保留">{busy === "discard-pull" ? <Loader2 size={14} className="animate-spin"/> : <RotateCcw size={14}/>}撤回修改并拉取</button><button type="button" onClick={onPush} disabled={Boolean(busy)} className="inline-flex h-10 items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 text-xs font-medium text-white hover:bg-blue-700 disabled:opacity-50">{busy === "push" ? <Loader2 size={14} className="animate-spin"/> : <Upload size={14}/>}提交并推送</button></div></> : <div className="mt-8 rounded-lg border border-dashed border-slate-300 bg-slate-50 p-5 text-sm text-slate-500">该代码库尚未初始化 Git。请先初始化或克隆到该代码库目录。</div>}</>;
}

function DiffTab({ active, onClick, children }: { active: boolean; onClick: () => void; children: string }) { return <button type="button" onClick={onClick} className={`rounded-md px-2.5 py-1.5 text-xs font-medium ${active ? "bg-blue-600 text-white" : "text-slate-600 hover:bg-slate-100"}`}>{children}</button>; }
function Stat({ label, value }: { label: string; value: string }) { return <div className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-3"><span className="block text-xs text-slate-500">{label}</span><strong className="mt-1 block truncate text-sm text-slate-900" title={value}>{value}</strong></div>; }
function text(value: unknown) { return value instanceof Error ? value.message : "操作失败，请稍后重试。"; }
