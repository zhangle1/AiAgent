"use client";

import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useSearchParams } from "next/navigation";
import { Braces, Check, FileCog, FilePenLine, FolderGit2, FolderOpen, GitBranch, Loader2, PackageOpen, Plus, RefreshCw, ShieldCheck, Terminal, Trash2, X } from "lucide-react";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";
import { browseCodeRepositoryDirectories, browseCodeRepositoryFiles, cloneCodeRepositoryViaWebSocket, createCodeProject, createCodeRepository, deleteCodeProject, deleteCodeRepository, getCodeProjects, getCodeRepositories, getCodeRepositoryHealth, inspectCodeRepository, packageCodeRepositoryViaWebSocket, readConfiguredCodeFile, updateCodeProject, updateCodeRepository, writeConfiguredCodeFile } from "@/lib/code-repository-api";
import type { CodeProject, CodeRepository, CodeRepositoryDirectoryBrowser, CodeRepositoryHealth } from "@/lib/code-repository-types";
import { listGitAccounts, type GitAccount } from "@/lib/git-account-api";
import { getCodeRuntimeLogs, startCodeProjectRuntime, stopCodeProjectRuntime } from "@/lib/code-runtime-api";
import type { CodeRuntimeRun } from "@/lib/code-runtime-types";

type ProjectDraft = { id?: number; name: string; displayName: string; rootPath: string; description: string };
type RepositoryDraft = { name: string; projectId: number | ""; displayName: string; rootPath: string; description: string; languages: string[]; solutionFiles: string[]; configurationFiles: string[]; publishTarget: string; publishConfiguration: string; publishRuntime: string; publishOutputPath: string; publishCommand: string };
type ConsoleLine = { stream?: "stdout" | "stderr"; line: string };
type CloneDraft = { projectId: number | ""; repositoryUrl: string; gitAccountId: number | "" };
type FileDraft = { path: string; content: string; sha256: string };
type FilePickerKind = "solution" | "configuration" | "package";

const languages = ["C#", "TypeScript/JavaScript"];
const emptyProject: ProjectDraft = { name: "", displayName: "", rootPath: "", description: "" };
const emptyRepository: RepositoryDraft = { name: "", projectId: "", displayName: "", rootPath: "", description: "", languages: [], solutionFiles: [], configurationFiles: [], publishTarget: "", publishConfiguration: "Release", publishRuntime: "", publishOutputPath: "artifacts/publish", publishCommand: "npm run build" };
const emptyClone: CloneDraft = { projectId: "", repositoryUrl: "", gitAccountId: "" };
const input = "mt-1.5 h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-sm outline-none transition placeholder:text-slate-400 focus:border-blue-500 focus:ring-2 focus:ring-blue-100";
const reactOutputPath = (packagePath: string) => {
  const directory = packagePath.split("/").slice(0, -1).join("/");
  return directory ? `${directory}/dist` : "dist";
};

export function CodeProjectSettingsPage() {
  const searchParams = useSearchParams();
  const requestedProjectId = Number(searchParams.get("projectId")) || null;
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [repositories, setRepositories] = useState<CodeRepository[]>([]);
  const [projectDraft, setProjectDraft] = useState<ProjectDraft>(emptyProject);
  const [repositoryDraft, setRepositoryDraft] = useState<RepositoryDraft>(emptyRepository);
  const [selectedProjectId, setSelectedProjectId] = useState<number | null>(null);
  const [selectedRepository, setSelectedRepository] = useState<CodeRepository | null>(null);
  const [health, setHealth] = useState<CodeRepositoryHealth | null>(null);
  const [browser, setBrowser] = useState<CodeRepositoryDirectoryBrowser | null>(null);
  const [browserTarget, setBrowserTarget] = useState<"project" | "repository">("project");
  const [mode, setMode] = useState<"project" | "repository">("project");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [cloneOpen, setCloneOpen] = useState(false);
  const [cloneDraft, setCloneDraft] = useState<CloneDraft>(emptyClone);
  const [accounts, setAccounts] = useState<GitAccount[]>([]);
  const [cloneLines, setCloneLines] = useState<ConsoleLine[]>([]);
  const [terminalTitle, setTerminalTitle] = useState("");
  const [terminalOpen, setTerminalOpen] = useState(false);
  const [packageDownloadUrl, setPackageDownloadUrl] = useState<string | null>(null);
  const [fileDraft, setFileDraft] = useState<FileDraft | null>(null);
  const [filePickerKind, setFilePickerKind] = useState<FilePickerKind | null>(null);
  const [fileBrowser, setFileBrowser] = useState<CodeRepositoryDirectoryBrowser | null>(null);
  const [runtimeTerminalOpen, setRuntimeTerminalOpen] = useState(false);
  const [runtimeRuns, setRuntimeRuns] = useState<CodeRuntimeRun[]>([]);
  const [runtimeLogs, setRuntimeLogs] = useState<Record<string, ConsoleLine[]>>({});
  const [activeRuntimeRunId, setActiveRuntimeRunId] = useState<string | null>(null);
  const runtimeSequences = useRef<Record<string, number>>({});

  const selectedProject = useMemo(() => projects.find((item) => item.id === selectedProjectId) ?? null, [projects, selectedProjectId]);
  const visibleProjects = useMemo(() => selectedProjectId === null ? projects : projects.filter((item) => item.id === selectedProjectId), [projects, selectedProjectId]);
  const selectedConfigs = repositoryDraft.configurationFiles;

  useEffect(() => { void reload(); }, []);
  useEffect(() => {
    if (!requestedProjectId) return;
    const project = projects.find((item) => item.id === requestedProjectId);
    if (!project) return;
    setSelectedProjectId(project.id);
    setSelectedRepository(null);
    setMode("project");
    setHealth(null);
    setError("");
    setProjectDraft({ id: project.id, name: project.name, displayName: project.display_name, rootPath: project.root_path, description: project.description ?? "" });
  }, [projects, requestedProjectId]);

  async function reload() {
    setLoading(true);
    try {
      const [nextProjects, nextRepositories] = await Promise.all([getCodeProjects(), getCodeRepositories()]);
      setProjects(nextProjects);
      setRepositories(nextRepositories);
    } catch (value) { setError(message(value)); }
    finally { setLoading(false); }
  }

  function updateProjectScope(projectId: number | null) {
    const url = new URL(window.location.href);
    if (projectId === null) url.searchParams.delete("projectId");
    else url.searchParams.set("projectId", String(projectId));
    window.history.replaceState(null, "", url);
  }

  function chooseProject(project: CodeProject) {
    updateProjectScope(project.id);
    setSelectedProjectId(project.id); setSelectedRepository(null); setMode("project"); setHealth(null); setError("");
    setProjectDraft({ id: project.id, name: project.name, displayName: project.display_name, rootPath: project.root_path, description: project.description ?? "" });
  }

  function chooseRepository(repository: CodeRepository) {
    updateProjectScope(repository.project_id ?? null);
    setSelectedRepository(repository); setSelectedProjectId(repository.project_id ?? null); setMode("repository"); setHealth(null); setError("");
    const selectedSolution = repository.publish_target && repository.solution_files.includes(repository.publish_target)
      ? repository.publish_target
      : repository.solution_files.find((path) => path.endsWith(".csproj")) ?? repository.solution_files[0] ?? "";
    const solutionFiles = selectedSolution ? [selectedSolution] : [];
    const configurationFiles = repository.configuration_files.length === 1 ? repository.configuration_files : [];
    const isFrontend = repository.languages.includes("TypeScript/JavaScript") || repository.languages.includes("React");
    const selectedLanguage = repository.languages.includes("C#") ? "C#" : isFrontend ? "TypeScript/JavaScript" : "";
    const packageFile = configurationFiles.find((path) => path.split("/").pop()?.toLowerCase() === "package.json") ?? "";
    setRepositoryDraft({ name: repository.name, projectId: repository.project_id ?? "", displayName: repository.display_name, rootPath: repository.root_path, description: repository.description ?? "", languages: selectedLanguage ? [selectedLanguage] : [], solutionFiles, configurationFiles, publishTarget: isFrontend ? packageFile : solutionFiles.includes(repository.publish_target ?? "") ? repository.publish_target ?? "" : "", publishConfiguration: repository.publish_configuration || "Release", publishRuntime: repository.publish_runtime ?? "", publishOutputPath: isFrontend && packageFile && (!repository.publish_output_path || repository.publish_output_path === "artifacts/publish") ? reactOutputPath(packageFile) : repository.publish_output_path || "artifacts/publish", publishCommand: repository.publish_command || "npm run build" });
  }

  function startProject() { updateProjectScope(null); setMode("project"); setSelectedProjectId(null); setSelectedRepository(null); setProjectDraft(emptyProject); setHealth(null); setFilePickerKind(null); setFileBrowser(null); setError(""); }
  function startRepository(projectId = selectedProjectId ?? "") { setMode("repository"); setSelectedRepository(null); setRepositoryDraft({ ...emptyRepository, projectId }); setHealth(null); setFilePickerKind(null); setFileBrowser(null); setError(""); }

  async function inspectRepository() {
    if (!repositoryDraft.rootPath.trim()) return setError("请先填写代码库目录。");
    setBusy(true); setError("");
    try {
      const result = await inspectCodeRepository(repositoryDraft.rootPath.trim());
      setRepositoryDraft((item) => ({ ...item, rootPath: result.root_path, name: item.name || result.suggested_name, displayName: item.displayName || result.suggested_display_name, languages: item.languages.length ? item.languages : result.languages }));
    } catch (value) { setError(message(value)); }
    finally { setBusy(false); }
  }

  async function saveProject() {
    if (!projectDraft.rootPath.trim()) return setError("项目必须关联服务器上的文件夹。");
    setBusy(true); setError("");
    try {
      const payload = { name: projectDraft.name, display_name: projectDraft.displayName, root_path: projectDraft.rootPath, description: projectDraft.description };
      const saved = projectDraft.id ? await updateCodeProject(projectDraft.id, payload) : await createCodeProject(payload);
      await reload(); chooseProject(saved);
    } catch (value) { setError(message(value)); }
    finally { setBusy(false); }
  }

  async function saveRepository() {
    if (!repositoryDraft.projectId || !repositoryDraft.rootPath.trim()) return setError("请选择项目并填写代码库目录。");
    setBusy(true); setError("");
    try {
      const payload = { name: repositoryDraft.name, project_id: Number(repositoryDraft.projectId), display_name: repositoryDraft.displayName, root_path: repositoryDraft.rootPath, description: repositoryDraft.description, languages: repositoryDraft.languages, solution_files: repositoryDraft.solutionFiles, configuration_files: repositoryDraft.configurationFiles, publish_target: repositoryDraft.publishTarget, publish_configuration: repositoryDraft.publishConfiguration, publish_runtime: repositoryDraft.publishRuntime, publish_output_path: repositoryDraft.publishOutputPath, publish_command: repositoryDraft.publishCommand };
      const saved = selectedRepository ? await updateCodeRepository(selectedRepository.name, payload) : await createCodeRepository(payload);
      await reload(); chooseRepository(saved);
    } catch (value) { setError(`${selectedRepository ? "保存" : "挂载"}代码库失败：${message(value)}`); }
    finally { setBusy(false); }
  }

  async function removeProject(project: CodeProject) {
    if (!confirm(`删除项目“${project.display_name}”只会移除登记信息，不会删除服务器文件。项目下必须没有代码库，是否继续？`)) return;
    setBusy(true); setError("");
    try { await deleteCodeProject(project.id); startProject(); await reload(); } catch (value) { setError(message(value)); } finally { setBusy(false); }
  }

  async function removeRepository(repository: CodeRepository) {
    if (!confirm(`移除代码库“${repository.display_name}”的登记信息？不会删除实际文件。`)) return;
    setBusy(true); setError("");
    try { await deleteCodeRepository(repository.name); startRepository(repository.project_id ?? ""); await reload(); } catch (value) { setError(message(value)); } finally { setBusy(false); }
  }

  async function openBrowser(target: "project" | "repository", path?: string) {
    setBrowserTarget(target);
    try { setBrowser(await browseCodeRepositoryDirectories(path)); } catch (value) { setError(message(value)); }
  }

  async function openSelectableFileBrowser(kind: FilePickerKind, path?: string) {
    if (!repositoryDraft.rootPath.trim()) return setError("请先填写代码库目录。");
    setError("");
    try {
      const result = await browseCodeRepositoryFiles(repositoryDraft.rootPath.trim(), kind, path);
      setFilePickerKind(kind); setFileBrowser(result);
    } catch (value) { setError(message(value)); }
  }

  async function checkHealth() {
    if (!selectedRepository) return;
    setBusy(true); setError("");
    try { setHealth(await getCodeRepositoryHealth(selectedRepository.name)); } catch (value) { setError(message(value)); } finally { setBusy(false); }
  }

  async function openClone() {
    setCloneLines([]); setError(""); setCloneDraft({ projectId: selectedProjectId ?? projects[0]?.id ?? "", repositoryUrl: "", gitAccountId: "" });
    try {
      const items = await listGitAccounts();
      setAccounts(items);
      setCloneDraft((item) => ({ ...item, gitAccountId: items.find((account) => account.is_active)?.id ?? items[0]?.id ?? "" }));
      setCloneOpen(true);
    } catch (value) { setError(message(value)); }
  }

  async function cloneRepository() {
    if (!cloneDraft.projectId || !cloneDraft.repositoryUrl.trim() || !cloneDraft.gitAccountId) return setError("请选择项目、填写 HTTPS 仓库地址并选择 Git 账号。");
    setBusy(true); setCloneLines([]); setPackageDownloadUrl(null); setTerminalTitle("克隆终端"); setTerminalOpen(true); setError("");
    try {
      const event = await cloneCodeRepositoryViaWebSocket({ project_id: Number(cloneDraft.projectId), repository_url: cloneDraft.repositoryUrl.trim(), git_account_id: Number(cloneDraft.gitAccountId) }, (entry) => {
        if (entry.line) setCloneLines((lines) => [...lines.slice(-499), { stream: entry.stream, line: entry.line! }]);
        if (entry.message) setCloneLines((lines) => [...lines.slice(-499), { line: entry.message! }]);
      });
      if (!event.success) throw new Error(event.message || "克隆失败。");
      setCloneOpen(false); await reload();
      if (event.repository) chooseRepository(event.repository);
    } catch (value) { setError(message(value)); }
    finally { setBusy(false); }
  }

  async function startPackage() {
    if (!selectedRepository) return;
    setBusy(true); setCloneLines([]); setPackageDownloadUrl(null); setTerminalTitle("打包终端"); setTerminalOpen(true); setError("");
    try {
      const event = await packageCodeRepositoryViaWebSocket(selectedRepository.name, (entry) => {
        if (entry.line) setCloneLines((lines) => [...lines.slice(-499), { stream: entry.stream, line: entry.line! }]);
        if (entry.message) setCloneLines((lines) => [...lines.slice(-499), { line: entry.message! }]);
      });
      if (!event.success) throw new Error(event.message || "打包失败。");
      if (event.archive_name) setPackageDownloadUrl(`/api/v1/code-repositories/${encodeURIComponent(selectedRepository.name)}/packages/${encodeURIComponent(event.archive_name)}`);
    } catch (value) { setError(message(value)); }
    finally { setBusy(false); }
  }

  async function testProjectRuntime() {
    if (!selectedProject) return setError("请先选择所属项目并保存代码库配置。");
    setBusy(true); setError(""); setRuntimeLogs({}); runtimeSequences.current = {};
    try {
      const runs = await startCodeProjectRuntime(selectedProject.id);
      setRuntimeRuns(runs); setActiveRuntimeRunId(runs[0]?.run_id ?? null); setRuntimeTerminalOpen(true);
    } catch (value) { setError(`启动项目程序失败：${message(value)}`); }
    finally { setBusy(false); }
  }

  async function stopRuntime(runId: string) {
    if (!selectedProject) return;
    try {
      await stopCodeProjectRuntime(selectedProject.id, runId);
      setRuntimeRuns((items) => items.map((item) => item.run_id === runId ? { ...item, status: "stopping" } : item));
    } catch (value) { setError(message(value)); }
  }

  useEffect(() => {
    if (!runtimeTerminalOpen || runtimeRuns.length === 0) return;
    let disposed = false;
    const refresh = async () => {
      const output = await Promise.all(runtimeRuns.map(async (run) => {
        try {
          const after = runtimeSequences.current[run.run_id] ?? 0;
          const lines = await getCodeRuntimeLogs(run.run_id, after);
          if (lines.length) runtimeSequences.current[run.run_id] = lines[lines.length - 1]!.sequence;
          return [run.run_id, lines.map((line) => ({ stream: (line.stream === "stderr" ? "stderr" : "stdout") as "stdout" | "stderr", line: line.line }))] as const;
        } catch {
          return [run.run_id, []] as const;
        }
      }));
      if (disposed) return;
      setRuntimeLogs((current) => {
        const next = { ...current };
        output.forEach(([runId, lines]) => { if (lines.length) next[runId] = [...(next[runId] ?? []), ...lines].slice(-600); });
        return next;
      });
    };
    void refresh();
    const timer = window.setInterval(() => void refresh(), 900);
    return () => { disposed = true; window.clearInterval(timer); };
  }, [runtimeTerminalOpen, runtimeRuns]);

  async function openFile(path: string) {
    if (!selectedRepository) return;
    setBusy(true); setError("");
    try { const file = await readConfiguredCodeFile(selectedRepository.name, path); setFileDraft(file); } catch (value) { setError(message(value)); } finally { setBusy(false); }
  }

  async function saveFile() {
    if (!selectedRepository || !fileDraft) return;
    setBusy(true); setError("");
    try {
      const result = await writeConfiguredCodeFile(selectedRepository.name, { path: fileDraft.path, content: fileDraft.content, expected_sha256: fileDraft.sha256 });
      setFileDraft((file) => file ? { ...file, sha256: result.sha256 } : file);
    } catch (value) { setError(message(value)); } finally { setBusy(false); }
  }

  function updateSelectedFile(kind: FilePickerKind, path: string) {
    setRepositoryDraft((item) => kind === "solution"
      ? { ...item, solutionFiles: [path], publishTarget: path }
      : kind === "package"
        ? { ...item, configurationFiles: [path], publishTarget: path, publishOutputPath: reactOutputPath(path) }
        : { ...item, configurationFiles: [path] });
  }

  return <section>
    <SettingsPageHeader title="项目与代码库" description="项目对应服务器文件夹；可在项目内克隆远程仓库，选择供 AI、调试和打包使用的入口文件。" action={<div className="flex gap-2"><button onClick={startProject} className="secondary-button"><FolderGit2 size={15}/>新建项目</button><button onClick={() => void openClone()} className="secondary-button"><GitBranch size={15}/>克隆代码库</button><button onClick={() => startRepository()} className="primary-button"><Plus size={15}/>挂载代码库</button></div>} />
    <div className="grid gap-5 xl:grid-cols-[310px_minmax(0,1fr)]">
      <aside className="workspace-scroll max-h-[calc(100vh-190px)] overflow-y-auto rounded-2xl border border-slate-200 bg-white p-3 shadow-sm">
        <div className="mb-2 flex items-center justify-between px-1"><div className="flex min-w-0 items-center gap-2"><span className="truncate text-xs font-semibold text-slate-700">{selectedProject ? `${selectedProject.display_name} · 项目资源` : "项目资源"}</span>{selectedProject && <button onClick={startProject} className="text-[11px] text-blue-600 hover:text-blue-700">全部项目</button>}</div><button onClick={() => void reload()} className="icon-button" aria-label="刷新"><RefreshCw size={14} className={loading ? "animate-spin" : ""}/></button></div>
        {loading ? <div className="flex items-center gap-2 px-2 py-6 text-xs text-slate-400"><Loader2 size={14} className="animate-spin"/>正在读取项目…</div> : <div className="space-y-1">{visibleProjects.map((project) => <ProjectTree key={project.id} project={project} selectedProjectId={selectedProjectId} selectedRepository={selectedRepository} onProject={chooseProject} onRepository={chooseRepository} onCreateRepository={startRepository} onDeleteProject={removeProject} onDeleteRepository={removeRepository} />)}</div>}
        {!loading && projects.length === 0 && <p className="px-2 py-5 text-xs leading-5 text-slate-400">先登记一个项目文件夹，再在该文件夹内克隆或挂载代码库。</p>}
      </aside>
      <div className="min-w-0 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        {mode === "project" ? <ProjectForm draft={projectDraft} busy={busy} onChange={setProjectDraft} onBrowse={() => void openBrowser("project", projectDraft.rootPath || undefined)} onSave={() => void saveProject()} onDelete={() => projectDraft.id && selectedProject && void removeProject(selectedProject)} /> : <RepositoryForm draft={repositoryDraft} projects={projects} health={health} busy={busy} editing={Boolean(selectedRepository)} canPackage={Boolean(selectedRepository)} selectedConfigs={selectedConfigs} onChange={setRepositoryDraft} onInspect={() => void inspectRepository()} onBrowse={() => void openBrowser("repository", repositoryDraft.rootPath || undefined)} onChooseFiles={(kind) => void openSelectableFileBrowser(kind)} onSave={() => void saveRepository()} onDelete={() => selectedRepository && void removeRepository(selectedRepository)} onHealth={() => void checkHealth()} onOpenFile={(path) => void openFile(path)} onPackage={() => void startPackage()} onRun={() => void testProjectRuntime()} />}
        {error && <div role="alert" className="mt-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800"><p className="font-semibold">{mode === "repository" ? "代码库操作失败" : "项目操作失败"}</p><p className="mt-1 leading-5">{error}</p>{mode === "repository" && <p className="mt-2 text-xs leading-5 text-red-700">请检查：所属项目与代码库目录是否匹配、目录是否仍存在，以及后端数据库表是否已初始化。</p>}</div>}
      </div>
    </div>
    {browser && <DirectoryPicker browser={browser} onClose={() => setBrowser(null)} onOpen={(path) => void openBrowser(browserTarget, path)} onChoose={() => { if (browserTarget === "project") setProjectDraft((item) => ({ ...item, rootPath: browser.path })); else setRepositoryDraft((item) => ({ ...item, rootPath: browser.path })); setBrowser(null); }} />}
    {filePickerKind && fileBrowser && <FilePicker title={filePickerKind === "solution" ? "选择解决方案与工程文件" : filePickerKind === "package" ? "选择 React 的 package.json" : "选择可编辑配置文件"} browser={fileBrowser} onClose={() => { setFilePickerKind(null); setFileBrowser(null); }} onOpen={(path) => void openSelectableFileBrowser(filePickerKind, path)} onChoose={(path) => { updateSelectedFile(filePickerKind, path); setFilePickerKind(null); setFileBrowser(null); }} />}
    {cloneOpen && <CloneDialog projects={projects} accounts={accounts} draft={cloneDraft} lines={cloneLines} busy={busy} onChange={setCloneDraft} onClose={() => !busy && setCloneOpen(false)} onSubmit={() => void cloneRepository()} />}
    {terminalOpen && <TerminalDialog title={terminalTitle} lines={cloneLines} busy={busy} downloadUrl={packageDownloadUrl} onClose={() => !busy && setTerminalOpen(false)} />}
    {runtimeTerminalOpen && <RuntimeTerminalDialog runs={runtimeRuns} logs={runtimeLogs} activeRunId={activeRuntimeRunId} onSelect={setActiveRuntimeRunId} onStop={(runId) => void stopRuntime(runId)} onClose={() => setRuntimeTerminalOpen(false)} />}
    {fileDraft && <FileEditor file={fileDraft} busy={busy} onChange={setFileDraft} onClose={() => !busy && setFileDraft(null)} onSave={() => void saveFile()} />}
  </section>;
}

function ProjectTree({ project, selectedProjectId, selectedRepository, onProject, onRepository, onCreateRepository, onDeleteProject, onDeleteRepository }: { project: CodeProject; selectedProjectId: number | null; selectedRepository: CodeRepository | null; onProject: (value: CodeProject) => void; onRepository: (value: CodeRepository) => void; onCreateRepository: (id: number) => void; onDeleteProject: (value: CodeProject) => void; onDeleteRepository: (value: CodeRepository) => void }) {
  return <div><div className={`group flex items-center rounded-lg ${selectedProjectId === project.id && !selectedRepository ? "bg-blue-50" : "hover:bg-slate-50"}`}><button onClick={() => onProject(project)} className={`flex min-w-0 flex-1 items-center gap-2 px-2.5 py-2 text-left text-sm ${selectedProjectId === project.id && !selectedRepository ? "text-blue-700" : "text-slate-700"}`}><FolderGit2 size={16}/><span className="min-w-0 flex-1 truncate font-medium">{project.display_name}</span><span className="text-[11px] text-slate-400">{project.repository_count}</span></button><button onClick={() => onDeleteProject(project)} className="mr-1 hidden h-7 w-7 place-items-center rounded text-slate-400 hover:bg-red-50 hover:text-red-600 group-hover:grid" aria-label="删除项目"><Trash2 size={13}/></button></div><div className="ml-4 border-l border-slate-100 pl-2">{project.repositories.map((repository) => <div key={repository.id} className={`group flex items-center rounded-md ${selectedRepository?.id === repository.id ? "bg-slate-100" : "hover:bg-slate-50"}`}><button onClick={() => onRepository(repository)} className="flex min-w-0 flex-1 items-center gap-2 px-2 py-1.5 text-left text-xs text-slate-600"><Braces size={13}/><span className="truncate">{repository.display_name}</span></button><button onClick={() => onDeleteRepository(repository)} className="mr-1 hidden h-6 w-6 place-items-center rounded text-slate-400 hover:bg-red-50 hover:text-red-600 group-hover:grid" aria-label="移除代码库"><Trash2 size={12}/></button></div>)}<button onClick={() => onCreateRepository(project.id)} className="mt-1 flex w-full items-center gap-1.5 rounded-md px-2 py-1.5 text-xs text-blue-600 hover:bg-blue-50"><Plus size={13}/>挂载代码库</button></div></div>;
}

function ProjectForm({ draft, busy, onChange, onBrowse, onSave, onDelete }: { draft: ProjectDraft; busy: boolean; onChange: (value: ProjectDraft) => void; onBrowse: () => void; onSave: () => void; onDelete: () => void }) {
  return <><FormHeader eyebrow="一级 · 项目文件夹" title={draft.id ? "编辑项目" : "新建项目"} description="项目只是一项服务器文件夹授权，不会移动或复制任何文件。" danger={draft.id ? onDelete : undefined}/><div className="grid gap-4 md:grid-cols-2"><Field label="项目标识"><input className={input} value={draft.name} onChange={(event) => onChange({ ...draft, name: event.target.value })} placeholder="manufacturing-suite"/></Field><Field label="显示名称"><input className={input} value={draft.displayName} onChange={(event) => onChange({ ...draft, displayName: event.target.value })} placeholder="制造执行系统"/></Field></div><Field label="项目文件夹" hint="该文件夹是所有挂载代码库的上级目录。"><PathInput value={draft.rootPath} onChange={(value) => onChange({ ...draft, rootPath: value })} onBrowse={onBrowse} placeholder="E:\\项目\\manufacturing-suite"/></Field><Field label="项目说明"><textarea className={`${input} h-auto py-2`} rows={3} value={draft.description} onChange={(event) => onChange({ ...draft, description: event.target.value })}/></Field><Footer busy={busy} onSave={onSave} label={draft.id ? "保存项目" : "创建项目"}/></>;
}

function RepositoryForm({ draft, projects, health, busy, editing, canPackage, selectedConfigs, onChange, onInspect, onBrowse, onChooseFiles, onSave, onDelete, onHealth, onOpenFile, onPackage, onRun }: { draft: RepositoryDraft; projects: CodeProject[]; health: CodeRepositoryHealth | null; busy: boolean; editing: boolean; canPackage: boolean; selectedConfigs: string[]; onChange: (value: RepositoryDraft) => void; onInspect: () => void; onBrowse: () => void; onChooseFiles: (kind: FilePickerKind) => void; onSave: () => void; onDelete: () => void; onHealth: () => void; onOpenFile: (path: string) => void; onPackage: () => void; onRun: () => void }) {
  const packageFile = draft.configurationFiles.find((path) => path.split("/").pop()?.toLowerCase() === "package.json");
  const isFrontend = draft.languages[0] === "TypeScript/JavaScript";
  const selectLanguage = (language: string) => onChange({ ...draft, languages: [language], publishTarget: language === "TypeScript/JavaScript" ? packageFile ?? "" : draft.publishTarget, publishOutputPath: language === "TypeScript/JavaScript" && draft.publishOutputPath === "artifacts/publish" ? packageFile ? reactOutputPath(packageFile) : "dist" : draft.publishOutputPath });
  return <>
    <FormHeader eyebrow="二级 · 代码库配置" title={editing ? "编辑代码库" : "挂载代码库"} description="识别目录后选择 AI 入口、调试配置与发布目标。" danger={editing ? onDelete : undefined}/>
    <div className="grid gap-4 md:grid-cols-2"><Field label="所属项目"><select className={input} value={draft.projectId} onChange={(event) => onChange({ ...draft, projectId: event.target.value ? Number(event.target.value) : "" })}><option value="">选择项目</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.display_name}</option>)}</select></Field><Field label="显示名称"><input className={input} value={draft.displayName} onChange={(event) => onChange({ ...draft, displayName: event.target.value })} placeholder="cps-api"/></Field></div>
    <Field label="代码库目录" hint="目录必须位于所属项目文件夹内。"><div className="flex gap-2"><PathInput value={draft.rootPath} onChange={(value) => onChange({ ...draft, rootPath: value })} onBrowse={onBrowse} placeholder="E:\\项目\\manufacturing-suite\\api"/><button onClick={onInspect} disabled={busy || !draft.rootPath.trim()} className="secondary-button mt-1.5 shrink-0 text-blue-700">识别目录</button></div></Field>
    <Field label="代码库说明"><textarea className={`${input} h-auto py-2`} rows={2} value={draft.description} onChange={(event) => onChange({ ...draft, description: event.target.value })}/></Field>
    <section className="mt-5 rounded-xl border border-slate-200 bg-slate-50/70 p-4"><div className="flex items-center gap-2 text-sm font-semibold text-slate-800"><FileCog size={16} className="text-blue-600"/>三级 · AI、调试与发布配置</div><p className="mt-1 text-xs text-slate-500">识别目录后，再按需选择允许 AI、调试和发布使用的文件。</p><div className="mt-4 flex flex-wrap gap-2">{languages.map((language) => <button key={language} onClick={() => selectLanguage(language)} className={`rounded-md px-2.5 py-1.5 text-xs ${draft.languages[0] === language ? "bg-blue-600 text-white" : "border border-slate-200 bg-white text-slate-600"}`}>{language}</button>)}</div>
      {isFrontend ? <><FileSelection title="前端构建入口" empty="请选择包含 build 脚本的 package.json。" selected={packageFile ? [packageFile] : []} onChoose={() => onChooseFiles("package")}/><div className="mt-5 grid gap-3 border-t border-slate-200 pt-4 md:grid-cols-2"><Field label="构建命令" hint="支持 npm run build:prod、npm run build -- --mode production 等 npm 脚本命令。"><input className={`${input} font-mono`} value={draft.publishCommand} onChange={(event) => onChange({ ...draft, publishCommand: event.target.value })} placeholder="npm run build"/></Field><Field label="构建输出目录" hint="相对于代码库；Vite 默认是 dist。"><input className={`${input} mt-1`} value={draft.publishOutputPath} onChange={(event) => onChange({ ...draft, publishOutputPath: event.target.value })}/></Field></div></> : <><FileSelection title="解决方案与工程文件" empty="点击选择文件后，可进入目录选择 .sln、.slnf 或 .csproj。" selected={draft.solutionFiles} onChoose={() => onChooseFiles("solution")}/><FileSelection title="可编辑配置文件" empty="点击选择文件后，可进入目录选择 appsettings、.env、package 与 Docker 配置。" selected={draft.configurationFiles} onChoose={() => onChooseFiles("configuration")}/><div className="mt-5 grid gap-3 border-t border-slate-200 pt-4 md:grid-cols-2"><Field label="打包目标"><select className={`${input} mt-1`} value={draft.publishTarget} onChange={(event) => onChange({ ...draft, publishTarget: event.target.value })}><option value="">选择已勾选的解决方案或工程</option>{draft.solutionFiles.map((file) => <option key={file} value={file}>{file}</option>)}</select></Field><Field label="构建配置"><select className={`${input} mt-1`} value={draft.publishConfiguration} onChange={(event) => onChange({ ...draft, publishConfiguration: event.target.value })}><option value="Release">Release</option><option value="Debug">Debug</option></select></Field><Field label="运行时（可选）"><input className={`${input} mt-1`} value={draft.publishRuntime} onChange={(event) => onChange({ ...draft, publishRuntime: event.target.value })} placeholder="win-x64"/></Field><Field label="发布输出目录" hint="相对于代码库，如 artifacts/publish。"><input className={`${input} mt-1`} value={draft.publishOutputPath} onChange={(event) => onChange({ ...draft, publishOutputPath: event.target.value })}/></Field></div></>}
      {!isFrontend && <div className="mt-4 flex min-w-0 items-center gap-2 rounded-lg border border-violet-100 bg-violet-50/60 px-3 py-2 text-xs text-violet-800"><span className="shrink-0 rounded bg-violet-100 px-1.5 py-0.5 font-semibold">C# / .NET</span><span className="truncate font-mono" title={draft.solutionFiles[0]}>{draft.solutionFiles[0] ? `当前运行入口：${draft.solutionFiles[0]}` : "请选择一个 .sln 或 .csproj 作为运行入口"}</span></div>}
    </section>
    {editing && <section className="mt-4 grid gap-3 lg:grid-cols-2"><div className="rounded-xl border border-emerald-100 bg-emerald-50/50 p-3"><div className="flex items-center justify-between"><span className="flex items-center gap-1.5 text-xs font-semibold text-emerald-800"><ShieldCheck size={15}/>挂载检查</span><button onClick={onHealth} disabled={busy} className="text-xs font-medium text-emerald-700 hover:underline">立即检查</button></div>{health ? <div className="mt-2 text-xs leading-5 text-emerald-900"><p>目录 {health.root_exists ? "✓" : "×"} · 项目 {health.project_match ? "✓" : "×"} · Git {health.is_git_repository ? "✓" : "×"}{health.branch ? ` (${health.branch})` : ""}</p>{health.messages.map((item) => <p key={item}>{item}</p>)}</div> : <p className="mt-2 text-xs text-emerald-700">检查目录、项目挂载、Git 与已选择文件。</p>}</div><div className="rounded-xl border border-blue-100 bg-blue-50/50 p-3"><div className="flex items-center justify-between"><span className="flex items-center gap-1.5 text-xs font-semibold text-blue-800"><FilePenLine size={15}/>配置文件调试</span></div><div className="mt-2 flex flex-wrap gap-1.5">{selectedConfigs.map((path) => <button key={path} onClick={() => onOpenFile(path)} disabled={busy} className="rounded border border-blue-200 bg-white px-2 py-1 font-mono text-[11px] text-blue-700 hover:bg-blue-50">{path}</button>)}{selectedConfigs.length === 0 && <span className="text-xs text-blue-700">保存勾选的配置文件后，可在这里编辑。</span>}</div></div></section>}
    <div className="mt-6 flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 pt-4"><div className="flex gap-2"><button onClick={onRun} disabled={busy || !editing} className="secondary-button border-blue-200 text-blue-700 disabled:opacity-50"><Terminal size={15}/>测试运行前后端</button><button onClick={onPackage} disabled={busy || !canPackage} className="secondary-button disabled:opacity-50"><PackageOpen size={15}/>实时打包</button></div><button onClick={onSave} disabled={busy} className="primary-button disabled:opacity-50">{busy ? <Loader2 size={14} className="animate-spin"/> : <Check size={14}/>} {editing ? "保存代码库配置" : "挂载代码库"}</button></div>
  </>;
}

function FormHeader({ eyebrow, title, description, danger }: { eyebrow: string; title: string; description: string; danger?: () => void }) { return <header className="mb-6 flex items-start justify-between"><div><p className="text-xs font-semibold uppercase tracking-[.14em] text-blue-600">{eyebrow}</p><h2 className="mt-1 text-lg font-semibold text-slate-950">{title}</h2><p className="mt-1 text-xs text-slate-500">{description}</p></div>{danger && <button onClick={danger} className="secondary-button border-red-200 text-red-700 hover:bg-red-50"><Trash2 size={14}/>删除</button>}</header>; }
function PathInput({ value, onChange, onBrowse, placeholder }: { value: string; onChange: (value: string) => void; onBrowse: () => void; placeholder: string }) { return <div className="flex min-w-0 flex-1 gap-2"><input className={`${input} min-w-0 flex-1 font-mono`} value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder}/><button onClick={onBrowse} className="secondary-button mt-1.5 shrink-0 px-3" aria-label="浏览目录"><FolderOpen size={15}/></button></div>; }
function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) { return <div className="mt-4 text-xs font-medium text-slate-700"><span>{label}</span>{hint && <span className="mt-1 block font-normal leading-5 text-slate-400">{hint}</span>}{children}</div>; }
function FileSelection({ title, empty, selected, onChoose }: { title: string; empty: string; selected: string[]; onChoose: () => void }) { const file = selected[0]; const extension = file?.split(".").pop()?.toUpperCase(); return <div className="mt-4"><div className="flex items-center justify-between gap-3"><span className="text-xs font-medium text-slate-700">{title}</span><button type="button" onClick={onChoose} className="secondary-button h-8 shrink-0 px-2.5 text-xs text-blue-700"><FolderOpen size={14}/>选择文件</button></div><div className="mt-2 flex min-h-11 items-center rounded-lg border border-slate-200 bg-white px-3 py-2">{file ? <><span className={`mr-2 shrink-0 rounded px-1.5 py-0.5 text-[10px] font-semibold ${extension === "SLN" || extension === "CSPROJ" ? "bg-violet-50 text-violet-700" : "bg-blue-50 text-blue-700"}`}>{extension}</span><span className="min-w-0 truncate font-mono text-xs text-slate-700" title={file}>{file}</span></> : <span className="text-xs text-slate-400">{empty}</span>}</div></div>; }
function Footer({ busy, onSave, label }: { busy: boolean; onSave: () => void; label: string }) { return <div className="mt-6 flex justify-end border-t border-slate-100 pt-4"><button onClick={onSave} disabled={busy} className="primary-button">{busy ? <Loader2 size={14} className="animate-spin"/> : <Check size={14}/>} {label}</button></div>; }
function DirectoryPicker({ browser, onClose, onOpen, onChoose }: { browser: CodeRepositoryDirectoryBrowser; onClose: () => void; onOpen: (path?: string) => void; onChoose: () => void }) { return <Modal title="选择服务器文件夹" onClose={onClose}><p className="truncate font-mono text-xs text-slate-500">{browser.path}</p><div className="workspace-scroll mt-4 max-h-72 overflow-auto rounded-lg border border-slate-200 p-2">{browser.parent_path && <button onClick={() => onOpen(browser.parent_path ?? undefined)} className="block w-full rounded px-2 py-2 text-left text-xs text-blue-600 hover:bg-blue-50">↑ 上级目录</button>}{browser.directories.map((path) => <button key={path} onClick={() => onOpen(path)} className="flex w-full items-center gap-2 rounded px-2 py-2 text-left text-sm hover:bg-slate-50"><FolderOpen size={15} className="text-amber-500"/>{path.split(/[\\/]/).pop()}</button>)}</div><div className="mt-4 flex justify-end"><button onClick={onChoose} className="primary-button">选择此文件夹</button></div></Modal>; }
function FilePicker({ title, browser, onClose, onOpen, onChoose }: { title: string; browser: CodeRepositoryDirectoryBrowser; onClose: () => void; onOpen: (path?: string) => void; onChoose: (path: string) => void }) {
  return <Modal title={title} onClose={onClose}><p className="truncate font-mono text-xs text-slate-500">{browser.path}</p><p className="mt-1 text-xs text-slate-400">进入目录后，点击一个文件即可选中并返回。</p><div className="workspace-scroll mt-4 max-h-[55vh] overflow-auto rounded-lg border border-slate-200 p-2">{browser.parent_path && <button type="button" onClick={() => onOpen(browser.parent_path ?? undefined)} className="block w-full rounded px-2 py-2 text-left text-xs text-blue-600 hover:bg-blue-50">↑ 上级目录</button>}{browser.directories.map((path) => <button type="button" key={path} onClick={() => onOpen(path)} className="flex w-full items-center gap-2 rounded px-2 py-2 text-left text-sm hover:bg-slate-50"><FolderOpen size={15} className="text-amber-500"/><span className="truncate">{path.split(/[\\/]/).pop()}</span></button>)}{browser.files?.map((file) => <button type="button" key={file.path} onClick={() => onChoose(file.path)} className="flex w-full items-center gap-2 rounded px-2 py-2 text-left text-sm text-slate-700 hover:bg-blue-50"><FileCog size={15} className="text-blue-500"/><span className="truncate font-mono">{file.name}</span></button>)}{browser.directories.length === 0 && (browser.files?.length ?? 0) === 0 && <p className="px-2 py-5 text-center text-xs text-slate-400">当前目录没有可选择的文件。</p>}</div><div className="mt-4 flex justify-end"><button type="button" onClick={onClose} className="secondary-button">取消</button></div></Modal>;
}
function CloneDialog({ projects, accounts, draft, lines, busy, onChange, onClose, onSubmit }: { projects: CodeProject[]; accounts: GitAccount[]; draft: CloneDraft; lines: ConsoleLine[]; busy: boolean; onChange: (value: CloneDraft) => void; onClose: () => void; onSubmit: () => void }) { return <Modal title="克隆远程代码库" onClose={onClose}><p className="text-xs leading-5 text-slate-500">克隆会在所选项目文件夹内执行。完成后会自动识别目录，并登记为该项目的代码库。</p><Field label="目标项目"><select className={input} value={draft.projectId} onChange={(event) => onChange({ ...draft, projectId: event.target.value ? Number(event.target.value) : "" })}><option value="">选择项目</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.display_name} · {project.root_path}</option>)}</select></Field><Field label="HTTPS 仓库地址"><input className={input} value={draft.repositoryUrl} onChange={(event) => onChange({ ...draft, repositoryUrl: event.target.value })} placeholder="https://gitee.com/org/repository.git"/></Field><Field label="Git 账号"><select className={input} value={draft.gitAccountId} onChange={(event) => onChange({ ...draft, gitAccountId: event.target.value ? Number(event.target.value) : "" })}><option value="">选择已配置账号</option>{accounts.map((account) => <option key={account.id} value={account.id}>{account.provider} · {account.display_name} (@{account.username})</option>)}</select></Field>{accounts.length === 0 && <p className="mt-3 text-xs text-amber-700">尚未配置 Git 账号，请先前往 Git 管理添加访问令牌。</p>}{lines.length > 0 && <Console lines={lines}/>}<div className="mt-5 flex justify-end gap-2"><button onClick={onClose} disabled={busy} className="secondary-button">取消</button><button onClick={onSubmit} disabled={busy} className="primary-button">{busy && <Loader2 size={14} className="animate-spin"/>}开始克隆</button></div></Modal>; }
function TerminalDialog({ title, lines, busy, downloadUrl, onClose }: { title: string; lines: ConsoleLine[]; busy: boolean; downloadUrl: string | null; onClose: () => void }) { return <Modal title={title} onClose={onClose}><Console lines={lines}/><div className="mt-4 flex items-center justify-between gap-3 text-xs text-slate-500"><span>{busy ? "服务端命令正在执行…" : downloadUrl ? "打包完成，压缩包已生成。" : "命令已结束。"}</span><div className="flex gap-2">{downloadUrl && <a href={downloadUrl} download className="primary-button">下载压缩包</a>}<button onClick={onClose} disabled={busy} className="secondary-button">关闭</button></div></div></Modal>; }
function RuntimeTerminalDialog({ runs, logs, activeRunId, onSelect, onStop, onClose }: { runs: CodeRuntimeRun[]; logs: Record<string, ConsoleLine[]>; activeRunId: string | null; onSelect: (runId: string) => void; onStop: (runId: string) => void; onClose: () => void }) { const active = runs.find((run) => run.run_id === activeRunId) ?? runs[0]; return <Modal title="项目程序终端" onClose={onClose} wide><p className="text-xs text-slate-500">每个前端或后端进程独立一个终端 Tab；输出会实时追加，不经过浏览器 Shell。</p><div className="mt-4 flex gap-1 overflow-x-auto border-b border-slate-200">{runs.map((run) => <button key={run.run_id} type="button" onClick={() => onSelect(run.run_id)} className={`inline-flex shrink-0 items-center gap-2 border-b-2 px-3 py-2 text-xs font-medium ${active?.run_id === run.run_id ? "border-blue-600 text-blue-700" : "border-transparent text-slate-500 hover:text-slate-800"}`}><span className={`h-2 w-2 rounded-full ${run.status === "running" ? "bg-emerald-500" : run.status === "failed" ? "bg-rose-500" : "bg-amber-400"}`}/>{run.repository_name} · {run.role}</button>)}</div>{active ? <><div className="mt-3 flex items-center justify-between gap-3 text-xs text-slate-500"><span className="truncate font-mono">:{active.port} · {active.status}</span>{["starting", "running", "stopping"].includes(active.status) && <button type="button" onClick={() => onStop(active.run_id)} className="secondary-button h-8 border-red-200 text-red-700">停止</button>}</div><Console lines={logs[active.run_id] ?? []}/></> : <p className="py-8 text-center text-sm text-slate-500">没有可显示的运行进程。</p>}<div className="mt-4 flex justify-end"><button type="button" onClick={onClose} className="secondary-button">关闭</button></div></Modal>; }
function Console({ lines }: { lines: ConsoleLine[] }) { return <pre className="workspace-scroll mt-4 max-h-80 overflow-auto rounded-xl bg-slate-950 p-3 font-mono text-[11px] leading-5 text-slate-100">{lines.length ? lines.map((item, index) => <span key={`${index}-${item.line}`} className={`block ${item.stream === "stderr" ? "text-amber-300" : "text-emerald-300"}`}>{item.line}</span>) : <span className="text-slate-400">等待服务端输出…</span>}</pre>; }
function FileEditor({ file, busy, onChange, onClose, onSave }: { file: FileDraft; busy: boolean; onChange: (value: FileDraft) => void; onClose: () => void; onSave: () => void }) { return <Modal title={`编辑配置：${file.path}`} onClose={onClose} wide><p className="text-xs text-slate-500">只允许编辑已在代码库配置中勾选的文本文件；保存会校验文件版本，避免覆盖服务器上的新修改。</p><textarea className="workspace-scroll mt-4 h-[55vh] w-full resize-none rounded-xl border border-slate-200 bg-slate-950 p-3 font-mono text-xs leading-5 text-slate-100 outline-none focus:border-blue-500" value={file.content} onChange={(event) => onChange({ ...file, content: event.target.value })}/><div className="mt-4 flex justify-end gap-2"><button onClick={onClose} disabled={busy} className="secondary-button">关闭</button><button onClick={onSave} disabled={busy} className="primary-button">{busy && <Loader2 size={14} className="animate-spin"/>}保存文件</button></div></Modal>; }
function Modal({ title, children, onClose, wide = false }: { title: string; children: ReactNode; onClose: () => void; wide?: boolean }) { return <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/45 p-4"><div className={`w-full ${wide ? "max-w-5xl" : "max-w-2xl"} rounded-2xl bg-white p-5 shadow-2xl`}><div className="flex items-center justify-between"><h3 className="text-base font-semibold text-slate-900">{title}</h3><button onClick={onClose} className="icon-button" aria-label="关闭"><X size={17}/></button></div>{children}</div></div>; }
function message(value: unknown) { return value instanceof Error ? value.message : "操作失败，请稍后重试。"; }
