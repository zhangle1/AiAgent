"use client";

import { useEffect, useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import { ArrowUp, Cpu, Loader2, Plus, Square, Terminal, Wrench } from "lucide-react";
import { getKnowledgeAgentOptions, streamKnowledgeAgent } from "@/lib/knowledge-agent-api";
import type { KnowledgeAgentContext, KnowledgeAgentHistory, KnowledgeAgentOptions } from "@/lib/knowledge-agent-types";
import type { KnowledgeResourceNode } from "@/lib/knowledge-types";
import { KnowledgeAgentCommandDialog, type KnowledgeAgentDialogCommand } from "./KnowledgeAgentCommandDialog";

type ToolItem = { id: string; name: string; arguments: string; result: string; status: "running" | "success" | "error" };
type Message = { id: string; role: "user" | "assistant" | "command"; text: string; thinking?: string; tools: ToolItem[]; complete?: boolean; error?: string };
const commands: KnowledgeAgentDialogCommand[] = ["ls", "read", "search", "status", "compiler"];

export function KnowledgeAgentChatPanel({ scope, nodes, onOpen, onQueued }: { scope: string; nodes: KnowledgeResourceNode[]; onOpen: (uri: string) => void; onQueued: () => void }) {
  const [options, setOptions] = useState<KnowledgeAgentOptions | null>(null);
  const [modelId, setModelId] = useState("");
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState("");
  const [running, setRunning] = useState(false);
  const [usage, setUsage] = useState<KnowledgeAgentContext | null>(null);
  const [phase, setPhase] = useState("");
  const [error, setError] = useState("");
  const [dialog, setDialog] = useState<KnowledgeAgentDialogCommand | null>(null);
  const [menu, setMenu] = useState(false);
  const controller = useRef<AbortController | null>(null);
  const scroll = useRef<HTMLDivElement>(null);
  const follow = useRef(true);
  const model = options?.llm_models.find(item => item.id === modelId);

  useEffect(() => {
    const abort = new AbortController();
    getKnowledgeAgentOptions(abort.signal).then(value => {
      if (!Array.isArray(value.llm_models)) throw new Error("模型目录返回格式不正确。");
      setOptions(value);
      setModelId(value.llm_models.find(item => item.is_default)?.id ?? value.llm_models[0]?.id ?? "");
    }).catch(ex => { if (!abort.signal.aborted) setError(ex instanceof Error ? ex.message : "模型目录加载失败"); });
    return () => abort.abort();
  }, []);

  useEffect(() => {
    controller.current?.abort(); controller.current = null;
    setMessages([]); setUsage(null); setRunning(false); setPhase(""); setInput(""); setDialog(null);
    follow.current = true;
    return () => { controller.current?.abort(); };
  }, [scope]);
  useEffect(() => { if (follow.current && scroll.current) scroll.current.scrollTop = scroll.current.scrollHeight; }, [messages, phase]);

  function stop() {
    controller.current?.abort(); controller.current = null;
    setRunning(false); setPhase("已停止");
    setMessages(current => current.map(message => message.role === "assistant" && !message.complete && !message.error ? { ...message, error: "已停止，保留收到的内容。", tools: message.tools.map(tool => tool.status === "running" ? { ...tool, status: "error", result: "已停止" } : tool) } : message));
  }
  function newConversation() { stop(); setMessages([]); setUsage(null); setPhase(""); setError(""); follow.current = true; }
  function command(value: KnowledgeAgentDialogCommand) { setMenu(false); setDialog(value); }
  function update(id: string, change: (message: Message) => Message) { setMessages(current => current.map(message => message.id === id ? change(message) : message)); }

  async function send() {
    if (controller.current) return;
    const question = input.trim(); if (!question) return;
    const match = question.match(/^\/?(ls|read|search|status|compiler|compile|parse)(?:\s|$)/);
    if (match) { command(["compile", "parse"].includes(match[1]) ? "compiler" : match[1] as KnowledgeAgentDialogCommand); setInput(""); return; }
    if (question.startsWith("/")) { setError("未知命令，请选择 /ls、/read、/search、/status 或 /compiler。"); return; }
    if (!modelId) { setError("请先选择已配置的 LLM API 模型。"); return; }
    const history: KnowledgeAgentHistory[] = messages.filter(message => message.role !== "command" && (message.role === "user" || message.complete)).map(message => ({ role: message.role as "user" | "assistant", content: message.text }));
    const abort = new AbortController(); controller.current = abort;
    const id = crypto.randomUUID();
    setMessages(current => [...current, { id: crypto.randomUUID(), role: "user", text: question, tools: [] }, { id, role: "assistant", text: "", tools: [] }]);
    setInput(""); setError(""); setRunning(true); setPhase("正在连接 LLM API…"); follow.current = true;
    try {
      await streamKnowledgeAgent({ uri: scope, model_id: modelId, message: question, history }, event => {
        if (abort.signal.aborted || controller.current !== abort) return;
        const metadata = event.metadata ?? {};
        if (event.type === "content") { setPhase("正在生成…"); update(id, message => ({ ...message, text: message.text + (event.content ?? "") })); }
        if (event.type === "thinking") update(id, message => ({ ...message, thinking: (message.thinking ?? "") + (event.content ?? "") }));
        if (["step", "compacting", "capability"].includes(event.type)) setPhase(event.content ?? "");
        if (["context", "context_compacted"].includes(event.type)) { setUsage(metadata as unknown as KnowledgeAgentContext); if (event.type === "context_compacted") setPhase("上下文已压缩，继续处理…"); }
        if (event.type === "tool") update(id, message => ({ ...message, tools: [...message.tools, { id: String(metadata.call_id), name: String(metadata.name), arguments: String(metadata.arguments ?? "{}"), result: "", status: "running" }] }));
        if (event.type === "tool_result") update(id, message => ({ ...message, tools: message.tools.map(tool => tool.id === metadata.call_id ? { ...tool, result: event.content ?? "", status: metadata.success ? "success" : "error" } : tool) }));
        if (event.type === "done") { update(id, message => ({ ...message, complete: true })); setPhase("本轮完成"); }
      }, abort.signal);
    } catch (ex) {
      if (!abort.signal.aborted && controller.current === abort) {
        const detail = ex instanceof Error ? ex.message : "知识 Agent 请求失败";
        update(id, message => ({ ...message, error: detail, tools: message.tools.map(tool => tool.status === "running" ? { ...tool, status: "error", result: detail } : tool) })); setPhase("本轮失败");
      }
    } finally { if (controller.current === abort) { controller.current = null; setRunning(false); } }
  }

  const used = usage ? Math.min(100, Math.round(usage.estimated_input_tokens / Math.max(1, usage.input_limit) * 100)) : 0;
  return <aside className="flex min-h-0 min-w-0 flex-col bg-white">
    <header className="flex h-12 shrink-0 items-center gap-2 border-b border-slate-200 px-4"><Terminal size={15} className="text-slate-500" /><span className="text-xs font-semibold">KnowAgent</span><span className="rounded-full bg-blue-50 px-2 py-0.5 text-[10px] text-blue-700">LLM API</span><button type="button" onClick={newConversation} className="ml-auto inline-flex items-center gap-1 rounded-md px-2 py-1 text-[11px] text-slate-500 hover:bg-slate-100"><Plus size={13} />新对话</button></header>
    <div ref={scroll} onScroll={() => { const box = scroll.current; if (box) follow.current = box.scrollHeight - box.scrollTop - box.clientHeight < 80; }} className="min-h-0 flex-1 overflow-auto bg-slate-50 px-4 py-5">
      {!messages.length && <div className="mx-auto mt-10 max-w-sm rounded-2xl border border-slate-200 bg-white p-6 text-center"><Cpu size={26} className="mx-auto text-blue-600" /><h2 className="mt-4 text-lg font-semibold">你想了解哪些资料？</h2><p className="mt-2 text-xs leading-6 text-slate-500">选择 LLM API，围绕当前目录流式问答。Agent 可按需列目录、搜索、读取正文和检查处理状态。</p><p className="mt-3 text-[11px] text-slate-400">/compiler 可配置原始文件的 L0 / L1 编译任务</p></div>}
      <div className="space-y-4">{messages.map(message => <article key={message.id} className={message.role === "user" ? "ml-8 rounded-xl bg-blue-600 px-4 py-3 text-sm text-white" : "rounded-xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-800"}>
        <div className="mb-2 text-[10px] font-medium opacity-60">{message.role === "user" ? "你" : message.role === "command" ? "目录命令" : "KnowAgent"}</div>
        {message.thinking && <details className="mb-2 text-xs text-slate-500"><summary className="cursor-pointer">模型思考</summary><p className="mt-2 whitespace-pre-wrap break-words">{message.thinking}</p></details>}
        {message.tools.map(tool => <details key={tool.id} className="mb-3 rounded-lg border border-slate-200 bg-slate-50 p-2 text-xs"><summary className="cursor-pointer"><Wrench size={12} className="mr-1 inline" />{tool.name} <span className={tool.status === "error" ? "text-rose-600" : "text-slate-400"}>{tool.status === "running" ? "执行中" : tool.status === "error" ? "失败" : "已完成"}</span></summary><pre className="mt-2 whitespace-pre-wrap break-words text-[11px] leading-5">{tool.arguments}{tool.result && `\n${tool.result}`}</pre></details>)}
        <div className="break-words text-sm leading-7 [&_pre]:whitespace-pre-wrap [&_table]:block [&_table]:overflow-auto"><ReactMarkdown urlTransform={url => url.startsWith("viking://") ? url : ""} components={{ img: () => null, a: ({ href, children }) => href?.startsWith("viking://") ? <button type="button" onClick={() => onOpen(href)} className="text-blue-600 underline">{children}</button> : <span>{children}</span> }}>{message.text}</ReactMarkdown></div>
        {message.error && <p role="alert" className="mt-2 rounded-lg bg-rose-50 p-2 text-xs text-rose-700">{message.error}</p>}
      </article>)}</div>
    </div>
    <div className="shrink-0 border-t border-slate-200 bg-white p-3">
      <div className="mb-2 flex items-center justify-between gap-2 text-[10px] text-slate-500"><span title={usage ? `使用 UTF-8 字节数作 token 上界估算；输出预留 ${usage.output_reserve}，协议预留 ${usage.reserve}；非供应商计费统计` : "上下文使用量为估算，非供应商计费统计"}>{usage ? `上下文 ≈ ${usage.estimated_input_tokens.toLocaleString()} / ${usage.input_limit.toLocaleString()}（窗口 ${usage.context_window.toLocaleString()}）` : model ? `模型窗口 ${model.context_window.toLocaleString()} · 尚未调用` : "请选择已配置的 LLM API"}</span><span title={usage ? `最近一次压缩：${usage.compression_before} → ${usage.compression_after}；当前输入用量约 ${used}%` : "尚未压缩"}>{usage ? `压缩 ${Math.round(usage.compression_ratio * 100)}% · ${usage.compactions} 次` : "压缩 0 次"}</span></div>
      <progress aria-label="上下文使用比例（估算）" value={used} max={100} className="mb-2 h-1 w-full accent-blue-500" />
      <div className="mb-2 flex flex-wrap gap-1.5">{commands.map(value => <button key={value} type="button" disabled={running} onClick={() => command(value)} className="rounded-md border border-slate-200 px-2 py-1 font-mono text-[10px] text-slate-500 hover:border-blue-300 disabled:opacity-40">/{value}</button>)}</div>
      <div className="relative rounded-xl border border-slate-200 bg-slate-50 p-3 focus-within:border-blue-300">
        <button type="button" title={scope} onClick={() => command("ls")} disabled={running} className="mb-2 block max-w-full truncate rounded-md bg-white px-2 py-1 font-mono text-[10px] text-slate-500">{scope} ›</button>
        <textarea aria-label="知识 Agent 输入" value={input} onChange={event => { setInput(event.target.value); setMenu(event.target.value === "/"); }} onKeyDown={event => { if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) { event.preventDefault(); void send(); } }} rows={3} maxLength={4000} placeholder="询问当前目录，或输入 / 选择命令…" className="w-full resize-none bg-transparent text-sm leading-6 outline-none" />
        {menu && <div className="absolute bottom-full left-0 z-20 mb-2 w-64 rounded-xl border border-slate-200 bg-white p-2 shadow-lg">{commands.map(value => <button key={value} type="button" onClick={() => { command(value); setInput(""); }} className="block w-full rounded-lg px-3 py-2 text-left text-xs hover:bg-blue-50">/{value} <span className="ml-2 text-slate-400">{value === "compiler" ? "配置编译任务" : "打开目录命令"}</span></button>)}</div>}
        <div className="mt-2 flex items-center gap-2 border-t border-slate-200 pt-2"><select aria-label="知识 Agent LLM API 模型" disabled={running} value={modelId} onChange={event => { setModelId(event.target.value); setUsage(null); }} className="min-w-0 flex-1 bg-transparent text-[11px] text-slate-600 outline-none"><option value="" disabled>选择已配置 LLM API</option>{options?.llm_models.map(item => <option key={item.id} value={item.id}>{item.profile} · {item.name}{item.native_tools ? " · 工具" : " · 文字"}</option>)}</select>{running ? <button type="button" onClick={stop} aria-label="停止知识 Agent" className="rounded-lg bg-slate-900 p-2 text-white"><Square size={15} /></button> : <button type="button" onClick={() => void send()} disabled={!input.trim() || (!modelId && !input.startsWith("/"))} aria-label="发送知识问题" className="rounded-lg bg-blue-600 p-2 text-white disabled:opacity-30"><ArrowUp size={16} /></button>}</div>
      </div>
      {error && <p role="alert" className="mt-2 text-xs text-rose-600">{error}</p>}
      <p role="status" className="mt-2 flex items-center gap-1 text-[10px] text-slate-400">{running && <Loader2 size={11} className="animate-spin" />}{phase || (model?.native_tools ? "原生工具已启用 · Ctrl/⌘ + Enter 发送" : "当前模型未启用原生工具，可使用目录命令")} {usage && `· 调用 ${usage.model_calls}/${usage.model_call_limit}`}</p>
    </div>
    {dialog && <KnowledgeAgentCommandDialog command={dialog} scope={scope} nodes={nodes} options={options} onClose={() => setDialog(null)} onOpen={uri => { setDialog(null); onOpen(uri); }} onQueued={onQueued} onResult={(title, text) => setMessages(current => [...current, { id: crypto.randomUUID(), role: "command", text: `${title}\n\n${text}`, tools: [], complete: true }])} />}
  </aside>;
}
