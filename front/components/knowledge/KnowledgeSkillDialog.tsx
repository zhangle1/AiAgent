"use client";

import { useEffect, useRef, useState } from "react";
import { ArrowUp, Loader2, X } from "lucide-react";
import { draftKnowledgeSkill, saveKnowledgeSkill } from "@/lib/knowledge-agent-api";
import type { KnowledgeAgentHistory, KnowledgeAgentModel, KnowledgeCompilationSkill } from "@/lib/knowledge-agent-types";
import styles from "./knowledge-workspace.module.css";

export function KnowledgeSkillDialog({ models, initial, onSaved, onClose }: {
  models: KnowledgeAgentModel[]; initial?: KnowledgeCompilationSkill; onSaved: (skill: KnowledgeCompilationSkill) => void; onClose: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null), request = useRef<AbortController | null>(null);
  const conversation = useRef<HTMLDivElement>(null);
  const [model, setModel] = useState(models.find(m => m.is_default)?.id ?? models[0]?.id ?? "");
  const [history, setHistory] = useState<KnowledgeAgentHistory[]>([]), [input, setInput] = useState("");
  const [draft, setDraft] = useState<KnowledgeCompilationSkill>(initial ? { ...initial, id: initial.builtin ? "" : initial.id, builtin: false } : { id: "", name: "", description: "", instructions: "", builtin: false });
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  useEffect(() => { if (conversation.current) conversation.current.scrollTop = conversation.current.scrollHeight; }, [history]);
  useEffect(() => { const previous = document.activeElement; const element = dialog.current; element?.showModal(); return () => { request.current?.abort(); element?.close(); if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); }; }, []);
  async function generate() {
    if (busy || !input.trim() || !model) return;
    const abort = new AbortController(); request.current = abort; setBusy(true); setError("");
    const message = input.trim();
    try {
      const result = await draftKnowledgeSkill({ model_id: model, message, history, draft: draft.name && draft.description && draft.instructions ? draft : undefined }, abort.signal);
      if (abort.signal.aborted) return;
      setHistory(current => [...current, { role: "user", content: message }, { role: "assistant", content: result.reply }]); setDraft(result.skill); setInput("");
    } catch (ex) { if (!abort.signal.aborted) setError(ex instanceof Error ? ex.message : "技能生成失败"); }
    finally { if (!abort.signal.aborted) setBusy(false); }
  }
  async function save() {
    if (busy) return;
    const abort = new AbortController(); request.current = abort; setBusy(true); setError("");
    try { const saved = await saveKnowledgeSkill(draft, abort.signal); if (!abort.signal.aborted) onSaved(saved); }
    catch (ex) { if (!abort.signal.aborted) setError(ex instanceof Error ? ex.message : "技能保存失败"); }
    finally { if (!abort.signal.aborted) setBusy(false); }
  }
  return <dialog ref={dialog} onCancel={onClose} aria-label="编译技能对话" className={`${styles.dialog} m-auto max-h-[calc(100dvh-32px)] w-[calc(100%-32px)] max-w-4xl overflow-auto rounded-2xl border-0 bg-white p-0 shadow-2xl`}>
    <header className="flex items-center justify-between border-b border-slate-100 px-5 py-4"><div><h2 className="font-semibold">编译技能对话</h2><p className="mt-1 text-xs text-slate-500">描述资料领域、输出结构和保留规则；保存后即可用于编译。</p></div><button type="button" onClick={onClose} aria-label="关闭技能对话" className="p-2 text-slate-400"><X size={18} /></button></header>
    <div className="grid gap-5 p-5 md:grid-cols-2"><section className="flex min-w-0 flex-col gap-3"><select aria-label="技能生成模型" value={model} disabled={busy} onChange={event => setModel(event.target.value)} className="rounded-lg border border-slate-200 p-2 text-xs">{models.map(m => <option key={m.id} value={m.id}>{m.profile} · {m.name}</option>)}</select>
      <div ref={conversation} aria-label="技能生成对话记录" className="max-h-64 min-h-32 space-y-3 overflow-auto rounded-lg bg-slate-50 p-3 text-xs leading-6">{history.length ? history.map((m, i) => <p key={i} className={`whitespace-pre-wrap break-words rounded-lg p-3 ${m.role === "user" ? "ml-4 bg-blue-50" : "mr-4 bg-white"}`}><strong>{m.role === "user" ? "你" : "技能助手"}</strong><br />{m.content}</p>) : <p className="text-slate-500">例如：为 BOS 项目资料生成编译技能，保留插件类型、生命周期、参数和版本约束，并按业务场景组织。</p>}</div>
      <textarea aria-label="技能需求" value={input} maxLength={4000} disabled={busy} onChange={event => setInput(event.target.value)} placeholder="输入需求或继续调整当前技能…" className="min-h-24 rounded-lg border border-slate-200 p-3 text-xs" />
      <div className="flex items-center justify-between"><button type="button" disabled={busy} onClick={() => { setHistory([]); setError(""); }} className="text-xs text-slate-500">清空对话</button><button type="button" onClick={() => void generate()} disabled={busy || !input.trim() || !model || history.length > 20} className="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-3 py-2 text-xs text-white disabled:opacity-40">{busy ? <Loader2 size={13} className="animate-spin" /> : <ArrowUp size={13} />}生成 / 调整技能</button></div>{history.length > 20 && <p className="text-xs text-slate-500">对话已满，请清空对话后继续调整草稿。</p>}
    </section><section className="min-w-0 space-y-3"><label className="block text-xs text-slate-600">技能名称<input aria-label="技能名称" value={draft.name} maxLength={80} disabled={busy} onChange={event => setDraft(d => ({ ...d, name: event.target.value }))} className="mt-1 w-full rounded-lg border border-slate-200 p-2" /></label>
      <label className="block text-xs text-slate-600">用途说明<input aria-label="技能说明" value={draft.description} maxLength={500} disabled={busy} onChange={event => setDraft(d => ({ ...d, description: event.target.value }))} className="mt-1 w-full rounded-lg border border-slate-200 p-2" /></label>
      <label className="block text-xs text-slate-600">SKILL.md 指令正文<textarea aria-label="技能指令" value={draft.instructions} maxLength={10000} disabled={busy} onChange={event => setDraft(d => ({ ...d, instructions: event.target.value }))} className="mt-1 min-h-60 w-full rounded-lg border border-slate-200 p-3 font-mono text-xs leading-5" /></label>
      <p className="text-[11px] leading-5 text-slate-500">草稿可手动编辑。技能只保存提示指令，编译的来源、目标、模型和预算由任务配置决定。</p></section></div>
    <footer className="border-t border-slate-100 p-4">{error && <p role="alert" className="mb-3 rounded-lg bg-rose-50 p-2 text-xs text-rose-700">{error}</p>}<div className="flex justify-end gap-2"><button type="button" onClick={onClose} className="rounded-lg border border-slate-200 px-3 py-2 text-xs">关闭</button><button type="button" onClick={() => void save()} disabled={busy || !draft.name.trim() || !draft.description.trim() || !draft.instructions.trim()} className="rounded-lg bg-blue-600 px-3 py-2 text-xs text-white disabled:opacity-40">保存并用于编译</button></div></footer>
  </dialog>;
}
