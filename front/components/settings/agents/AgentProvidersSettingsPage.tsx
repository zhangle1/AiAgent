"use client";

import { useEffect, useState } from "react";
import { Bot, CheckCircle2, CircleAlert, Loader2, RefreshCw, Terminal } from "lucide-react";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";
import { getAgentProviderEnvironments, type AgentProviderEnvironment } from "@/lib/agent-provider-api";
import { getUiSettings, updateUiSettings } from "@/lib/api";

export function AgentProvidersSettingsPage() {
  const [providers, setProviders] = useState<AgentProviderEnvironment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [preferredAgent, setPreferredAgent] = useState<"codex" | "codebuddy" | "none">("codex");
  const [saving, setSaving] = useState(false);

  async function load() {
    setLoading(true);
    try {
      const [detected, ui] = await Promise.all([getAgentProviderEnvironments(), getUiSettings()]);
      setProviders(detected);
      setPreferredAgent(ui.preferred_agent ?? "codex");
      setError(null);
    }
    catch (ex) { setError(ex instanceof Error ? ex.message : "无法检测本地代理环境。"); }
    finally { setLoading(false); }
  }

  useEffect(() => { void load(); }, []);

  async function savePreferredAgent() {
    setSaving(true);
    try { const saved = await updateUiSettings({ preferred_agent: preferredAgent }); setPreferredAgent(saved.preferred_agent ?? "codex"); setError(null); }
    catch (ex) { setError(ex instanceof Error ? ex.message : "默认代理保存失败。"); }
    finally { setSaving(false); }
  }

  return <section>
    <SettingsPageHeader title="第三方代理" description="检测后端运行账户可用的本地编码代理。只有具备结构化 app-server 协议的代理，才能在聊天中安全接管项目。" action={<div className="flex gap-2"><button type="button" onClick={() => void load()} disabled={loading || saving} className="inline-flex h-9 items-center gap-2 rounded-md border border-slate-200 bg-white px-3 text-xs text-slate-600 hover:bg-slate-50 disabled:opacity-50"><RefreshCw size={14} className={loading ? "animate-spin" : ""}/>刷新检测</button><button type="button" onClick={() => void savePreferredAgent()} disabled={saving} className="inline-flex h-9 items-center gap-2 rounded-md bg-blue-600 px-3 text-xs font-medium text-white hover:bg-blue-700 disabled:bg-slate-300">{saving && <Loader2 size={14} className="animate-spin"/>}保存首选项</button></div>}/>
    <section className="mb-4 rounded-2xl border border-blue-100 bg-blue-50/50 p-4"><label className="block text-sm font-semibold text-slate-800">默认聊天代理</label><p className="mt-1 text-xs leading-5 text-slate-500">每次打开聊天时默认选中此代理；若本机环境不满足要求，会自动回退到可用的 Codex 或“不接管”。</p><select value={preferredAgent} onChange={(event) => setPreferredAgent(event.target.value as "codex" | "codebuddy" | "none")} className="mt-3 h-9 min-w-56 rounded-md border border-slate-200 bg-white px-3 text-xs text-slate-700 outline-none focus:border-blue-400"><option value="codex">Codex 本地</option><option value="none">不接管</option><option value="codebuddy" disabled>CodeBuddy CLI（待协议适配）</option></select></section>
    <div className="grid gap-4 lg:grid-cols-2">
      {providers.map((provider) => <ProviderCard key={provider.id} provider={provider}/>) }
      {loading && providers.length === 0 && <div className="flex min-h-48 items-center justify-center gap-2 rounded-2xl border border-dashed border-slate-200 text-sm text-slate-500"><Loader2 size={16} className="animate-spin"/>正在检测本地 CLI…</div>}
    </div>
    {error && <p className="mt-4 rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-xs text-rose-700">{error}</p>}
    <div className="mt-5 rounded-2xl border border-amber-200 bg-amber-50/70 p-4 text-xs leading-5 text-amber-900"><p className="font-semibold">CodeBuddy 兼容性说明</p><p className="mt-1">CodeBuddy 官方页面说明可通过 <code>npm install -g @tencent-ai/codebuddy-code</code> 安装，并支持 Windows；当前公开资料未说明兼容 Codex <code>app-server --stdio</code> JSONL 协议。因此本页只做环境检测，聊天中不会把 CodeBuddy 当作 Codex 调用。</p></div>
  </section>;
}

function ProviderCard({ provider }: { provider: AgentProviderEnvironment }) {
  return <article className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm"><div className="flex items-start justify-between gap-3"><div className="flex items-center gap-3"><span className={`grid h-10 w-10 place-items-center rounded-xl ${provider.installed ? "bg-emerald-50 text-emerald-600" : "bg-slate-100 text-slate-400"}`}><Bot size={19}/></span><div><h2 className="text-sm font-semibold text-slate-900">{provider.name}</h2><p className="mt-0.5 text-[11px] text-slate-500">{provider.protocol}</p></div></div><span className={`inline-flex items-center gap-1 rounded-full px-2 py-1 text-[10px] font-medium ${provider.installed ? "bg-emerald-50 text-emerald-700" : "bg-slate-100 text-slate-500"}`}>{provider.installed ? <CheckCircle2 size={12}/> : <CircleAlert size={12}/>}{provider.installed ? "已检测" : "未安装"}</span></div><div className="mt-4 space-y-2 text-xs"><p className="text-slate-600">{provider.message}</p><p className="flex items-center gap-1.5 rounded-lg bg-slate-50 px-2.5 py-2 font-mono text-[11px] text-slate-600"><Terminal size={13} className="shrink-0 text-slate-400"/><span className="truncate">{provider.command || "未找到命令"}{provider.version ? ` · ${provider.version}` : ""}</span></p><p className={provider.chat_supported ? "text-emerald-700" : "text-amber-700"}>{provider.chat_supported ? "聊天接管：可用" : "聊天接管：等待协议适配"}</p></div></article>;
}
