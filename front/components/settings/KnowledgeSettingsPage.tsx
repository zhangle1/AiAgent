"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { getSettings } from "@/lib/api";
import { getCodexModelPolicy } from "@/lib/agent-provider-api";
import { checkKnowledgeCompilerChain, checkKnowledgeEnvironment, getKnowledgeCompilerSettings, getKnowledgeProviderConfig, getKnowledgeProviders, saveKnowledgeCompilerSettings, saveKnowledgeProviderConfig } from "@/lib/knowledge-api";
import type { KnowledgeChainCheckResult, KnowledgeCompilerSettings, KnowledgeProvider, KnowledgeProviderConfig } from "@/lib/knowledge-types";
import { SettingsPageHeader } from "./layout/SettingsShell";

const inputClass = "mt-2 block w-full rounded-lg border border-[var(--border)] bg-white px-3 py-2 text-sm";
type CompilerModelOption = { id: string; label: string };

export function KnowledgeSettingsPage() {
  const [compiler, setCompiler] = useState<KnowledgeCompilerSettings | null>(null);
  const [providers, setProviders] = useState<KnowledgeProvider[]>([]);
  const [provider, setProvider] = useState("");
  const [config, setConfig] = useState<KnowledgeProviderConfig | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [chainCheck, setChainCheck] = useState<KnowledgeChainCheckResult | null>(null);
  const [apiModels, setApiModels] = useState<CompilerModelOption[]>([]);
  const [codexModels, setCodexModels] = useState<CompilerModelOption[]>([]);
  useEffect(() => {
    let disposed = false;
    getKnowledgeCompilerSettings().then((settings) => {
      if (disposed) return;
      setCompiler(settings);
    }).catch((ex) => { if (!disposed) setError(String(ex)); });
    return () => { disposed = true; };
  }, []);
  useEffect(() => {
    let disposed = false;
    Promise.allSettled([getSettings(), getCodexModelPolicy()]).then(([settingsResult, codexResult]) => {
      if (disposed) return;
      if (settingsResult.status === "fulfilled") {
        const service = settingsResult.value.catalog.services.llm;
        setApiModels(service.profiles.flatMap((profile) => profile.models.map((model) => ({
          id: model.id,
          label: `${profile.name} · ${model.name || model.model}`,
        }))));
      }
      if (codexResult.status === "fulfilled") {
        const allowed = new Set(codexResult.value.allowed_model_ids);
        setCodexModels(codexResult.value.models.filter((model) => allowed.has(model.id)).map((model) => ({ id: model.id, label: model.name || model.id })));
      }
    });
    return () => { disposed = true; };
  }, []);
  useEffect(() => {
    if (compiler?.retrieval_mode !== "rag") return;
    let disposed = false;
    getKnowledgeProviders().then((rows) => {
      if (!disposed) { setProviders(rows); setProvider((current) => current || rows[0]?.id || ""); }
    }).catch((ex) => { if (!disposed) setError(String(ex)); });
    return () => { disposed = true; };
  }, [compiler?.retrieval_mode]);
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
    <SettingsPageHeader title="知识库设置" description="原始资料 → 知识表示层 → 模型检索。默认导入 raw，按需提炼，索引可选。" action={<Link href="/knowledge" className="text-sm text-blue-600">返回知识库</Link>} />
    {error && <p role="alert" className="mb-4 rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    {message && <p role="status" className="mb-4 rounded bg-green-50 p-3 text-sm text-green-700">{message}</p>}
    {!compiler && !error && <p role="status">正在加载设置…</p>}
    {compiler && <form className="mb-6 rounded-xl border p-6" onSubmit={(e) => { e.preventDefault(); void perform(async () => { setCompiler(await saveKnowledgeCompilerSettings(compiler)); setMessage("知识设置已保存，下次提炼和检索生效。"); }); }}>
      <h2 className="text-lg font-semibold">知识表示层与模型检索</h2><p className="mt-2 text-sm leading-6 text-zinc-500">上传仅保存原始文件到 raw，不自动提炼或建立索引。按需将原文整理为主题、概念和关系等 Markdown 知识草稿，保留来源证据。模型检索先浏览知识目录、读取页面，再返回引用。</p>
      <p className="mt-2 text-sm leading-6 text-zinc-500">提炼与模型检索共用以下模型配置。默认使用平台已保存的 LLM API（例如 DeepSeek）；也可切换到后端机器已登录的 Codex CLI。仅有原始文件时，请先点击「提炼知识」。</p>
      <fieldset disabled={busy} className="mt-5 grid gap-5 md:grid-cols-2 disabled:opacity-60">
        <label className="text-sm md:col-span-2">检索方式<select className={inputClass} value={compiler.retrieval_mode} onChange={(e) => setCompiler({ ...compiler, retrieval_mode: e.target.value as KnowledgeCompilerSettings["retrieval_mode"] })}><option value="wiki">模型检索知识表示层（默认，无需索引）</option><option value="rag">RAG 索引检索（需手动创建索引）</option></select></label>
        <label className="text-sm">执行方式<select className={inputClass} value={compiler.generator} onChange={(e) => setCompiler({ ...compiler, generator: e.target.value as KnowledgeCompilerSettings["generator"], model_id: null })}><option value="codex">本地 Codex CLI</option><option value="llm_api">LLM API</option></select></label>
        <label className="text-sm">提炼模型<select className={inputClass} value={compiler.model_id || ""} onChange={(e) => setCompiler({ ...compiler, model_id: e.target.value || null })}><option value="">使用{compiler.generator === "codex" ? " Codex CLI" : "平台 LLM API"}默认模型</option>{(compiler.generator === "codex" ? codexModels : apiModels).map((model) => <option key={model.id} value={model.id}>{model.label}</option>)}</select><span className="mt-1 block text-xs text-zinc-500">{compiler.generator === "llm_api" ? "来自「模型服务 → LLM」中已保存的配置，选择模型时会同时使用其所属 API 配置档。" : "来自 Agent 提供方中已启用的 Codex 模型与 CLI Profile。"}</span></label>
        <label className="text-sm">推理强度<select className={inputClass} value={compiler.reasoning_effort || ""} onChange={(e) => setCompiler({ ...compiler, reasoning_effort: e.target.value || null })}><option value="">默认</option>{["low", "medium", "high", "xhigh"].map((v) => <option key={v}>{v}</option>)}</select></label>
        <label className="text-sm">最大执行步数<input type="number" min={8} max={96} required className={inputClass} value={compiler.max_steps} onChange={(e) => setCompiler({ ...compiler, max_steps: Number(e.target.value) })} /></label>
        <label className="text-sm">超时（分钟）<input type="number" min={1} max={60} required className={inputClass} value={compiler.timeout_minutes} onChange={(e) => setCompiler({ ...compiler, timeout_minutes: Number(e.target.value) })} /></label>
        <div className="flex items-end gap-3"><button className="rounded-lg bg-blue-600 px-4 py-2 text-sm text-white">{busy ? "处理中…" : "保存知识设置"}</button><button type="button" className="rounded-lg border px-4 py-2 text-sm" onClick={() => void perform(async () => { setChainCheck(null); const result = await checkKnowledgeCompilerChain(compiler); setChainCheck(result); setMessage(result.ok ? "模拟检测通过：提炼、校验证据和模型检索链路通顺。" : "模拟检测未通过。"); })}>{busy ? "检测中…" : "模拟检测"}</button></div>
      </fieldset>
      {chainCheck && <div className="mt-5 rounded-lg border border-emerald-200 bg-emerald-50 p-4">
        <div className="flex flex-wrap items-center justify-between gap-2"><strong className="text-sm text-emerald-800">链路检测通过</strong><span className="text-xs text-emerald-700">{chainCheck.provider || "model"}{chainCheck.model ? ` · ${chainCheck.model}` : ""}</span></div>
        <ol className="mt-3 grid gap-2 md:grid-cols-2">{chainCheck.steps.map((step, index) => <li key={step.key} className="rounded-md bg-white px-3 py-2 text-xs text-zinc-600"><span className="font-semibold text-emerald-700">{index + 1}. {step.label}</span><p className="mt-1 leading-5">{step.detail}</p></li>)}</ol>
        <p className="mt-3 text-xs text-emerald-700">检测仅使用内置测试文本和内存知识页，不保存草稿、不修改知识库、不创建索引。</p>
      </div>}
    </form>}
    {compiler?.retrieval_mode === "rag" ? <form className="rounded-xl border p-6" onSubmit={(e) => { e.preventDefault(); if (config) void perform(async () => { setConfig(await saveKnowledgeProviderConfig(provider, config)); setMessage("检索配置已保存，分块调整在下次重建索引时生效。"); }); }}>
      <h2 className="text-lg font-semibold">可选 RAG 索引与分块</h2>
      <p className="mt-2 text-sm leading-6 text-zinc-500">此模式使用已有活动索引。请在知识库的「索引版本」中手动创建或更新索引；上传文件不会自动重建。</p>
      <label className="mt-4 block text-sm">引擎<select disabled={busy} className={inputClass} value={provider} onChange={(e) => { setProvider(e.target.value); setMessage(""); setError(""); }}>{providers.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
      {config && <fieldset disabled={busy} className="mt-5 grid gap-5 md:grid-cols-2 disabled:opacity-60">
        <label className="text-sm">检索方式<select className={inputClass} value={config.retrieval_profile} onChange={(e) => setConfig({ ...config, retrieval_profile: e.target.value as "hybrid" | "vector" })}><option value="hybrid">混合检索</option><option value="vector">向量检索</option></select></label>
        {([["top_k", "返回条数"], ["vector_candidate_multiplier", "向量候选倍数"], ["keyword_candidate_multiplier", "关键词候选倍数"], ["chunk_size", "分块大小"], ["chunk_overlap", "分块重叠"]] as const).map(([key, label]) => <label key={key} className="text-sm">{label}<input required type="number" min={key === "chunk_overlap" ? 0 : 1} className={inputClass} value={config[key]} onChange={(e) => setConfig({ ...config, [key]: Number(e.target.value) })} /></label>)}
        <div className="flex gap-3"><button className="rounded-lg bg-blue-600 px-4 py-2 text-sm text-white">保存检索设置</button><button type="button" className="rounded-lg border px-4 py-2 text-sm" onClick={() => void perform(async () => { const result = await checkKnowledgeEnvironment(provider); if (result.ok ?? result.Ok) setMessage("环境检查通过"); else throw new Error(result.error_message || result.ErrorMessage || "环境检查未通过，请检查引擎与模型配置。"); })}>检查环境</button></div>
      </fieldset>}
    </form> : <div className="rounded-xl border p-6 text-sm text-zinc-500">RAG 索引是可选能力。当前模型检索无需配置向量引擎、Embedding 或分块参数；已有索引仍然保留。</div>}
  </>;
}
