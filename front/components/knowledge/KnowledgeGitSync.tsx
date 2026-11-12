"use client";

import { useEffect, useRef, useState } from "react";
import { Loader2, RefreshCw, Upload } from "lucide-react";
import { pushKnowledgeGit, refreshKnowledgeGit } from "@/lib/knowledge-git-api";
import type { KnowledgeGitState, KnowledgeGitValidation } from "@/lib/knowledge-git-types";

// Porcelain -z emits a second path for renames/copies; it belongs to the same change.
function changedFiles(files: string[]) {
  const entries: { status: string; path: string; from?: string }[] = [];
  for (let i = 0; i < files.length; i++) {
    const status = files[i].slice(0, 2);
    entries.push({ status, path: files[i].slice(3), from: /[RC]/.test(status) ? files[++i] : undefined });
  }
  return entries;
}

export function KnowledgeGitSync({ state, autoDetect = false, disabled = false, disabledReason, onBusyChange, onPendingChange }: {
  state: KnowledgeGitState; autoDetect?: boolean;
  disabled?: boolean; disabledReason?: string; onBusyChange?: (busy: boolean) => void; onPendingChange?: (count: number | null) => void;
}) {
  const [phase, setPhase] = useState("");
  const [validation, setValidation] = useState<KnowledgeGitValidation | null>(null);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [output, setOutput] = useState("");
  const running = useRef(false);
  const busy = Boolean(phase);
  const reason = disabledReason || (!state.config.remote_url ? "尚未配置目标仓库，请先打开同步设置。" : "");
  const files = validation ? changedFiles(validation.files) : [];
  const unchanged = validation?.checks.length && validation.checks.every(check => check.passed || check.name === "changes") && !validation.is_valid;

  async function run(push: boolean) {
    if (running.current || disabled || reason) return;
    running.current = true; onBusyChange?.(true); onPendingChange?.(null);
    setValidation(null); setError(""); setMessage(""); if (push) setOutput("");
    try {
      setPhase("正在检测变更…");
      const next = await refreshKnowledgeGit([], true, undefined, true);
      setValidation(next); onPendingChange?.(changedFiles(next.files).length);
      const failed = next.checks.filter(check => !check.passed && check.name !== "changes");
      if (failed.length) throw new Error(failed.map(check => check.message).join("\n"));
      if (!next.is_valid) {
        if (next.checks.some(check => check.name === "changes" && !check.passed)) {
          setMessage("服务器内容已同步，无需推送。"); return;
        }
        throw new Error("同步检查失败，请重新检测。");
      }
      if (!push) return;
      setPhase("正在强制推送…");
      const result = await pushKnowledgeGit("同步服务器知识资料", next.snapshot_sha256, true);
      setOutput(result.output);
      if (!result.ok) throw new Error(result.output || "推送失败，服务器提交已保留，请重试。");
      const remaining = result.status?.changes ?? [];
      const needsSync = remaining.length > 0 || Boolean(result.status?.ahead || result.status?.behind);
      setValidation({ ...next, files: remaining, is_valid: needsSync, checks: [{ name: "changes", passed: needsSync, message: "推送后状态" }] });
      onPendingChange?.(changedFiles(remaining).length);
      setMessage(`已推送到团队仓库${result.commit_sha ? ` · ${result.commit_sha}` : ""}`);
    } catch (ex) { setError(ex instanceof Error ? ex.message : String(ex)); }
    finally { running.current = false; setPhase(""); onBusyChange?.(false); }
  }
  useEffect(() => { if (autoDetect) void run(false); }, []);

  return <section aria-label="服务器知识同步" className="min-w-0 space-y-3">
    <div><p className="break-all text-xs leading-5 text-slate-600">{state.config.remote_url || "未配置目标仓库"}</p><p className="mt-1 text-[11px] text-slate-400">分支 {state.config.branch} · 以服务器内容为准</p></div>
    <div className="rounded-lg bg-slate-50 p-3">
      <div className="flex items-center justify-between gap-2"><p role="status" className="text-xs font-medium text-slate-700">{phase === "正在检测变更…" ? phase : validation ? `待提交 ${files.length} 个文件` : "尚未检测变更"}</p><button type="button" aria-label="重新检测 Git 变更" disabled={busy || disabled || Boolean(reason)} onClick={() => void run(false)} className="rounded p-1 text-slate-500 hover:bg-slate-200 disabled:opacity-40"><RefreshCw size={13} className={phase === "正在检测变更…" ? "animate-spin" : ""} /></button></div>
      {files.length > 0 && <details className="mt-2 text-[11px] text-slate-500"><summary className="cursor-pointer">查看变更文件</summary><ul aria-label="待提交文件" className="mt-2 max-h-48 space-y-1.5 overflow-auto">{files.map((file, index) => <li key={index} className="flex items-start gap-2"><code className="shrink-0 whitespace-pre rounded bg-white px-1 text-emerald-700">{file.status}</code><span className="min-w-0 break-all">{file.from ? `${file.from} → ` : ""}{file.path}</span></li>)}</ul></details>}
      {validation?.is_valid && files.length === 0 && <p className="mt-2 text-[11px] leading-5 text-slate-500">有待推送提交或远端差异，可强制同步。</p>}
    </div>
    <p className="text-[11px] leading-5 text-slate-500">自动检测共享资料、项目及本人全部会话，强制覆盖远端目标分支。</p>
    {reason && <p role="status" className="text-xs text-amber-700">{reason}</p>}
    {error && <p role="alert" className="whitespace-pre-wrap break-words rounded-lg bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>}
    {message && <p role="status" className="rounded-lg bg-emerald-50 px-3 py-2 text-xs text-emerald-700">{message}</p>}
    {output && <details className="text-xs text-slate-500"><summary className="cursor-pointer">查看推送日志</summary><pre aria-label="Git 命令输出" className="mt-2 max-h-40 overflow-auto whitespace-pre-wrap break-words rounded-lg bg-slate-50 p-3 text-slate-600">{output}</pre></details>}
    <button type="button" disabled={busy || disabled || Boolean(reason) || Boolean(unchanged && !error)} onClick={() => void run(true)} className="inline-flex w-full items-center justify-center gap-2 rounded-lg bg-emerald-600 px-4 py-2.5 text-sm font-medium text-white hover:bg-emerald-700 disabled:cursor-not-allowed disabled:opacity-50">
      {busy ? <Loader2 size={15} className="animate-spin" /> : <Upload size={15} />}{phase || (error ? "重试强制推送" : "强制推送")}
    </button>
  </section>;
}
