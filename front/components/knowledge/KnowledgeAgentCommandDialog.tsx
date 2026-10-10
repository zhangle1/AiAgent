"use client";

import { useEffect, useRef, useState } from "react";
import { Loader2, Play, X } from "lucide-react";
import { compileKnowledgeAgent, runKnowledgeAgentCommand } from "@/lib/knowledge-agent-api";
import type { KnowledgeAgentCommand, KnowledgeAgentCommandResult, KnowledgeAgentOptions } from "@/lib/knowledge-agent-types";
import type { KnowledgeCompilerSettings, KnowledgeResourceNode } from "@/lib/knowledge-types";
import styles from "./knowledge-workspace.module.css";

export type KnowledgeAgentDialogCommand = KnowledgeAgentCommand | "compiler";

export function KnowledgeAgentCommandDialog({ command, scope, nodes, options, onClose, onResult, onOpen, onQueued }: {
  command: KnowledgeAgentDialogCommand; scope: string; nodes: KnowledgeResourceNode[]; options: KnowledgeAgentOptions | null;
  onClose: () => void; onResult: (title: string, text: string) => void; onOpen: (uri: string) => void; onQueued: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const request = useRef<AbortController | null>(null);
  const [target, setTarget] = useState(scope);
  const [query, setQuery] = useState("");
  const [offset, setOffset] = useState(0);
  const [result, setResult] = useState<KnowledgeAgentCommandResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const files = nodes.filter(node => node.kind === "file" && (node.uri === scope || (scope.endsWith("/") && node.uri.startsWith(scope))) && !/^viking:\/\/user\/[^/]+\/(summaries|wiki)\//.test(node.uri));
  const [selected, setSelected] = useState<string[]>(files.slice(0, 32).map(file => file.uri));
  const cliModels = options?.cli_policy.models.filter(model => options.cli_policy.allowed_model_ids.includes(model.id) &&
    (options.cli_policy.allow_chat_model_override !== false || model.id === options.cli_policy.default_model_id)) ?? [];
  const [configuration, setConfiguration] = useState<KnowledgeCompilerSettings>(() => ({
    ...(options?.compiler ?? { generator: "llm_api", retrieval_mode: "wiki", max_steps: 48, timeout_minutes: 20 }),
    generator: "llm_api", model_id: options?.llm_models.find(model => model.is_default)?.id ?? options?.llm_models[0]?.id ?? "",
  }));
  const reasoning = cliModels.find(model => model.id === configuration.model_id)?.reasoning_efforts ?? [];

  useEffect(() => {
    const element = dialog.current; const previous = document.activeElement;
    element?.showModal();
    return () => { request.current?.abort(); element?.close(); if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); };
  }, []);

  async function run(nextOffset = offset) {
    request.current?.abort(); const controller = new AbortController(); request.current = controller;
    setBusy(true); setError(""); setNotice("");
    try {
      if (command === "compiler") {
        const response = await compileKnowledgeAgent(selected, configuration, controller.signal);
        if (controller.signal.aborted) return;
        const message = (response.tasks.length ? `${response.tasks.length} 个任务已入队；将依次解析 L2、生成文件摘要及目录 L0/L1。` : "没有新任务入队。") + response.warnings.join("；");
        setNotice(message); onResult(`/compiler ${scope}`, message); if (response.tasks.length) onQueued();
      } else {
        const response = await runKnowledgeAgentCommand({ command, uri: scope, target_uri: target, query, offset: nextOffset, limit: command === "read" ? 3000 : 40 }, controller.signal);
        if (controller.signal.aborted) return;
        setResult(response); setOffset(nextOffset); onResult(`/${command} ${target}`, JSON.stringify(response, null, 2));
      }
    } catch (ex) { if (!controller.signal.aborted) setError(ex instanceof Error ? ex.message : "命令执行失败"); }
    finally { if (!controller.signal.aborted) setBusy(false); }
  }

  return <dialog ref={dialog} onCancel={onClose} aria-labelledby="knowledge-agent-command-title" className={`${styles.dialog} m-auto max-h-[calc(100dvh-32px)] w-[calc(100%-32px)] max-w-3xl overflow-auto rounded-2xl border-0 bg-white p-0 text-slate-900 shadow-2xl`}>
    <header className="flex items-center justify-between border-b border-slate-100 px-5 py-4"><div><h2 id="knowledge-agent-command-title" className="font-semibold">{command === "compiler" ? "编译原始资料 · L0 / L1" : `/${command} · 目录命令`}</h2><p className="mt-1 text-xs text-slate-500">{command === "compiler" ? "选择已配置的 LLM API 或命令行配置，仅用于本批任务" : "读取真实资源，访问范围由后端校验"}</p></div><button type="button" onClick={onClose} aria-label="关闭命令配置" className="rounded-lg p-2 text-slate-400 hover:bg-slate-100"><X size={18} /></button></header>
    <div className="space-y-4 p-5">
      <p className="break-all rounded-lg bg-slate-50 px-3 py-2 font-mono text-[11px] text-slate-500">{scope}</p>
      {command === "compiler" ? <>
        <div className="grid gap-3 sm:grid-cols-2"><label className="text-xs text-slate-600">生成通道<select aria-label="编译生成通道" disabled={busy} value={configuration.generator} onChange={event => {
          const generator = event.target.value as "llm_api" | "codex";
          const modelId = generator === "llm_api" ? options?.llm_models.find(model => model.is_default)?.id ?? options?.llm_models[0]?.id : options?.cli_policy.default_model_id;
          const effort = generator === "codex" ? options?.cli_policy.default_reasoning_effort : undefined;
          setConfiguration(current => ({ ...current, generator, model_id: modelId, reasoning_effort: effort }));
        }} className="mt-1 w-full rounded-lg border border-slate-200 bg-white p-2 text-sm"><option value="llm_api">LLM API</option><option value="codex">配置的 Codex / CLI Profile</option></select></label>
        <label className="text-xs text-slate-600">{configuration.generator === "codex" ? "命令行模型 / Profile" : "LLM API 模型"}<select aria-label="编译模型" disabled={busy} value={configuration.model_id ?? ""} onChange={event => {
          const model = cliModels.find(item => item.id === event.target.value);
          setConfiguration(current => ({ ...current, model_id: event.target.value, reasoning_effort: model?.reasoning_efforts.includes(current.reasoning_effort ?? "") ? current.reasoning_effort : model?.reasoning_efforts[0] }));
        }} className="mt-1 w-full rounded-lg border border-slate-200 bg-white p-2 text-sm"><option value="" disabled>请选择已配置模型</option>{configuration.generator === "llm_api" ? options?.llm_models.map(model => <option key={model.id} value={model.id}>{model.profile} · {model.name}</option>) : cliModels.map(model => <option key={model.id} value={model.id}>{model.name}{model.profile_name ? ` · ${model.profile_name}` : ""}</option>)}</select></label></div>
        <div className="grid gap-3 sm:grid-cols-3">{configuration.generator === "codex" && reasoning.length > 0 && <label className="text-xs text-slate-600">推理等级<select aria-label="编译推理等级" disabled={busy || options?.cli_policy.allow_chat_reasoning_effort_override === false} value={configuration.reasoning_effort ?? reasoning[0]} onChange={event => setConfiguration(current => ({ ...current, reasoning_effort: event.target.value }))} className="mt-1 w-full rounded-lg border border-slate-200 p-2 text-sm">{reasoning.map(value => <option key={value}>{value}</option>)}</select></label>}<label className="text-xs text-slate-600">模型调用上限<input aria-label="编译调用上限" type="number" min={8} max={96} disabled={busy} value={configuration.max_steps} onChange={event => setConfiguration(current => ({ ...current, max_steps: Number(event.target.value) }))} className="mt-1 w-full rounded-lg border border-slate-200 p-2 text-sm" /></label><label className="text-xs text-slate-600">超时（分钟）<input aria-label="编译超时" type="number" min={1} max={60} disabled={busy} value={configuration.timeout_minutes} onChange={event => setConfiguration(current => ({ ...current, timeout_minutes: Number(event.target.value) }))} className="mt-1 w-full rounded-lg border border-slate-200 p-2 text-sm" /></label></div>
        <fieldset className="rounded-lg border border-slate-200"><legend className="px-2 text-xs text-slate-500">原始文件 · 已选 {selected.length}/32</legend><div className="max-h-56 overflow-auto p-2">{files.length ? files.map(file => <label key={file.uri} className="flex items-center gap-2 rounded-md px-2 py-2 text-xs hover:bg-slate-50"><input type="checkbox" checked={selected.includes(file.uri)} disabled={busy || (!selected.includes(file.uri) && selected.length >= 32)} onChange={event => setSelected(current => event.target.checked ? [...current, file.uri] : current.filter(uri => uri !== file.uri))} /><span className="min-w-0 truncate" title={file.uri}>{file.name}</span></label>) : <p className="p-4 text-xs text-slate-400">当前范围没有可编译的原始文件，请选择资源目录或上传资料。</p>}</div></fieldset>
        <p className="text-xs leading-5 text-slate-500">CLI 沿用服务器已配置的模型/Profile，以受控只读工作目录运行。摘要和 L0/L1 发布到本人工作区；进度、错误与重试在任务中心查看。</p>
        <code className="block break-all rounded-lg bg-slate-900 p-3 text-[11px] text-slate-200">compiler {scope} --generator {configuration.generator} --model {configuration.model_id || "未选择"} --files {selected.length}</code>
      </> : <>
        <label className="block text-xs text-slate-600">目标 URI<input aria-label="命令目标 URI" value={target} onChange={event => { setTarget(event.target.value); setOffset(0); setResult(null); }} className="mt-1 w-full rounded-lg border border-slate-200 p-2 font-mono text-xs" /></label>
        {command === "search" && <label className="block text-xs text-slate-600">搜索词<input aria-label="资源搜索词" value={query} onChange={event => setQuery(event.target.value)} className="mt-1 w-full rounded-lg border border-slate-200 p-2 text-sm" /></label>}
        {result && <div className="max-h-80 overflow-auto rounded-lg border border-slate-200 p-3 text-xs">{result.nodes ? <><p className="mb-2 text-slate-500">共 {result.total} 项 · 从 {offset} 开始</p>{result.nodes.map(node => <button key={node.uri} type="button" onClick={() => onOpen(node.uri)} className="block w-full truncate rounded px-2 py-2 text-left text-blue-600 hover:bg-blue-50">{node.kind === "directory" ? "目录" : "文件"} · {node.name}</button>)}</> : <pre className="whitespace-pre-wrap break-words">{result.content ?? JSON.stringify(result, null, 2)}</pre>}{result.next_offset != null && <button type="button" disabled={busy} onClick={() => void run(result.next_offset!)} className="mt-3 text-blue-600">读取下一页 →</button>}</div>}
      </>}
      {error && <p role="alert" className="rounded-lg bg-rose-50 p-3 text-xs text-rose-700">{error}</p>}
      {notice && <p role="status" className="rounded-lg bg-blue-50 p-3 text-xs leading-5 text-blue-700">{notice} <a href="/task-center?domain=knowledge" className="underline">查看任务中心</a></p>}
      <footer className="flex justify-end gap-2 border-t border-slate-100 pt-4"><button type="button" onClick={onClose} className="rounded-lg border border-slate-200 px-3 py-2 text-xs">关闭</button><button type="button" onClick={() => void run()} disabled={busy || (command === "compiler" && (!selected.length || !configuration.model_id || !options))} className="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-xs text-white disabled:opacity-40">{busy ? <Loader2 size={14} className="animate-spin" /> : <Play size={14} />}{command === "compiler" ? "提交编译任务" : "执行命令"}</button></footer>
    </div>
  </dialog>;
}
