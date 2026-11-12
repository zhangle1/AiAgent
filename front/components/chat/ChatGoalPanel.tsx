"use client";

import { useEffect, useRef, useState } from "react";
import { Target, X } from "lucide-react";
import { controlChatGoal, getChatGoal } from "@/lib/chat-goal-api";
import { CHAT_GOAL_TOKEN_BUDGET, type ChatGoal, type ChatGoalRequest } from "@/lib/chat-goal-types";

const labels: Record<string, string> = { active: "执行中", paused: "已暂停", blocked: "等待补充", complete: "已完成", ended: "已结束", budgetLimited: "预算耗尽", usageLimited: "用量受限" };

export function ChatGoalPanel({ sessionId, enabled, sending, onChange, activation = 0, submission = 0 }: {
  sessionId: string | null; enabled: boolean; sending: boolean; onChange: (value: ChatGoalRequest | undefined) => void; activation?: number; submission?: number;
}) {
  const [goal, setGoal] = useState<ChatGoal | null>(null);
  const [selection, setSelection] = useState<number | null>(null);
  const selected = selection === submission;
  const [objective, setObjective] = useState("");
  const [editing, setEditing] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const seen = useRef(activation);
  const controlling = useRef(false);
  useEffect(() => { setSelection(null); setObjective(""); setGoal(null); setError(""); setEditing(false); }, [sessionId]);
  useEffect(() => {
    if (!sessionId) { setLoading(false); return; }
    let alive = true;
    let refreshing = false;
    setLoading(true);
    const refresh = async () => {
      if (refreshing) return;
      refreshing = true;
      try { const value = await getChatGoal(sessionId); if (alive) { setGoal(previous => previous && (!value || previous.version > value.version) ? previous : value); setError(""); setLoading(false); } }
      catch (reason) { if (alive) { setError(reason instanceof Error ? reason.message : "读取目标失败"); setLoading(false); } }
      finally { refreshing = false; }
    };
    void refresh();
    const timer = window.setInterval(() => void refresh(), 2000);
    return () => { alive = false; window.clearInterval(timer); };
  }, [sessionId]);
  useEffect(() => { setSelection(null); setObjective(""); setEditing(false); }, [submission]);
  const current = goal && !["complete", "ended"].includes(goal.status) ? goal : null;
  useEffect(() => {
    if (seen.current === activation || loading) return;
    seen.current = activation;
    if (!enabled) return;
    setEditing(true);
  }, [activation, enabled, loading, current]);
  useEffect(() => { if (goal?.status === "complete" || goal?.status === "ended") setSelection(null); }, [goal?.status]);
  useEffect(() => { if (!enabled) { setSelection(null); setEditing(false); } }, [enabled]);
  useEffect(() => {
    if (!selected || !enabled) { onChange(undefined); return; }
    if (loading || error) { onChange({ action: "start", objective: "", token_budget: 0 }); return; }
    onChange(current ? { action: "resume", expected_version: current.version }
      : { action: "start", objective: objective.trim(), expected_version: goal?.version ?? 0, token_budget: CHAT_GOAL_TOKEN_BUDGET });
  }, [selected, enabled, loading, error, current?.id, current?.version, goal?.version, objective, onChange]);
  async function control(action: "pause" | "end") {
    if (!sessionId || !goal || controlling.current) return;
    controlling.current = true; setBusy(true); setError("");
    try { const value = await controlChatGoal(sessionId, action, goal.version); setGoal(previous => previous && value && previous.version > value.version ? previous : value); setSelection(null); if (action === "end") { setObjective(""); setEditing(false); } }
    catch (reason) { setError(reason instanceof Error ? reason.message : "操作失败"); }
    finally { controlling.current = false; setBusy(false); }
  }
  function remove() {
    if (goal && goal.status !== "ended") void control("end");
    else { setSelection(null); setObjective(""); setEditing(false); }
  }
  if (!selected && !current && !editing) return null;
  return <>
    {(selected || current) && <div className="mb-2 flex flex-wrap items-center gap-2 text-xs text-slate-600">
      <div className={`inline-flex max-w-full items-center rounded-lg border ${selected ? "border-blue-200 bg-blue-50 text-blue-700" : "border-slate-200 bg-slate-50"}`}>
        <button type="button" disabled={sending || loading || busy} onClick={() => setEditing(true)} title={current?.objective || objective} className="inline-flex min-w-0 items-center gap-1.5 px-2 py-1.5 disabled:opacity-50"><Target size={13} className="shrink-0" /><span className="truncate">目标{selected ? " · 已启用" : current ? ` · ${labels[current.status] || current.status}` : ""}：{current?.objective || objective}</span></button>
        {selected && <button type="button" aria-label="取消目标模式" disabled={sending} onClick={() => setSelection(null)} className="rounded p-1.5 hover:bg-blue-100 disabled:opacity-40"><X size={13} /></button>}
      </div>
      {current && <><span>{labels[current.status] || current.status} · 已用 {current.tokensUsed.toLocaleString()} Token · {current.timeUsedSeconds} 秒</span>{current.stopReason && <span>{current.stopReason}</span>}</>}
      {current?.running && <div className="flex gap-3"><button type="button" disabled={busy} onClick={() => void control("pause")} className="text-blue-600">暂停目标</button></div>}
      {error && <p role="alert" className="w-full text-red-600">{error}</p>}
    </div>}
    {editing && <GoalSetupDialog objective={current?.objective || objective} goal={goal?.status === "ended" ? null : goal} resuming={Boolean(current)} disabled={!enabled || sending || loading || busy || Boolean(error)} busy={busy} deletingDisabled={sending || loading || busy || Boolean(goal?.running)} error={error} onDelete={remove} onPause={() => void control("pause")} onClose={() => setEditing(false)} onApply={value => { setObjective(value); setSelection(submission); setEditing(false); }} />}
  </>;
}

function GoalSetupDialog({ objective, goal, resuming, disabled, busy, deletingDisabled, error, onClose, onApply, onDelete, onPause }: {
  objective: string; goal: ChatGoal | null; resuming: boolean; disabled: boolean; busy: boolean; deletingDisabled: boolean; error: string;
  onClose: () => void; onApply: (objective: string) => void; onDelete: () => void; onPause: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const [draft, setDraft] = useState(objective);
  useEffect(() => {
    const dialog = ref.current; const previous = document.activeElement;
    dialog?.showModal();
    return () => { dialog?.close(); if (previous instanceof HTMLElement && previous.isConnected) previous.focus(); };
  }, []);
  return <dialog ref={ref} aria-label="设置目标模式" onCancel={event => { event.preventDefault(); if (!busy) onClose(); }} className="m-auto max-h-[calc(100dvh-2rem)] w-[calc(100%-2rem)] max-w-lg overflow-auto rounded-2xl bg-white p-5 text-slate-800 shadow-2xl backdrop:bg-slate-950/30">
    <header className="flex items-center justify-between gap-3"><h2 className="flex items-center gap-2 text-sm font-semibold"><Target size={16} className="text-blue-600" />目标模式</h2><button type="button" disabled={busy} onClick={onClose} aria-label="关闭目标设置" className="rounded p-1.5 text-slate-400 hover:bg-slate-100"><X size={16} /></button></header>
    <p className="mt-2 text-xs leading-6 text-slate-500">目标仅用于本次发送，完成后自动收起。未完成时可明确继续，断线后暂停。</p>
    {goal && <div className="mt-3 rounded-lg bg-slate-50 p-3 text-xs text-slate-600"><p className="break-words">{labels[goal.status] || goal.status}：{goal.objective}</p><p className="mt-1">已用 {goal.tokensUsed.toLocaleString()} Token · {goal.timeUsedSeconds} 秒</p>{goal.running && <button type="button" disabled={busy} onClick={onPause} className="mt-2 text-blue-600">暂停目标</button>}</div>}
    <label className="mt-3 block text-xs font-medium">目标与验收标准<textarea autoFocus readOnly={resuming} disabled={busy} aria-label="目标与验收标准" maxLength={4000} rows={5} value={draft} onChange={event => setDraft(event.target.value)} placeholder="例如：完成登录功能，并通过相关测试" className="mt-2 w-full resize-y rounded-lg border border-slate-200 bg-white p-3 text-sm leading-6 outline-none focus:border-blue-400" /></label>
    {error && <p role="alert" className="mt-2 text-xs text-red-600">{error}</p>}
    <footer className="mt-4 flex flex-wrap justify-end gap-2"><button type="button" disabled={deletingDisabled} onClick={onDelete} className="mr-auto rounded-lg px-3 py-2 text-xs text-red-600 hover:bg-red-50 disabled:opacity-40">删除目标</button><button type="button" disabled={busy} onClick={onClose} className="rounded-lg border border-slate-200 px-3 py-2 text-xs">取消</button><button type="button" disabled={disabled || Boolean(goal?.running) || !draft.trim()} onClick={() => onApply(draft.trim())} className="rounded-lg bg-blue-600 px-3 py-2 text-xs text-white disabled:opacity-40">{resuming ? "继续目标" : "启用目标"}</button></footer>
  </dialog>;
}
