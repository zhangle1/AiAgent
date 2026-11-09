"use client";

import { useEffect, useState } from "react";
import { controlChatGoal, getChatGoal } from "@/lib/chat-goal-api";
import type { ChatGoal, ChatGoalRequest } from "@/lib/chat-goal-types";

const labels: Record<string, string> = { active: "执行中", paused: "已暂停", blocked: "等待补充", complete: "已完成", ended: "已结束", budgetLimited: "预算耗尽", usageLimited: "用量受限" };

export function ChatGoalPanel({ sessionId, enabled, sending, onChange }: {
  sessionId: string | null; enabled: boolean; sending: boolean; onChange: (value: ChatGoalRequest | undefined) => void;
}) {
  const [goal, setGoal] = useState<ChatGoal | null>(null);
  const [selected, setSelected] = useState(false);
  const [objective, setObjective] = useState("");
  const [budget, setBudget] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    setSelected(false); setObjective(""); setBudget(""); setGoal(null); setError("");
  }, [sessionId]);
  useEffect(() => {
    if (!sessionId) { setLoading(false); return; }
    let alive = true;
    setLoading(true);
    const refresh = async () => {
      try { const value = await getChatGoal(sessionId); if (alive) { setGoal(value); setError(""); setLoading(false); } }
      catch (reason) { if (alive) { setError(reason instanceof Error ? reason.message : "读取目标失败"); setLoading(false); } }
    };
    void refresh();
    const timer = window.setInterval(() => void refresh(), 2000);
    return () => { alive = false; window.clearInterval(timer); };
  }, [sessionId]);
  const current = goal && !["complete", "ended"].includes(goal.status) ? goal : null;
  const validBudget = !budget || (/^[1-9]\d*$/.test(budget) && Number.isSafeInteger(Number(budget)));
  useEffect(() => { if (goal?.status === "complete" || goal?.status === "ended") setSelected(false); }, [goal?.status]);
  useEffect(() => { if (!enabled) setSelected(false); }, [enabled]);
  useEffect(() => {
    if (!selected || !enabled) { onChange(undefined); return; }
    if (loading || error) { onChange({ action: "start", objective: "", token_budget: 0 }); return; }
    onChange(current
      ? { action: "resume", expected_version: current.version }
      : { action: "start", objective: objective.trim(), expected_version: goal?.version ?? 0, ...(budget ? { token_budget: validBudget ? Number(budget) : 0 } : {}) });
  }, [selected, enabled, loading, error, validBudget, current?.id, current?.version, goal?.version, objective, budget, onChange]);
  if (!enabled && !goal) return null;
  async function control(action: "pause" | "end") {
    if (!sessionId || !goal) return;
    setBusy(true);
    try { setGoal(await controlChatGoal(sessionId, action, goal.version)); setSelected(false); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "操作失败"); }
    finally { setBusy(false); }
  }
  return <div className="mb-2 rounded-xl border border-blue-100 bg-blue-50/50 px-3 py-2 text-xs text-slate-700">
    <label className="flex items-center gap-2"><input type="checkbox" checked={selected} disabled={!enabled || sending || loading || Boolean(error)} onChange={e => setSelected(e.target.checked)} />目标模式{current ? " · 勾选后发送消息继续目标" : " · 在线持续执行，断线后暂停"}</label>
    {goal && <div className="mt-2 space-y-1"><p className="whitespace-pre-wrap break-words">{goal.objective}</p><p>{labels[goal.status] || goal.status} · {goal.tokensUsed.toLocaleString()} / {goal.tokenBudget?.toLocaleString() || "未设预算"} Token · {goal.timeUsedSeconds} 秒</p>{goal.stopReason && <p>{goal.stopReason}</p>}
      {current && <div className="flex gap-3">{goal.running && <button type="button" disabled={busy} onClick={() => void control("pause")}>暂停目标</button>}<button type="button" disabled={busy || goal.running || sending} onClick={() => void control("end")}>结束目标</button></div>}
    </div>}
    {selected && !current && <div className="mt-2 space-y-2"><textarea aria-label="目标与验收标准" maxLength={4000} disabled={sending} value={objective} onChange={e => setObjective(e.target.value)} placeholder="描述目标与验收标准（必填）" className="w-full rounded border border-slate-200 bg-white p-2" /><input aria-label="目标 Token 预算" value={budget} disabled={sending} onChange={e => setBudget(e.target.value)} inputMode="numeric" placeholder="Token 预算（可选）" className="w-full rounded border border-slate-200 bg-white p-2" />{!validBudget && <p role="alert">预算须为正整数。</p>}</div>}
    {error && <p role="alert" className="mt-1 text-red-600">{error}</p>}
  </div>;
}
