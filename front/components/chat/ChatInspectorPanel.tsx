"use client";

import { type PointerEvent as ReactPointerEvent, type RefObject, useEffect, useMemo, useRef, useState } from "react";
import { ArrowRight, ChevronDown, ChevronRight, Download, FileText, Folder, FolderOpen, FolderPlus, Globe2, Code2, FileCode2, ListTodo, Loader2, PanelRightClose, Plus, RefreshCw, Terminal, Trash2, X } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { getCodeProjectRuntime, getCodeRuntimeLogs } from "@/lib/code-runtime-api";
import { createProjectMarkdownDirectory, deleteProjectMarkdownDocument, getCodeFile, getProjectMarkdownDirectories, getProjectMarkdownDocuments, projectMarkdownDocumentDownloadUrl, readProjectMarkdownDocument, uploadProjectMarkdownDocument } from "@/lib/code-repository-api";
import type { CodeProject, CodeProjectMarkdownDirectory, CodeProjectMarkdownDocument, CodeProjectMarkdownDocumentContent } from "@/lib/code-repository-types";
import type { CodeProjectRuntime, CodeRuntimeLog } from "@/lib/code-runtime-types";

export type ChatCodeFileReference = {
  repositoryName: string;
  filePath: string;
  line?: number;
};
type WorkspaceTab = "preview" | "file" | "documents" | "tasks" | "terminal";

function terminalPanelWidth(viewportWidth: number) {
  const minimum = Math.min(360, Math.max(280, Math.round(viewportWidth * 0.45)));
  const maximum = Math.max(minimum, viewportWidth - 320);
  return Math.max(minimum, Math.min(maximum, Math.round(viewportWidth / 2)));
}

export function ChatInspectorPanel({ isOpen, project, fileReference, requestedTab, requestedMarkdownDocument, refreshToken, onInsertMarkdownReference, onPrepareAgentMarkdown, onClose }: { isOpen: boolean; project: CodeProject | null; fileReference: ChatCodeFileReference | null; requestedTab?: WorkspaceTab | null; requestedMarkdownDocument?: CodeProjectMarkdownDocument | null; refreshToken?: number; onInsertMarkdownReference?: (document: CodeProjectMarkdownDocument) => void; onPrepareAgentMarkdown?: (prompt: string) => void; onClose: () => void }) {
  const [tabs, setTabs] = useState<WorkspaceTab[]>([]);
  const [activeTab, setActiveTab] = useState<WorkspaceTab | null>(null);
  const [addMenuOpen, setAddMenuOpen] = useState(false);
  const [runtime, setRuntime] = useState<CodeProjectRuntime | null>(null);
  const [browserAddress, setBrowserAddress] = useState("");
  const [browserUrl, setBrowserUrl] = useState("");
  const [browserRevision, setBrowserRevision] = useState(0);
  const [file, setFile] = useState<{ path: string; content: string; line_count: number } | null>(null);
  const [loadingFile, setLoadingFile] = useState(false);
  const [markdownDocuments, setMarkdownDocuments] = useState<CodeProjectMarkdownDocument[]>([]);
  const [markdownDirectories, setMarkdownDirectories] = useState<CodeProjectMarkdownDirectory[]>([]);
  const [selectedMarkdownDirectory, setSelectedMarkdownDirectory] = useState<CodeProjectMarkdownDirectory>({ repository_name: "aiagent-uploads", path: "" });
  const [loadingMarkdownDocuments, setLoadingMarkdownDocuments] = useState(false);
  const [selectedMarkdownDocument, setSelectedMarkdownDocument] = useState<CodeProjectMarkdownDocument | null>(null);
  const [markdownDocumentContent, setMarkdownDocumentContent] = useState<CodeProjectMarkdownDocumentContent | null>(null);
  const [loadingMarkdownDocument, setLoadingMarkdownDocument] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [runtimeLogs, setRuntimeLogs] = useState<Record<string, CodeRuntimeLog[]>>({});
  const [panelWidth, setPanelWidth] = useState(380);
  const [resizing, setResizing] = useState(false);
  const runtimeSequences = useRef<Record<string, number>>({});
  const resizeStart = useRef<{ x: number; width: number } | null>(null);
  const addMenuRef = useRef<HTMLDivElement | null>(null);
  const markdownUploadInputRef = useRef<HTMLInputElement | null>(null);

  const previewRuns = useMemo(() => runtime?.runs.filter((run) => run.role === "frontend" && (run.status === "starting" || run.status === "running")) ?? [], [runtime]);

  useEffect(() => {
    if (!requestedTab) return;
    setTabs((current) => current.includes(requestedTab) ? current : [...current, requestedTab]);
    setActiveTab(requestedTab);
    if (requestedTab === "terminal") setPanelWidth(terminalPanelWidth(window.innerWidth));
  }, [requestedTab]);

  useEffect(() => {
    if (!requestedMarkdownDocument) return;
    setTabs((current) => current.includes("documents") ? current : [...current, "documents"]);
    setActiveTab("documents");
    setSelectedMarkdownDocument(requestedMarkdownDocument);
  }, [requestedMarkdownDocument]);

  useEffect(() => {
    if (!addMenuOpen) return;
    const closeWhenOutside = (event: PointerEvent) => {
      if (addMenuRef.current && !addMenuRef.current.contains(event.target as Node)) setAddMenuOpen(false);
    };
    document.addEventListener("pointerdown", closeWhenOutside);
    return () => document.removeEventListener("pointerdown", closeWhenOutside);
  }, [addMenuOpen]);

  useEffect(() => {
    if (!resizing) return;
    const resize = (event: PointerEvent) => {
      const start = resizeStart.current;
      if (!start) return;
      const minimum = Math.min(360, Math.max(280, Math.round(window.innerWidth * 0.45)));
      const maximum = Math.max(minimum, window.innerWidth - 320);
      setPanelWidth(Math.max(minimum, Math.min(maximum, start.width + start.x - event.clientX)));
    };
    const stopResize = () => { resizeStart.current = null; setResizing(false); };
    window.addEventListener("pointermove", resize);
    window.addEventListener("pointerup", stopResize);
    return () => { window.removeEventListener("pointermove", resize); window.removeEventListener("pointerup", stopResize); };
  }, [resizing]);

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
    setTabs((current) => current.includes("file") ? current : [...current, "file"]);
    setActiveTab("file");
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

  useEffect(() => {
    if (!isOpen || !project || (activeTab !== "documents" && requestedTab !== "documents")) return;
    let disposed = false;
    setLoadingMarkdownDocuments(true);
    setError(null);
    void Promise.all([getProjectMarkdownDocuments(project.id), getProjectMarkdownDirectories(project.id)])
      .then(([items, directories]) => {
        if (disposed) return;
        setMarkdownDocuments(items);
        setMarkdownDirectories(directories);
        setSelectedMarkdownDirectory((current) => directories.find((item) => item.repository_name === current.repository_name && item.path === current.path) ?? directories[0] ?? { repository_name: "aiagent-uploads", path: "" });
        setSelectedMarkdownDocument((current) => {
          const target = requestedMarkdownDocument ?? current;
          return target && items.some((item) => item.repository_name === target.repository_name && item.path === target.path) ? target : items[0] ?? null;
        });
      })
      .catch((ex) => { if (!disposed) setError(ex instanceof Error ? ex.message : "无法读取项目 Markdown 文档。"); })
      .finally(() => { if (!disposed) setLoadingMarkdownDocuments(false); });
    return () => { disposed = true; };
  }, [isOpen, project, activeTab, requestedTab, requestedMarkdownDocument, refreshToken]);

  useEffect(() => {
    if (!project || !selectedMarkdownDocument) { setMarkdownDocumentContent(null); return; }
    let disposed = false;
    setLoadingMarkdownDocument(true);
    setError(null);
    void readProjectMarkdownDocument(project.id, selectedMarkdownDocument.repository_name, selectedMarkdownDocument.path)
      .then((value) => { if (!disposed) setMarkdownDocumentContent(value); })
      .catch((ex) => { if (!disposed) setError(ex instanceof Error ? ex.message : "无法读取 Markdown 文档。"); })
      .finally(() => { if (!disposed) setLoadingMarkdownDocument(false); });
    return () => { disposed = true; };
  }, [project, selectedMarkdownDocument]);

  useEffect(() => {
    setBrowserAddress("");
    setBrowserUrl("");
    setMarkdownDocuments([]);
    setMarkdownDirectories([]);
    setSelectedMarkdownDirectory({ repository_name: "aiagent-uploads", path: "" });
    setSelectedMarkdownDocument(null);
    setMarkdownDocumentContent(null);
  }, [project?.id]);

  useEffect(() => {
    if (browserAddress || previewRuns.length === 0) return;
    const run = previewRuns[0];
    const suggestedUrl = run.access_urls?.find((url) => !url.includes("127.0.0.1")) ?? run.access_urls?.[0] ?? run.preview_url ?? "";
    setBrowserAddress(suggestedUrl);
  }, [browserAddress, previewRuns]);

  function openTab(nextTab: WorkspaceTab) {
    setTabs((current) => current.includes(nextTab) ? current : [...current, nextTab]);
    setActiveTab(nextTab);
    setAddMenuOpen(false);
    if (nextTab === "terminal" && typeof window !== "undefined") {
      setPanelWidth(terminalPanelWidth(window.innerWidth));
    }
  }

  function closeTab(nextTab: WorkspaceTab) {
    const index = tabs.indexOf(nextTab);
    const nextTabs = tabs.filter((item) => item !== nextTab);
    setTabs(nextTabs);
    if (activeTab === nextTab) setActiveTab(nextTabs[index] ?? nextTabs[index - 1] ?? null);
  }

  function startResize(event: ReactPointerEvent<HTMLDivElement>) {
    event.preventDefault();
    resizeStart.current = { x: event.clientX, width: panelWidth };
    setResizing(true);
  }

  function loadBrowser() {
    const rawAddress = browserAddress.trim();
    if (!rawAddress) return;
    const normalizedUrl = /^https?:\/\//i.test(rawAddress) ? rawAddress : `http://${rawAddress}`;
    setBrowserAddress(normalizedUrl);
    setBrowserUrl(normalizedUrl);
    setBrowserRevision((value) => value + 1);
  }

  async function refreshMarkdownDocuments(selectDocument?: CodeProjectMarkdownDocument, selectDirectory?: CodeProjectMarkdownDirectory) {
    if (!project) return;
    setLoadingMarkdownDocuments(true);
    try {
      const [items, directories] = await Promise.all([getProjectMarkdownDocuments(project.id), getProjectMarkdownDirectories(project.id)]);
      setMarkdownDocuments(items);
      setMarkdownDirectories(directories);
      setSelectedMarkdownDocument((current) => items.find((item) => item.repository_name === current?.repository_name && item.path === current?.path)
        ?? (selectDocument ? items.find((item) => item.repository_name === selectDocument.repository_name && item.path === selectDocument.path) : null)
        ?? items[0]
        ?? null);
      setSelectedMarkdownDirectory((current) => directories.find((item) => item.repository_name === (selectDirectory ?? current).repository_name && item.path === (selectDirectory ?? current).path) ?? directories[0] ?? { repository_name: "aiagent-uploads", path: "" });
    } finally {
      setLoadingMarkdownDocuments(false);
    }
  }

  async function uploadMarkdownDocument(file: File) {
    if (!project) return;
    setError(null);
    try {
      const document = await uploadProjectMarkdownDocument(project.id, selectedMarkdownDirectory.repository_name, selectedMarkdownDirectory.path, file);
      await refreshMarkdownDocuments(document, selectedMarkdownDirectory);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "无法上传 Markdown 文档。");
    }
  }

  function prepareAgentMarkdown() {
    if (!project || !onPrepareAgentMarkdown) return;
    const repositories = project.repositories.map((repository) => `- ${repository.display_name}（repository_name: ${repository.name}）`).join("\n");
    onPrepareAgentMarkdown(`请为当前项目生成可长期维护的 Agent 文档，不要只给出目录扫描摘要。\n\n先使用代码检索、项目概览和按需文件读取理解每个已选代码库的真实入口、模块边界、业务术语、运行/构建方式与跨仓库协作关系。随后必须调用 write_dashboard_file，在下列每个已选代码库的根目录创建或更新 UTF-8 的 AGENT.md（repository_name 必须与列表一致，path 固定为 AGENT.md）。不要只在聊天中输出文档内容。\n\n已选代码库：\n${repositories || "- 当前项目尚未注册代码库"}\n\n每份 AGENT.md 必须包含：\n1. 该仓库职责、技术栈和启动入口；\n2. 关键目录/模块与业务关键词映射；\n3. 与其他已选仓库的调用或数据边界；\n4. 构建、运行、测试及部署注意点（仅有代码证据时写入）；\n5. 给后续 AI 的检索顺序与禁止事项。\n\n安全要求：不得写入密钥、连接串、绝对服务器路径、.git、依赖或构建产物信息；不确定的内容要标为“待确认”。写入后读取并核对每个 AGENT.md，再在回复中列出实际写入的仓库和路径。`);
  }

  async function createMarkdownDirectory() {
    if (!project) return;
    const name = window.prompt("新建文件夹名称");
    if (!name?.trim()) return;
    setError(null);
    try {
      const created = await createProjectMarkdownDirectory(project.id, selectedMarkdownDirectory.repository_name, selectedMarkdownDirectory.path, name.trim());
      await refreshMarkdownDocuments(undefined, created);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "无法新建文件夹。");
    }
  }

  function downloadMarkdownDocument(document: CodeProjectMarkdownDocument) {
    if (!project) return;
    const link = window.document.createElement("a");
    link.href = projectMarkdownDocumentDownloadUrl(project.id, document.repository_name, document.path);
    link.download = document.name;
    window.document.body.appendChild(link);
    link.click();
    link.remove();
  }

  async function deleteMarkdownDocument(document: CodeProjectMarkdownDocument) {
    if (!project || document.source === "agent_index") return;
    const source = document.source === "repository" ? "这会删除注册仓库中的真实文件，Git 将显示该删除变更。" : "这会删除专用上传区中的文件。";
    if (!window.confirm(`删除 Markdown 文件“${document.name}”？\n${source}`)) return;
    setError(null);
    try {
      await deleteProjectMarkdownDocument(project.id, document.repository_name, document.path);
      setSelectedMarkdownDocument(null);
      setMarkdownDocumentContent(null);
      await refreshMarkdownDocuments(undefined, selectedMarkdownDirectory);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "无法删除 Markdown 文件。");
    }
  }

  if (!isOpen) return null;
  const lines = file?.content.split("\n").slice(0, 2500) ?? [];
  const line = fileReference?.line;

  return (
    <aside style={{ width: panelWidth }} className={`relative flex h-full min-h-0 shrink-0 flex-col border-l border-slate-200 bg-white shadow-[-16px_0_40px_rgba(15,23,42,0.08)] ${resizing ? "select-none" : ""}`}>
      <div role="separator" aria-orientation="vertical" aria-label="调整聊天与右侧工作区宽度" onPointerDown={startResize} className="absolute -left-1.5 inset-y-0 z-20 w-3 cursor-col-resize touch-none before:absolute before:inset-y-0 before:left-1.5 before:w-px before:bg-transparent hover:before:bg-blue-400 active:before:bg-blue-500"/>
      <div className="flex h-14 shrink-0 items-center gap-2 border-b border-slate-200 px-3">
        <div className="flex min-w-0 flex-1 items-center gap-1 overflow-x-auto">
          {tabs.map((item) => <WorkspaceTabButton key={item} tab={item} active={activeTab === item} onSelect={() => setActiveTab(item)} onClose={() => closeTab(item)}/>) }
        </div>
        <div ref={addMenuRef} className="relative shrink-0">
          <button type="button" onClick={() => setAddMenuOpen((current) => !current)} className={`grid h-8 w-8 place-items-center rounded-lg border transition ${addMenuOpen ? "border-blue-300 bg-blue-50 text-blue-700" : "border-slate-200 text-slate-600 hover:border-blue-300 hover:text-blue-600"}`} aria-label="新增右侧页签" aria-expanded={addMenuOpen}><Plus size={16}/></button>
          {addMenuOpen && <AddTabMenu tabs={tabs} onOpen={openTab}/>}
        </div>
        <button type="button" onClick={onClose} className="grid h-8 w-8 shrink-0 place-items-center rounded-lg text-slate-500 hover:bg-slate-100" aria-label="关闭右侧面板"><PanelRightClose size={17}/></button>
      </div>

      {activeTab === null ? <EmptyWorkspace onOpen={openTab}/> : activeTab === "preview" ? (
        <div className="flex min-h-0 flex-1 flex-col">
          <form onSubmit={(event) => { event.preventDefault(); loadBrowser(); }} className="flex items-center gap-2 border-b border-slate-100 px-3 py-2">
            <Code2 size={14} className="text-blue-600"/>
            <input value={browserAddress} onChange={(event) => setBrowserAddress(event.target.value)} className="h-8 min-w-0 flex-1 rounded-md border border-slate-200 bg-white px-2.5 font-mono text-xs text-slate-700 outline-none placeholder:font-sans placeholder:text-slate-400 focus:border-blue-400 focus:ring-2 focus:ring-blue-100" placeholder="输入 IP、域名或 URL，例如 192.168.3.199:4300" aria-label="浏览器地址"/>
            <button type="submit" disabled={!browserAddress.trim()} className="grid h-7 w-7 place-items-center rounded-md bg-blue-600 text-white hover:bg-blue-700 disabled:bg-slate-200 disabled:text-slate-400" aria-label="加载地址"><ArrowRight size={14}/></button>
            <button type="button" onClick={() => { if (browserUrl) setBrowserRevision((value) => value + 1); else if (project) void getCodeProjectRuntime(project.id).then(setRuntime); }} className="grid h-7 w-7 place-items-center rounded-md text-slate-500 hover:bg-slate-100" aria-label="刷新页面"><RefreshCw size={14}/></button>
          </form>
          {browserUrl ? <iframe key={`${browserUrl}-${browserRevision}`} title="浏览器预览" src={browserUrl} className="min-h-0 flex-1 bg-white" sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-popups" /> : <div className="flex flex-1 items-center justify-center px-8 text-center text-sm leading-6 text-slate-500">输入可访问的 IP、域名或完整 URL 后按回车加载。已启动前端时会自动填入一个可用地址。</div>}
        </div>
      ) : activeTab === "file" ? (
        <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
          <div className="border-b border-slate-100 px-4 py-3"><p className="truncate text-xs font-semibold text-slate-800">{file?.path || fileReference?.filePath || "选择代码引用以查看文件"}</p><p className="mt-1 text-[11px] text-slate-400">{file ? `${file.line_count} 行` : "代码文件仅在已注册仓库范围内读取"}</p></div>
          {loadingFile ? <div className="flex min-h-0 flex-1 items-center justify-center gap-2 text-sm text-slate-500"><Loader2 size={15} className="animate-spin"/>读取文件中…</div> : file ? <pre className="workspace-scroll min-h-0 flex-1 overflow-auto bg-slate-950 py-3 text-[12px] leading-6 text-slate-100">{lines.map((content, index) => { const number = index + 1; const highlighted = number === line; return <div id={`chat-code-line-${number}`} key={number} className={`flex min-w-max px-4 ${highlighted ? "bg-amber-300/20 ring-1 ring-inset ring-amber-300/50" : ""}`}><span className="mr-4 w-10 select-none text-right text-slate-500">{number}</span><code className="whitespace-pre">{content || " "}</code></div>; })}{file.line_count > lines.length && <p className="px-4 pt-2 text-slate-500">为保持面板流畅，仅显示前 {lines.length} 行。</p>}</pre> : <div className="flex min-h-0 flex-1 items-center justify-center px-8 text-center text-sm leading-6 text-slate-500">从聊天结果中的代码引用卡片打开文件。</div>}
        </div>
      ) : activeTab === "documents" ? (
        <ProjectDocumentsTab
          documents={markdownDocuments}
          loadingDocuments={loadingMarkdownDocuments}
          selectedDocument={selectedMarkdownDocument}
          content={markdownDocumentContent}
          loadingContent={loadingMarkdownDocument}
          onSelect={setSelectedMarkdownDocument}
          directories={markdownDirectories}
          selectedDirectory={selectedMarkdownDirectory}
          onSelectDirectory={setSelectedMarkdownDirectory}
          onInsert={onInsertMarkdownReference}
          uploadInputRef={markdownUploadInputRef}
          onUpload={uploadMarkdownDocument}
          onCreateDirectory={createMarkdownDirectory}
          onDownload={downloadMarkdownDocument}
          onDelete={deleteMarkdownDocument}
          onPrepareAgentMarkdown={prepareAgentMarkdown}
          onRefresh={() => { void refreshMarkdownDocuments().catch((ex) => setError(ex instanceof Error ? ex.message : "无法刷新项目 Markdown 文档。")); }}
        />
      ) : activeTab === "tasks" ? <SideTaskTab project={project} runtime={runtime} /> : <RuntimeTerminalTab runtime={runtime} logs={runtimeLogs} />}
      {error && <div className="border-t border-rose-100 bg-rose-50 px-3 py-2 text-xs text-rose-700">{error}</div>}
    </aside>
  );
}

function workspaceTabMeta(tab: WorkspaceTab) {
  if (tab === "file") return { label: "文件", icon: FileCode2 };
  if (tab === "documents") return { label: "项目文档", icon: FileText };
  if (tab === "tasks") return { label: "侧边任务", icon: ListTodo };
  if (tab === "preview") return { label: "浏览器", icon: Globe2 };
  return { label: "终端", icon: Terminal };
}

function WorkspaceTabButton({ tab, active, onSelect, onClose }: { tab: WorkspaceTab; active: boolean; onSelect: () => void; onClose: () => void }) {
  const { label, icon: Icon } = workspaceTabMeta(tab);
  return <div className={`flex h-8 shrink-0 items-center rounded-lg border transition ${active ? "border-blue-200 bg-blue-50 text-blue-700" : "border-transparent text-slate-500 hover:bg-slate-100"}`}><button type="button" onClick={onSelect} className="inline-flex h-full items-center gap-1.5 pl-2 pr-1.5 text-xs font-medium"><Icon size={14}/>{label}</button><button type="button" onClick={onClose} className="mr-1 grid h-5 w-5 place-items-center rounded text-slate-400 hover:bg-white hover:text-slate-700" aria-label={`关闭${label}页签`}><X size={12}/></button></div>;
}

function AddTabMenu({ tabs, onOpen }: { tabs: WorkspaceTab[]; onOpen: (tab: WorkspaceTab) => void }) {
  const options: WorkspaceTab[] = ["documents", "file", "tasks", "preview", "terminal"];
  return <div className="absolute right-0 top-10 z-50 w-56 rounded-xl border border-slate-200 bg-white p-1.5 shadow-[0_18px_42px_rgba(15,23,42,0.2)]">{options.map((tab) => { const { label, icon: Icon } = workspaceTabMeta(tab); const exists = tabs.includes(tab); return <button key={tab} type="button" onClick={() => onOpen(tab)} className="flex h-9 w-full items-center gap-2 rounded-lg px-2.5 text-left text-xs text-slate-700 transition hover:bg-slate-100"><Icon size={15} className="text-slate-500"/><span className="flex-1">{label}</span><span className="text-[10px] text-slate-400">{exists ? "切换" : "新增"}</span></button>; })}</div>;
}

function EmptyWorkspace({ onOpen }: { onOpen: (tab: WorkspaceTab) => void }) {
  const options: WorkspaceTab[] = ["documents", "file", "tasks", "preview", "terminal"];
  return <div className="flex min-h-0 flex-1 items-center justify-center p-6"><div className="w-full max-w-sm space-y-2">{options.map((tab) => { const { label, icon: Icon } = workspaceTabMeta(tab); return <button key={tab} type="button" onClick={() => onOpen(tab)} className="flex h-11 w-full items-center gap-2.5 rounded-lg bg-slate-50 px-3 text-left text-sm text-slate-700 transition hover:bg-blue-50 hover:text-blue-700"><Icon size={16}/><span className="flex-1">{label}</span><Plus size={15} className="text-slate-400"/></button>; })}</div></div>;
}

type MarkdownTreeNode = {
  children: Map<string, MarkdownTreeNode>;
  document?: CodeProjectMarkdownDocument;
  directory?: CodeProjectMarkdownDirectory;
};

function buildMarkdownDocumentTree(documents: CodeProjectMarkdownDocument[], directories: CodeProjectMarkdownDirectory[]) {
  const root: MarkdownTreeNode = { children: new Map() };
  const ensurePath = (segments: string[]) => {
    let node = root;
    for (const segment of segments) {
      let child = node.children.get(segment);
      if (!child) { child = { children: new Map() }; node.children.set(segment, child); }
      node = child;
    }
    return node;
  };
  for (const directory of directories) ensurePath([directory.repository_name, ...directory.path.split("/").filter(Boolean)]).directory = directory;
  for (const document of documents) {
    const path = document.source === "upload" || document.source === "agent_index"
      ? [document.repository_name, ...(document.directory_path ?? "").split("/").filter(Boolean), document.name]
      : [document.repository_name, ...document.path.split("/").filter(Boolean)];
    ensurePath(path).document = document;
  }
  return root;
}

function ProjectDocumentsTab({ documents, directories, loadingDocuments, selectedDocument, selectedDirectory, content, loadingContent, onSelect, onSelectDirectory, onInsert, uploadInputRef, onUpload, onCreateDirectory, onDownload, onDelete, onPrepareAgentMarkdown, onRefresh }: {
  documents: CodeProjectMarkdownDocument[];
  directories: CodeProjectMarkdownDirectory[];
  loadingDocuments: boolean;
  selectedDocument: CodeProjectMarkdownDocument | null;
  selectedDirectory: CodeProjectMarkdownDirectory;
  content: CodeProjectMarkdownDocumentContent | null;
  loadingContent: boolean;
  onSelect: (document: CodeProjectMarkdownDocument) => void;
  onSelectDirectory: (directory: CodeProjectMarkdownDirectory) => void;
  onInsert?: (document: CodeProjectMarkdownDocument) => void;
  uploadInputRef: RefObject<HTMLInputElement | null>;
  onUpload: (file: File) => void;
  onCreateDirectory: () => void;
  onDownload: (document: CodeProjectMarkdownDocument) => void;
  onDelete: (document: CodeProjectMarkdownDocument) => void;
  onPrepareAgentMarkdown: () => void;
  onRefresh: () => void;
}) {
  const tree = useMemo(() => buildMarkdownDocumentTree(documents, directories), [documents, directories]);
  return <div className="flex min-h-0 flex-1 overflow-hidden">
    <div className="workspace-scroll w-[44%] min-w-[160px] max-w-[300px] overflow-auto border-r border-slate-200 bg-slate-50/60 p-2">
      <div className="px-2 py-1.5"><div className="flex items-center gap-1"><span className="flex-1 text-[11px] font-semibold text-slate-500">项目 Markdown 文档</span><button type="button" onClick={onRefresh} disabled={loadingDocuments} className="grid h-6 w-6 place-items-center rounded text-slate-500 hover:bg-slate-100 hover:text-blue-700 disabled:opacity-40" title="刷新目录" aria-label="刷新目录"><RefreshCw size={13} className={loadingDocuments ? "animate-spin" : ""}/></button><button type="button" onClick={onPrepareAgentMarkdown} className="rounded px-1.5 py-1 text-[10px] font-medium text-blue-700 hover:bg-blue-50">生成 Agent 文档</button></div><p className="mt-1 truncate text-[10px] text-slate-400">上传目标：{selectedDirectory.repository_name}{selectedDirectory.path ? ` / ${selectedDirectory.path}` : " / 根目录"}</p><p className="mt-0.5 text-[10px] leading-4 text-slate-400">{selectedDirectory.repository_name === "aiagent-uploads" ? "专用上传区，不写入 Git 仓库。" : "已注册仓库：上传、新建和删除都会成为 Git 可见变更。"}</p><div className="mt-1 flex gap-1"><button type="button" onClick={onCreateDirectory} className="inline-flex items-center gap-1 rounded px-1.5 py-1 text-[10px] font-medium text-blue-700 hover:bg-blue-50"><FolderPlus size={12}/>新建文件夹</button><button type="button" onClick={() => uploadInputRef.current?.click()} className="rounded px-1.5 py-1 text-[10px] font-medium text-blue-700 hover:bg-blue-50">上传到此处</button></div><input ref={uploadInputRef} type="file" accept=".md,.markdown,text/markdown,text/plain" className="hidden" onChange={(event) => { const file = event.target.files?.[0]; event.target.value = ""; if (file) onUpload(file); }}/></div>
      {loadingDocuments ? <div className="flex min-h-24 items-center justify-center gap-2 text-xs text-slate-500"><Loader2 size={14} className="animate-spin"/>正在读取目录…</div>
        : directories.length === 0 && documents.length === 0 ? <p className="px-2 py-5 text-center text-xs leading-5 text-slate-500">当前项目没有可读取的 Markdown 目录。</p>
          : <MarkdownDocumentTreeNode node={tree} selectedDocument={selectedDocument} selectedDirectory={selectedDirectory} onSelect={onSelect} onSelectDirectory={onSelectDirectory}/>}
    </div>
    <div className="flex min-w-0 flex-1 flex-col overflow-hidden bg-white">
      <div className="flex min-h-12 items-center gap-2 border-b border-slate-100 px-3">
        <FileText size={15} className="shrink-0 text-blue-600"/>
        <div className="min-w-0 flex-1"><p className="truncate text-xs font-semibold text-slate-800">{selectedDocument?.path || "选择一个 Markdown 文档"}</p>{selectedDocument && <p className="mt-0.5 truncate text-[10px] text-slate-400">{selectedDocument.repository_name}</p>}</div>
        {selectedDocument && <button type="button" onClick={() => onDownload(selectedDocument)} title="下载 Markdown" className="grid h-7 w-7 shrink-0 place-items-center rounded-md text-slate-500 hover:bg-slate-100 hover:text-blue-700"><Download size={14}/></button>}{selectedDocument && selectedDocument.source !== "agent_index" && <button type="button" onClick={() => onDelete(selectedDocument)} title="删除 Markdown" className="grid h-7 w-7 shrink-0 place-items-center rounded-md text-slate-500 hover:bg-rose-50 hover:text-rose-700"><Trash2 size={14}/></button>}{selectedDocument && onInsert && <button type="button" onClick={() => onInsert(selectedDocument)} className="shrink-0 rounded-md bg-blue-600 px-2 py-1.5 text-[11px] font-medium text-white transition hover:bg-blue-700">引用到聊天</button>}
      </div>
      {loadingContent ? <div className="flex min-h-0 flex-1 items-center justify-center gap-2 text-sm text-slate-500"><Loader2 size={15} className="animate-spin"/>正在预览文档…</div>
        : content ? <div className="workspace-scroll min-h-0 flex-1 overflow-auto px-5 py-5 text-sm leading-7 text-slate-700"><article className="markdown-document-preview mx-auto max-w-4xl"><ReactMarkdown remarkPlugins={[remarkGfm]} components={{
          h1: ({ className, ...props }) => <h1 className={`mb-5 border-b border-slate-200 pb-3 text-2xl font-bold tracking-tight text-slate-950 ${className ?? ""}`} {...props}/>,
          h2: ({ className, ...props }) => <h2 className={`mb-3 mt-8 border-b border-slate-100 pb-2 text-xl font-bold text-slate-900 ${className ?? ""}`} {...props}/>,
          h3: ({ className, ...props }) => <h3 className={`mb-2 mt-6 text-base font-bold text-slate-900 ${className ?? ""}`} {...props}/>,
          h4: ({ className, ...props }) => <h4 className={`mb-2 mt-5 text-sm font-bold text-slate-800 ${className ?? ""}`} {...props}/>,
          p: ({ className, ...props }) => <p className={`my-3 text-[14px] leading-7 text-slate-700 ${className ?? ""}`} {...props}/>,
          a: ({ className, ...props }) => <a className={`font-medium text-blue-700 underline decoration-blue-300 underline-offset-2 hover:text-blue-900 ${className ?? ""}`} target="_blank" rel="noreferrer" {...props}/>,
          ul: ({ className, ...props }) => <ul className={`my-3 list-disc space-y-1 pl-6 marker:text-slate-400 ${className ?? ""}`} {...props}/>,
          ol: ({ className, ...props }) => <ol className={`my-3 list-decimal space-y-1 pl-6 marker:font-semibold marker:text-slate-500 ${className ?? ""}`} {...props}/>,
          li: ({ className, ...props }) => <li className={`pl-1 ${className ?? ""}`} {...props}/>,
          blockquote: ({ className, ...props }) => <blockquote className={`my-4 border-l-4 border-blue-300 bg-blue-50 px-4 py-2 text-slate-700 ${className ?? ""}`} {...props}/>,
          hr: ({ className, ...props }) => <hr className={`my-7 border-slate-200 ${className ?? ""}`} {...props}/>,
          table: ({ className, ...props }) => <table className={`my-4 min-w-full border-collapse text-left text-[13px] leading-6 ${className ?? ""}`} {...props}/>,
          thead: ({ className, ...props }) => <thead className={`bg-slate-100 text-slate-800 ${className ?? ""}`} {...props}/>,
          th: ({ className, ...props }) => <th className={`border border-slate-200 px-3 py-2 font-semibold ${className ?? ""}`} {...props}/>,
          td: ({ className, ...props }) => <td className={`border border-slate-200 px-3 py-2 align-top ${className ?? ""}`} {...props}/>,
          pre: ({ className, ...props }) => <pre className={`my-4 overflow-x-auto rounded-xl border border-slate-800 bg-slate-950 p-4 text-[12px] leading-6 text-slate-100 shadow-sm ${className ?? ""}`} {...props}/>,
          code: ({ className, ...props }) => <code className={`${className ? "font-mono" : "rounded bg-slate-100 px-1.5 py-0.5 font-mono text-[0.9em] text-rose-700"} ${className ?? ""}`} {...props}/>,
          img: ({ className, alt, ...props }) => <img className={`my-4 max-w-full rounded-lg border border-slate-200 shadow-sm ${className ?? ""}`} alt={alt ?? "文档图片"} {...props}/>,
        }}>{content.content}</ReactMarkdown></article>{content.is_truncated && <p className="mt-5 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs leading-5 text-amber-800">预览已达到安全长度上限；引用聊天时将使用同一受控内容。</p>}</div>
          : <div className="flex min-h-0 flex-1 items-center justify-center px-6 text-center text-sm leading-6 text-slate-500">从左侧树状目录选择文档后即可预览，文档内容不会作为代码文件暴露。</div>}
    </div>
  </div>;
}

function MarkdownDocumentTreeNode({ node, selectedDocument, selectedDirectory, onSelect, onSelectDirectory, depth = 0 }: { node: MarkdownTreeNode; selectedDocument: CodeProjectMarkdownDocument | null; selectedDirectory: CodeProjectMarkdownDirectory; onSelect: (document: CodeProjectMarkdownDocument) => void; onSelectDirectory: (directory: CodeProjectMarkdownDirectory) => void; depth?: number }) {
  return <div>{[...node.children.entries()].sort(([left], [right]) => left.localeCompare(right)).map(([name, child]) => <MarkdownDocumentTreeItem key={name} name={name} node={child} selectedDocument={selectedDocument} selectedDirectory={selectedDirectory} onSelect={onSelect} onSelectDirectory={onSelectDirectory} depth={depth}/>)}</div>;
}

function MarkdownDocumentTreeItem({ name, node, selectedDocument, selectedDirectory, onSelect, onSelectDirectory, depth }: { name: string; node: MarkdownTreeNode; selectedDocument: CodeProjectMarkdownDocument | null; selectedDirectory: CodeProjectMarkdownDirectory; onSelect: (document: CodeProjectMarkdownDocument) => void; onSelectDirectory: (directory: CodeProjectMarkdownDirectory) => void; depth: number }) {
  const [expanded, setExpanded] = useState(depth < 2);
  const hasChildren = node.children.size > 0;
  if (node.document && !hasChildren) {
    const active = selectedDocument?.repository_name === node.document.repository_name && selectedDocument.path === node.document.path;
    return <button type="button" onClick={() => onSelect(node.document!)} className={`flex min-h-8 w-full items-center gap-1.5 rounded-md py-1 pr-2 text-left text-xs transition ${active ? "bg-blue-100 text-blue-800" : "text-slate-700 hover:bg-slate-100"}`} style={{ paddingLeft: `${depth * 12 + 8}px` }}><FileText size={14} className="shrink-0 text-blue-500"/><span className="min-w-0 truncate">{name}</span></button>;
  }
  const activeDirectory = node.directory && node.directory.repository_name === selectedDirectory.repository_name && node.directory.path === selectedDirectory.path;
  return <div><button type="button" onClick={() => { setExpanded((current) => !current); if (node.directory) onSelectDirectory(node.directory); }} className={`flex min-h-8 w-full items-center gap-1 rounded-md py-1 pr-2 text-left text-xs font-medium transition ${activeDirectory ? "bg-blue-100 text-blue-800" : "text-slate-700 hover:bg-slate-100"}`} style={{ paddingLeft: `${depth * 12 + 4}px` }}><span className="grid h-4 w-4 place-items-center">{expanded ? <ChevronDown size={13}/> : <ChevronRight size={13}/>}</span>{expanded ? <FolderOpen size={14} className="shrink-0 text-amber-500"/> : <Folder size={14} className="shrink-0 text-amber-500"/>}<span className="min-w-0 truncate">{name}</span></button>{expanded && <MarkdownDocumentTreeNode node={node} selectedDocument={selectedDocument} selectedDirectory={selectedDirectory} onSelect={onSelect} onSelectDirectory={onSelectDirectory} depth={depth + 1}/>}</div>;
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
