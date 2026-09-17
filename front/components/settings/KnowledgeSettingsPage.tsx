"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { checkKnowledgeEnvironment, getKnowledgeCompilerSettings, getKnowledgeProviderConfig, getKnowledgeProviders, saveKnowledgeCompilerSettings, saveKnowledgeProviderConfig } from "@/lib/knowledge-api";
import type { KnowledgeCompilerSettings, KnowledgeProvider, KnowledgeProviderConfig } from "@/lib/knowledge-types";
import { SettingsPageHeader } from "./layout/SettingsShell";

const inputClass = "mt-2 block w-full rounded-lg border border-[var(--border)] bg-white px-3 py-2 text-sm";

export function KnowledgeSettingsPage() {
  const [compiler, setCompiler] = useState<KnowledgeCompilerSettings | null>(null);
  const [providers, setProviders] = useState<KnowledgeProvider[]>([]);
  const [provider, setProvider] = useState("");
  const [config, setConfig] = useState<KnowledgeProviderConfig | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  useEffect(() => {
    let disposed = false;
    Promise.all([getKnowledgeCompilerSettings(), getKnowledgeProviders()]).then(([settings, rows]) => {
      if (disposed) return;
      setCompiler(settings); setProviders(rows); setProvider(rows[0]?.id || "");
    }).catch((ex) => { if (!disposed) setError(String(ex)); });
    return () => { disposed = true; };
  }, []);
  useEffect(() => {
    if (!provider) return;
    let disposed = false;
    setConfig(null);
    getKnowledgeProviderConfig(provider).then((value) => { if (!disposed) setConfig(value); })
      .catch((ex) => { if (!disposed) setError(String(ex)); });
    return () => { disposed = true; };
  }, [provider]);
  async function perform(action: () => Promise<void>) {
    setBusy(true); setError(""); setMessage("");
    try { await action(); } catch (ex) { setError(ex instanceof Error ? ex.message : String(ex)); }
    finally { setBusy(false); }
  }
  return <>
    <SettingsPageHeader title="知识库设置" description="集中管理知识提炼方式，以及原有检索与分块配置。" action={<Link href="/knowledge" className="text-sm text-blue-600">返回知识库</Link>} />
    {error && <p role="alert" className="mb-4 rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    {message && <p role="status" className="mb-4 rounded bg-green-50 p-3 text-sm text-green-700">{message}</p>}
    {!compiler && !error && <p role="status">正在加载设置…</p>}
    {compiler && <form className="mb-6 rounded-xl border p-6" onSubmit={(e) => { e.preventDefault(); void perform(async () => { setCompiler(await saveKnowledgeCompilerSettings(compiler)); setMessage("提炼设置已保存，下次提炼生效。"); }); }}>
      <h2 className="text-lg font-semibold">知识提炼</h2><p className="mt-2 text-sm leading-6 text-zinc-500">Codex CLI 使用运行后端的机器上的登录态与模型配置。API 使用平台模型服务。提炼结果保留为草稿，可回查原文。</p>
      <fieldset disabled={busy} className="mt-5 grid gap-5 md:grid-cols-2 disabled:opacity-60">
        <label className="text-sm">执行方式<select className={inputClass} value={compiler.generator} onChange={(e) => setCompiler({ ...compiler, generator: e.target.value as KnowledgeCompilerSettings["generator"], model_id: null })}><option value="codex">本地 Codex CLI</option><option value="llm_api">LLM API</option></select></label>
        <label className="text-sm">模型 ID（留空使用平台默认）<input maxLength={256} className={inputClass} value={compiler.model_id || ""} onChange={(e) => setCompiler({ ...compiler, model_id: e.target.value || null })} /></label>
        <label className="text-sm">推理强度<select className={inputClass} value={compiler.reasoning_effort || ""} onChange={(e) => setCompiler({ ...compiler, reasoning_effort: e.target.value || null })}><option value="">默认</option>{["low", "medium", "high", "xhigh"].map((v) => <option key={v}>{v}</option>)}</select></label>
        <label className="text-sm">最大执行步数<input type="number" min={8} max={96} required className={inputClass} value={compiler.max_steps} onChange={(e) => setCompiler({ ...compiler, max_steps: Number(e.target.value) })} /></label>
        <label className="text-sm">超时（分钟）<input type="number" min={1} max={60} required className={inputClass} value={compiler.timeout_minutes} onChange={(e) => setCompiler({ ...compiler, timeout_minutes: Number(e.target.value) })} /></label>
        <div className="flex items-end"><button className="rounded-lg bg-blue-600 px-4 py-2 text-sm text-white">{busy ? "处理中…" : "保存提炼设置"}</button></div>
      </fieldset>
    </form>}
    <form className="rounded-xl border p-6" onSubmit={(e) => { e.preventDefault(); if (config) void perform(async () => { setConfig(await saveKnowledgeProviderConfig(provider, config)); setMessage("检索配置已保存，分块调整在下次重建索引时生效。"); }); }}>
      <h2 className="text-lg font-semibold">检索引擎与分块</h2>
      <label className="mt-4 block text-sm">引擎<select disabled={busy} className={inputClass} value={provider} onChange={(e) => { setProvider(e.target.value); setMessage(""); setError(""); }}>{providers.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
      {config && <fieldset disabled={busy} className="mt-5 grid gap-5 md:grid-cols-2 disabled:opacity-60">
        <label className="text-sm">检索方式<select className={inputClass} value={config.retrieval_profile} onChange={(e) => setConfig({ ...config, retrieval_profile: e.target.value as "hybrid" | "vector" })}><option value="hybrid">混合检索</option><option value="vector">向量检索</option></select></label>
        {([["top_k", "返回条数"], ["vector_candidate_multiplier", "向量候选倍数"], ["keyword_candidate_multiplier", "关键词候选倍数"], ["chunk_size", "分块大小"], ["chunk_overlap", "分块重叠"]] as const).map(([key, label]) => <label key={key} className="text-sm">{label}<input required type="number" min={key === "chunk_overlap" ? 0 : 1} className={inputClass} value={config[key]} onChange={(e) => setConfig({ ...config, [key]: Number(e.target.value) })} /></label>)}
        <div className="flex gap-3"><button className="rounded-lg bg-blue-600 px-4 py-2 text-sm text-white">保存检索设置</button><button type="button" className="rounded-lg border px-4 py-2 text-sm" onClick={() => void perform(async () => { const result = await checkKnowledgeEnvironment(provider); if (result.ok ?? result.Ok) setMessage("环境检查通过"); else throw new Error(result.error_message || result.ErrorMessage || "环境检查未通过，请检查引擎与模型配置。"); })}>检查环境</button></div>
      </fieldset>}
    </form>
  </>;
}
