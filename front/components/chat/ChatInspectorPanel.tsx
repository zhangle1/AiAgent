"use client";

import { type PointerEvent as ReactPointerEvent, useEffect, useMemo, useRef, useState } from "react";
import { ArrowRight, ChevronDown, ChevronRight, FileText, Folder, FolderOpen, Globe2, Code2, FileCode2, ListTodo, Loader2, PanelRightClose, Plus, RefreshCw, Terminal, X } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { getCodeProjectRuntime, getCodeRuntimeLogs } from "@/lib/code-runtime-api";
import { getCodeFile, getProjectMarkdownDocuments, readProjectMarkdownDocument } from "@/lib/code-repository-api";
import type { CodeProject, CodeProjectMarkdownDocument, CodeProjectMarkdownDocumentContent } from "@/lib/code-repository-types";
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

export function ChatInspectorPanel({ isOpen, project, fileReference, requestedTab, onInsertMarkdownReference, onClose }: { isOpen: boolean; project: CodeProject | null; fileReference: ChatCodeFileReference | null; requestedTab?: WorkspaceTab | null; onInsertMarkdownReference?: (document: CodeProjectMarkdownDocument) => void; onClose: () => void }) {
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

  const previewRuns = useMemo(() => runtime?.runs.filter((run) => run.role === "frontend" && (run.status === "starting" || run.status === "running")) ?? [], [runtime]);

  useEffect(() => {
    if (!requestedTab) return;
    setTabs((current) => current.includes(requestedTab) ? current : [...current, requestedTab]);
    setActiveTab(requestedTab);
    if (requestedTab === "terminal") setPanelWidth(terminalPanelWidth(window.innerWidth));
  }, [requestedTab]);

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
    void getProjectMarkdownDocuments(project.id)
      .then((items) => {
        if (disposed) return;
        setMarkdownDocuments(items);
        setSelectedMarkdownDocument((current) => current && items.some((item) => item.repository_name === current.repository_name && item.path === current.path) ? current : items[0] ?? null);
      })
      .catch((ex) => { if (!disposed) setError(ex instanceof Error ? ex.message : "无法读取项目 Markdown 文档。"); })
      .finally(() => { if (!disposed) setLoadingMarkdownDocuments(false); });
    return () => { disposed = true; };
  }, [isOpen, project, activeTab, requestedTab]);

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
          onInsert={onInsertMarkdownReference}
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
};

function buildMarkdownDocumentTree(documents: CodeProjectMarkdownDocument[]) {
  const root: MarkdownTreeNode = { children: new Map() };
  for (const document of documents) {
    let node = root;
    for (const segment of [document.repository_name, ...document.path.split("/")]) {
      let child = node.children.get(segment);
      if (!child) {
        child = { children: new Map() };
        node.children.set(segment, child);
      }
      node = child;
    }
    node.document = document;
  }
  return root;
}

function ProjectDocumentsTab({ documents, loadingDocuments, selectedDocument, content, loadingContent, onSelect, onInsert }: {
  documents: CodeProjectMarkdownDocument[];
  loadingDocuments: boolean;
  selectedDocument: CodeProjectMarkdownDocument | null;
  content: CodeProjectMarkdownDocumentContent | null;
  loadingContent: boolean;
  onSelect: (document: CodeProjectMarkdownDocument) => void;
  onInsert?: (document: CodeProjectMarkdownDocument) => void;
}) {
  const tree = useMemo(() => buildMarkdownDocumentTree(documents), [documents]);
  return <div className="flex min-h-0 flex-1 overflow-hidden">
    <div className="workspace-scroll w-[44%] min-w-[160px] max-w-[300px] overflow-auto border-r border-slate-200 bg-slate-50/60 p-2">
      <div className="px-2 py-1.5 text-[11px] font-semibold text-slate-500">项目 Markdown 文档</div>
      {loadingDocuments ? <div className="flex min-h-24 items-center justify-center gap-2 text-xs text-slate-500"><Loader2 size={14} className="animate-spin"/>正在读取目录…</div>
        : documents.length === 0 ? <p className="px-2 py-5 text-center text-xs leading-5 text-slate-500">当前项目的已注册仓库中没有可引用的 Markdown 文档。</p>
          : <MarkdownDocumentTreeNode node={tree} selectedDocument={selectedDocument} onSelect={onSelect}/>}
    </div>
    <div className="flex min-w-0 flex-1 flex-col overflow-hidden bg-white">
      <div className="flex min-h-12 items-center gap-2 border-b border-slate-100 px-3">
        <FileText size={15} className="shrink-0 text-blue-600"/>
        <div className="min-w-0 flex-1"><p className="truncate text-xs font-semibold text-slate-800">{selectedDocument?.path || "选择一个 Markdown 文档"}</p>{selectedDocument && <p className="mt-0.5 truncate text-[10px] text-slate-400">{selectedDocument.repository_name}</p>}</div>
        {selectedDocument && onInsert && <button type="button" onClick={() => onInsert(selectedDocument)} className="shrink-0 rounded-md bg-blue-600 px-2 py-1.5 text-[11px] font-medium text-white transition hover:bg-blue-700">引用到聊天</button>}
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

function MarkdownDocumentTreeNode({ node, selectedDocument, onSelect, depth = 0 }: { node: MarkdownTreeNode; selectedDocument: CodeProjectMarkdownDocument | null; onSelect: (document: CodeProjectMarkdownDocument) => void; depth?: number }) {
  return <div>{[...node.children.entries()].sort(([left], [right]) => left.localeCompare(right)).map(([name, child]) => <MarkdownDocumentTreeItem key={name} name={name} node={child} selectedDocument={selectedDocument} onSelect={onSelect} depth={depth}/>)}</div>;
}

function MarkdownDocumentTreeItem({ name, node, selectedDocument, onSelect, depth }: { name: string; node: MarkdownTreeNode; selectedDocument: CodeProjectMarkdownDocument | null; onSelect: (document: CodeProjectMarkdownDocument) => void; depth: number }) {
  const [expanded, setExpanded] = useState(depth < 2);
  const hasChildren = node.children.size > 0;
  if (node.document && !hasChildren) {
    const active = selectedDocument?.repository_name === node.document.repository_name && selectedDocument.path === node.document.path;
    return <button type="button" onClick={() => onSelect(node.document!)} className={`flex min-h-8 w-full items-center gap-1.5 rounded-md py-1 pr-2 text-left text-xs transition ${active ? "bg-blue-100 text-blue-800" : "text-slate-700 hover:bg-slate-100"}`} style={{ paddingLeft: `${depth * 12 + 8}px` }}><FileText size={14} className="shrink-0 text-blue-500"/><span className="min-w-0 truncate">{name}</span></button>;
  }
  return <div><button type="button" onClick={() => setExpanded((current) => !current)} className="flex min-h-8 w-full items-center gap-1 rounded-md py-1 pr-2 text-left text-xs font-medium text-slate-700 transition hover:bg-slate-100" style={{ paddingLeft: `${depth * 12 + 4}px` }}><span className="grid h-4 w-4 place-items-center">{expanded ? <ChevronDown size={13}/> : <ChevronRight size={13}/>}</span>{expanded ? <FolderOpen size={14} className="shrink-0 text-amber-500"/> : <Folder size={14} className="shrink-0 text-amber-500"/>}<span className="min-w-0 truncate">{name}</span></button>{expanded && <MarkdownDocumentTreeNode node={node} selectedDocument={selectedDocument} onSelect={onSelect} depth={depth + 1}/>}</div>;
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
