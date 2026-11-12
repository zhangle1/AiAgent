"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { getKnowledgeGit, saveKnowledgeGit } from "@/lib/knowledge-git-api";
import type { KnowledgeGitConfig, KnowledgeGitState } from "@/lib/knowledge-git-types";
import { KnowledgeGitSync } from "@/components/knowledge/KnowledgeGitSync";

export function KnowledgeGitSettings() {
  const [state, setState] = useState<KnowledgeGitState | null>(null);
  const [config, setConfig] = useState<KnowledgeGitConfig>({ remote_url: "", branch: "main" });
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let disposed = false;
    getKnowledgeGit().then(value => { if (!disposed) { setState(value); setConfig(value.config); } })
      .catch(ex => { if (!disposed) setError(ex instanceof Error ? ex.message : "读取同步设置失败"); });
    return () => { disposed = true; };
  }, []);
  const dirty = !state || config.remote_url !== state.config.remote_url || config.branch !== state.config.branch;
  const input = "mt-2 block min-w-0 w-full rounded-lg border bg-white px-3 py-2 text-sm";
  return <section id="team-git" className="mb-6 min-w-0 rounded-xl border bg-white p-4 sm:p-6">
    <h2 className="text-lg font-semibold">团队知识 Git</h2>
    <p className="mt-2 text-sm leading-6 text-zinc-500">配置团队仓库，以服务器知识内容为准同步。共享资料、项目及本人会话由 Git 自动检测变更。</p>
    {error && <p role="alert" className="mt-3 rounded bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    {message && <p role="status" className="mt-3 rounded bg-emerald-50 p-3 text-sm text-emerald-700">{message}</p>}
    {!state && !error && <p className="mt-3 text-sm">正在读取服务器配置…</p>}
    {state && <>
      <form className="mt-5" onSubmit={event => { event.preventDefault(); if (busy) return; setBusy(true); setError(""); setMessage(""); void (async () => {
        try { await saveKnowledgeGit(config); const next = await getKnowledgeGit(); setState(next); setConfig(next.config); setMessage("团队仓库配置已保存。"); }
        catch (ex) { setError(ex instanceof Error ? ex.message : "保存失败"); }
        finally { setBusy(false); }
      })(); }}>
        <fieldset disabled={busy} className="grid min-w-0 gap-4 md:grid-cols-2">
          <label className="text-sm">远程仓库 HTTPS 地址<input required type="url" placeholder="https://git.example.com/team/knowledge.git" value={config.remote_url} className={input} onChange={event => setConfig({ ...config, remote_url: event.target.value })} /></label>
          <label className="text-sm">分支<input required value={config.branch} className={input} onChange={event => setConfig({ ...config, branch: event.target.value })} /></label>
          <p className="text-xs text-zinc-500 md:col-span-2">使用当前管理员的已激活 Git 账号。<Link href="/settings/git-accounts" className="text-blue-600">管理 Git 账号 →</Link></p>
          <div><button className="rounded-lg border px-4 py-2 text-sm">{busy ? "处理中…" : "保存远程配置"}</button></div>
        </fieldset>
      </form>
      <details className="mt-4 text-xs text-slate-500"><summary className="cursor-pointer">服务器目录</summary><p className="mt-2 break-all">知识存储：{state.root_path}</p><p className="mt-1 break-all">Git 工作区：{state.workspace_path}</p><p className="mt-2 leading-6">根目录由服务器 KnowledgeStorage:RootPath 指定；切换前需先迁移已有资料。</p></details>
      <div className="mt-5 border-t pt-5"><KnowledgeGitSync key={`${state.config.remote_url}:${state.config.branch}`} state={state} disabled={busy} disabledReason={dirty ? "请先保存仓库配置。" : undefined} onBusyChange={setBusy} /></div>
    </>}
  </section>;
}
