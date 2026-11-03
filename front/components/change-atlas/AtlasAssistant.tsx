"use client";

import { ArrowUpRight, Loader2, Sparkles, Square } from "lucide-react";
import type { CodexModelPolicy } from "@/lib/agent-provider-api";
import type { AtlasAnalysis, AtlasEvidence, AtlasModule } from "@/lib/change-atlas-types";

type Props = {
  module?: AtlasModule; scope: string; policy: CodexModelPolicy | null; modelId: string; effort: string;
  question: string; result: AtlasAnalysis | null; error: string; busy: boolean; status: string; canAnalyze: boolean;
  onQuestion: (value: string) => void; onModel: (value: string) => void; onEffort: (value: string) => void;
  onAnalyze: () => void; onCancel: () => void; onEvidence: (value: AtlasEvidence) => void;
};
const input = "w-full min-w-0 rounded-lg border border-slate-200 bg-white px-2 py-2 text-xs text-slate-800";
export function AtlasAssistant(props: Props) {
  const options = props.policy?.models.filter(model => props.policy!.allowed_model_ids.includes(model.id)) || [];
  const selected = options.find(model => model.id === props.modelId);
  const efforts = selected?.reasoning_efforts.filter(value => props.policy?.allowed_reasoning_efforts.includes(value)) || [];
  const claimLabel = { code_observation: "AI 代码解读", inference: "AI 推测", suggestion: "验证建议" };
  return <aside className="min-w-0 rounded-xl border border-slate-200 bg-white p-4" aria-label="AI 变化解读">
    <header className="flex items-center gap-2"><span className="rounded-lg bg-blue-50 p-2 text-blue-600"><Sparkles size={18}/></span><div><h2 className="text-sm font-semibold">和 AI 一起看变化</h2><p className="text-[11px] text-slate-500">Codex · 独立只读分析</p></div></header>
    <p className="mt-4 break-all rounded-lg bg-slate-50 px-3 py-2 text-xs leading-6 text-slate-600">{props.module?.directory || "先选择一个模块"}<br/>{props.scope}</p>
    <div className="mt-3 grid grid-cols-[minmax(0,1fr)_86px] gap-2"><label className="min-w-0 text-[11px] text-slate-500">模型<select aria-label="AI 解读模型" className={`${input} mt-1`} value={props.modelId} disabled={props.busy || props.policy?.allow_chat_model_override === false} onChange={event => props.onModel(event.target.value)}><option value="">平台默认模型</option>{options.map(model => <option key={model.id} value={model.id}>{model.name}</option>)}</select></label><label className="text-[11px] text-slate-500">推理<select aria-label="AI 解读推理等级" className={`${input} mt-1`} value={props.effort} disabled={props.busy || !selected?.supports_reasoning_effort || props.policy?.allow_chat_reasoning_effort_override === false} onChange={event => props.onEffort(event.target.value)}><option value="">默认</option>{efforts.map(effort => <option key={effort} value={effort}>{effort}</option>)}</select></label></div>
    <label className="mt-3 block text-[11px] text-slate-500">围绕当前范围提问<textarea aria-label="AI 变化问题" rows={3} maxLength={2000} disabled={props.busy} value={props.question} onChange={event => props.onQuestion(event.target.value)} className={`${input} mt-1 resize-y leading-6`}/></label>
    <div className="mt-2 flex flex-wrap gap-1.5">{["解释这次变化", "建议一起验证哪些场景？", "从开发维护角度说明这些改动"].map(question => <button type="button" key={question} disabled={props.busy} onClick={() => props.onQuestion(question)} className="rounded-md border border-slate-200 px-2 py-1 text-[11px] text-slate-500 hover:bg-slate-50 disabled:opacity-50">{question}</button>)}</div>
    <div className="mt-3 flex gap-2"><button type="button" disabled={!props.canAnalyze || props.busy || !props.question.trim()} onClick={props.onAnalyze} className="inline-flex flex-1 items-center justify-center gap-2 rounded-lg bg-blue-600 px-3 py-2.5 text-xs font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50">{props.busy ? <Loader2 className="animate-spin" size={15}/> : <Sparkles size={15}/>}{props.busy ? "正在读取证据并解读…" : "解读当前模块"}</button>{props.busy && <button type="button" onClick={props.onCancel} aria-label="停止 AI 解读" className="rounded-lg border border-slate-200 px-3"><Square size={14}/></button>}</div>
    {props.error && <p role="alert" className="mt-3 rounded-lg bg-red-50 p-3 text-xs leading-6 text-red-700">{props.error}</p>}
    {props.busy && <p role="status" className="mt-3 text-xs leading-6 text-blue-600">{props.status || "正在准备 AI 解读…"}</p>}
    {!props.result && !props.busy && <p className="mt-4 text-xs leading-6 text-slate-500">选择模块、日期或人员后，点击解读。AI 会读取这批提交的部分差异，并附可打开的证据引用。</p>}
    {props.result && <div className="mt-5 space-y-4" aria-live="polite"><div><h3 className="text-xs font-medium">变化概括</h3><p className="mt-2 whitespace-pre-wrap break-words text-sm leading-7">{props.result.summary}</p>{props.result.module_label && <p className="mt-2 text-xs text-blue-600">业务名称候选：{props.result.module_label}</p>}</div>{props.result.claims.map((claim, index) => <div key={index} className="border-t border-slate-100 pt-3"><span className={`rounded px-1.5 py-0.5 text-[11px] ${claim.kind === "code_observation" ? "bg-blue-50 text-blue-700" : "bg-amber-50 text-amber-800"}`}>{claimLabel[claim.kind]}</span><p className="mt-2 whitespace-pre-wrap break-words text-xs leading-6">{claim.text}</p><div className="mt-2 flex flex-wrap gap-1">{claim.evidence_ids.map(id => { const evidence = props.result!.evidence.find(item => item.id === id); return evidence ? <button type="button" key={id} onClick={() => props.onEvidence(evidence)} className="inline-flex max-w-full items-center gap-1 rounded bg-slate-50 px-2 py-1 text-left text-[11px] text-blue-600"><span className="min-w-0 break-all">{evidence.path.split('/').pop()} · {evidence.sha.slice(0, 7)}</span><ArrowUpRight size={12} className="shrink-0"/></button> : null; })}</div></div>)}{props.result.checks.length > 0 && <div className="border-t border-slate-100 pt-3"><h3 className="text-xs font-medium">建议验证</h3><ul className="mt-2 list-disc space-y-1 pl-4 text-xs leading-6">{props.result.checks.map((check, index) => <li key={index}>{check}</li>)}</ul></div>}<div className="rounded-lg bg-slate-50 p-3 text-[11px] leading-6 text-slate-500">{props.result.unknowns.map((unknown, index) => <p key={index}>{unknown}</p>)}<p className="mt-2 break-all">模型：{props.result.model} · {new Date(props.result.created_at).toLocaleString()}</p></div></div>}
    <p className="mt-5 border-t border-slate-100 pt-3 text-[11px] leading-6 text-slate-500">AI 的业务含义和影响解释需要核对。推送、合入、部署和测试结果分别判断。</p>
  </aside>;
}
