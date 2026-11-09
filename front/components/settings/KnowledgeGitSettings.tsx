"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { getKnowledgeGit, saveKnowledgeGit, refreshKnowledgeGit, pushKnowledgeGit } from "@/lib/knowledge-git-api";
import type { KnowledgeGitConfig, KnowledgeGitState, KnowledgeGitValidation } from "@/lib/knowledge-git-types";

export function KnowledgeGitSettings({ initialSessionUri, initialSessionUris, initialResourceUris, onBusyChange, compact = false }: { initialSessionUri?: string; initialSessionUris?: string[]; initialResourceUris?: string[]; onBusyChange?: (busy: boolean) => void; compact?: boolean } = {}) {
  const [state, setState] = useState<KnowledgeGitState | null>(null);
  const [config, setConfig] = useState<KnowledgeGitConfig>({ remote_url: "", branch: "main" });
  const [validation, setValidation] = useState<KnowledgeGitValidation | null>(null);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [commit, setCommit] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [sessionUris, setSessionUris] = useState<string[]>([]);
  const [output, setOutput] = useState("");
  const [force, setForce] = useState(true);
  useEffect(() => {
    let disposed = false;
    getKnowledgeGit().then(value => { if (!disposed) {
      setState(value); setConfig(value.config);
      if (initialSessionUris) setSessionUris(initialSessionUris);
      else if (initialSessionUri && value.session_options?.some(node => node.uri === initialSessionUri)) setSessionUris([initialSessionUri]);
    } })
      .catch(ex => { if (!disposed) setError(String(ex)); });
    return () => { disposed = true; };
  }, [initialSessionUri, initialSessionUris]);
  async function perform(action: () => Promise<void>) {
    setBusy(true); onBusyChange?.(true); setError(""); setMessage(""); setOutput("");
    try { await action(); } catch (ex) { setError(ex instanceof Error ? ex.message : String(ex)); }
    finally { setBusy(false); onBusyChange?.(false); }
  }
  const dirty = !state || config.remote_url !== state.config.remote_url || config.branch !== state.config.branch;
  const input = "mt-2 block min-w-0 w-full rounded-lg border bg-white px-3 py-2 text-sm";
  return <section id="team-git" className="mb-6 min-w-0 rounded-xl border bg-white p-4 sm:p-6">
    <h2 className="text-lg font-semibold">团队知识 Git</h2>
    <p className="mt-2 text-sm leading-6 text-zinc-500">团队共用一个仓库，保存资料、项目知识及明确选择发布的本人会话原文。配置与推送由管理员操作。</p>
    {error && <p role="alert" className="mt-3 rounded bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    {message && <p role="status" className="mt-3 whitespace-pre-wrap rounded bg-green-50 p-3 text-sm text-green-700">{message}</p>}
    {busy && <p role="status" className="mt-3 text-sm text-blue-700">正在处理，请等待检查或推送结果…</p>}
    {output && <pre aria-label="Git 命令输出" className="mt-3 max-h-48 overflow-auto whitespace-pre-wrap break-words rounded-lg bg-slate-900 p-3 text-xs text-slate-100">{output}</pre>}
    {!state && !error && <p className="mt-3 text-sm">正在读取服务器配置…</p>}
    {state && <>
      {compact && state.config.remote_url && <p className="mt-3 break-all text-xs text-slate-500">{state.config.remote_url} · {state.config.branch}</p>}
      <details open={!compact || !state.config.remote_url} className="mt-4">
      <summary className="cursor-pointer text-sm font-medium text-slate-700">仓库与目录配置</summary>
      <div className="mt-4 rounded-lg bg-slate-50 p-4 text-sm">
        <p>知识存储目录：<code className="break-all">{state.root_path}</code></p>
        <p className="mt-2">团队 Git 工作区：<code className="break-all">{state.workspace_path}</code></p>
        <p className="mt-2 text-xs text-zinc-500">固定根目录由服务器 appsettings 的 KnowledgeStorage:RootPath 指定，重启后生效；已有资料需先迁移。未配置时沿用 DataPath。</p>
      </div>
      <form className="mt-5" onSubmit={e => { e.preventDefault(); void perform(async () => {
        await saveKnowledgeGit(config); const next = await getKnowledgeGit(); setState(next); setConfig(next.config); setValidation(null); setConfirmed(false); setMessage("团队仓库配置已保存。");
      }); }}>
        <fieldset disabled={busy} className="grid min-w-0 gap-4 md:grid-cols-2">
          <label className="text-sm">远程仓库 HTTPS 地址<input required type="url" placeholder="https://git.example.com/team/knowledge.git" value={config.remote_url} className={input} onChange={e => { setConfig({ ...config, remote_url: e.target.value }); setValidation(null); setConfirmed(false); }} /></label>
          <label className="text-sm">分支<input required value={config.branch} className={input} onChange={e => { setConfig({ ...config, branch: e.target.value }); setValidation(null); setConfirmed(false); }} /></label>
          <p className="text-xs text-zinc-500 md:col-span-2">使用当前管理员的已激活 Git 账号凭据。<Link href="/settings/git-accounts" className="text-blue-600">管理 Git 账号 →</Link> 默认以本地内容同步目标分支，可覆盖远端已有历史。</p>
          <div><button className="rounded-lg border px-4 py-2 text-sm">保存远程配置</button></div>
        </fieldset>
      </form>
      </details>
      <div className="mt-6 border-t pt-5">
        <div className="mb-4 rounded-lg bg-slate-50 p-3 text-xs leading-6 text-slate-600">
          {initialResourceUris === undefined ? "资料范围：全部共享资料与项目目录。" : initialResourceUris.length ? <details><summary className="cursor-pointer">资料范围：在目录中选择的 {initialResourceUris.length} 个文件或目录</summary>{initialResourceUris.map(uri => <p key={uri} className="break-all">{decodeURI(uri)}</p>)}</details> : "本次只更新所选会话，其他已发布资料保持原版。"}
          <label className="mt-2 flex items-start gap-2 text-sm font-medium text-slate-800"><input type="checkbox" checked={force} disabled={busy} className="mt-1.5" onChange={e => { setForce(e.target.checked); setValidation(null); setConfirmed(false); }} />本地优先，强制同步远端</label>
          <p>{force ? "确认推送后，以本地版本覆盖目标分支，包括远端领先或分叉的历史。" : "普通推送：远端领先或历史分叉时停止，保留本地内容。"}</p>
        </div>
        <fieldset disabled={busy} className="mb-5 min-w-0 rounded-lg border border-slate-200 p-3">
          <legend className="px-1 text-sm font-medium">发布本人会话（可选）</legend>
          <p className="text-xs leading-5 text-zinc-500">勾选后将会话原文复制到团队仓库的 sessions/ 目录，仓库成员可以阅读。未选会话及其他个人内容不会加入；已发布副本保留，新消息需再次勾选发布。</p>
          <div className="mt-2 max-h-48 space-y-1 overflow-auto">
            {(state.session_options ?? []).map(node => <label key={node.uri} className="flex min-w-0 items-start gap-2 rounded px-1 py-1.5 text-sm hover:bg-slate-50">
              <input type="checkbox" aria-label={`发布 ${node.name}`} className="mt-1 shrink-0" checked={sessionUris.includes(node.uri)} onChange={e => {
                setSessionUris(current => e.target.checked ? [...current, node.uri] : current.filter(uri => uri !== node.uri)); setValidation(null); setConfirmed(false);
              }} /><span className="min-w-0 break-words" title={node.uri}>{node.name}</span>
            </label>)}
            {!state.session_options?.length && <p className="py-2 text-xs text-slate-400">暂无可发布的本人会话，可先在知识工作区导入平台会话。</p>}
          </div>
          <p className="mt-2 text-xs text-zinc-500">本次选择 {sessionUris.length} 个会话，每次最多 100 个。</p>
        </fieldset>
        <div className="flex flex-wrap items-center justify-between gap-3"><h3 className="font-medium">变更与推送</h3><button disabled={busy || dirty || !config.remote_url || sessionUris.length > 100} className="rounded-lg border px-3 py-2 text-sm disabled:opacity-50" onClick={() => void perform(async () => { setValidation(null); setConfirmed(false); setValidation(await refreshKnowledgeGit(sessionUris, force, initialResourceUris)); setState(await getKnowledgeGit()); setMessage("服务器资料已导出，请核对下面的变更文件后推送。"); })}>{busy ? "处理中…" : "刷新资料并检查远端"}</button></div>
        <p className="mt-2 text-xs text-zinc-500">刷新更新服务器查阅副本，确认推送后提交到 Git。Git 状态不代表源资料已重新导出。</p>
        {state.status && <p className="mt-3 text-sm text-zinc-600">分支 {state.status.branch} · {state.status.changes.length} 个变更 · 待推送 {state.status.ahead} 个提交 · 落后 {state.status.behind} 个提交</p>}
        {state.status?.remote_refresh_error && <p className="mt-2 text-sm text-amber-700">远端检查失败，请检查地址、网络和账号权限。</p>}
        {validation && <div className="mt-4">
          <ul className="space-y-1 text-sm">{validation.checks.map(check => <li key={check.name} className={check.passed ? "text-green-700" : "text-red-700"}>{check.passed ? "✓" : "!"} {check.message}</li>)}</ul>
          <pre className="mt-3 max-h-64 overflow-auto rounded-lg bg-slate-50 p-3 text-xs">{validation.files.join("\n") || "无文件变更（可能存在待推送提交）"}</pre>
          <label className="mt-4 block text-sm">提交说明<input value={commit} disabled={busy} className={input} onChange={e => setCommit(e.target.value)} /></label>
          <label className="mt-3 flex items-center gap-2 text-sm"><input type="checkbox" checked={confirmed} disabled={busy} onChange={e => setConfirmed(e.target.checked)} />已核对文件列表，确认提交到团队仓库</label>
          <button disabled={busy || dirty || !validation.is_valid || !confirmed || !commit.trim()} className="mt-4 rounded-lg bg-blue-600 px-4 py-2 text-sm text-white disabled:opacity-50" onClick={() => void perform(async () => {
            const result = await pushKnowledgeGit(commit, validation.snapshot_sha256, force);
            setValidation(null); setConfirmed(false); setOutput(result.output);
            if (!result.ok) {
              try { setState(await getKnowledgeGit()); } catch { /* Preserve the actual push failure if status refresh also fails. */ }
              throw new Error(result.output || "推送失败，本地提交已保留，请刷新后重试。");
            }
            setMessage(`已推送到团队仓库${result.commit_sha ? ` · ${result.commit_sha}` : ""}`);
            try { setState(await getKnowledgeGit()); } catch { setError("推送已成功，但最新 Git 状态读取失败，请手动刷新。"); }
          })}>确认提交并推送</button>
        </div>}
      </div>
    </>}
  </section>;
}
