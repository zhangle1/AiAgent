"use client";

import { useEffect, useRef, useState } from "react";
import { FolderOpen, Loader2, Play, X } from "lucide-react";
import { compileKnowledgeAgent, runKnowledgeAgentCommand } from "@/lib/knowledge-agent-api";
import type { KnowledgeAgentCommand, KnowledgeAgentCommandResult, KnowledgeAgentOptions, KnowledgeCompilationSkill } from "@/lib/knowledge-agent-types";
import type { KnowledgeCompilerSettings, KnowledgeResourceNode } from "@/lib/knowledge-types";
import styles from "./knowledge-workspace.module.css";
import { isGeneratedResource, KnowledgeResourcePicker, KnowledgeResourceTree } from "./KnowledgeResourcePicker";
import { KnowledgeSkillDialog } from "./KnowledgeSkillDialog";

export type KnowledgeAgentDialogCommand = KnowledgeAgentCommand | "compiler";
export const knowledgeAgentCommandLabels = { ls: "浏览目录", read: "读取正文", search: "搜索资料", status: "处理状态", compiler: "编译资料" };

export function KnowledgeAgentCommandDialog({ command, scope, nodes, options, onClose, onResult, onOpen, onQueued, onSkillsChanged }: {
  command: KnowledgeAgentDialogCommand; scope: string; nodes: KnowledgeResourceNode[]; options: KnowledgeAgentOptions | null;
  onClose: () => void; onResult: (title: string, text: string) => void; onOpen: (uri: string) => void; onQueued: () => void;
  onSkillsChanged: () => void;
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
  const [picker, setPicker] = useState<"command" | "destination" | null>(null);
  const [skillDialog, setSkillDialog] = useState(false);
  const [skills, setSkills] = useState<KnowledgeCompilationSkill[]>(options?.skills ?? []);
  const sourceNodes = nodes.filter(node => !isGeneratedResource(node.uri));
  const files = sourceNodes.filter(node => node.kind === "file");
  const [selected, setSelected] = useState<string[]>(files.filter(node => node.uri === scope || scope.endsWith("/") && node.uri.startsWith(scope)).slice(0, 32).map(file => file.uri));
  const cliModels = options?.cli_policy.models.filter(model => options.cli_policy.allowed_model_ids.includes(model.id) &&
    (options.cli_policy.allow_chat_model_override !== false || model.id === options.cli_policy.default_model_id)) ?? [];
  const [configuration, setConfiguration] = useState<KnowledgeCompilerSettings>(() => ({
    ...(options?.compiler ?? { generator: "llm_api", retrieval_mode: "wiki", max_steps: 0, timeout_minutes: 20 }),
    generator: "llm_api", model_id: options?.llm_models.find(model => model.is_default)?.id ?? options?.llm_models[0]?.id ?? "",
    target_uri: null, skill_id: "llm-wiki", max_steps: 0,
  }));
  const reasoning = cliModels.find(model => model.id === configuration.model_id)?.reasoning_efforts ?? [];
  const compilerWindow = options?.llm_models.find(model => model.id === configuration.model_id)?.context_window;

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
    <header className="flex items-center justify-between border-b border-slate-100 px-5 py-4"><div><h2 id="knowledge-agent-command-title" className="font-semibold">{command === "compiler" ? "编译原始资料 · L0 / L1" : `${knowledgeAgentCommandLabels[command]} · /${command}`}</h2><p className="mt-1 text-xs text-slate-500">{command === "compiler" ? "选择来源、产物目录和编译技能，仅用于本批任务" : "读取真实资源，访问范围由后端校验"}</p></div><button type="button" onClick={onClose} aria-label="关闭命令配置" className="rounded-lg p-2 text-slate-400 hover:bg-slate-100"><X size={18} /></button></header>
    <div className="space-y-4 p-5">
      <p className="break-all rounded-lg bg-slate-50 px-3 py-2 font-mono text-[11px] text-slate-500">{scope}</p>
      {command === "compiler" ? <>
        <section className="space-y-2"><label className="block text-xs text-slate-600">产物保存位置</label><div className="flex min-w-0 flex-wrap items-center gap-2"><p className="min-w-0 flex-1 break-all rounded-lg border border-slate-200 bg-slate-50 p-2 text-xs">{configuration.target_uri ? decodeURI(configuration.target_uri) : "人员 → 我的工作区 → 摘要（默认）"}</p><button type="button" disabled={busy} onClick={() => setPicker("destination")} className="inline-flex items-center gap-1 rounded-lg border border-slate-200 px-3 py-2 text-xs"><FolderOpen size={13} />选择产物目录</button>{configuration.target_uri && <button type="button" disabled={busy} onClick={() => setConfiguration(c => ({ ...c, target_uri: null }))} className="text-xs text-slate-500">恢复默认</button>}</div><p className="text-[11px] leading-5 text-slate-500">选择目录后，产物保存在其“编译产物”子目录。资料 / 项目目录沿用公共资源可见范围；个人来源只能保存到本人目录。</p></section>
        <section className="space-y-2"><div className="flex items-center gap-2"><label className="min-w-0 flex-1 text-xs text-slate-600">编译技能<select aria-label="编译技能" disabled={busy || !skills.length} value={configuration.skill_id ?? "llm-wiki"} onChange={event => setConfiguration(c => ({ ...c, skill_id: event.target.value }))} className="mt-1 w-full rounded-lg border border-slate-200 bg-white p-2 text-sm">{skills.map(skill => <option key={skill.id} value={skill.id}>{skill.name}{skill.builtin ? " · 内置" : ""}</option>)}</select></label><button type="button" disabled={busy} onClick={() => setSkillDialog(true)} className="mt-4 rounded-lg border border-slate-200 px-3 py-2 text-xs">生成 / 编辑技能</button></div><p className="text-[11px] text-slate-500">{skills.find(skill => skill.id === configuration.skill_id)?.description ?? "技能目录尚未加载，请确认后端已更新后刷新页面。"}</p></section>
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
        <div className="grid gap-3 sm:grid-cols-3">{configuration.generator === "codex" && reasoning.length > 0 && <label className="text-xs text-slate-600">推理等级<select aria-label="编译推理等级" disabled={busy || options?.cli_policy.allow_chat_reasoning_effort_override === false} value={configuration.reasoning_effort ?? reasoning[0]} onChange={event => setConfiguration(current => ({ ...current, reasoning_effort: event.target.value }))} className="mt-1 w-full rounded-lg border border-slate-200 p-2 text-sm">{reasoning.map(value => <option key={value}>{value}</option>)}</select></label>}<label className="text-xs text-slate-600">调用次数上限（0 = 不限）<input aria-label="编译调用上限" type="number" min={0} disabled={busy} value={configuration.max_steps} onChange={event => setConfiguration(current => ({ ...current, max_steps: Number(event.target.value) }))} className="mt-1 w-full rounded-lg border border-slate-200 p-2 text-sm" /></label><label className="text-xs text-slate-600">超时（分钟）<input aria-label="编译超时" type="number" min={1} max={60} disabled={busy} value={configuration.timeout_minutes} onChange={event => setConfiguration(current => ({ ...current, timeout_minutes: Number(event.target.value) }))} className="mt-1 w-full rounded-lg border border-slate-200 p-2 text-sm" /></label></div>
        <fieldset className="min-w-0 rounded-lg border border-slate-200 p-3"><legend className="px-2 text-xs text-slate-500">原始文件 · 已选 {selected.length}/32</legend><KnowledgeResourceTree nodes={sourceNodes} selected={selected} multiple label="编译来源" onSelect={uri => { if (busy) return; setSelected(current => current.includes(uri) ? current.filter(item => item !== uri) : current.length < 32 ? [...current, uri] : current); }} /><p className="mt-2 text-[11px] text-slate-500">筛选不会清除已选文件。{selected.map(uri => decodeURI(uri).split('/').pop()).join("、")}</p></fieldset>
        <p className="text-xs leading-5 text-slate-500">CLI 沿用服务器已配置的模型/Profile，以受控只读工作目录运行。任务保存本批技能和目录快照；进度、错误与重试在任务中心查看。</p>
        <p className="text-xs leading-5 text-slate-500">默认 0 不限制调用次数，可手动填写正整数。{configuration.generator === "llm_api" && compilerWindow ? `当前模型配置窗口 ${compilerWindow.toLocaleString()} token，` : ""}L0/L1 按可用上下文自动分段与归并，输出采用模型服务默认限制。</p>
        <code className="block break-all rounded-lg bg-slate-900 p-3 text-[11px] text-slate-200">compiler {scope} --generator {configuration.generator} --model {configuration.model_id || "未选择"} --skill {configuration.skill_id || "llm-wiki"} --target {configuration.target_uri || "personal-summaries"} --files {selected.length}</code>
      </> : <>
        <label className="block text-xs text-slate-600">目标资源<div className="mt-1 flex min-w-0 gap-2"><input aria-label="命令目标 URI" value={target} onChange={event => { setTarget(event.target.value); setOffset(0); setResult(null); }} className="min-w-0 flex-1 rounded-lg border border-slate-200 p-2 font-mono text-xs" /><button type="button" disabled={busy} onClick={() => setPicker("command")} className="inline-flex shrink-0 items-center gap-1 rounded-lg border border-slate-200 px-3 py-2"><FolderOpen size={13} />选择资源</button></div></label>
        {command === "search" && <label className="block text-xs text-slate-600">搜索词<input aria-label="资源搜索词" value={query} onChange={event => setQuery(event.target.value)} className="mt-1 w-full rounded-lg border border-slate-200 p-2 text-sm" /></label>}
        {result && <div className="max-h-80 overflow-auto rounded-lg border border-slate-200 p-3 text-xs">{result.nodes ? <><p className="mb-2 text-slate-500">共 {result.total} 项 · 从 {offset} 开始</p>{result.nodes.map(node => <button key={node.uri} type="button" onClick={() => onOpen(node.uri)} className="block w-full truncate rounded px-2 py-2 text-left text-blue-600 hover:bg-blue-50">{node.kind === "directory" ? "目录" : "文件"} · {node.name}</button>)}</> : <pre className="whitespace-pre-wrap break-words">{result.content ?? JSON.stringify(result, null, 2)}</pre>}{result.next_offset != null && <button type="button" disabled={busy} onClick={() => void run(result.next_offset!)} className="mt-3 text-blue-600">读取下一页 →</button>}</div>}
      </>}
      {error && <p role="alert" className="rounded-lg bg-rose-50 p-3 text-xs text-rose-700">{error}</p>}
      {notice && <p role="status" className="rounded-lg bg-blue-50 p-3 text-xs leading-5 text-blue-700">{notice} <a href="/task-center?domain=knowledge" className="underline">查看任务中心</a></p>}
      <footer className="flex justify-end gap-2 border-t border-slate-100 pt-4"><button type="button" onClick={onClose} className="rounded-lg border border-slate-200 px-3 py-2 text-xs">关闭</button><button type="button" onClick={() => void run()} disabled={busy || (command === "compiler" && (!selected.length || !configuration.model_id || !options))} className="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-xs text-white disabled:opacity-40">{busy ? <Loader2 size={14} className="animate-spin" /> : <Play size={14} />}{command === "compiler" ? "提交编译任务" : "执行命令"}</button></footer>
    </div>
    {picker && <KnowledgeResourcePicker label={picker === "destination" ? "选择产物目录" : "选择命令目标"} nodes={picker === "destination" ? sourceNodes : nodes.filter(n => n.uri === scope || scope.endsWith("/") && n.uri.startsWith(scope))} value={picker === "destination" ? configuration.target_uri ?? scope : target} directoriesOnly={picker === "destination" || command === "ls"} onClose={() => setPicker(null)} onSelect={uri => { if (picker === "destination") setConfiguration(c => ({ ...c, target_uri: uri })); else { setTarget(uri); setOffset(0); setResult(null); } setPicker(null); }} />}
    {skillDialog && <KnowledgeSkillDialog models={options?.llm_models ?? []} initial={skills.find(skill => skill.id === configuration.skill_id)} onClose={() => setSkillDialog(false)} onSaved={skill => { setSkills(current => [...current.filter(s => s.id !== skill.id), skill]); setConfiguration(c => ({ ...c, skill_id: skill.id })); setSkillDialog(false); onSkillsChanged(); }} />}
  </dialog>;
}
