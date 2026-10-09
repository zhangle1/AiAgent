"use client";

import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import ReactMarkdown from "react-markdown";
import { ChevronDown, ChevronRight, FileText, Folder, FolderOpen, Plus, RefreshCw, Send, Terminal, Upload, UserRound, FolderKanban, Library, Download, RotateCw, X, Globe2, FolderPlus, Loader2 } from "lucide-react";
import { askKnowledgeResources, getKnowledgeResourceTask, createKnowledgeResourceDirectory, getKnowledgeResourceTree, knowledgeResourceFileUrl, parseKnowledgeResource, readKnowledgeResource, uploadKnowledgeResources } from "@/lib/knowledge-api";
import { KnowledgeSemanticPreview } from "./KnowledgeSemanticPreview";
import type { KnowledgeCompilationJob, KnowledgeResourceNode, KnowledgeResourceRead } from "@/lib/knowledge-types";

const ROOTS = [
  { uri: "viking://resources/", label: "资料", hint: "共享资料", icon: Library },
  { uri: "viking://projects/", label: "项目", hint: "项目资料", icon: FolderKanban },
  { uri: "viking://user/", label: "人员", hint: "个人目录", icon: UserRound },
];

function formatBytes(value?: number | null) {
  if (!value) return "";
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / 1024 / 1024).toFixed(1)} MB`;
}

export function KnowledgeWorkspace() {
  const [nodes, setNodes] = useState<KnowledgeResourceNode[]>([]);
  const [selectedUri, setSelectedUri] = useState("viking://resources/");
  const [resource, setResource] = useState<KnowledgeResourceRead | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(new Set(ROOTS.map(root => root.uri)));
  const [loading, setLoading] = useState(true);
  const [reading, setReading] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [showResourceDialog, setShowResourceDialog] = useState(false);
  const [previewMode, setPreviewMode] = useState<"source" | "parsed" | "semantic">("source");
  const [terminalInput, setTerminalInput] = useState("");
  const [terminalLines, setTerminalLines] = useState<string[]>(["AgentViking 工作区已就绪。输入问题，或使用 /ls、/open、/parse。"]);
  const [asking, setAsking] = useState(false);
  const [job, setJob] = useState<KnowledgeCompilationJob | null>(null);
  const [showTaskLink, setShowTaskLink] = useState(false);
  const readSequence = useRef(0);

  const children = useMemo(() => {
    const map = new Map<string, KnowledgeResourceNode[]>();
    nodes.forEach(node => { if (node.parent_uri) map.set(node.parent_uri, [...(map.get(node.parent_uri) ?? []), node]); });
    map.forEach(value => value.sort((a, b) => (a.kind === b.kind ? a.name.localeCompare(b.name, "zh-CN") : a.kind === "directory" ? -1 : 1)));
    return map;
  }, [nodes]);
  const selected = nodes.find(node => node.uri === selectedUri) ?? ROOTS.find(root => root.uri === selectedUri) as KnowledgeResourceNode | undefined;
  const activeText = resource && (previewMode === "parsed" ? resource.parsed_content : previewMode === "semantic" ? resource.semantic_content : resource.source_text);
  const isPdf = selected?.kind === "file" && /\.pdf$/i.test(selected.extension ?? selected.name);
  const isImage = selected?.kind === "file" && /\.(png|jpe?g|webp|gif|bmp)$/i.test(selected.extension ?? selected.name);

  async function loadTree(select = selectedUri) {
    setLoading(true); setError("");
    try {
      const value = await getKnowledgeResourceTree(); setNodes(value);
      if (value.some(node => node.uri === select)) await open(select, value);
      else await open("viking://resources/", value);
    } catch (ex) { setError(ex instanceof Error ? ex.message : "目录读取失败"); }
    finally { setLoading(false); }
  }
  async function open(uri: string, tree = nodes) {
    const sequence = ++readSequence.current;
    setSelectedUri(uri); setResource(null); setJob(null); setReading(true); setError(""); setPreviewMode("source");
    try {
      const value = await readKnowledgeResource(uri); if (sequence !== readSequence.current) return; setResource(value);
      const node = tree.find(item => item.uri === uri);
      if (node?.kind === "directory" || value.node.kind === "directory") setExpanded(current => new Set(current).add(uri));
    } catch (ex) { if (sequence === readSequence.current) setError(ex instanceof Error ? ex.message : "资源读取失败"); }
    finally { if (sequence === readSequence.current) setReading(false); }
  }
  useEffect(() => { void loadTree(); }, []);
  useEffect(() => {
    if (selected?.kind !== "file") return;
    const uri = selected.uri;
    let disposed = false;
    let busy = false;
    let lastTask = "";
    async function poll() {
      if (busy) return;
      busy = true;
      const sequence = readSequence.current;
      try {
        const next = await getKnowledgeResourceTask(uri);
        if (disposed || sequence !== readSequence.current) return;
        setJob(next);
        const task = next ? `${next.id}:${next.status}` : "";
        if (next && task !== lastTask && ["success", "error", "cancelled"].includes(next.status)) {
          setNotice(next.message ?? "任务结束");
          const value = await readKnowledgeResource(uri);
          if (disposed || sequence !== readSequence.current) return;
          setResource(value);
        }
        lastTask = task;
      } catch (ex) { if (!disposed && sequence === readSequence.current) setError(ex instanceof Error ? ex.message : "任务状态读取失败"); }
      finally { busy = false; }
    }
    void poll();
    const timer = setInterval(() => void poll(), 1800);
    return () => { disposed = true; clearInterval(timer); };
  }, [selected?.uri, selected?.kind]);

  async function upload(files: File[]): Promise<boolean> {
    if (!files.length || !selected || selected.kind !== "directory") return false;
    setNotice("资料已接收，正在排队解析…"); setError("");
    try { const result = await uploadKnowledgeResources(selected.uri, files, true); setNotice(result.warnings.length ? result.warnings.join(" ") : "资料已保存，文档解析已加入任务队列。"); setShowTaskLink(true); await loadTree(selected.uri); return true; }
    catch (ex) { setError(ex instanceof Error ? ex.message : "上传失败"); return false; }
  }
  async function parse() {
    if (!selected || selected.kind !== "file") return;
    const sequence = readSequence.current;
    try { const next = await parseKnowledgeResource(selected.uri); if (sequence !== readSequence.current) return; setJob(next); setNotice("已加入正文解析与 L0/L1 语义生成队列"); setShowTaskLink(true); }
    catch (ex) { if (sequence === readSequence.current) setError(ex instanceof Error ? ex.message : "解析任务创建失败"); }
  }
  async function submitTerminal(event?: FormEvent) {
    event?.preventDefault(); const command = terminalInput.trim(); if (!command) return;
    setTerminalInput(""); setTerminalLines(lines => [...lines, `> ${command}`]);
    if (command === "/ls") { setTerminalLines(lines => [...lines, (children.get(selectedUri) ?? []).map(item => `${item.kind === "directory" ? "📁" : "📄"} ${item.name}`).join("\n") || "（空目录）"]); return; }
    if (command === "/open") { if (selected) setTerminalLines(lines => [...lines, selected.uri]); return; }
    if (command === "/parse") { await parse(); setTerminalLines(lines => [...lines, "已开始文档解析。"]); return; }
    if (command.startsWith("/ask ")) { await ask(command.slice(5)); return; }
    await ask(command);
  }
  async function ask(question: string) {
    setAsking(true); try { const result = await askKnowledgeResources(selectedUri, question); setTerminalLines(lines => [...lines, result.answer, result.truncated ? "[上下文已截断，回答基于部分资料]" : ""]); }
    catch (ex) { setTerminalLines(lines => [...lines, `错误：${ex instanceof Error ? ex.message : "问答失败"}`]); }
    finally { setAsking(false); }
  }

  return <main className="flex h-[calc(100vh-24px)] min-h-[640px] flex-col overflow-hidden bg-[#f8fafc] text-slate-900">
    <header className="flex h-16 shrink-0 items-center justify-between border-b border-slate-200 bg-white px-5">
      <div><div className="flex items-center gap-2 text-[15px] font-semibold"><span className="grid h-7 w-7 place-items-center rounded-lg bg-slate-900 text-white"><Library size={15} /></span>知识工作区</div><p className="mt-0.5 text-[11px] text-slate-500">目录即入口 · 原文、解析正文和 Agent 问答在同一空间完成</p></div>
      <div className="flex items-center gap-2"><button type="button" onClick={() => void loadTree()} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-slate-200 px-2.5 text-xs hover:border-blue-300"><RefreshCw size={13} className={loading ? "animate-spin" : ""} />刷新</button></div>
    </header>
    <div className="grid min-h-0 flex-1 grid-cols-[272px_minmax(0,1fr)_360px]">
      <aside className="min-h-0 overflow-auto border-r border-slate-200 bg-white"><div className="flex items-center justify-between border-b border-slate-100 px-4 py-3"><div><div className="text-xs font-semibold text-slate-800">目录</div><div className="mt-0.5 text-[10px] text-slate-400">Viking URI 资源树</div></div><button type="button" title="添加资源" onClick={() => setShowResourceDialog(true)} className="grid h-7 w-7 place-items-center rounded-md text-slate-500 hover:bg-blue-50 hover:text-blue-600"><Plus size={15} /></button></div>
        <nav className="p-2">{ROOTS.map(root => <TreeRow key={root.uri} node={{ ...root, kind: "directory", name: root.label, uri: root.uri }} depth={0} selectedUri={selectedUri} expanded={expanded} children={children} onToggle={uri => setExpanded(current => { const next = new Set(current); next.has(uri) ? next.delete(uri) : next.add(uri); return next; })} onOpen={uri => void open(uri)} />)}</nav>
      </aside>
      <section className="min-w-0 min-h-0 overflow-hidden bg-[#fbfcfe]">
        <div className="flex h-12 items-center justify-between border-b border-slate-200 bg-white px-5"><div className="min-w-0 truncate font-mono text-[11px] text-slate-500">{selectedUri}</div>{selected?.kind === "file" && <div className="flex items-center gap-2"><button type="button" onClick={() => void parse()} className="inline-flex items-center gap-1 rounded-md border border-slate-200 px-2 py-1 text-[11px] hover:border-blue-300"><RotateCw size={12} />重新解析</button><a href={knowledgeResourceFileUrl(selectedUri, true)} download className="inline-flex items-center gap-1 rounded-md border border-slate-200 px-2 py-1 text-[11px] hover:border-blue-300"><Download size={12} />下载</a></div>}</div>
        <div className="h-full overflow-auto p-6">{notice && <div className="mb-4 flex flex-wrap items-center gap-3 rounded-md border border-blue-100 bg-blue-50 px-3 py-2 text-xs text-blue-700"><span>{notice}</span>{showTaskLink && <a href="/task-center?domain=knowledge" className="font-semibold underline underline-offset-2">查看任务中心</a>}</div>}{error && <div className="mb-4 rounded-md border border-red-100 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</div>}{reading ? <div className="flex h-64 items-center justify-center text-sm text-slate-400">正在读取资料…</div> : selected?.kind === "directory" ? <DirectoryPreview resource={resource} node={selected} children={children.get(selectedUri) ?? []} onOpen={uri => void open(uri)} /> : <FilePreview node={selected} resource={resource} mode={previewMode} setMode={setPreviewMode} activeText={activeText} isPdf={isPdf} isImage={isImage} job={job} />}</div>
      </section>
      <aside className="flex min-h-0 flex-col border-l border-slate-200 bg-white"><div className="flex h-12 shrink-0 items-center gap-2 border-b border-slate-200 px-4"><Terminal size={15} className="text-slate-500" /><span className="text-xs font-semibold">Agent 终端</span><span className="ml-auto rounded-full bg-emerald-50 px-2 py-0.5 text-[10px] text-emerald-700">目录上下文</span></div><div className="min-h-0 flex-1 overflow-auto bg-[#111827] p-4 font-mono text-[11px] leading-5 text-slate-200">{terminalLines.map((line, index) => <div key={`${index}-${line.slice(0, 12)}`} className={line.startsWith(">") ? "mt-2 text-blue-300" : "whitespace-pre-wrap text-slate-300"}>{line}</div>)}{asking && <div className="mt-2 text-amber-300">Agent 正在读取目录…</div>}</div><div className="border-t border-slate-200 p-3"><div className="mb-2 flex flex-wrap gap-1.5">{["/ls", "/open", "/parse"].map(command => <button key={command} type="button" onClick={() => { setTerminalInput(command); }} className="rounded border border-slate-200 px-2 py-1 font-mono text-[10px] text-slate-500 hover:border-blue-300">{command}</button>)}</div><form onSubmit={submitTerminal} className="flex items-center gap-2"><span className="font-mono text-xs text-slate-400">›</span><input value={terminalInput} onChange={event => setTerminalInput(event.target.value)} placeholder="询问当前目录…" className="min-w-0 flex-1 bg-transparent text-xs outline-none" /><button type="submit" disabled={asking || !terminalInput.trim()} className="grid h-7 w-7 place-items-center rounded-md bg-slate-900 text-white disabled:opacity-30"><Send size={13} /></button></form></div></aside>
    </div>
    {showResourceDialog && <ResourceDialog parentUri={selected?.kind === "directory" ? selectedUri : selected?.parent_uri ?? "viking://resources/"} onClose={() => setShowResourceDialog(false)} onUploaded={async files => { if (await upload(files)) setShowResourceDialog(false); }} onDirectoryCreated={async name => { const parentUri = selected?.kind === "directory" ? selectedUri : selected?.parent_uri ?? "viking://resources/"; await createKnowledgeResourceDirectory(parentUri, name); setNotice("目录已创建"); setShowResourceDialog(false); await loadTree(parentUri); }} />}
  </main>;
}

function TreeRow({ node, depth, selectedUri, expanded, children, onToggle, onOpen }: { node: KnowledgeResourceNode; depth: number; selectedUri: string; expanded: Set<string>; children: Map<string, KnowledgeResourceNode[]>; onToggle: (uri: string) => void; onOpen: (uri: string) => void }) {
  const isDir = node.kind === "directory"; const isExpanded = expanded.has(node.uri); const items = children.get(node.uri) ?? [];
  return <div><button type="button" onClick={() => { onOpen(node.uri); if (isDir) onToggle(node.uri); }} className={`flex w-full items-center gap-1.5 rounded-md py-1.5 pr-2 text-left text-xs ${selectedUri === node.uri ? "bg-blue-50 text-blue-700" : "text-slate-700 hover:bg-slate-50"}`} style={{ paddingLeft: 10 + depth * 16 }}><span className="w-3 shrink-0 text-slate-400">{isDir ? (isExpanded ? <ChevronDown size={13} /> : <ChevronRight size={13} />) : null}</span>{isDir ? (isExpanded ? <FolderOpen size={14} className="text-amber-500" /> : <Folder size={14} className="text-amber-500" />) : <FileText size={14} className="text-slate-400" />}<span className="min-w-0 flex-1 truncate">{node.name}</span>{node.kind === "file" && node.status !== "processed" && <span className="h-1.5 w-1.5 rounded-full bg-amber-400" />}</button>{isDir && isExpanded && items.map(item => <TreeRow key={item.uri} node={item} depth={depth + 1} selectedUri={selectedUri} expanded={expanded} children={children} onToggle={onToggle} onOpen={onOpen} />)}</div>;
}

function ResourceDialog({ parentUri, onClose, onUploaded, onDirectoryCreated }: { parentUri: string; onClose: () => void; onUploaded: (files: File[]) => Promise<void>; onDirectoryCreated: (name: string) => Promise<void> }) {
  const [tab, setTab] = useState<"upload" | "remote" | "directory">("upload");
  const [files, setFiles] = useState<File[]>([]);
  const [directory, setDirectory] = useState("");
  const [remoteUrl, setRemoteUrl] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  async function submit(event: FormEvent) {
    event.preventDefault(); setError("");
    if (tab === "remote") { setError("远程资源接入尚未启用，请先上传本地文件。"); return; }
    if (tab === "directory") {
      if (!directory.trim()) { setError("请输入目录名称。"); return; }
      setBusy(true); try { await onDirectoryCreated(directory.trim()); } catch (ex) { setError(ex instanceof Error ? ex.message : "目录创建失败"); } finally { setBusy(false); }
      return;
    }
    if (!files.length) { setError("请先选择至少一个文件。"); return; }
    setBusy(true); try { await onUploaded(files); } catch (ex) { setError(ex instanceof Error ? ex.message : "上传失败"); } finally { setBusy(false); }
  }
  return <div role="dialog" aria-modal="true" aria-label="添加资源" className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/35 p-4 backdrop-blur-[2px]"><section className="max-h-[calc(100vh-2rem)] w-full max-w-3xl overflow-y-auto rounded-2xl bg-white shadow-2xl"><header className="flex items-start justify-between border-b border-slate-100 px-6 py-5"><div><h2 className="text-lg font-semibold text-slate-900">添加资源</h2><p className="mt-1 text-sm text-slate-500">添加完成后，目录会刷新，文档解析任务会进入任务中心。</p></div><button type="button" onClick={onClose} className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="关闭"><X size={18} /></button></header><form onSubmit={submit} className="p-6"><div className="grid grid-cols-3 gap-1 rounded-lg bg-slate-100 p-1 text-sm"><button type="button" onClick={() => setTab("upload")} className={`inline-flex items-center justify-center gap-2 rounded-md py-2 ${tab === "upload" ? "bg-white font-medium text-slate-900 shadow-sm" : "text-slate-500"}`}><Upload size={15} />上传文件</button><button type="button" onClick={() => setTab("remote")} className={`inline-flex items-center justify-center gap-2 rounded-md py-2 ${tab === "remote" ? "bg-white font-medium text-slate-900 shadow-sm" : "text-slate-500"}`}><Globe2 size={15} />远程资源</button><button type="button" onClick={() => setTab("directory")} className={`inline-flex items-center justify-center gap-2 rounded-md py-2 ${tab === "directory" ? "bg-white font-medium text-slate-900 shadow-sm" : "text-slate-500"}`}><FolderPlus size={15} />新建目录</button></div>{tab === "upload" && <><button type="button" onClick={() => inputRef.current?.click()} onDragOver={event => event.preventDefault()} onDrop={event => { event.preventDefault(); setFiles(current => [...current, ...Array.from(event.dataTransfer.files)]); }} className="mt-5 flex min-h-44 w-full flex-col items-center justify-center rounded-xl border-2 border-dashed border-slate-200 bg-slate-50/70 px-6 text-center hover:border-blue-300 hover:bg-blue-50/30"><Upload size={29} className="text-slate-400" /><span className="mt-3 text-sm font-medium text-slate-700">拖拽文件到此处，或点击选择文件</span><span className="mt-1 text-xs text-slate-400">支持 PDF、Word、PPTX、Excel、Markdown、代码文件和图片</span></button><input ref={inputRef} type="file" multiple className="hidden" onChange={event => setFiles(current => [...current, ...Array.from(event.target.files ?? [])])} />{files.length > 0 && <div className="mt-3 max-h-28 overflow-auto rounded-lg border border-slate-200 bg-white p-2">{files.map((file, index) => <div key={`${file.name}-${index}`} className="flex items-center gap-2 px-2 py-1.5 text-xs"><FileText size={14} className="text-slate-400" /><span className="min-w-0 flex-1 truncate">{file.name}</span><span className="text-slate-400">{formatBytes(file.size)}</span><button type="button" onClick={() => setFiles(current => current.filter((_, fileIndex) => fileIndex !== index))} className="text-slate-400 hover:text-red-500" aria-label={`移除 ${file.name}`}><X size={13} /></button></div>)}</div>}</>}{tab === "remote" && <div className="mt-5 rounded-xl border border-dashed border-slate-200 bg-slate-50 p-8 text-center"><Globe2 size={28} className="mx-auto text-slate-400" /><p className="mt-3 text-sm font-medium text-slate-700">远程资源连接</p><p className="mt-1 text-xs text-slate-500">OpenViking 的远程资源接入位保留在这里，当前版本先使用受控本地上传。</p><input value={remoteUrl} onChange={event => setRemoteUrl(event.target.value)} placeholder="https://…" className="mx-auto mt-4 h-9 w-full max-w-md rounded-md border border-slate-200 bg-white px-3 text-sm outline-none focus:border-blue-400" /></div>}{tab === "directory" && <div className="mt-5 rounded-xl border border-slate-200 bg-slate-50 p-6"><label className="block text-sm font-medium text-slate-700">目录名称<input autoFocus value={directory} onChange={event => setDirectory(event.target.value)} placeholder="例如：产品文档" className="mt-2 h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-sm outline-none focus:border-blue-400" /></label><p className="mt-3 text-xs text-slate-500">目录将创建在当前 URI：<code className="break-all">{parentUri}</code></p></div>}<div className="mt-5"><label className="text-xs font-medium text-slate-600">目标 URI</label><div className="mt-2 flex items-center gap-2 rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-700"><span className="truncate font-mono">{parentUri}</span></div><p className="mt-1 text-xs text-slate-400">文件名由原始文件名生成，并保存在当前目录下。</p></div>{error && <div role="alert" className="mt-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>}<footer className="mt-6 flex items-center justify-between border-t border-slate-100 pt-5"><span className="text-xs text-slate-400">解析完成后可在任务中心查看阶段、进度和错误原因。</span><div className="flex gap-2"><button type="button" onClick={onClose} disabled={busy} className="h-9 rounded-lg border border-slate-200 px-4 text-sm text-slate-600 hover:bg-slate-50">取消</button><button disabled={busy} className="inline-flex h-9 items-center gap-2 rounded-lg bg-blue-600 px-4 text-sm font-medium text-white disabled:bg-slate-300">{busy && <Loader2 size={15} className="animate-spin" />}{tab === "directory" ? "创建目录" : tab === "remote" ? "连接资源" : "开始处理"}</button></div></footer></form></section></div>;
}

function DirectoryPreview({ node, children, onOpen, resource }: { resource: KnowledgeResourceRead | null; node: KnowledgeResourceNode; children: KnowledgeResourceNode[]; onOpen: (uri: string) => void }) { return <div className="mx-auto max-w-3xl"><div className="mb-8"><div className="mb-3 grid h-12 w-12 place-items-center rounded-xl bg-amber-50 text-amber-600"><FolderOpen size={25} /></div><h1 className="text-2xl font-semibold tracking-tight">{node.name}</h1><p className="mt-2 text-sm text-slate-500">这是一个目录。上传资料后，系统会自动解析正文并建立可问答的上下文。</p></div><KnowledgeSemanticPreview resource={resource} onOpen={onOpen} /><div className="grid gap-2">{children.length ? children.map(item => <button key={item.uri} type="button" onClick={() => onOpen(item.uri)} className="flex items-center gap-3 rounded-lg border border-slate-200 bg-white px-4 py-3 text-left hover:border-blue-300"><span className="text-amber-500">{item.kind === "directory" ? <Folder size={17} /> : <FileText size={17} />}</span><span className="min-w-0 flex-1 truncate text-sm font-medium">{item.name}</span><span className="text-[11px] text-slate-400">{item.kind === "file" ? formatBytes(item.size) : "目录"}</span></button>) : <div className="rounded-lg border border-dashed border-slate-200 px-4 py-10 text-center text-sm text-slate-400">目录为空，点击左侧目录标题旁的加号添加资源或新建目录。</div>}</div></div>; }

function FilePreview({ node, resource, mode, setMode, activeText, isPdf, isImage, job }: { node?: KnowledgeResourceNode; resource: KnowledgeResourceRead | null; mode: "source" | "parsed" | "semantic"; setMode: (value: "source" | "parsed" | "semantic") => void; activeText?: string | null; isPdf?: boolean; isImage?: boolean; job: KnowledgeCompilationJob | null }) { if (!node) return null; const fileUrl = knowledgeResourceFileUrl(node.uri); return <div className="mx-auto flex h-full max-w-4xl flex-col"><div className="mb-4 flex flex-wrap items-start justify-between gap-3"><div><h1 className="text-xl font-semibold">{node.name}</h1><div className="mt-1 text-xs text-slate-400">{node.extension || "文件"} {formatBytes(node.size) && `· ${formatBytes(node.size)}`} {resource?.parser && `· ${resource.parser}`}</div></div>{job && ["queued", "processing", "cancelling"].includes(job.status) && <span className="rounded-full bg-amber-50 px-2.5 py-1 text-[11px] text-amber-700">正在解析 {job.progress}%</span>}</div><div className="mb-3 flex gap-1 rounded-lg bg-slate-100 p-1 text-xs">{(["source", "parsed", "semantic"] as const).map(value => <button key={value} type="button" onClick={() => setMode(value)} className={`rounded-md px-3 py-1.5 ${mode === value ? "bg-white font-medium text-slate-900 shadow-sm" : "text-slate-500"}`}>{value === "source" ? "原文" : value === "parsed" ? "解析正文" : "知识摘要"}</button>)}</div>{isPdf && mode === "source" ? <iframe title={node.name} src={fileUrl} className="min-h-[620px] flex-1 rounded-lg border border-slate-200 bg-white" sandbox="allow-same-origin" /> : isImage && mode === "source" ? <div className="flex min-h-[420px] flex-1 items-center justify-center rounded-lg border border-slate-200 bg-white p-4"><img src={fileUrl} alt={node.name} className="max-h-[620px] max-w-full object-contain" /></div> : activeText ? <article className="min-h-[420px] flex-1 overflow-auto rounded-lg border border-slate-200 bg-white p-6 text-sm leading-7"><ReactMarkdown components={mode === "semantic" ? { img: () => null, a: ({ children }) => <span>{children}</span> } : undefined}>{activeText ?? ""}</ReactMarkdown></article> : <div className="flex min-h-[420px] flex-1 items-center justify-center rounded-lg border border-dashed border-slate-200 bg-white text-sm text-slate-400">尚未生成该视图。点击右上角「重新解析」读取正文。</div>}</div>; }
