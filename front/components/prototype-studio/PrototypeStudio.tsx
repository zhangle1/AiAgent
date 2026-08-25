"use client";

import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "next/navigation";
import { AppSidebar } from "@/components/layout/AppSidebar";
import { createSession } from "@/lib/session-api";
import { streamCompleteChat, type ChatStreamEvent } from "@/lib/chat-api";
import { getCodeFile, getCodeProjects, getCodeTree, type CodeFile, type CodeTree } from "@/lib/code-repository-api";
import type { CodeProject, CodeRepository } from "@/lib/code-repository-types";
import { ChevronDown, ChevronRight, Code2, ExternalLink, FileCode2, Folder, FolderOpen, Loader2, Monitor, PanelLeftClose, RefreshCw, Search, Send, Share2, Smartphone, Tablet, X } from "lucide-react";

type ChatItem = { id: string; role: "user" | "assistant"; content: string };
type Viewport = "desktop" | "tablet" | "mobile";

const PROTOTYPE_EXTENSIONS = new Set([".html", ".htm", ".md", ".markdown", ".css", ".js", ".mjs", ".svg"]);
const IGNORED_DIRECTORIES = new Set(["node_modules", ".git", ".next", "bin", "obj", "dist", "build", "coverage", ".cache", "data"]);

export function PrototypeStudio() {
  const search = useSearchParams();
  const [sidebarCompact, setSidebarCompact] = useState(true);
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [projectId, setProjectId] = useState(search.get("project") ?? "");
  const [repositoryName, setRepositoryName] = useState(search.get("repository") ?? "");
  const [trees, setTrees] = useState<Record<string, CodeTree>>({});
  const [file, setFile] = useState<CodeFile | null>(null);
  const [fileQuery, setFileQuery] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [viewport, setViewport] = useState<Viewport>("desktop");
  const [showSource, setShowSource] = useState(false);
  const [chatInput, setChatInput] = useState("");
  const [messages, setMessages] = useState<ChatItem[]>([{ id: "welcome", role: "assistant", content: "选择项目和原型文件后，描述你想创建或修改的界面。我会把 HTML、Markdown 和相关资源保存在代码库的 design/ 目录。" }]);
  const [sessionId, setSessionId] = useState("");
  const [sending, setSending] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);

  const project = projects.find((item) => String(item.id) === projectId) ?? null;
  const repositories = project?.repositories ?? [];
  const repository = repositories.find((item) => item.name === repositoryName) ?? null;

  useEffect(() => {
    const toggle = () => setSidebarCompact((value) => !value);
    window.addEventListener("aiagent:sidebar-toggle", toggle);
    return () => window.removeEventListener("aiagent:sidebar-toggle", toggle);
  }, []);

  useEffect(() => {
    void getCodeProjects().then((items) => {
      setProjects(items);
      const selected = items.find((item) => String(item.id) === projectId) ?? items[0];
      if (selected) {
        setProjectId(String(selected.id));
        setRepositoryName((current) => selected.repositories.some((item) => item.name === current) ? current : selected.repositories[0]?.name ?? "");
      }
    }).catch((value) => setError(messageOf(value, "项目加载失败。"))).finally(() => setLoading(false));
  }, []); // initial selection only

  const loadDirectory = useCallback(async (path = "") => {
    if (!repositoryName) return;
    setError("");
    try {
      const tree = await getCodeTree(repositoryName, path);
      setTrees((current) => ({ ...current, [path]: tree }));
    } catch (value) { setError(messageOf(value, "原型目录加载失败。")); }
  }, [repositoryName]);

  useEffect(() => {
    setTrees({}); setFile(null);
    if (repositoryName) {
      void loadDirectory();
      const requestedFile = search.get("file");
      if (requestedFile && PROTOTYPE_EXTENSIONS.has(`.${requestedFile.split(".").pop()?.toLowerCase()}`)) void openFile(requestedFile);
    }
  }, [repositoryName, loadDirectory]);

  async function openFile(path: string) {
    setError("");
    try { setFile(await getCodeFile(repositoryName, path)); setShowSource(!/\.html?$/i.test(path)); }
    catch (value) { setError(messageOf(value, "文件读取失败。")); }
  }

  async function submitChat(event: FormEvent) {
    event.preventDefault();
    const prompt = chatInput.trim();
    if (!prompt || !project || !repository || sending) return;
    setChatInput(""); setSending(true); setError("");
    setMessages((items) => [...items, { id: crypto.randomUUID(), role: "user", content: prompt }, { id: "streaming", role: "assistant", content: "" }]);
    try {
      const activeSession = sessionId || (await createSession({ project_id: project.id, title: "原型设计" })).id;
      if (!sessionId) setSessionId(activeSession);
      const instruction = `你正在使用 AiAgent 原型设计工作台。请在代码库 ${repository.display_name} 的 design/ 目录内完成请求；原型入口使用单文件 HTML，设计说明使用 Markdown，相关 CSS/JS/SVG 也必须位于 design/ 下。不要修改 design/ 外文件。完成后明确列出修改的相对路径。\n\n当前文件：${file?.path ?? "尚未选择，请创建 design/prototype/index.html"}\n用户需求：${prompt}`;
      await streamCompleteChat({ session_id: activeSession, message: instruction, code_project_id: project.id, code_repository_names: [repository.name], agent: "codex", mode: "agent", codex_sandbox_mode: "workspace-write" }, (chatEvent) => applyChatEvent(chatEvent, setMessages));
      await loadDirectory("");
      if (file) await openFile(file.path);
      window.dispatchEvent(new Event("aiagent:sessions-updated"));
    } catch (value) {
      setMessages((items) => items.map((item) => item.id === "streaming" ? { ...item, id: crypto.randomUUID(), content: `生成失败：${messageOf(value, "未知错误")}` } : item));
    } finally { setSending(false); }
  }

  const previewDocument = useMemo(() => file && /\.html?$/i.test(file.path) ? securePreview(file.content) : "", [file]);
  const shareUrl = typeof window === "undefined" || !project || !repository || !file ? "" : `${window.location.origin}/prototype-studio?${new URLSearchParams({ project: String(project.id), repository: repository.name, file: file.path })}`;

  return <div className="h-screen overflow-hidden bg-[#f4f5f7] text-slate-900">
    <AppSidebar compact={sidebarCompact}/>
    <main className={`flex h-full min-w-0 flex-col transition-[margin] ${sidebarCompact ? "lg:ml-[72px]" : "lg:ml-[240px]"}`}>
      <header className="flex h-14 shrink-0 items-center gap-3 border-b border-slate-200 bg-white px-4">
        <button className="grid h-9 w-9 place-items-center rounded-lg text-slate-500 hover:bg-slate-100 lg:hidden" onClick={() => window.dispatchEvent(new Event("aiagent:mobile-drawer-toggle"))}><PanelLeftClose size={18}/></button>
        <div className="grid h-8 w-8 place-items-center rounded-lg bg-violet-100 text-violet-700"><Code2 size={17}/></div>
        <div><h1 className="text-sm font-semibold">原型设计</h1><p className="text-[10px] text-slate-400">对话生成 · 代码库持久化 · 安全预览</p></div>
        <div className="ml-auto flex min-w-0 items-center gap-2">
          <select aria-label="选择项目" value={projectId} onChange={(event) => { const value = event.target.value; const next = projects.find((item) => String(item.id) === value); setProjectId(value); setRepositoryName(next?.repositories[0]?.name ?? ""); }} className="h-9 max-w-44 rounded-lg border border-slate-200 bg-white px-2 text-xs outline-none">
            {projects.map((item) => <option key={item.id} value={item.id}>{item.display_name}</option>)}
          </select>
          <select aria-label="选择代码库" value={repositoryName} onChange={(event) => setRepositoryName(event.target.value)} className="h-9 max-w-44 rounded-lg border border-slate-200 bg-white px-2 text-xs outline-none">
            {repositories.map((item) => <option key={item.name} value={item.name}>{item.display_name}</option>)}
          </select>
          <button disabled={!file} onClick={() => setShareOpen(true)} className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-slate-900 px-3 text-xs font-medium text-white disabled:opacity-40"><Share2 size={14}/>分享</button>
        </div>
      </header>

      <div className="grid min-h-0 flex-1 grid-cols-1 lg:grid-cols-[250px_minmax(360px,1fr)_340px]">
        <aside className="hidden min-h-0 flex-col border-r border-slate-200 bg-white lg:flex">
          <div className="flex h-11 items-center justify-between border-b border-slate-100 px-3"><b className="text-xs">原型文件</b><button onClick={() => void loadDirectory("")} title="刷新"><RefreshCw size={13} className="text-slate-400"/></button></div>
          <div className="m-3 flex h-8 items-center gap-2 rounded-lg bg-slate-100 px-2"><Search size={13} className="text-slate-400"/><input value={fileQuery} onChange={(event) => setFileQuery(event.target.value)} placeholder="过滤 HTML 相关文件" className="min-w-0 flex-1 bg-transparent text-xs outline-none"/></div>
          <div className="workspace-scroll min-h-0 flex-1 overflow-auto px-1 pb-3">{trees[""] ? <PrototypeTree tree={trees[""]} trees={trees} query={fileQuery} selected={file?.path} onDirectory={(path) => trees[path] ? setTrees((items) => { const next = { ...items }; delete next[path]; return next; }) : void loadDirectory(path)} onFile={(path) => void openFile(path)}/> : <Empty loading={loading} text="此代码库暂无可浏览文件"/>}</div>
          <p className="border-t border-slate-100 px-3 py-2 text-[10px] leading-4 text-slate-400">仅显示 HTML、Markdown、CSS、JS、SVG；自动隐藏依赖、构建输出和运行数据。</p>
        </aside>

        <section className="flex min-h-0 min-w-0 flex-col bg-[#e9ebef]">
          <div className="flex h-11 items-center gap-2 border-b border-slate-200 bg-white px-3">
            <div className="flex rounded-lg bg-slate-100 p-0.5"><button onClick={() => setShowSource(false)} className={`rounded-md px-3 py-1.5 text-[11px] ${!showSource ? "bg-white shadow-sm" : "text-slate-500"}`}>预览</button><button onClick={() => setShowSource(true)} className={`rounded-md px-3 py-1.5 text-[11px] ${showSource ? "bg-white shadow-sm" : "text-slate-500"}`}>源码</button></div>
            <span className="min-w-0 flex-1 truncate text-center font-mono text-[10px] text-slate-400">{file?.path ?? "从左侧选择 HTML，或在右侧对话创建原型"}</span>
            <div className="flex gap-1">{([["desktop", Monitor], ["tablet", Tablet], ["mobile", Smartphone]] as const).map(([value, Icon]) => <button key={value} onClick={() => setViewport(value)} className={`grid h-7 w-7 place-items-center rounded ${viewport === value ? "bg-violet-100 text-violet-700" : "text-slate-400"}`}><Icon size={13}/></button>)}</div>
            {file && <button onClick={() => void openFile(file.path)} title="刷新预览"><RefreshCw size={13} className="text-slate-400"/></button>}
          </div>
          <div className="workspace-scroll min-h-0 flex-1 overflow-auto p-4 lg:p-6">
            {!file ? <div className="grid h-full place-items-center"><div className="max-w-sm text-center"><Code2 className="mx-auto text-violet-400" size={38}/><h2 className="mt-4 text-base font-semibold">从想法开始设计</h2><p className="mt-2 text-xs leading-5 text-slate-500">选择已有 HTML，或在右侧描述页面。Agent 会把可交付原型写入当前项目的 design/ 目录。</p></div></div> : showSource || !previewDocument ? <pre className="mx-auto min-h-full max-w-5xl overflow-auto rounded-xl border border-slate-200 bg-[#16181d] p-5 font-mono text-xs leading-5 text-slate-200 shadow-xl"><code>{file.content}</code></pre> : <div className={`mx-auto h-full min-h-[560px] overflow-hidden rounded-xl border border-slate-200 bg-white shadow-xl transition-[width] ${viewport === "mobile" ? "w-[390px]" : viewport === "tablet" ? "w-[768px]" : "w-full"}`}><iframe title={`原型预览 ${file.path}`} sandbox="allow-scripts allow-forms allow-modals allow-popups" referrerPolicy="no-referrer" srcDoc={previewDocument} className="h-full w-full border-0"/></div>}
          </div>
        </section>

        <aside className="flex min-h-0 flex-col border-l border-slate-200 bg-white">
          <div className="flex h-11 items-center border-b border-slate-100 px-4"><b className="text-xs">设计对话</b><span className="ml-auto rounded-full bg-emerald-50 px-2 py-1 text-[9px] font-medium text-emerald-700">写入 design/</span></div>
          <div className="workspace-scroll min-h-0 flex-1 space-y-3 overflow-y-auto p-4">{messages.map((item) => <article key={item.id} className={`rounded-xl px-3 py-2.5 text-xs leading-5 ${item.role === "user" ? "ml-8 bg-violet-600 text-white" : "mr-4 bg-slate-100 text-slate-700"}`}>{item.content || <span className="inline-flex items-center gap-2"><Loader2 size={13} className="animate-spin"/>正在设计并写入代码库…</span>}</article>)}</div>
          <form onSubmit={submitChat} className="border-t border-slate-100 p-3"><textarea value={chatInput} onChange={(event) => setChatInput(event.target.value)} placeholder="例如：在 design/onboarding 创建一个三步注册流程原型…" className="h-28 w-full resize-none rounded-xl border border-slate-200 p-3 text-xs leading-5 outline-none focus:border-violet-400"/><div className="mt-2 flex items-center"><span className="text-[10px] text-slate-400">Agent 仅被授权修改 design/</span><button disabled={!chatInput.trim() || !repository || sending} className="ml-auto inline-flex h-8 items-center gap-1.5 rounded-lg bg-violet-600 px-3 text-xs text-white disabled:opacity-40">{sending ? <Loader2 size={13} className="animate-spin"/> : <Send size={13}/>}发送</button></div></form>
        </aside>
      </div>
      {error && <div className="fixed bottom-4 left-1/2 z-50 flex max-w-lg -translate-x-1/2 items-center gap-3 rounded-xl bg-red-600 px-4 py-3 text-xs text-white shadow-xl">{error}<button onClick={() => setError("")}><X size={14}/></button></div>}
      {shareOpen && <ShareDialog url={shareUrl} file={file} onClose={() => setShareOpen(false)}/>} 
    </main>
  </div>;
}

function PrototypeTree({ tree, trees, query, selected, onDirectory, onFile, depth = 0 }: { tree: CodeTree; trees: Record<string, CodeTree>; query: string; selected?: string; onDirectory: (path: string) => void; onFile: (path: string) => void; depth?: number }) {
  const directories = tree.directories.filter((item) => !IGNORED_DIRECTORIES.has(item.name.toLowerCase()) && (!query || item.path.toLowerCase().includes(query.toLowerCase())));
  const files = tree.files.filter((item) => PROTOTYPE_EXTENSIONS.has(`.${item.extension.replace(/^\./, "").toLowerCase()}`) && (!query || item.path.toLowerCase().includes(query.toLowerCase())));
  return <>{directories.map((item) => <div key={item.path}><button onClick={() => onDirectory(item.path)} className="flex h-8 w-full items-center gap-1.5 rounded px-2 text-left text-xs text-slate-600 hover:bg-slate-100" style={{ paddingLeft: 8 + depth * 14 }}>{trees[item.path] ? <ChevronDown size={13}/> : <ChevronRight size={13}/>} {trees[item.path] ? <FolderOpen size={14} className="text-violet-500"/> : <Folder size={14} className="text-violet-500"/>}<span className="truncate">{item.name}</span></button>{trees[item.path] && <PrototypeTree tree={trees[item.path]} trees={trees} query={query} selected={selected} onDirectory={onDirectory} onFile={onFile} depth={depth + 1}/>}</div>)}{files.map((item) => <button key={item.path} onClick={() => onFile(item.path)} className={`flex h-8 w-full items-center gap-2 rounded px-2 text-left text-xs ${selected === item.path ? "bg-violet-50 text-violet-700" : "text-slate-500 hover:bg-slate-100"}`} style={{ paddingLeft: 25 + depth * 14 }}><FileCode2 size={13}/><span className="truncate">{item.name}</span></button>)}</>;
}

function ShareDialog({ url, file, onClose }: { url: string; file: CodeFile | null; onClose: () => void }) {
  const copy = async () => { await navigator.clipboard.writeText(url); };
  const download = () => { if (!file) return; const anchor = document.createElement("a"); anchor.href = URL.createObjectURL(new Blob([file.content], { type: "text/html;charset=utf-8" })); anchor.download = file.path.split("/").pop() ?? "prototype.html"; anchor.click(); URL.revokeObjectURL(anchor.href); };
  return <div className="fixed inset-0 z-[70] grid place-items-center bg-slate-950/45 p-4" onClick={onClose}><section onClick={(event) => event.stopPropagation()} className="w-full max-w-lg rounded-2xl bg-white p-5 shadow-2xl"><div className="flex items-center"><h2 className="font-semibold">分享原型</h2><button onClick={onClose} className="ml-auto"><X size={17}/></button></div><p className="mt-2 text-xs leading-5 text-slate-500">链接面向已登录的团队成员，并始终读取代码库中的当前版本；也可以下载独立 HTML 文件进行评审。</p><div className="mt-4 flex rounded-lg bg-slate-100 p-2"><input readOnly value={url} className="min-w-0 flex-1 bg-transparent px-1 text-xs outline-none"/><button onClick={() => void copy()} className="rounded-md bg-white px-3 py-1.5 text-xs shadow-sm">复制链接</button></div><div className="mt-4 flex justify-end gap-2"><button onClick={download} className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 px-3 py-2 text-xs"><ExternalLink size={13}/>下载 HTML</button><button onClick={() => void copy()} className="rounded-lg bg-slate-900 px-3 py-2 text-xs text-white">分享给团队</button></div></section></div>;
}

function Empty({ loading, text }: { loading: boolean; text: string }) { return <div className="grid h-40 place-items-center text-xs text-slate-400">{loading ? <Loader2 size={16} className="animate-spin"/> : text}</div>; }
function securePreview(content: string) { const policy = `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data: blob:; style-src 'unsafe-inline'; font-src data:; script-src 'unsafe-inline'; media-src data: blob:; connect-src 'none'; frame-src 'none';">`; const clean = content.replace(/<base\b[^>]*>/gi, ""); return /<head(\s[^>]*)?>/i.test(clean) ? clean.replace(/<head(\s[^>]*)?>/i, (match) => `${match}${policy}`) : `${policy}${clean}`; }
function applyChatEvent(event: ChatStreamEvent, setMessages: React.Dispatch<React.SetStateAction<ChatItem[]>>) { if (event.type === "content") setMessages((items) => items.map((item) => item.id === "streaming" ? { ...item, content: item.content + (event.content ?? "") } : item)); if (event.type === "done" || event.type === "completed") setMessages((items) => items.map((item) => item.id === "streaming" ? { ...item, id: crypto.randomUUID(), content: item.content || event.content || "原型已更新。" } : item)); }
function messageOf(value: unknown, fallback: string) { return value instanceof Error ? value.message : fallback; }
