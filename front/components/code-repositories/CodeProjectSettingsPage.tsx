"use client";

import { useEffect, useMemo, useState, type ReactNode } from "react";
import { ArrowUpDown, Braces, Check, ChevronDown, ChevronUp, FileCog, FilePenLine, FolderGit2, FolderOpen, GitBranch, Loader2, PackageOpen, Plus, RefreshCw, Search, ShieldCheck, Terminal, Trash2, X } from "lucide-react";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";
import { browseCodeRepositoryDirectories, browseCodeRepositoryFiles, cloneCodeRepositoryViaWebSocket, createCodeProject, createCodeRepository, deleteCodeProject, deleteCodeRepository, getCodeProjects, getCodeRepositories, getCodeRepositoryHealth, inspectCodeRepository, packageCodeRepositoryViaWebSocket, readConfiguredCodeFile, updateCodeProject, updateCodeRepository, writeConfiguredCodeFile } from "@/lib/code-repository-api";
import type { CodeProject, CodeRepository, CodeRepositoryDirectoryBrowser, CodeRepositoryHealth, CodeRepositoryInspection } from "@/lib/code-repository-types";
import { listGitAccounts, type GitAccount } from "@/lib/git-account-api";
import { getCodeProjectRuntime, saveCodeRuntimeProfile } from "@/lib/code-runtime-api";
import type { CodeRuntimeProfile } from "@/lib/code-runtime-types";

type ProjectDraft = { id?: number; name: string; displayName: string; rootPath: string; description: string };
type RepositoryDraft = { name: string; projectId: number | ""; displayName: string; rootPath: string; description: string; languages: string[]; solutionFiles: string[]; configurationFiles: string[]; chatEditableConfigurationFiles: string[]; publishTarget: string; publishConfiguration: string; publishRuntime: string; publishOutputPath: string };
type ConsoleLine = { stream?: "stdout" | "stderr"; line: string };
type CloneDraft = { projectId: number | ""; repositoryUrl: string; gitAccountId: number | "" };
type FileDraft = { path: string; content: string; sha256: string };
type RuntimeDraft = { id?: number; role: "frontend" | "backend"; entryPath: string; runScript: string; testScript: string; preferredPort: string; healthPath: string; isEnabled: boolean; isPreviewEnabled: boolean };
type FileBrowserTarget = "solution" | "configuration" | "backendEntry" | "frontendEntry";

const languages = ["C#", "TypeScript/JavaScript"];
const emptyProject: ProjectDraft = { name: "", displayName: "", rootPath: "", description: "" };
const emptyRepository: RepositoryDraft = { name: "", projectId: "", displayName: "", rootPath: "", description: "", languages: [], solutionFiles: [], configurationFiles: [], chatEditableConfigurationFiles: [], publishTarget: "", publishConfiguration: "Release", publishRuntime: "", publishOutputPath: "artifacts/publish" };
const emptyClone: CloneDraft = { projectId: "", repositoryUrl: "", gitAccountId: "" };
const emptyRuntime: RuntimeDraft = { role: "backend", entryPath: "", runScript: "dev", testScript: "dotnet test", preferredPort: "5100", healthPath: "/", isEnabled: true, isPreviewEnabled: false };
const input = "mt-1.5 h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-sm outline-none transition placeholder:text-slate-400 focus:border-blue-500 focus:ring-2 focus:ring-blue-100";

export function CodeProjectSettingsPage() {
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [repositories, setRepositories] = useState<CodeRepository[]>([]);
  const [projectDraft, setProjectDraft] = useState<ProjectDraft>(emptyProject);
  const [repositoryDraft, setRepositoryDraft] = useState<RepositoryDraft>(emptyRepository);
  const [selectedProjectId, setSelectedProjectId] = useState<number | null>(null);
  const [selectedRepository, setSelectedRepository] = useState<CodeRepository | null>(null);
  const [inspection, setInspection] = useState<CodeRepositoryInspection | null>(null);
  const [health, setHealth] = useState<CodeRepositoryHealth | null>(null);
  const [browser, setBrowser] = useState<CodeRepositoryDirectoryBrowser | null>(null);
  const [browserTarget, setBrowserTarget] = useState<"project" | "repository">("project");
  const [fileBrowser, setFileBrowser] = useState<CodeRepositoryDirectoryBrowser | null>(null);
  const [fileBrowserTarget, setFileBrowserTarget] = useState<FileBrowserTarget>("solution");
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
  const [fileDraft, setFileDraft] = useState<FileDraft | null>(null);
  const [runtimeDraft, setRuntimeDraft] = useState<RuntimeDraft>(emptyRuntime);

  const selectedProject = useMemo(() => projects.find((item) => item.id === selectedProjectId) ?? null, [projects, selectedProjectId]);
  const selectedConfigs = repositoryDraft.configurationFiles;

  useEffect(() => { void reload(); }, []);

  async function reload() {
    setLoading(true);
    try {
      const [nextProjects, nextRepositories] = await Promise.all([getCodeProjects(), getCodeRepositories()]);
      setProjects(nextProjects);
      setRepositories(nextRepositories);
    } catch (value) { setError(message(value)); }
    finally { setLoading(false); }
  }

  function chooseProject(project: CodeProject) {
    setSelectedProjectId(project.id); setSelectedRepository(null); setMode("project"); setHealth(null); setError("");
    setProjectDraft({ id: project.id, name: project.name, displayName: project.display_name, rootPath: project.root_path, description: project.description ?? "" });
  }

  function chooseRepository(repository: CodeRepository) {
    setSelectedRepository(repository); setSelectedProjectId(repository.project_id ?? null); setMode("repository"); setHealth(null); setError("");
    setRepositoryDraft({ name: repository.name, projectId: repository.project_id ?? "", displayName: repository.display_name, rootPath: repository.root_path, description: repository.description ?? "", languages: repository.languages, solutionFiles: repository.solution_files, configurationFiles: repository.configuration_files, chatEditableConfigurationFiles: repository.chat_editable_configuration_files ?? [], publishTarget: repository.publish_target ?? "", publishConfiguration: repository.publish_configuration || "Release", publishRuntime: repository.publish_runtime ?? "", publishOutputPath: repository.publish_output_path || "artifacts/publish" });
    setInspection({ root_path: repository.root_path, suggested_name: repository.name, suggested_display_name: repository.display_name, languages: repository.languages, build_systems: repository.build_systems, is_git_repository: repository.is_git_repository, branch: repository.branch, marker_files: [], solution_files: repository.solution_files, configuration_files: repository.configuration_files });
    void loadRuntimeProfile(repository);
  }

  function startProject() { setMode("project"); setSelectedProjectId(null); setSelectedRepository(null); setProjectDraft(emptyProject); setHealth(null); setError(""); }
  function startRepository(projectId: number | "" = selectedProjectId ?? "") { setMode("repository"); setSelectedRepository(null); setRepositoryDraft({ ...emptyRepository, projectId }); setRuntimeDraft(emptyRuntime); setInspection(null); setHealth(null); setError(""); }

  async function loadRuntimeProfile(repository: CodeRepository, requestedRole?: "frontend" | "backend") {
    if (!repository.project_id) return;
    const defaultRole = requestedRole ?? (repository.build_systems.includes("dotnet") || repository.languages.includes("C#") ? "backend" : "frontend");
    const defaultEntry = defaultRole === "frontend"
      ? repository.configuration_files.find((path) => path.endsWith("package.json")) ?? ""
      : repository.solution_files.find((path) => path.endsWith(".csproj")) ?? "";
    try {
      const runtime = await getCodeProjectRuntime(repository.project_id);
      const profile = runtime.profiles.find((item) => item.repository_id === repository.id && item.role === defaultRole);
      setRuntimeDraft(toRuntimeDraft(profile, defaultRole, defaultEntry));
    } catch (value) { setError(message(value)); }
  }

  async function saveRuntimeProfile() {
    if (!selectedRepository?.project_id) return;
    if (!runtimeDraft.entryPath) return setError("请选择调试入口文件后再保存。");
    setBusy(true); setError("");
    try {
      const saved = await saveCodeRuntimeProfile(selectedRepository.project_id, {
        repository_name: selectedRepository.name,
        role: runtimeDraft.role,
        entry_path: runtimeDraft.entryPath,
        run_script: runtimeDraft.role === "frontend" ? runtimeDraft.runScript || "dev" : undefined,
        test_script: runtimeDraft.testScript,
        preferred_port: runtimeDraft.preferredPort.trim() ? Number(runtimeDraft.preferredPort) : undefined,
        health_path: runtimeDraft.healthPath || "/",
        is_enabled: runtimeDraft.isEnabled,
        is_preview_enabled: runtimeDraft.role === "frontend" && runtimeDraft.isPreviewEnabled,
      }, runtimeDraft.id);
      setRuntimeDraft(toRuntimeDraft(saved, runtimeDraft.role, runtimeDraft.entryPath));
    } catch (value) { setError(message(value)); }
    finally { setBusy(false); }
  }

  async function inspectRepository() {
    if (!repositoryDraft.rootPath.trim()) return setError("请先填写代码库目录。");
    setBusy(true); setError("");
    try {
      const result = await inspectCodeRepository(repositoryDraft.rootPath.trim());
      setInspection(result);
      setRepositoryDraft((item) => ({ ...item, rootPath: result.root_path, name: item.name || result.suggested_name, displayName: item.displayName || result.suggested_display_name, languages: item.languages.length ? item.languages : result.languages, solutionFiles: item.solutionFiles.length ? item.solutionFiles : result.solution_files, configurationFiles: item.configurationFiles.length ? item.configurationFiles : result.configuration_files, publishTarget: item.publishTarget || result.solution_files.find((file) => file.endsWith(".csproj")) || result.solution_files[0] || "" }));
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
      const payload = { name: repositoryDraft.name, project_id: Number(repositoryDraft.projectId), display_name: repositoryDraft.displayName, root_path: repositoryDraft.rootPath, description: repositoryDraft.description, languages: repositoryDraft.languages, solution_files: repositoryDraft.solutionFiles, configuration_files: repositoryDraft.configurationFiles, chat_editable_configuration_files: repositoryDraft.chatEditableConfigurationFiles, publish_target: repositoryDraft.publishTarget, publish_configuration: repositoryDraft.publishConfiguration, publish_runtime: repositoryDraft.publishRuntime, publish_output_path: repositoryDraft.publishOutputPath };
      const saved = selectedRepository ? await updateCodeRepository(selectedRepository.name, payload) : await createCodeRepository(payload);
      await reload(); chooseRepository(saved);
    } catch (value) { setError(message(value)); }
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

  async function openFileBrowser(target: FileBrowserTarget, path?: string) {
    if (!repositoryDraft.rootPath.trim()) {
      setError("请先选择并识别代码库目录。");
      return;
    }
    setFileBrowserTarget(target);
    const kind = target === "configuration" || target === "frontendEntry" ? "configuration" : "solution";
    try { setFileBrowser(await browseCodeRepositoryFiles(repositoryDraft.rootPath.trim(), kind, path)); } catch (value) { setError(message(value)); }
  }

  function chooseFile(path: string) {
    if (fileBrowserTarget === "solution") {
      setRepositoryDraft((item) => ({ ...item, solutionFiles: item.solutionFiles.includes(path) ? item.solutionFiles : [...item.solutionFiles, path] }));
    } else if (fileBrowserTarget === "configuration") {
      setRepositoryDraft((item) => ({ ...item, configurationFiles: item.configurationFiles.includes(path) ? item.configurationFiles : [...item.configurationFiles, path] }));
    } else if (fileBrowserTarget === "backendEntry") {
      setRepositoryDraft((item) => ({ ...item, solutionFiles: item.solutionFiles.includes(path) ? item.solutionFiles : [...item.solutionFiles, path] }));
      setRuntimeDraft((item) => ({ ...item, role: "backend", entryPath: path, runScript: "", testScript: item.role === "backend" ? item.testScript : "dotnet test", preferredPort: item.role === "backend" ? item.preferredPort : "5100", isPreviewEnabled: false }));
    } else {
      setRepositoryDraft((item) => ({ ...item, configurationFiles: item.configurationFiles.includes(path) ? item.configurationFiles : [...item.configurationFiles, path] }));
      setRuntimeDraft((item) => ({ ...item, role: "frontend", entryPath: path, runScript: item.role === "frontend" ? item.runScript : "dev", testScript: item.role === "frontend" ? item.testScript : "test", preferredPort: item.role === "frontend" ? item.preferredPort : "4300", isPreviewEnabled: true }));
    }
    setFileBrowser(null);
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
    setBusy(true); setCloneLines([]); setTerminalTitle("克隆终端"); setTerminalOpen(true); setError("");
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
    setBusy(true); setCloneLines([]); setTerminalTitle("打包终端"); setTerminalOpen(true); setError("");
    try {
      const event = await packageCodeRepositoryViaWebSocket(selectedRepository.name, (entry) => {
        if (entry.line) setCloneLines((lines) => [...lines.slice(-499), { stream: entry.stream, line: entry.line! }]);
        if (entry.message) setCloneLines((lines) => [...lines.slice(-499), { line: entry.message! }]);
      });
      if (!event.success) throw new Error(event.message || "打包失败。");
    } catch (value) { setError(message(value)); }
    finally { setBusy(false); }
  }

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

  return <section>
    <SettingsPageHeader title="项目与代码库" description="项目对应服务器文件夹；可在项目内克隆远程仓库，选择供 AI、调试和打包使用的入口文件。" action={<div className="flex gap-2"><button onClick={startProject} className="secondary-button"><FolderGit2 size={15}/>新建项目</button><button onClick={() => void openClone()} className="secondary-button"><GitBranch size={15}/>克隆代码库</button><button onClick={() => startRepository()} className="primary-button"><Plus size={15}/>挂载代码库</button></div>} />
    <div className="grid gap-5 xl:grid-cols-[310px_minmax(0,1fr)]">
      <aside className="workspace-scroll max-h-[calc(100vh-190px)] overflow-y-auto rounded-2xl border border-slate-200 bg-white p-3 shadow-sm">
        <div className="mb-2 flex items-center justify-between px-1"><span className="text-xs font-semibold text-slate-700">项目资源</span><button onClick={() => void reload()} className="icon-button" aria-label="刷新"><RefreshCw size={14} className={loading ? "animate-spin" : ""}/></button></div>
        {loading ? <div className="flex items-center gap-2 px-2 py-6 text-xs text-slate-400"><Loader2 size={14} className="animate-spin"/>正在读取项目…</div> : <div className="space-y-1">{projects.map((project) => <ProjectTree key={project.id} project={project} selectedProjectId={selectedProjectId} selectedRepository={selectedRepository} onProject={chooseProject} onRepository={chooseRepository} onCreateRepository={startRepository} onDeleteProject={removeProject} onDeleteRepository={removeRepository} />)}</div>}
        {!loading && projects.length === 0 && <p className="px-2 py-5 text-xs leading-5 text-slate-400">先登记一个项目文件夹，再在该文件夹内克隆或挂载代码库。</p>}
      </aside>
      <div className="min-w-0 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        {mode === "project" ? <ProjectForm draft={projectDraft} busy={busy} onChange={setProjectDraft} onBrowse={() => void openBrowser("project", projectDraft.rootPath || undefined)} onSave={() => void saveProject()} onDelete={() => projectDraft.id && selectedProject && void removeProject(selectedProject)} /> : <>
          <RepositoryForm draft={repositoryDraft} projects={projects} inspection={inspection} health={health} busy={busy} editing={Boolean(selectedRepository)} canPackage={Boolean(selectedRepository)} selectedConfigs={selectedConfigs} onChange={setRepositoryDraft} onInspect={() => void inspectRepository()} onBrowse={() => void openBrowser("repository", repositoryDraft.rootPath || undefined)} onBrowseFile={(target) => void openFileBrowser(target)} onSave={() => void saveRepository()} onDelete={() => selectedRepository && void removeRepository(selectedRepository)} onHealth={() => void checkHealth()} onOpenFile={(path) => void openFile(path)} onPackage={() => void startPackage()} />
          {selectedRepository && <RuntimeDebugSection repository={selectedRepository} draft={runtimeDraft} busy={busy} onChange={setRuntimeDraft} onSelectRole={(role) => void loadRuntimeProfile(selectedRepository, role)} onBrowseEntry={() => void openFileBrowser(runtimeDraft.role === "frontend" ? "frontendEntry" : "backendEntry")} onSave={() => void saveRuntimeProfile()} />}
        </>}
        {error && <p className="mt-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>}
      </div>
    </div>
    {browser && <DirectoryPicker browser={browser} onClose={() => setBrowser(null)} onOpen={(path) => void openBrowser(browserTarget, path)} onChoose={() => { if (browserTarget === "project") setProjectDraft((item) => ({ ...item, rootPath: browser.path })); else setRepositoryDraft((item) => ({ ...item, rootPath: browser.path })); setBrowser(null); }} />}
    {fileBrowser && <FilePicker browser={fileBrowser} target={fileBrowserTarget} onClose={() => setFileBrowser(null)} onOpen={(path) => void openFileBrowser(fileBrowserTarget, path)} onChoose={chooseFile} />}
    {cloneOpen && <CloneDialog projects={projects} accounts={accounts} draft={cloneDraft} lines={cloneLines} busy={busy} onChange={setCloneDraft} onClose={() => !busy && setCloneOpen(false)} onSubmit={() => void cloneRepository()} />}
    {terminalOpen && <TerminalDialog title={terminalTitle} lines={cloneLines} busy={busy} onClose={() => !busy && setTerminalOpen(false)} />}
    {fileDraft && <FileEditor file={fileDraft} busy={busy} onChange={setFileDraft} onClose={() => !busy && setFileDraft(null)} onSave={() => void saveFile()} />}
  </section>;
}

function ProjectTree({ project, selectedProjectId, selectedRepository, onProject, onRepository, onCreateRepository, onDeleteProject, onDeleteRepository }: { project: CodeProject; selectedProjectId: number | null; selectedRepository: CodeRepository | null; onProject: (value: CodeProject) => void; onRepository: (value: CodeRepository) => void; onCreateRepository: (id: number) => void; onDeleteProject: (value: CodeProject) => void; onDeleteRepository: (value: CodeRepository) => void }) {
  return <div><div className={`group flex items-center rounded-lg ${selectedProjectId === project.id && !selectedRepository ? "bg-blue-50" : "hover:bg-slate-50"}`}><button onClick={() => onProject(project)} className={`flex min-w-0 flex-1 items-center gap-2 px-2.5 py-2 text-left text-sm ${selectedProjectId === project.id && !selectedRepository ? "text-blue-700" : "text-slate-700"}`}><FolderGit2 size={16}/><span className="min-w-0 flex-1 truncate font-medium">{project.display_name}</span><span className="text-[11px] text-slate-400">{project.repository_count}</span></button><button onClick={() => onDeleteProject(project)} className="mr-1 hidden h-7 w-7 place-items-center rounded text-slate-400 hover:bg-red-50 hover:text-red-600 group-hover:grid" aria-label="删除项目"><Trash2 size={13}/></button></div><div className="ml-4 border-l border-slate-100 pl-2">{project.repositories.map((repository) => <div key={repository.id} className={`group flex items-center rounded-md ${selectedRepository?.id === repository.id ? "bg-slate-100" : "hover:bg-slate-50"}`}><button onClick={() => onRepository(repository)} className="flex min-w-0 flex-1 items-center gap-2 px-2 py-1.5 text-left text-xs text-slate-600"><Braces size={13}/><span className="truncate">{repository.display_name}</span></button><button onClick={() => onDeleteRepository(repository)} className="mr-1 hidden h-6 w-6 place-items-center rounded text-slate-400 hover:bg-red-50 hover:text-red-600 group-hover:grid" aria-label="移除代码库"><Trash2 size={12}/></button></div>)}<button onClick={() => onCreateRepository(project.id)} className="mt-1 flex w-full items-center gap-1.5 rounded-md px-2 py-1.5 text-xs text-blue-600 hover:bg-blue-50"><Plus size={13}/>挂载代码库</button></div></div>;
}

function ProjectForm({ draft, busy, onChange, onBrowse, onSave, onDelete }: { draft: ProjectDraft; busy: boolean; onChange: (value: ProjectDraft) => void; onBrowse: () => void; onSave: () => void; onDelete: () => void }) {
  return <><FormHeader eyebrow="一级 · 项目文件夹" title={draft.id ? "编辑项目" : "新建项目"} description="项目只是一项服务器文件夹授权，不会移动或复制任何文件。" danger={draft.id ? onDelete : undefined}/><div className="grid gap-4 md:grid-cols-2"><Field label="项目标识"><input className={input} value={draft.name} onChange={(event) => onChange({ ...draft, name: event.target.value })} placeholder="manufacturing-suite"/></Field><Field label="显示名称"><input className={input} value={draft.displayName} onChange={(event) => onChange({ ...draft, displayName: event.target.value })} placeholder="制造执行系统"/></Field></div><Field label="项目文件夹" hint="该文件夹是所有挂载代码库的上级目录。"><PathInput value={draft.rootPath} onChange={(value) => onChange({ ...draft, rootPath: value })} onBrowse={onBrowse} placeholder="E:\\项目\\manufacturing-suite"/></Field><Field label="项目说明"><textarea className={`${input} h-auto py-2`} rows={3} value={draft.description} onChange={(event) => onChange({ ...draft, description: event.target.value })}/></Field><Footer busy={busy} onSave={onSave} label={draft.id ? "保存项目" : "创建项目"}/></>;
}

function RepositoryForm({ draft, projects, inspection, health, busy, editing, canPackage, selectedConfigs, onChange, onInspect, onBrowse, onBrowseFile, onSave, onDelete, onHealth, onOpenFile, onPackage }: { draft: RepositoryDraft; projects: CodeProject[]; inspection: CodeRepositoryInspection | null; health: CodeRepositoryHealth | null; busy: boolean; editing: boolean; canPackage: boolean; selectedConfigs: string[]; onChange: (value: RepositoryDraft) => void; onInspect: () => void; onBrowse: () => void; onBrowseFile: (target: "solution" | "configuration") => void; onSave: () => void; onDelete: () => void; onHealth: () => void; onOpenFile: (path: string) => void; onPackage: () => void }) {
  return <RepositoryFormContent draft={draft} projects={projects} inspection={inspection} health={health} busy={busy} editing={editing} canPackage={canPackage} selectedConfigs={selectedConfigs} onChange={onChange} onInspect={onInspect} onBrowse={onBrowse} onBrowseFile={onBrowseFile} onSave={onSave} onDelete={onDelete} onHealth={onHealth} onOpenFile={onOpenFile} onPackage={onPackage} />;
  const toggle = (key: "languages" | "solutionFiles" | "configurationFiles", value: string) => onChange({ ...draft, [key]: draft[key].includes(value) ? draft[key].filter((item) => item !== value) : [...draft[key], value] });
  return <><FormHeader eyebrow="二级 · 代码库配置" title={editing ? "编辑代码库" : "挂载代码库"} description="识别目录后选择 AI 入口、调试配置与发布目标。" danger={editing ? onDelete : undefined}/><div className="grid gap-4 md:grid-cols-2"><Field label="所属项目"><select className={input} value={draft.projectId} onChange={(event) => onChange({ ...draft, projectId: event.target.value ? Number(event.target.value) : "" })}><option value="">选择项目</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.display_name}</option>)}</select></Field><Field label="显示名称"><input className={input} value={draft.displayName} onChange={(event) => onChange({ ...draft, displayName: event.target.value })} placeholder="cps-api"/></Field></div><Field label="代码库目录" hint="目录必须位于所属项目文件夹内。"><div className="flex gap-2"><PathInput value={draft.rootPath} onChange={(value) => onChange({ ...draft, rootPath: value })} onBrowse={onBrowse} placeholder="E:\\项目\\manufacturing-suite\\api"/><button onClick={onInspect} disabled={busy || !draft.rootPath.trim()} className="secondary-button mt-1.5 shrink-0 text-blue-700">识别目录</button></div></Field><Field label="代码库说明"><textarea className={`${input} h-auto py-2`} rows={2} value={draft.description} onChange={(event) => onChange({ ...draft, description: event.target.value })}/></Field><section className="mt-5 rounded-xl border border-slate-200 bg-slate-50/70 p-4"><div className="flex items-center gap-2 text-sm font-semibold text-slate-800"><FileCog size={16} className="text-blue-600"/>三级 · AI、调试与发布配置</div><p className="mt-1 text-xs text-slate-500">先识别目录，再勾选需要给 AI 使用或允许调试的文件。</p><div className="mt-4 flex flex-wrap gap-2">{languages.map((language) => <button key={language} onClick={() => toggle("languages", language)} className={`rounded-md px-2.5 py-1.5 text-xs ${draft.languages.includes(language) ? "bg-blue-600 text-white" : "border border-slate-200 bg-white text-slate-600"}`}>{language}</button>)}</div><FileSelection title="解决方案与工程文件" empty="识别后会列出 .sln、.slnf 和 .csproj。" items={inspection?.solution_files ?? []} selected={draft.solutionFiles} onToggle={(value) => toggle("solutionFiles", value)}/><FileSelection title="可编辑配置文件" empty="识别后会列出 appsettings、.env、package 与 Docker 配置。" items={inspection?.configuration_files ?? []} selected={draft.configurationFiles} onToggle={(value) => toggle("configurationFiles", value)}/><div className="mt-5 grid gap-3 border-t border-slate-200 pt-4 md:grid-cols-2"><Field label="打包目标"><select className={`${input} mt-1`} value={draft.publishTarget} onChange={(event) => onChange({ ...draft, publishTarget: event.target.value })}><option value="">选择已勾选的解决方案或工程</option>{draft.solutionFiles.map((file) => <option key={file} value={file}>{file}</option>)}</select></Field><Field label="构建配置"><select className={`${input} mt-1`} value={draft.publishConfiguration} onChange={(event) => onChange({ ...draft, publishConfiguration: event.target.value })}><option value="Release">Release</option><option value="Debug">Debug</option></select></Field><Field label="运行时（可选）"><input className={`${input} mt-1`} value={draft.publishRuntime} onChange={(event) => onChange({ ...draft, publishRuntime: event.target.value })} placeholder="win-x64"/></Field><Field label="发布输出目录" hint="相对于代码库，如 artifacts/publish。"><input className={`${input} mt-1`} value={draft.publishOutputPath} onChange={(event) => onChange({ ...draft, publishOutputPath: event.target.value })}/></Field></div></section>{editing && <section className="mt-4 grid gap-3 lg:grid-cols-2"><div className="rounded-xl border border-emerald-100 bg-emerald-50/50 p-3"><div className="flex items-center justify-between"><span className="flex items-center gap-1.5 text-xs font-semibold text-emerald-800"><ShieldCheck size={15}/>挂载检查</span><button onClick={onHealth} disabled={busy} className="text-xs font-medium text-emerald-700 hover:underline">立即检查</button></div>{health ? <div className="mt-2 text-xs leading-5 text-emerald-900"><p>目录 {health.root_exists ? "✓" : "×"} · 项目 {health.project_match ? "✓" : "×"} · Git {health.is_git_repository ? "✓" : "×"}{health.branch ? ` (${health.branch})` : ""}</p>{health.messages.map((item) => <p key={item}>{item}</p>)}</div> : <p className="mt-2 text-xs text-emerald-700">检查目录、项目挂载、Git 与已选择文件。</p>}</div><div className="rounded-xl border border-blue-100 bg-blue-50/50 p-3"><div className="flex items-center justify-between"><span className="flex items-center gap-1.5 text-xs font-semibold text-blue-800"><FilePenLine size={15}/>配置文件调试</span></div><div className="mt-2 flex flex-wrap gap-1.5">{selectedConfigs.map((path) => <button key={path} onClick={() => onOpenFile(path)} disabled={busy} className="rounded border border-blue-200 bg-white px-2 py-1 font-mono text-[11px] text-blue-700 hover:bg-blue-50">{path}</button>)}{selectedConfigs.length === 0 && <span className="text-xs text-blue-700">保存勾选的配置文件后，可在这里编辑。</span>}</div></div></section>}<div className="mt-6 flex items-center justify-between border-t border-slate-100 pt-4"><button onClick={onPackage} disabled={busy || !canPackage} className="secondary-button disabled:opacity-50"><PackageOpen size={15}/>实时打包</button><button onClick={onSave} disabled={busy} className="primary-button disabled:opacity-50">{busy ? <Loader2 size={14} className="animate-spin"/> : <Check size={14}/>} {editing ? "保存代码库配置" : "挂载代码库"}</button></div></>;
}

function RuntimeDebugSection({ repository, draft, busy, onChange, onSelectRole, onBrowseEntry, onSave }: { repository: CodeRepository; draft: RuntimeDraft; busy: boolean; onChange: (value: RuntimeDraft) => void; onSelectRole: (role: "frontend" | "backend") => void; onBrowseEntry: () => void; onSave: () => void }) {
  return <RuntimeDebugContent repository={repository} draft={draft} busy={busy} onChange={onChange} onSelectRole={onSelectRole} onBrowseEntry={onBrowseEntry} onSave={onSave} />;
  const isFrontend = draft.role === "frontend";
  const entryOptions = isFrontend
    ? repository.configuration_files.filter((path) => path.endsWith("package.json"))
    : repository.solution_files.filter((path) => path.endsWith(".csproj"));
  const runCommand = isFrontend
    ? `npm run ${draft.runScript || "dev"} -- --host 0.0.0.0 --port ${draft.preferredPort || "4300"}`
    : `dotnet run --project ${draft.entryPath || "<选择 .csproj>"} -- --urls http://0.0.0.0:${draft.preferredPort || "5100"}`;
  const testCommand = isFrontend ? `npm run ${draft.testScript || "test"}` : draft.testScript || "dotnet test";

  return <section className="mt-5 rounded-xl border border-violet-200 bg-violet-50/40 p-4">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><div className="flex items-center gap-2 text-sm font-semibold text-slate-800"><Terminal size={16} className="text-violet-600"/>四级 · 本地调试与测试</div><p className="mt-1 text-xs leading-5 text-slate-500">此配置会在聊天顶部“项目程序运行”中展示并用于启动。服务会监听全部 IPv4 网卡，聊天中会列出本机、内网及当前访问地址；端口被占用时自动分配空闲端口。</p></div>
      <span className="rounded-full bg-violet-100 px-2.5 py-1 text-xs font-semibold text-violet-700">{isFrontend ? "前端 / npm" : "C# 后端 / dotnet"}</span>
    </div>
    <div className="mt-4 grid gap-3 md:grid-cols-2">
      <Field label={isFrontend ? "前端启动入口" : "C# 启动工程"} hint={isFrontend ? "仅允许已选中的 package.json。" : "请选择 Web/API 或 OutputType=Exe 的 .csproj；Controllers、Services 等类库不能启动。"}>
        <select className={`${input} mt-1`} value={draft.entryPath} onChange={(event) => onChange({ ...draft, entryPath: event.target.value })}><option value="">选择调试入口</option>{entryOptions.map((path) => <option key={path} value={path}>{path}</option>)}</select>
      </Field>
      <Field label="默认调试端口" hint={isFrontend ? "前端默认 4300；被占用会自动换端口。" : "后端默认 5100；被占用会自动换端口。"}>
        <input type="number" min="1024" max="65535" className={`${input} mt-1`} value={draft.preferredPort} onChange={(event) => onChange({ ...draft, preferredPort: event.target.value })}/>
      </Field>
      {isFrontend ? <Field label="前端启动脚本" hint="填写 package.json scripts 中的名称，例如 dev、start。"><input className={`${input} mt-1 font-mono`} value={draft.runScript} onChange={(event) => onChange({ ...draft, runScript: event.target.value })} placeholder="dev"/></Field> : <Field label="后端启动命令" hint="固定使用 Development 环境；上方端口会覆盖 Visual Studio launchSettings 中的 localhost 端口。"><input readOnly className={`${input} mt-1 bg-slate-100 font-mono text-xs text-slate-600`} value="dotnet run --no-launch-profile（Development）"/></Field>}
      <Field label={isFrontend ? "前端测试脚本" : "C# 测试命令"} hint={isFrontend ? "填写 package.json scripts 中的名称，例如 test、test:unit。" : "仅支持以 dotnet test 开头的受控命令。"}><input className={`${input} mt-1 font-mono`} value={draft.testScript} onChange={(event) => onChange({ ...draft, testScript: event.target.value })} placeholder={isFrontend ? "test" : "dotnet test"}/></Field>
      <Field label="健康检查路径（可选）" hint="例如 /health；前端预览通常使用 /。"><input className={`${input} mt-1 font-mono`} value={draft.healthPath} onChange={(event) => onChange({ ...draft, healthPath: event.target.value })} placeholder="/"/></Field>
      <label className="mt-4 flex items-center gap-2 text-xs text-slate-700"><input type="checkbox" checked={draft.isEnabled} onChange={(event) => onChange({ ...draft, isEnabled: event.target.checked })} className="rounded border-slate-300 text-violet-600 focus:ring-violet-500"/>在聊天中允许启动此程序</label>
      {isFrontend && <label className="mt-4 flex items-center gap-2 text-xs text-slate-700"><input type="checkbox" checked={draft.isPreviewEnabled} onChange={(event) => onChange({ ...draft, isPreviewEnabled: event.target.checked })} className="rounded border-slate-300 text-violet-600 focus:ring-violet-500"/>允许在右侧浏览器预览</label>}
    </div>
    <div className="mt-4 grid gap-2 border-t border-violet-100 pt-3 text-[11px] leading-5 md:grid-cols-2"><div className="rounded-lg border border-violet-100 bg-white px-3 py-2"><span className="font-semibold text-violet-700">启动：</span><code className="break-all text-slate-600">{runCommand}</code></div><div className="rounded-lg border border-violet-100 bg-white px-3 py-2"><span className="font-semibold text-violet-700">测试：</span><code className="break-all text-slate-600">{testCommand}</code></div></div>
    <div className="mt-4 flex justify-end"><button onClick={onSave} disabled={busy || !draft.entryPath} className="primary-button disabled:opacity-50">{busy ? <Loader2 size={14} className="animate-spin"/> : <Check size={14}/>}保存调试配置</button></div>
  </section>;
}

function RepositoryFormContent({ draft, projects, inspection, health, busy, editing, canPackage, selectedConfigs, onChange, onInspect, onBrowse, onBrowseFile, onSave, onDelete, onHealth, onOpenFile, onPackage }: { draft: RepositoryDraft; projects: CodeProject[]; inspection: CodeRepositoryInspection | null; health: CodeRepositoryHealth | null; busy: boolean; editing: boolean; canPackage: boolean; selectedConfigs: string[]; onChange: (value: RepositoryDraft) => void; onInspect: () => void; onBrowse: () => void; onBrowseFile: (target: "solution" | "configuration") => void; onSave: () => void; onDelete: () => void; onHealth: () => void; onOpenFile: (path: string) => void; onPackage: () => void }) {
  const toggleFile = (key: "solutionFiles" | "configurationFiles", value: string) => onChange({ ...draft, [key]: draft[key].includes(value) ? draft[key].filter((item) => item !== value) : [...draft[key], value] });
  const frontendRepository = draft.languages.includes("TypeScript/JavaScript");
  const publishTargets = frontendRepository ? draft.configurationFiles.filter((path) => path.endsWith("package.json")) : draft.solutionFiles;
  return <>
    <FormHeader eyebrow="二级 · 代码库配置" title={editing ? "编辑代码库" : "挂载代码库"} description="识别目录后选择 AI 入口、调试配置与发布目标。" danger={editing ? onDelete : undefined}/>
    <div className="grid gap-4 md:grid-cols-2">
      <Field label="所属项目"><select className={input} value={draft.projectId} onChange={(event) => onChange({ ...draft, projectId: event.target.value ? Number(event.target.value) : "" })}><option value="">选择项目</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.display_name}</option>)}</select></Field>
      <Field label="显示名称"><input className={input} value={draft.displayName} onChange={(event) => onChange({ ...draft, displayName: event.target.value })} placeholder="cps-api"/></Field>
    </div>
    <Field label="代码库目录" hint="目录必须位于所属项目文件夹内。"><div className="flex gap-2"><PathInput value={draft.rootPath} onChange={(value) => onChange({ ...draft, rootPath: value })} onBrowse={onBrowse} placeholder="E:\\项目\\manufacturing-suite\\api"/><button onClick={onInspect} disabled={busy || !draft.rootPath.trim()} className="secondary-button mt-1.5 shrink-0 text-blue-700">识别目录</button></div></Field>
    <Field label="代码库说明"><textarea className={`${input} h-auto py-2`} rows={2} value={draft.description} onChange={(event) => onChange({ ...draft, description: event.target.value })}/></Field>
    <section className="mt-5 rounded-xl border border-slate-200 bg-slate-50/70 p-4">
      <div className="flex items-center gap-2 text-sm font-semibold text-slate-800"><FileCog size={16} className="text-blue-600"/>三级 · AI、调试与发布配置</div>
      <p className="mt-1 text-xs text-slate-500">先识别目录；语言为单选。C# 请选择解决方案及 Web/API 启动工程，前端请选择 package.json。</p>
      <div className="mt-4 flex flex-wrap gap-2">{languages.map((language) => <button type="button" key={language} onClick={() => onChange({ ...draft, languages: [language] })} className={`rounded-md px-2.5 py-1.5 text-xs ${draft.languages[0] === language ? "bg-blue-600 text-white" : "border border-slate-200 bg-white text-slate-600"}`}>{language}</button>)}</div>
      <FileSelection title="解决方案与工程文件" empty="请选择 .sln 解决方案；运行 API 时还需要选择可启动的 .csproj。" items={inspection?.solution_files ?? []} selected={draft.solutionFiles} onToggle={(value) => toggleFile("solutionFiles", value)} onBrowse={() => onBrowseFile("solution")}/>
      <ConfigurationFileSelection candidates={inspection?.configuration_files ?? []} selected={draft.configurationFiles} chatEditable={draft.chatEditableConfigurationFiles} onBrowse={() => onBrowseFile("configuration")} onAdd={(path) => toggleFile("configurationFiles", path)} onRemove={(path) => onChange({ ...draft, configurationFiles: draft.configurationFiles.filter((item) => item !== path), chatEditableConfigurationFiles: draft.chatEditableConfigurationFiles.filter((item) => item !== path) })} onChatEditable={(path, enabled) => onChange({ ...draft, chatEditableConfigurationFiles: enabled ? [...draft.chatEditableConfigurationFiles, path] : draft.chatEditableConfigurationFiles.filter((item) => item !== path) })}/>
      <div className="mt-5 grid gap-3 border-t border-slate-200 pt-4 md:grid-cols-2">
        <Field label="打包目标" hint={frontendRepository ? "从已添加的 package.json 中选择。" : "从已选择的解决方案或工程中选择。"}><select className={`${input} mt-1`} value={draft.publishTarget} onChange={(event) => onChange({ ...draft, publishTarget: event.target.value })}><option value="">{frontendRepository ? "选择已添加的 package.json" : "选择已选的解决方案或工程"}</option>{publishTargets.map((file) => <option key={file} value={file}>{file}</option>)}</select></Field>
        <Field label="构建配置"><select className={`${input} mt-1`} value={draft.publishConfiguration} onChange={(event) => onChange({ ...draft, publishConfiguration: event.target.value })}><option value="Release">Release</option><option value="Debug">Debug</option></select></Field>
        <Field label="运行时（可选）"><input className={`${input} mt-1`} value={draft.publishRuntime} onChange={(event) => onChange({ ...draft, publishRuntime: event.target.value })} placeholder="win-x64"/></Field>
        <Field label="发布输出目录" hint="相对于代码库，如 artifacts/publish。"><input className={`${input} mt-1`} value={draft.publishOutputPath} onChange={(event) => onChange({ ...draft, publishOutputPath: event.target.value })}/></Field>
      </div>
    </section>
    {editing && <section className="mt-4 grid gap-3 lg:grid-cols-2"><div className="rounded-xl border border-emerald-100 bg-emerald-50/50 p-3"><div className="flex items-center justify-between"><span className="flex items-center gap-1.5 text-xs font-semibold text-emerald-800"><ShieldCheck size={15}/>挂载检查</span><button onClick={onHealth} disabled={busy} className="text-xs font-medium text-emerald-700 hover:underline">立即检查</button></div>{health ? <div className="mt-2 text-xs leading-5 text-emerald-900"><p>目录 {health.root_exists ? "✓" : "×"} · 项目 {health.project_match ? "✓" : "×"} · Git {health.is_git_repository ? "✓" : "×"}{health.branch ? ` (${health.branch})` : ""}</p>{health.messages.map((item) => <p key={item}>{item}</p>)}</div> : <p className="mt-2 text-xs text-emerald-700">检查目录、项目挂载、Git 与已选择文件。</p>}</div><div className="rounded-xl border border-blue-100 bg-blue-50/50 p-3"><span className="flex items-center gap-1.5 text-xs font-semibold text-blue-800"><FilePenLine size={15}/>配置文件调试</span><div className="mt-2 flex flex-wrap gap-1.5">{selectedConfigs.map((path) => <button key={path} onClick={() => onOpenFile(path)} disabled={busy} className="rounded border border-blue-200 bg-white px-2 py-1 font-mono text-[11px] text-blue-700 hover:bg-blue-50">{path}</button>)}{selectedConfigs.length === 0 && <span className="text-xs text-blue-700">保存所选配置文件后，可在这里编辑。</span>}</div></div></section>}
    <div className="mt-6 flex items-center justify-between border-t border-slate-100 pt-4"><button onClick={onPackage} disabled={busy || !canPackage} className="secondary-button disabled:opacity-50"><PackageOpen size={15}/>实时打包</button><button onClick={onSave} disabled={busy} className="primary-button disabled:opacity-50">{busy ? <Loader2 size={14} className="animate-spin"/> : <Check size={14}/>} {editing ? "保存代码库配置" : "挂载代码库"}</button></div>
  </>;
}

function RuntimeDebugContent({ repository, draft, busy, onChange, onSelectRole, onBrowseEntry, onSave }: { repository: CodeRepository; draft: RuntimeDraft; busy: boolean; onChange: (value: RuntimeDraft) => void; onSelectRole: (role: "frontend" | "backend") => void; onBrowseEntry: () => void; onSave: () => void }) {
  const isFrontend = draft.role === "frontend";
  const configuredEntries = isFrontend ? repository.configuration_files.filter((path) => path.endsWith("package.json")) : repository.solution_files.filter((path) => path.endsWith(".csproj"));
  const entryOptions = draft.entryPath && !configuredEntries.includes(draft.entryPath) ? [draft.entryPath, ...configuredEntries] : configuredEntries;
  const runCommand = isFrontend ? `npm run ${draft.runScript || "dev"} -- --host 0.0.0.0 --port ${draft.preferredPort || "4300"}` : `dotnet run --project ${draft.entryPath || "<选择 Web/API .csproj>"} --no-launch-profile -- --urls http://0.0.0.0:${draft.preferredPort || "5100"}`;
  return <section className="mt-5 rounded-xl border border-violet-200 bg-violet-50/40 p-4">
    <div className="flex flex-wrap items-start justify-between gap-3"><div><div className="flex items-center gap-2 text-sm font-semibold text-slate-800"><Terminal size={16} className="text-violet-600"/>四级 · 本地调试与测试</div><p className="mt-1 text-xs leading-5 text-slate-500">先保存上方文件选择，再保存本节运行配置。C# 的 .sln 用来组织工程，真正启动的是其中的 Web/API .csproj。</p></div><span className="rounded-full bg-violet-100 px-2.5 py-1 text-xs font-semibold text-violet-700">{isFrontend ? "前端 / npm" : "C# 后端 / dotnet"}</span></div>
    <div className="mt-4 flex gap-2"><button type="button" onClick={() => onSelectRole("backend")} className={`rounded-md px-3 py-1.5 text-xs ${!isFrontend ? "bg-violet-600 text-white" : "border border-violet-200 bg-white text-violet-700"}`}>C# 后端</button><button type="button" onClick={() => onSelectRole("frontend")} className={`rounded-md px-3 py-1.5 text-xs ${isFrontend ? "bg-violet-600 text-white" : "border border-violet-200 bg-white text-violet-700"}`}>前端（TypeScript/JavaScript）</button></div>
    <div className="mt-4 grid gap-3 md:grid-cols-2">
      <Field label={isFrontend ? "前端启动入口" : "C# API 启动工程"} hint={isFrontend ? "选择 package.json，然后填写其中的启动脚本。" : "先选 .sln，再选择实际可启动的 Web/API .csproj；类库无法启动。"}><div className="mt-1 flex gap-2"><select className={`${input} mt-0 min-w-0 flex-1`} value={draft.entryPath} onChange={(event) => onChange({ ...draft, entryPath: event.target.value })}><option value="">选择调试入口</option>{entryOptions.map((path) => <option key={path} value={path}>{path}</option>)}</select><button type="button" onClick={onBrowseEntry} className="secondary-button shrink-0"> <FolderOpen size={15}/>选择文件</button></div></Field>
      <Field label="默认调试端口" hint="端口占用时会自动寻找可用端口。"><input type="number" min="1024" max="65535" className={`${input} mt-1`} value={draft.preferredPort} onChange={(event) => onChange({ ...draft, preferredPort: event.target.value })}/></Field>
      {isFrontend ? <Field label="前端启动脚本" hint="填写 package.json scripts 中的名称，例如 dev、start。"><input className={`${input} mt-1 font-mono`} value={draft.runScript} onChange={(event) => onChange({ ...draft, runScript: event.target.value })} placeholder="dev"/></Field> : <Field label="后端启动命令"><input readOnly className={`${input} mt-1 bg-slate-100 font-mono text-xs text-slate-600`} value="dotnet run --no-launch-profile（Development）"/></Field>}
      <Field label={isFrontend ? "前端测试脚本" : "C# 测试命令"}><input className={`${input} mt-1 font-mono`} value={draft.testScript} onChange={(event) => onChange({ ...draft, testScript: event.target.value })} placeholder={isFrontend ? "test" : "dotnet test"}/></Field>
      <Field label="健康检查路径（可选）"><input className={`${input} mt-1 font-mono`} value={draft.healthPath} onChange={(event) => onChange({ ...draft, healthPath: event.target.value })} placeholder="/"/></Field>
    </div>
    <div className="mt-4 rounded-lg border border-violet-100 bg-white px-3 py-2 text-[11px] leading-5"><span className="font-semibold text-violet-700">启动：</span><code className="break-all text-slate-600">{runCommand}</code></div>
    <div className="mt-4 flex justify-end"><button onClick={onSave} disabled={busy || !draft.entryPath} className="primary-button disabled:opacity-50">{busy ? <Loader2 size={14} className="animate-spin"/> : <Check size={14}/>}保存调试配置</button></div>
  </section>;
}

function toRuntimeDraft(profile: CodeRuntimeProfile | undefined, role: "frontend" | "backend", entryPath: string): RuntimeDraft {
  const frontend = role === "frontend";
  return {
    id: profile?.id,
    role,
    entryPath: profile?.entry_path ?? entryPath,
    runScript: profile?.run_script ?? "dev",
    testScript: profile?.test_script ?? (frontend ? "test" : "dotnet test"),
    preferredPort: String(profile?.preferred_port ?? (frontend ? 4300 : 5100)),
    healthPath: profile?.health_path ?? "/",
    isEnabled: profile?.is_enabled ?? true,
    isPreviewEnabled: profile?.is_preview_enabled ?? frontend,
  };
}

function FormHeader({ eyebrow, title, description, danger }: { eyebrow: string; title: string; description: string; danger?: () => void }) { return <header className="mb-6 flex items-start justify-between"><div><p className="text-xs font-semibold uppercase tracking-[.14em] text-blue-600">{eyebrow}</p><h2 className="mt-1 text-lg font-semibold text-slate-950">{title}</h2><p className="mt-1 text-xs text-slate-500">{description}</p></div>{danger && <button onClick={danger} className="secondary-button border-red-200 text-red-700 hover:bg-red-50"><Trash2 size={14}/>删除</button>}</header>; }
function PathInput({ value, onChange, onBrowse, placeholder }: { value: string; onChange: (value: string) => void; onBrowse: () => void; placeholder: string }) { return <div className="flex min-w-0 flex-1 gap-2"><input className={`${input} min-w-0 flex-1 font-mono`} value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder}/><button onClick={onBrowse} className="secondary-button mt-1.5 shrink-0 px-3" aria-label="浏览目录"><FolderOpen size={15}/></button></div>; }
function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) { return <div className="mt-4 text-xs font-medium text-slate-700"><span>{label}</span>{hint && <span className="mt-1 block font-normal leading-5 text-slate-400">{hint}</span>}{children}</div>; }
function FileSelection({ title, empty, items, selected, onToggle, onBrowse }: { title: string; empty: string; items: string[]; selected: string[]; onToggle: (value: string) => void; onBrowse?: () => void }) { return <div className="mt-4"><div className="flex items-center justify-between gap-2"><span className="text-xs font-medium text-slate-700">{title}</span>{onBrowse && <button type="button" onClick={onBrowse} className="secondary-button px-2.5 py-1 text-xs"><FolderOpen size={14}/>选择文件</button>}</div>{items.length ? <div className="mt-2 grid gap-1 sm:grid-cols-2">{items.map((item) => <label key={item} className="flex min-w-0 cursor-pointer items-center gap-2 rounded-md bg-white px-2 py-1.5 text-xs text-slate-600"><input type="checkbox" checked={selected.includes(item)} onChange={() => onToggle(item)} className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"/><span className="truncate font-mono" title={item}>{item}</span></label>)}</div> : <p className="mt-2 text-xs text-slate-400">{empty}</p>}</div>; }
function ConfigurationFileSelection({ candidates, selected, chatEditable, onBrowse, onAdd, onRemove, onChatEditable }: { candidates: string[]; selected: string[]; chatEditable: string[]; onBrowse: () => void; onAdd: (path: string) => void; onRemove: (path: string) => void; onChatEditable: (path: string, enabled: boolean) => void }) {
  const available = candidates.filter((path) => !selected.includes(path));
  return <section className="mt-5 rounded-xl border border-blue-100 bg-white p-3.5"><div className="flex flex-wrap items-start justify-between gap-3"><div><div className="flex items-center gap-1.5 text-xs font-semibold text-slate-800"><FilePenLine size={15} className="text-blue-600"/>配置文件</div><p className="mt-1 text-[11px] leading-5 text-slate-500">添加后可供运行、打包与代码库编辑使用；单独开启“聊天可修改”才会出现在聊天菜单。</p></div><button type="button" onClick={onBrowse} className="secondary-button shrink-0 px-2.5 py-1 text-xs"><FolderOpen size={14}/>添加文件</button></div>{selected.length ? <div className="mt-3 space-y-1.5">{selected.map((path) => <div key={path} className="flex min-w-0 items-center gap-2 rounded-lg border border-slate-100 bg-slate-50 px-2.5 py-2"><FileCog size={14} className="shrink-0 text-slate-400"/><span className="min-w-0 flex-1 truncate font-mono text-[11px] text-slate-700" title={path}>{path}</span><label className="inline-flex shrink-0 cursor-pointer items-center gap-1.5 text-[11px] text-blue-700"><input type="checkbox" checked={chatEditable.includes(path)} onChange={(event) => onChatEditable(path, event.target.checked)} className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"/>聊天可改</label><button type="button" onClick={() => onRemove(path)} className="grid h-6 w-6 shrink-0 place-items-center rounded text-slate-400 hover:bg-red-50 hover:text-red-600" aria-label={`移除配置文件：${path}`} title="从代码库配置中移除，不删除磁盘文件"><Trash2 size={13}/></button></div>)}</div> : <p className="mt-3 rounded-lg border border-dashed border-slate-200 px-3 py-3 text-xs text-slate-400">尚未添加配置文件。可选择 package.json、.env、appsettings 等。</p>}{available.length > 0 && <div className="mt-3 border-t border-slate-100 pt-3"><p className="text-[11px] text-slate-400">已识别，可快速添加</p><div className="mt-2 flex flex-wrap gap-1.5">{available.slice(0, 12).map((path) => <button type="button" key={path} onClick={() => onAdd(path)} className="max-w-full truncate rounded-md border border-slate-200 bg-white px-2 py-1 font-mono text-[10px] text-slate-600 hover:border-blue-200 hover:bg-blue-50 hover:text-blue-700" title={path}>+ {path}</button>)}</div></div>}</section>;
}
function Footer({ busy, onSave, label }: { busy: boolean; onSave: () => void; label: string }) { return <div className="mt-6 flex justify-end border-t border-slate-100 pt-4"><button onClick={onSave} disabled={busy} className="primary-button">{busy ? <Loader2 size={14} className="animate-spin"/> : <Check size={14}/>} {label}</button></div>; }
function FilePicker({ browser, target, onClose, onOpen, onChoose }: { browser: CodeRepositoryDirectoryBrowser; target: FileBrowserTarget; onClose: () => void; onOpen: (path?: string) => void; onChoose: (path: string) => void }) {
  const files = (browser.files ?? []).filter((file) => target === "backendEntry" ? file.path.toLowerCase().endsWith(".csproj") : target === "frontendEntry" ? file.path.toLowerCase().endsWith("package.json") : true);
  const title = target === "backendEntry" ? "选择 C# API 启动工程" : target === "frontendEntry" ? "选择前端 package.json" : target === "solution" ? "选择解决方案或工程文件" : "选择配置文件";
  return <Modal title={title} onClose={onClose}><p className="truncate font-mono text-xs text-slate-500">{browser.path}</p><p className="mt-1 text-xs leading-5 text-slate-500">{target === "backendEntry" ? "只会显示 .csproj；请选择 Web/API 或 OutputType=Exe 工程。" : target === "frontendEntry" ? "只会显示 package.json。" : "进入文件夹后，单击一个文件即可选中。"}</p><div className="workspace-scroll mt-4 max-h-72 overflow-auto rounded-lg border border-slate-200 p-2">{browser.parent_path && <button type="button" onClick={() => onOpen(browser.parent_path ?? undefined)} className="block w-full rounded px-2 py-2 text-left text-xs text-blue-600 hover:bg-blue-50">↑ 上级目录</button>}{browser.directories.map((path) => <button type="button" key={path} onClick={() => onOpen(path)} className="flex w-full items-center gap-2 rounded px-2 py-2 text-left text-sm hover:bg-slate-50"><FolderOpen size={15} className="text-amber-500"/>{path.split(/[\\/]/).pop()}</button>)}{files.map((file) => <button type="button" key={file.path} onClick={() => onChoose(file.path)} className="flex w-full items-center gap-2 rounded px-2 py-2 text-left text-sm hover:bg-blue-50"><FileCog size={15} className="text-blue-500"/><span className="truncate font-mono text-xs" title={file.path}>{file.name}</span></button>)}{files.length === 0 && <p className="px-2 py-4 text-xs text-slate-400">此目录没有可选文件，请进入下级目录。</p>}</div></Modal>;
}

function DirectoryPicker({ browser, onClose, onOpen, onChoose }: { browser: CodeRepositoryDirectoryBrowser; onClose: () => void; onOpen: (path?: string) => void; onChoose: () => void }) {
  const [filter, setFilter] = useState("");
  const [sort, setSort] = useState<{ key: "name" | "modified"; direction: "asc" | "desc" }>({ key: "name", direction: "asc" });
  useEffect(() => setFilter(""), [browser.path]);
  const entries = useMemo(() => {
    const source = browser.directory_entries ?? browser.directories.map((path) => ({ name: path.split(/[\\/]/).pop() ?? path, path, modified_at: null }));
    const keyword = filter.trim().toLocaleLowerCase();
    return [...source]
      .filter((entry) => !keyword || entry.name.toLocaleLowerCase().includes(keyword))
      .sort((left, right) => {
        const value = sort.key === "name"
          ? left.name.localeCompare(right.name, "zh-CN", { sensitivity: "base" })
          : (left.modified_at ?? "").localeCompare(right.modified_at ?? "");
        return sort.direction === "asc" ? value : -value;
      });
  }, [browser.directories, browser.directory_entries, filter, sort]);
  const toggleSort = (key: "name" | "modified") => setSort((current) => current.key === key ? { key, direction: current.direction === "asc" ? "desc" : "asc" } : { key, direction: key === "name" ? "asc" : "desc" });
  const sortIcon = (key: "name" | "modified") => sort.key !== key ? <ArrowUpDown size={13}/> : sort.direction === "asc" ? <ChevronUp size={14}/> : <ChevronDown size={14}/>;
  const formatModifiedAt = (value?: string | null) => {
    if (!value) return "—";
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? "—" : date.toLocaleString("zh-CN", { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });
  };

  return <Modal title="选择服务器文件夹" onClose={onClose}>
    <p className="truncate font-mono text-xs text-slate-500" title={browser.path}>{browser.path}</p>
    <label className="relative mt-4 block">
      <Search size={15} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-slate-400"/>
      <input value={filter} onChange={(event) => setFilter(event.target.value)} className="h-10 w-full rounded-lg border border-slate-200 bg-slate-50 pl-9 pr-3 text-sm outline-none transition placeholder:text-slate-400 focus:border-blue-500 focus:bg-white focus:ring-2 focus:ring-blue-100" placeholder="过滤文件夹名称" aria-label="过滤文件夹名称"/>
    </label>
    <div className="mt-3 overflow-hidden rounded-lg border border-slate-200">
      <div className="grid grid-cols-[minmax(0,1fr)_11rem] border-b border-slate-200 bg-slate-50 px-3 text-xs font-medium text-slate-500">
        <button type="button" onClick={() => toggleSort("name")} className="flex items-center gap-1 py-2 text-left hover:text-blue-700">名称 {sortIcon("name")}</button>
        <button type="button" onClick={() => toggleSort("modified")} className="flex items-center justify-end gap-1 py-2 text-right hover:text-blue-700">最近修改时间 {sortIcon("modified")}</button>
      </div>
      <div className="workspace-scroll max-h-64 overflow-auto p-1.5">
        {browser.parent_path && <button type="button" onClick={() => onOpen(browser.parent_path ?? undefined)} className="mb-1 block w-full rounded-md px-2.5 py-2 text-left text-xs text-blue-600 hover:bg-blue-50">↑ 上级目录</button>}
        {entries.map((entry) => <button type="button" key={entry.path} onClick={() => onOpen(entry.path)} className="grid w-full grid-cols-[minmax(0,1fr)_11rem] items-center rounded-md px-2.5 py-2 text-left text-sm hover:bg-slate-50"><span className="flex min-w-0 items-center gap-2"><FolderOpen size={15} className="shrink-0 text-amber-500"/><span className="truncate" title={entry.path}>{entry.name}</span></span><span className="text-right text-xs tabular-nums text-slate-400">{formatModifiedAt(entry.modified_at)}</span></button>)}
        {entries.length === 0 && <p className="px-2.5 py-7 text-center text-xs text-slate-400">{filter.trim() ? "没有匹配的文件夹" : "此目录没有可访问的下级文件夹"}</p>}
      </div>
    </div>
    <div className="mt-2 text-right text-[11px] text-slate-400">显示 {entries.length} 个文件夹</div>
    <div className="mt-3 flex justify-end"><button onClick={onChoose} className="primary-button">选择此文件夹</button></div>
  </Modal>;
}
function CloneDialog({ projects, accounts, draft, lines, busy, onChange, onClose, onSubmit }: { projects: CodeProject[]; accounts: GitAccount[]; draft: CloneDraft; lines: ConsoleLine[]; busy: boolean; onChange: (value: CloneDraft) => void; onClose: () => void; onSubmit: () => void }) { return <Modal title="克隆远程代码库" onClose={onClose}><p className="text-xs leading-5 text-slate-500">克隆会在所选项目文件夹内执行。完成后会自动识别目录，并登记为该项目的代码库。</p><Field label="目标项目"><select className={input} value={draft.projectId} onChange={(event) => onChange({ ...draft, projectId: event.target.value ? Number(event.target.value) : "" })}><option value="">选择项目</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.display_name} · {project.root_path}</option>)}</select></Field><Field label="HTTPS 仓库地址"><input className={input} value={draft.repositoryUrl} onChange={(event) => onChange({ ...draft, repositoryUrl: event.target.value })} placeholder="https://gitee.com/org/repository.git"/></Field><Field label="Git 账号"><select className={input} value={draft.gitAccountId} onChange={(event) => onChange({ ...draft, gitAccountId: event.target.value ? Number(event.target.value) : "" })}><option value="">选择已配置账号</option>{accounts.map((account) => <option key={account.id} value={account.id}>{account.provider} · {account.display_name} (@{account.username})</option>)}</select></Field>{accounts.length === 0 && <p className="mt-3 text-xs text-amber-700">尚未配置 Git 账号，请先前往 Git 管理添加访问令牌。</p>}{lines.length > 0 && <Console lines={lines}/>}<div className="mt-5 flex justify-end gap-2"><button onClick={onClose} disabled={busy} className="secondary-button">取消</button><button onClick={onSubmit} disabled={busy} className="primary-button">{busy && <Loader2 size={14} className="animate-spin"/>}开始克隆</button></div></Modal>; }
function TerminalDialog({ title, lines, busy, onClose }: { title: string; lines: ConsoleLine[]; busy: boolean; onClose: () => void }) { return <Modal title={title} onClose={onClose}><Console lines={lines}/><div className="mt-4 flex items-center justify-between text-xs text-slate-500"><span>{busy ? "服务端命令正在执行…" : "命令已结束。"}</span><button onClick={onClose} disabled={busy} className="secondary-button">关闭</button></div></Modal>; }
function Console({ lines }: { lines: ConsoleLine[] }) { return <pre className="workspace-scroll mt-4 max-h-80 overflow-auto rounded-xl bg-slate-950 p-3 font-mono text-[11px] leading-5 text-slate-100">{lines.length ? lines.map((item, index) => <span key={`${index}-${item.line}`} className={`block ${item.stream === "stderr" ? "text-amber-300" : "text-emerald-300"}`}>{item.line}</span>) : <span className="text-slate-400">等待服务端输出…</span>}</pre>; }
function FileEditor({ file, busy, onChange, onClose, onSave }: { file: FileDraft; busy: boolean; onChange: (value: FileDraft) => void; onClose: () => void; onSave: () => void }) { return <Modal title={`编辑配置：${file.path}`} onClose={onClose} wide><p className="text-xs text-slate-500">只允许编辑已在代码库配置中勾选的文本文件；保存会校验文件版本，避免覆盖服务器上的新修改。</p><textarea className="workspace-scroll mt-4 h-[55vh] w-full resize-none rounded-xl border border-slate-200 bg-slate-950 p-3 font-mono text-xs leading-5 text-slate-100 outline-none focus:border-blue-500" value={file.content} onChange={(event) => onChange({ ...file, content: event.target.value })}/><div className="mt-4 flex justify-end gap-2"><button onClick={onClose} disabled={busy} className="secondary-button">关闭</button><button onClick={onSave} disabled={busy} className="primary-button">{busy && <Loader2 size={14} className="animate-spin"/>}保存文件</button></div></Modal>; }
function Modal({ title, children, onClose, wide = false }: { title: string; children: ReactNode; onClose: () => void; wide?: boolean }) { return <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/45 p-4"><div className={`w-full ${wide ? "max-w-5xl" : "max-w-2xl"} rounded-2xl bg-white p-5 shadow-2xl`}><div className="flex items-center justify-between"><h3 className="text-base font-semibold text-slate-900">{title}</h3><button onClick={onClose} className="icon-button" aria-label="关闭"><X size={17}/></button></div>{children}</div></div>; }
function message(value: unknown) { return value instanceof Error ? value.message : "操作失败，请稍后重试。"; }
