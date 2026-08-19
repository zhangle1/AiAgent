"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { CheckSquare, ExternalLink, FileUp, Link2, Loader2, Plus, RefreshCw, Search, Send, Trash2, X } from "lucide-react";
import { getCodeProjects } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";
import { createProjectTask, deleteProjectTask, importProjectTasks, listGiteeIssues, listGiteeMembers, listGiteeProjects, listProjectTasks, updateProjectTaskStatus, type GiteeIssue, type GiteeMember, type GiteeProject, type ProjectTask, type TaskImportResult } from "@/lib/project-task-api";

export function TaskBoardPage() {
  const router = useRouter();
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [tasks, setTasks] = useState<ProjectTask[]>([]);
  const [projectId, setProjectId] = useState<number | "all">("all");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [linkedIssue, setLinkedIssue] = useState<GiteeIssue | null>(null);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [giteeProjects, setGiteeProjects] = useState<GiteeProject[]>([]);
  const [giteeProjectQuery, setGiteeProjectQuery] = useState("");
  const [giteeProjectPage, setGiteeProjectPage] = useState(1);
  const [giteeProjectsMore, setGiteeProjectsMore] = useState(false);
  const [remoteProject, setRemoteProject] = useState<GiteeProject | null>(null);
  const [issues, setIssues] = useState<GiteeIssue[]>([]);
  const [issuePage, setIssuePage] = useState(1);
  const [issuesMore, setIssuesMore] = useState(false);
  const [giteeMembers, setGiteeMembers] = useState<GiteeMember[]>([]);
  const [assignee, setAssignee] = useState("");
  const [issueState, setIssueState] = useState("open");
  const [issueQuery, setIssueQuery] = useState("");
  const [giteeLoading, setGiteeLoading] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [importFile, setImportFile] = useState<File | null>(null);
  const [importHeaders, setImportHeaders] = useState<string[]>([]);
  const [importPreview, setImportPreview] = useState<string[][]>([]);
  const [importMappings, setImportMappings] = useState<Record<string, string>>({});
  const [importSubmitting, setImportSubmitting] = useState(false);
  const [importResult, setImportResult] = useState<TaskImportResult | null>(null);
  const [movingTaskId, setMovingTaskId] = useState<number | null>(null);
  const [deletingTaskId, setDeletingTaskId] = useState<number | null>(null);

  const selectedProject = projects.find((project) => project.id === projectId) ?? null;
  const visible = useMemo(() => projectId === "all" ? tasks : tasks.filter((task) => task.project_id === projectId), [projectId, tasks]);

  const reload = async () => {
    setLoading(true);
    setError(null);
    const [projectResult, taskResult] = await Promise.allSettled([getCodeProjects(), listProjectTasks()]);
    const messages: string[] = [];
    if (projectResult.status === "fulfilled") setProjects(projectResult.value);
    else { setProjects([]); messages.push(`本地项目未加载：${requestMessage(projectResult.reason, "请检查项目权限或稍后重试。")}`); }
    if (taskResult.status === "fulfilled") setTasks(taskResult.value);
    else { setTasks([]); messages.push(`任务列表暂不可用：${requestMessage(taskResult.reason, "可先选择本地项目后新建或导入任务。")}`); }
    if (messages.length > 0) setError(messages.join("\n"));
    setLoading(false);
  };

  useEffect(() => { void reload(); }, []);

  const loadGiteeProjects = async (page = 1) => {
    setGiteeLoading(true); setError(null);
    try {
      const result = await listGiteeProjects(giteeProjectQuery, page);
      setGiteeProjects(result.items); setGiteeProjectPage(result.page); setGiteeProjectsMore(result.has_more);
    } catch (value) {
      setError(value instanceof Error ? value.message : "无法读取 Gitee 项目。");
    } finally { setGiteeLoading(false); }
  };

  const loadGiteeIssues = async (page = 1, project = remoteProject) => {
    if (!project) return;
    setGiteeLoading(true); setError(null);
    try {
      const result = await listGiteeIssues({ owner: project.owner, repository: project.name, assignee, state: issueState, query: issueQuery, page });
      setIssues(result.items); setIssuePage(result.page); setIssuesMore(result.has_more);
    } catch (value) {
      setError(value instanceof Error ? value.message : "无法读取 Gitee 任务。");
    } finally { setGiteeLoading(false); }
  };

  const openPicker = () => {
    setPickerOpen(true); setIssues([]); setRemoteProject(null);
    void loadGiteeProjects();
  };

  const chooseRemoteProject = (project: GiteeProject) => {
    setRemoteProject(project); setIssues([]); setGiteeMembers([]); setIssuePage(1);
    void listGiteeMembers(project.owner, project.name).then((result) => setGiteeMembers(result.items)).catch(() => setGiteeMembers([]));
    void loadGiteeIssues(1, project);
  };

  const chooseIssue = (issue: GiteeIssue) => {
    setLinkedIssue(issue);
    if (!title.trim() && issue.title) setTitle(issue.title);
    setPickerOpen(false);
  };

  const create = async () => {
    if (!selectedProject || !title.trim()) return;
    try {
      const task = await createProjectTask({ project_id: selectedProject.id, title, description, ...(linkedIssue ? { gitee_issue: linkedIssue } : {}) });
      setTasks((items) => [task, ...items]);
      setTitle(""); setDescription(""); setLinkedIssue(null); setCreating(false);
    } catch (value) {
      setError(value instanceof Error ? value.message : "创建任务失败。");
    }
  };

  const selectDefaultProject = () => {
    if (projectId === "all" && projects.length > 0) setProjectId(projects[0].id);
  };

  const openCreate = () => {
    if (projects.length === 0) { setError("当前账户没有可用的本地项目，请先在系统内创建或分配项目。"); return; }
    selectDefaultProject(); setCreating(true);
  };

  const openImport = () => {
    if (projects.length === 0) { setError("当前账户没有可用的本地项目，请先在系统内创建或分配项目。"); return; }
    selectDefaultProject();
    setImportFile(null); setImportHeaders([]); setImportPreview([]); setImportMappings({}); setImportResult(null); setImportOpen(true);
  };

  const chooseImportFile = async (file: File | null) => {
    setImportFile(file); setImportResult(null);
    if (!file) { setImportHeaders([]); setImportPreview([]); setImportMappings({}); return; }
    try {
      const preview = await readCsvPreview(file);
      if (preview.headers.length === 0) throw new Error("未读取到 CSV 表头。");
      setImportHeaders(preview.headers); setImportPreview(preview.rows); setImportMappings(createDefaultMappings(preview.headers));
    } catch (value) {
      setImportFile(null); setImportHeaders([]); setImportPreview([]); setImportMappings({});
      setError(value instanceof Error ? value.message : "无法读取 CSV 文件。");
    }
  };

  const submitImport = async () => {
    if (!selectedProject || !importFile || !importMappings.work_item_id) return;
    setImportSubmitting(true); setError(null);
    try {
      const result = await importProjectTasks({ projectId: selectedProject.id, file: importFile, mappings: importMappings });
      setImportResult(result);
      await reload();
    } catch (value) {
      setError(value instanceof Error ? value.message : "任务导入失败。");
    } finally { setImportSubmitting(false); }
  };

  const openTask = (task: ProjectTask) => {
    const content = `请处理以下任务，并先结合当前项目代码评估实施方案。\n\n任务：${task.title}${task.work_item_id ? `\n企业工作项 ID：${task.work_item_id}` : ""}${task.work_item_type ? `\n类型：${task.work_item_type}` : ""}${task.status ? `\n状态：${task.status}` : ""}${task.assignee ? `\n负责人：${task.assignee}` : ""}${task.description ? `\n\n任务详情：\n${task.description}` : ""}${task.external_id ? `\n\n已关联 Gitee Issue：${task.external_id}` : ""}${task.external_url ? `\n原始链接：${task.external_url}` : ""}`;
    const handoff = `${task.id}-${Date.now()}`;
    try {
      sessionStorage.setItem("aiagent:pending-template-turn", JSON.stringify({ handoff_id: handoff, project_id: task.project_id, content }));
      const query = new URLSearchParams({ template_handoff: handoff });
      if (task.project_id) query.set("project", String(task.project_id));
      router.push(`/chat?${query.toString()}`);
    } catch (value) {
      setError(`无法打开任务处理会话：${requestMessage(value, "请刷新页面后重试。")}`);
    }
  };

  const moveTask = async (task: ProjectTask, status: BoardStatus) => {
    if (toBoardStatus(task.status) === status || movingTaskId === task.id) return;
    const previous = tasks;
    setMovingTaskId(task.id);
    setTasks((items) => items.map((item) => item.id === task.id ? { ...item, status } : item));
    try {
      const updated = await updateProjectTaskStatus(task.id, status);
      setTasks((items) => items.map((item) => item.id === task.id ? updated : item));
    } catch (value) {
      setTasks(previous);
      setError(value instanceof Error ? value.message : "更新任务状态失败。");
    } finally { setMovingTaskId(null); }
  };

  const removeTask = async (task: ProjectTask) => {
    if (deletingTaskId === task.id || !window.confirm(`确认删除任务“${task.title}”？此操作会从我的任务中移除。`)) return;
    setDeletingTaskId(task.id);
    try {
      await deleteProjectTask(task.id);
      setTasks((items) => items.filter((item) => item.id !== task.id));
    } catch (value) {
      setError(value instanceof Error ? value.message : "删除任务失败。");
    } finally { setDeletingTaskId(null); }
  };

  return <main className="min-h-screen bg-slate-50 px-4 py-6 lg:px-8"><div className="mx-auto max-w-[1440px]">
    <header className="flex flex-wrap items-start justify-between gap-4"><div><p className="text-xs font-semibold tracking-[.16em] text-blue-600">TASK WORKBENCH</p><h1 className="mt-1 text-2xl font-semibold text-slate-950">我的任务</h1><p className="mt-2 text-sm text-slate-500">关联 Gitee Issue 或导入企业工作项 CSV；拖拽卡片即可在不同状态列表之间流转。</p></div><div className="flex gap-2"><button onClick={openImport} className="inline-flex h-10 items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 text-sm font-medium text-slate-700 hover:bg-slate-100"><FileUp size={16}/>导入工作项</button><button onClick={openCreate} className="inline-flex h-10 items-center gap-2 rounded-xl bg-blue-600 px-3 text-sm font-medium text-white hover:bg-blue-700"><Plus size={16}/>新建任务</button></div></header>
    <div className="mt-6 flex flex-wrap gap-2">{[{ id: "all" as const, name: "全部项目" }, ...projects.map((project) => ({ id: project.id, name: project.display_name }))].map((project) => <button key={project.id} onClick={() => setProjectId(project.id)} className={`rounded-full px-3 py-1.5 text-sm ${projectId === project.id ? "bg-slate-900 text-white" : "border border-slate-200 bg-white text-slate-600 hover:bg-slate-100"}`}>{project.name}</button>)}</div>
    {error && <div className="mt-4 flex flex-wrap items-start justify-between gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3"><div><p className="text-sm font-medium text-amber-900">部分数据暂时不可用</p><p className="mt-1 whitespace-pre-line text-xs leading-5 text-amber-800">{error}</p></div><button onClick={() => void reload()} className="inline-flex h-8 shrink-0 items-center gap-1.5 rounded-lg border border-amber-200 bg-white px-2.5 text-xs font-medium text-amber-800 hover:bg-amber-100"><RefreshCw size={13}/>重试</button></div>}
    {loading ? <div className="flex h-64 items-center justify-center text-slate-400"><Loader2 className="animate-spin"/></div> : <section className="mt-5 grid gap-4 xl:grid-cols-4">{boardColumns.map((column) => <TaskColumn key={column.status} column={column} tasks={visible.filter((task) => toBoardStatus(task.status) === column.status)} movingTaskId={movingTaskId} deletingTaskId={deletingTaskId} onMove={(taskId, status) => { const task = tasks.find((item) => item.id === taskId); if (task) void moveTask(task, status); }} onRemove={removeTask} onOpen={openTask}/>)}</section>}
    {creating && <TaskCreateDialog projects={projects} selectedProject={selectedProject} title={title} description={description} linkedIssue={linkedIssue} onProjectChange={(value) => setProjectId(value)} onTitleChange={setTitle} onDescriptionChange={setDescription} onOpenPicker={openPicker} onClearIssue={() => setLinkedIssue(null)} onClose={() => setCreating(false)} onCreate={() => void create()}/>}
    {importOpen && <TaskImportDialog projects={projects} selectedProject={selectedProject} file={importFile} headers={importHeaders} preview={importPreview} mappings={importMappings} submitting={importSubmitting} result={importResult} onProjectChange={(value) => setProjectId(value)} onFileChange={(file) => void chooseImportFile(file)} onMappingChange={(target, source) => setImportMappings((current) => ({ ...current, [target]: source }))} onClose={() => setImportOpen(false)} onSubmit={() => void submitImport()}/>}
    {pickerOpen && <GiteeIssuePicker projects={giteeProjects} projectQuery={giteeProjectQuery} projectPage={giteeProjectPage} projectsMore={giteeProjectsMore} remoteProject={remoteProject} issues={issues} issuePage={issuePage} issuesMore={issuesMore} issueState={issueState} issueQuery={issueQuery} assignee={assignee} members={giteeMembers} loading={giteeLoading} onProjectQueryChange={setGiteeProjectQuery} onSearchProjects={() => void loadGiteeProjects(1)} onPreviousProjects={() => void loadGiteeProjects(giteeProjectPage - 1)} onNextProjects={() => void loadGiteeProjects(giteeProjectPage + 1)} onChooseProject={chooseRemoteProject} onAssigneeChange={setAssignee} onStateChange={setIssueState} onIssueQueryChange={setIssueQuery} onSearchIssues={() => void loadGiteeIssues(1)} onPreviousIssues={() => void loadGiteeIssues(issuePage - 1)} onNextIssues={() => void loadGiteeIssues(issuePage + 1)} onChooseIssue={chooseIssue} onClose={() => setPickerOpen(false)}/>}
  </div></main>;
}

type BoardStatus = "todo" | "in_progress" | "in_review" | "done";
const boardColumns: { status: BoardStatus; title: string; hint: string; tone: string }[] = [
  { status: "todo", title: "待办", hint: "TODO", tone: "border-slate-200" },
  { status: "in_progress", title: "进行中", hint: "IN PROGRESS", tone: "border-blue-200" },
  { status: "in_review", title: "待验收", hint: "IN REVIEW", tone: "border-violet-200" },
  { status: "done", title: "已完成", hint: "DONE", tone: "border-emerald-200" },
];

function toBoardStatus(value?: string | null): BoardStatus {
  const status = value?.trim().toLowerCase();
  if (["done", "closed", "completed", "已完成", "完成", "关闭"].includes(status ?? "")) return "done";
  if (["in_review", "review", "待测试", "待验收", "待提单人自测"].includes(status ?? "")) return "in_review";
  if (["in_progress", "progress", "doing", "处理中", "进行中"].includes(status ?? "")) return "in_progress";
  return "todo";
}

function TaskColumn({ column, tasks, movingTaskId, deletingTaskId, onMove, onRemove, onOpen }: { column: typeof boardColumns[number]; tasks: ProjectTask[]; movingTaskId: number | null; deletingTaskId: number | null; onMove: (taskId: number, status: BoardStatus) => void; onRemove: (task: ProjectTask) => void; onOpen: (task: ProjectTask) => void }) {
  const [dragOver, setDragOver] = useState(false);
  return <section onDragOver={(event) => { event.preventDefault(); setDragOver(true); }} onDragLeave={() => setDragOver(false)} onDrop={(event) => { event.preventDefault(); setDragOver(false); const taskId = Number(event.dataTransfer.getData("text/project-task-id")); if (Number.isSafeInteger(taskId) && taskId > 0) onMove(taskId, column.status); }} className={`min-h-[460px] rounded-2xl border bg-slate-100/70 p-3 transition ${column.tone} ${dragOver ? "ring-2 ring-blue-400 ring-offset-2" : ""}`}><header className="flex items-start justify-between gap-2 px-1 pb-3"><div><p className="text-sm font-semibold text-slate-800">{column.title}</p><p className="mt-0.5 text-[10px] font-semibold tracking-[.12em] text-slate-400">{column.hint}</p></div><span className="rounded-full bg-white px-2 py-0.5 text-xs font-medium text-slate-500 shadow-sm">{tasks.length}</span></header><div className="space-y-3">{tasks.map((task) => <TaskCard key={task.id} task={task} moving={movingTaskId === task.id} deleting={deletingTaskId === task.id} onOpen={() => onOpen(task)} onRemove={() => onRemove(task)} />)}{tasks.length === 0 && <div className="grid h-28 place-items-center rounded-xl border border-dashed border-slate-300 bg-white/60 text-xs text-slate-400">拖到这里</div>}</div></section>;
}

function TaskCard({ task, moving, deleting, onOpen, onRemove }: { task: ProjectTask; moving: boolean; deleting: boolean; onOpen: () => void; onRemove: () => void }) {
  return <article draggable={!moving && !deleting} onDragStart={(event) => { event.dataTransfer.effectAllowed = "move"; event.dataTransfer.setData("text/project-task-id", String(task.id)); }} className={`cursor-grab rounded-xl border border-slate-200 bg-white p-3 shadow-sm transition hover:border-blue-300 hover:shadow active:cursor-grabbing ${moving || deleting ? "opacity-50" : ""}`}><div className="flex items-start gap-3"><span className="mt-0.5 grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-blue-50 text-blue-600"><CheckSquare size={17}/></span><div className="min-w-0 flex-1"><div className="flex items-center justify-between gap-2"><span className="rounded-full bg-blue-50 px-2 py-0.5 text-[11px] text-blue-600">{task.source === "gitee_enterprise_csv" ? "导入工作项" : "本地任务"}</span><span className="truncate text-[11px] text-slate-400">{task.project_name ?? "未归属项目"}</span></div><h2 className="mt-2 line-clamp-2 font-semibold text-slate-900">{task.title}</h2><p className="mt-2 line-clamp-3 text-sm leading-5 text-slate-500">{task.description || "未提供任务说明"}</p><div className="mt-3 flex flex-wrap gap-1.5 text-xs">{task.work_item_id && <span className="rounded bg-slate-100 px-1.5 py-0.5 text-slate-600">ID {task.work_item_id}</span>}{task.work_item_type && <span className="rounded bg-violet-50 px-1.5 py-0.5 text-violet-700">{task.work_item_type}</span>}{task.priority && <span className="rounded bg-amber-50 px-1.5 py-0.5 text-amber-700">{task.priority}</span>}{task.assignee && <span className="rounded bg-emerald-50 px-1.5 py-0.5 text-emerald-700">负责人 {task.assignee}</span>}</div>{task.external_id && task.source !== "gitee_enterprise_csv" && <p className="mt-3 flex items-center gap-1 text-xs text-orange-600"><Link2 size={13}/>已关联 Gitee Issue</p>}<div className="mt-4 flex items-center justify-between gap-2"><div className="flex gap-1">{task.external_url && <a href={task.external_url} target="_blank" rel="noreferrer" draggable={false} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="打开 Gitee"><ExternalLink size={15}/></a>}<button onClick={onRemove} disabled={deleting} draggable={false} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-red-50 hover:text-red-600 disabled:opacity-50" aria-label="删除任务"><Trash2 size={15}/></button></div><button onClick={onOpen} draggable={false} className="inline-flex h-8 items-center gap-1 rounded-lg bg-blue-600 px-2.5 text-xs font-medium text-white hover:bg-blue-700"><Send size={13}/>处理</button></div></div></div></article>;
}

function TaskCreateDialog({ projects, selectedProject, title, description, linkedIssue, onProjectChange, onTitleChange, onDescriptionChange, onOpenPicker, onClearIssue, onClose, onCreate }: { projects: CodeProject[]; selectedProject: CodeProject | null; title: string; description: string; linkedIssue: GiteeIssue | null; onProjectChange: (value: number) => void; onTitleChange: (value: string) => void; onDescriptionChange: (value: string) => void; onOpenPicker: () => void; onClearIssue: () => void; onClose: () => void; onCreate: () => void }) {
  return <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/35 p-4"><div className="w-full max-w-lg rounded-2xl bg-white p-5 shadow-2xl"><div className="flex justify-between"><div><h2 className="font-semibold text-slate-900">新建任务</h2><p className="mt-1 text-xs text-slate-500">先创建 AiAgent 本地任务，可选关联一条 Gitee Issue。</p></div><button onClick={onClose} className="text-slate-400 hover:text-slate-700" aria-label="关闭"><X/></button></div><label className="mt-5 block text-sm text-slate-700">AiAgent 项目<select value={selectedProject?.id ?? ""} onChange={(event) => onProjectChange(Number(event.target.value))} className="mt-1.5 h-10 w-full rounded-lg border border-slate-200 px-3"><option value="">请选择项目</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.display_name}</option>)}</select></label><label className="mt-3 block text-sm text-slate-700">标题<input value={title} onChange={(event) => onTitleChange(event.target.value)} className="mt-1.5 h-10 w-full rounded-lg border border-slate-200 px-3" autoFocus/></label><label className="mt-3 block text-sm text-slate-700">说明<textarea value={description} onChange={(event) => onDescriptionChange(event.target.value)} className="mt-1.5 min-h-28 w-full rounded-lg border border-slate-200 p-3"/></label><div className="mt-4 rounded-xl border border-slate-200 bg-slate-50 p-3"><div className="flex items-center justify-between gap-3"><div><p className="text-sm font-medium text-slate-700">关联 Gitee Issue</p><p className="mt-0.5 text-xs text-slate-500">不做全量同步，仅关联你选中的一条任务。</p></div><button type="button" onClick={onOpenPicker} className="inline-flex h-8 shrink-0 items-center gap-1 rounded-lg border border-slate-200 bg-white px-2.5 text-xs text-slate-700 hover:bg-slate-100"><Search size={13}/>选择</button></div>{linkedIssue && <div className="mt-3 flex items-start justify-between gap-2 rounded-lg border border-orange-100 bg-white px-3 py-2"><span className="min-w-0 text-xs text-slate-700">Gitee #{linkedIssue.number ?? linkedIssue.id} · {linkedIssue.title}</span><button type="button" onClick={onClearIssue} className="text-xs text-slate-400 hover:text-slate-700">解除</button></div>}</div><div className="mt-5 flex justify-end gap-2"><button onClick={onClose} className="h-9 rounded-lg px-3 text-sm text-slate-600 hover:bg-slate-100">取消</button><button onClick={onCreate} disabled={!selectedProject || !title.trim()} className="h-9 rounded-lg bg-blue-600 px-3 text-sm text-white disabled:bg-slate-200">创建并保存</button></div></div></div>;
}

const importFields = [
  { key: "work_item_id", label: "工作项 ID", required: true, aliases: ["工作项 ID", "工作项 ID 编号", "工作项id", "id"] },
  { key: "work_item_type", label: "工作项类型", required: false, aliases: ["工作项类型", "类型"] },
  { key: "title", label: "标题", required: false, aliases: ["标题", "名称"] },
  { key: "description", label: "描述", required: false, aliases: ["描述", "详情"] },
  { key: "status", label: "状态", required: false, aliases: ["状态"] },
  { key: "creator", label: "创建人", required: false, aliases: ["创建人"] },
  { key: "assignee", label: "负责人", required: false, aliases: ["负责人", "处理人"] },
  { key: "collaborators", label: "协作者", required: false, aliases: ["协作者"] },
  { key: "priority", label: "优先级", required: false, aliases: ["优先级"] },
  { key: "labels", label: "标签", required: false, aliases: ["标签"] },
  { key: "created_at", label: "创建时间", required: false, aliases: ["创建时间"] },
  { key: "updated_at", label: "更新时间", required: false, aliases: ["更新时间", "更新日期"] },
] as const;

function TaskImportDialog({ projects, selectedProject, file, headers, preview, mappings, submitting, result, onProjectChange, onFileChange, onMappingChange, onClose, onSubmit }: { projects: CodeProject[]; selectedProject: CodeProject | null; file: File | null; headers: string[]; preview: string[][]; mappings: Record<string, string>; submitting: boolean; result: TaskImportResult | null; onProjectChange: (value: number) => void; onFileChange: (file: File | null) => void; onMappingChange: (target: string, source: string) => void; onClose: () => void; onSubmit: () => void }) {
  const canSubmit = Boolean(selectedProject && file && mappings.work_item_id && !submitting);
  return <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/35 p-4"><div className="flex max-h-[min(800px,calc(100dvh-32px))] w-full max-w-5xl flex-col rounded-2xl bg-white shadow-2xl"><div className="flex items-start justify-between border-b border-slate-100 p-5"><div><h2 className="font-semibold text-slate-900">导入企业工作项</h2><p className="mt-1 text-xs text-slate-500">先选 AiAgent 本地 Git 项目，再上传 CSV 并确认字段映射。工作项 ID 用于插入或更新识别。</p></div><button onClick={onClose} className="text-slate-400 hover:text-slate-700" aria-label="关闭"><X/></button></div><div className="min-h-0 flex-1 overflow-y-auto p-5"><div className="grid gap-4 md:grid-cols-2"><label className="block text-sm text-slate-700">1. AiAgent 本地 Git 项目<select value={selectedProject?.id ?? ""} onChange={(event) => onProjectChange(Number(event.target.value))} className="mt-1.5 h-10 w-full rounded-lg border border-slate-200 px-3"><option value="">请选择项目</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.display_name}</option>)}</select></label><label className="block text-sm text-slate-700">2. 企业工作项 CSV<input type="file" accept=".csv,text/csv" disabled={!selectedProject} onChange={(event) => onFileChange(event.target.files?.[0] ?? null)} className="mt-1.5 block w-full rounded-lg border border-slate-200 p-2 text-sm file:mr-3 file:rounded-md file:border-0 file:bg-blue-50 file:px-2 file:py-1 file:text-blue-700 disabled:cursor-not-allowed disabled:opacity-50"/>{!selectedProject && <p className="mt-1 text-xs text-amber-700">请先选择本地 Git 项目。</p>}{file && <p className="mt-1 text-xs text-slate-500">{file.name} · {(file.size / 1024).toFixed(1)} KB</p>}</label></div>{headers.length > 0 && <><div className="mt-6 flex items-center justify-between"><div><h3 className="font-medium text-slate-900">3. 字段映射</h3><p className="mt-1 text-xs text-slate-500">空映射不会覆盖本地字段；CSV 中空值也不会清空已有任务信息。</p></div><span className="text-xs text-slate-400">已识别 {headers.length} 列</span></div><div className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{importFields.map((field) => <label key={field.key} className="text-xs text-slate-600"><span>{field.label}{field.required && <span className="text-red-500"> *</span>}</span><select value={mappings[field.key] ?? ""} onChange={(event) => onMappingChange(field.key, event.target.value)} className="mt-1 block h-9 w-full rounded-lg border border-slate-200 bg-white px-2 text-sm text-slate-700"><option value="">不导入</option>{headers.map((header) => <option key={header} value={header}>{header}</option>)}</select></label>)}</div><div className="mt-6 overflow-x-auto rounded-xl border border-slate-200"><table className="min-w-full text-left text-xs"><thead className="bg-slate-50 text-slate-500"><tr>{headers.map((header) => <th key={header} className="whitespace-nowrap px-3 py-2 font-medium">{header}</th>)}</tr></thead><tbody>{preview.map((row, rowIndex) => <tr key={rowIndex} className="border-t border-slate-100">{headers.map((_, index) => <td key={index} className="max-w-48 truncate px-3 py-2 text-slate-600">{row[index] || "—"}</td>)}</tr>)}</tbody></table></div></>}{result && <div className="mt-5 rounded-xl border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-800">导入完成：读取 {result.total_rows} 行，新增 {result.inserted} 条，更新 {result.updated} 条，跳过 {result.skipped} 条。{result.warnings.length > 0 && <ul className="mt-2 list-disc pl-5 text-xs">{result.warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul>}</div>}</div><div className="flex justify-end gap-2 border-t border-slate-100 p-4"><button onClick={onClose} className="h-9 rounded-lg px-3 text-sm text-slate-600 hover:bg-slate-100">{result ? "关闭" : "取消"}</button><button onClick={onSubmit} disabled={!canSubmit} className="inline-flex h-9 items-center gap-2 rounded-lg bg-blue-600 px-3 text-sm text-white disabled:bg-slate-200">{submitting && <Loader2 size={14} className="animate-spin"/>}开始导入</button></div></div></div>;
}

function createDefaultMappings(headers: string[]) {
  const normalize = (value: string) => value.replace(/[\s_－-]/g, "").toLowerCase();
  return Object.fromEntries(importFields.map((field) => [field.key, headers.find((header) => field.aliases.some((alias) => normalize(alias) === normalize(header))) ?? ""]));
}

async function readCsvPreview(file: File): Promise<{ headers: string[]; rows: string[][] }> {
  const text = await file.slice(0, 512 * 1024).text();
  const records = parseCsvRecords(text, 4);
  const headers = (records.shift() ?? []).map((header) => header.replace(/^\uFEFF/, "").trim());
  return { headers, rows: records };
}

function parseCsvRecords(text: string, maxRows: number) {
  const records: string[][] = []; let row: string[] = []; let field = ""; let quoted = false;
  const finish = () => { row.push(field); records.push(row); row = []; field = ""; };
  for (let index = 0; index < text.length && records.length < maxRows; index++) {
    const char = text[index];
    if (quoted) { if (char === '"' && text[index + 1] === '"') { field += '"'; index++; } else if (char === '"') quoted = false; else field += char; continue; }
    if (char === '"' && field.length === 0) { quoted = true; continue; }
    if (char === ',') { row.push(field); field = ""; continue; }
    if (char === '\n' || char === '\r') { if (char === '\r' && text[index + 1] === '\n') index++; finish(); continue; }
    field += char;
  }
  return records;
}

function requestMessage(value: unknown, fallback: string) {
  return value instanceof Error && value.message.trim() ? value.message : fallback;
}

function GiteeIssuePicker({ projects, projectQuery, projectPage, projectsMore, remoteProject, issues, issuePage, issuesMore, issueState, issueQuery, assignee, members, loading, onProjectQueryChange, onSearchProjects, onPreviousProjects, onNextProjects, onChooseProject, onAssigneeChange, onStateChange, onIssueQueryChange, onSearchIssues, onPreviousIssues, onNextIssues, onChooseIssue, onClose }: { projects: GiteeProject[]; projectQuery: string; projectPage: number; projectsMore: boolean; remoteProject: GiteeProject | null; issues: GiteeIssue[]; issuePage: number; issuesMore: boolean; issueState: string; issueQuery: string; assignee: string; members: GiteeMember[]; loading: boolean; onProjectQueryChange: (value: string) => void; onSearchProjects: () => void; onPreviousProjects: () => void; onNextProjects: () => void; onChooseProject: (project: GiteeProject) => void; onAssigneeChange: (value: string) => void; onStateChange: (value: string) => void; onIssueQueryChange: (value: string) => void; onSearchIssues: () => void; onPreviousIssues: () => void; onNextIssues: () => void; onChooseIssue: (issue: GiteeIssue) => void; onClose: () => void }) {
  return <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-950/35 p-4"><div className="flex max-h-[min(760px,calc(100dvh-32px))] w-full max-w-4xl flex-col rounded-2xl bg-white shadow-2xl"><div className="flex items-center justify-between border-b border-slate-100 p-5"><div><h2 className="font-semibold text-slate-900">关联 Gitee Issue</h2><p className="mt-1 text-xs text-slate-500">按项目、人员和条件分页浏览；仅点击关联时才会保存。</p></div><button onClick={onClose} className="text-slate-400 hover:text-slate-700" aria-label="关闭"><X/></button></div><div className="grid min-h-0 flex-1 gap-4 overflow-y-auto p-5 md:grid-cols-[280px_minmax(0,1fr)]"><section className="rounded-xl border border-slate-200 p-3"><p className="text-xs font-semibold text-slate-700">1. Gitee 项目</p><div className="mt-2 flex gap-2"><input value={projectQuery} onChange={(event) => onProjectQueryChange(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") onSearchProjects(); }} placeholder="搜索项目" className="h-9 min-w-0 flex-1 rounded-lg border border-slate-200 px-2 text-xs"/><button onClick={onSearchProjects} className="grid h-9 w-9 place-items-center rounded-lg border border-slate-200 text-slate-600"><Search size={14}/></button></div><div className="mt-3 space-y-1">{projects.map((project) => <button key={`${project.owner}/${project.name}`} type="button" onClick={() => onChooseProject(project)} className={`w-full rounded-lg px-2.5 py-2 text-left text-xs ${remoteProject?.owner === project.owner && remoteProject.name === project.name ? "bg-blue-50 text-blue-700" : "hover:bg-slate-50 text-slate-700"}`}><span className="block truncate font-medium">{project.display_name}</span><span className="block truncate pt-0.5 text-slate-400">{project.owner}/{project.name}</span></button>)}{!loading && projects.length === 0 && <p className="py-4 text-center text-xs text-slate-400">没有可用项目</p>}</div><Pager page={projectPage} hasMore={projectsMore} disabled={loading} onPrevious={onPreviousProjects} onNext={onNextProjects}/></section><section className="min-w-0"><div className="flex flex-wrap items-end gap-2"><label className="min-w-32 flex-1 text-xs text-slate-500">人员<select value={assignee} onChange={(event) => onAssigneeChange(event.target.value)} className="mt-1 block h-9 w-full rounded-lg border border-slate-200 px-2 text-sm text-slate-700"><option value="">全部人员</option>{members.map((member) => <option key={member.login} value={member.login}>{member.display_name || member.login}</option>)}</select></label><label className="w-24 text-xs text-slate-500">状态<select value={issueState} onChange={(event) => onStateChange(event.target.value)} className="mt-1 h-9 w-full rounded-lg border border-slate-200 px-2 text-sm text-slate-700"><option value="open">Open</option><option value="closed">Closed</option><option value="all">全部</option></select></label><label className="min-w-32 flex-1 text-xs text-slate-500">关键词<input value={issueQuery} onChange={(event) => onIssueQueryChange(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") onSearchIssues(); }} placeholder="标题或编号" className="mt-1 block h-9 w-full rounded-lg border border-slate-200 px-2 text-sm text-slate-700"/></label><button onClick={onSearchIssues} disabled={!remoteProject || loading} className="h-9 rounded-lg bg-blue-600 px-3 text-xs text-white disabled:bg-slate-200">查询</button></div>{!remoteProject ? <div className="mt-4 grid min-h-56 place-items-center rounded-xl border border-dashed border-slate-200 text-sm text-slate-400">先从左侧选择一个 Gitee 项目</div> : <div className="mt-4 space-y-2">{loading && <div className="flex h-32 items-center justify-center text-slate-400"><Loader2 className="animate-spin"/></div>}{!loading && issues.map((issue) => <article key={issue.id} className="rounded-xl border border-slate-200 p-3"><div className="flex items-start justify-between gap-3"><div className="min-w-0"><p className="truncate text-sm font-medium text-slate-800">#{issue.number ?? issue.id} · {issue.title}</p><p className="mt-1 text-xs text-slate-500">{issue.state ?? "open"}{issue.assignee ? ` · ${issue.assignee}` : ""}</p></div><button type="button" onClick={() => onChooseIssue(issue)} className="shrink-0 rounded-lg border border-blue-200 px-2.5 py-1.5 text-xs text-blue-700 hover:bg-blue-50">关联</button></div></article>)}{!loading && issues.length === 0 && <div className="grid h-32 place-items-center rounded-xl border border-dashed border-slate-200 text-sm text-slate-400">没有符合条件的 Gitee Issue</div>}<Pager page={issuePage} hasMore={issuesMore} disabled={loading} onPrevious={onPreviousIssues} onNext={onNextIssues}/></div>}</section></div></div></div>;
}

function Pager({ page, hasMore, disabled, onPrevious, onNext }: { page: number; hasMore: boolean; disabled: boolean; onPrevious: () => void; onNext: () => void }) {
  return <div className="mt-3 flex items-center justify-end gap-2 text-xs text-slate-500"><button type="button" disabled={disabled || page <= 1} onClick={onPrevious} className="rounded-md border border-slate-200 px-2 py-1 disabled:opacity-40">上一页</button><span>第 {page} 页</span><button type="button" disabled={disabled || !hasMore} onClick={onNext} className="rounded-md border border-slate-200 px-2 py-1 disabled:opacity-40">下一页</button></div>;
}
