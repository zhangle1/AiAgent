"use client";

import { useEffect, useMemo, useState } from "react";
import { BarChart3, Bot, ChevronDown, Database, Loader2, RefreshCw, Send, ShieldCheck, Sparkles, X } from "lucide-react";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";
import { getUsageDayDetail, getUsageSummary, type UsageActivityDay, type UsageDayDetail, type UsageProviderSummary, type UsageSummary } from "@/lib/usage-api";

export function UsageSettingsPage() {
  const [summary, setSummary] = useState<UsageSummary | null>(null);
  const [scope, setScope] = useState<"me" | "all">("me");
  const [dayDetail, setDayDetail] = useState<UsageDayDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load(nextScope = scope) {
    setLoading(true);
    try {
      const value = await getUsageSummary({ scope: nextScope, days: 365 });
      setSummary(value);
      setScope(value.scope);
      setError(null);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "无法加载流量统计。");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void load(); }, []);

  async function openDayDetail(date: string) {
    setDetailLoading(true);
    setDayDetail(null);
    try {
      setDayDetail(await getUsageDayDetail(date.slice(0, 10), scope));
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "无法加载该日的 Token 明细。");
    } finally {
      setDetailLoading(false);
    }
  }

  return <section>
    <SettingsPageHeader
      title="流量统计"
      description="查看自有 Agent 与第三方代理的 Token 消耗、调用次数和使用趋势。当前显示的 Token 为本地估算值；当代理提供原始 usage 数据后会自动替换为实测值。"
      action={<button type="button" onClick={() => void load()} disabled={loading} className="inline-flex h-9 items-center gap-2 rounded-md border border-slate-200 bg-white px-3 text-xs text-slate-600 hover:bg-slate-50 disabled:opacity-50"><RefreshCw size={14} className={loading ? "animate-spin" : ""}/>刷新数据</button>}
    />

    <section className="mb-5 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-blue-100 bg-blue-50/60 px-4 py-3">
      <div className="flex min-w-0 items-center gap-2 text-xs text-blue-900"><ShieldCheck size={16} className="shrink-0 text-blue-600"/><span>数据按登录用户隔离记录；管理员接口已预留，可在后端配置管理员账号后查看全员汇总。</span></div>
      <div className="flex shrink-0 items-center gap-2">
        {summary?.can_view_all && <label className="relative"><select value={scope} onChange={(event) => void load(event.target.value as "me" | "all")} className="h-8 appearance-none rounded-lg border border-blue-200 bg-white py-0 pl-3 pr-7 text-xs text-blue-800 outline-none"><option value="me">我的数据</option><option value="all">全员汇总</option></select><ChevronDown size={13} className="pointer-events-none absolute right-2 top-2 text-blue-500"/></label>}
      </div>
    </section>

    {loading && !summary ? <div className="flex min-h-64 items-center justify-center gap-2 rounded-2xl border border-dashed border-slate-200 text-sm text-slate-500"><Loader2 size={16} className="animate-spin"/>正在汇总流量数据…</div> : summary ? <UsageDashboard summary={summary} onSelectDay={(date) => void openDayDetail(date)}/> : null}
    {error && <p className="mt-4 rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-xs text-rose-700">{error}</p>}
    {(detailLoading || dayDetail) && <UsageDayDetailDialog detail={dayDetail} loading={detailLoading} onClose={() => { setDayDetail(null); setDetailLoading(false); }}/>} 
  </section>;
}

function UsageDashboard({ summary, onSelectDay }: { summary: UsageSummary; onSelectDay: (date: string) => void }) {
  const cards = [
    { label: "累计 Token", value: formatTokens(summary.total_tokens), icon: Sparkles, tone: "text-violet-600 bg-violet-50" },
    { label: "输入 Token", value: formatTokens(summary.prompt_tokens), icon: Send, tone: "text-blue-600 bg-blue-50" },
    { label: "输出 Token", value: formatTokens(summary.completion_tokens), icon: Bot, tone: "text-emerald-600 bg-emerald-50" },
    { label: "完成轮次", value: String(summary.turn_count), icon: Database, tone: "text-amber-600 bg-amber-50" },
  ];

  return <>
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      {cards.map((card) => { const Icon = card.icon; return <article key={card.label} className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm"><div className="flex items-center justify-between"><span className={`grid h-9 w-9 place-items-center rounded-xl ${card.tone}`}><Icon size={17}/></span><span className="text-[11px] text-slate-400">近 {summary.period_days} 天</span></div><p className="mt-5 text-2xl font-semibold tracking-tight text-slate-900">{card.value}</p><p className="mt-1 text-xs text-slate-500">{card.label}</p></article>; })}
    </div>

    <section className="mt-5 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm"><div className="flex flex-wrap items-start justify-between gap-3"><div><h2 className="flex items-center gap-2 text-[15px] font-semibold text-slate-900"><BarChart3 size={17} className="text-blue-600"/>Token 活动</h2><p className="mt-1 text-xs text-slate-500">每个方块代表一天；点击日期可查看当日按代理和模型拆分的 Token。</p></div><span className="rounded-full bg-slate-100 px-2.5 py-1 text-[10px] text-slate-500">{summary.estimated_turn_count > 0 ? `${summary.estimated_turn_count} 次为预估值` : "实测 usage"}</span></div><UsageHeatmap activity={summary.activity} onSelectDay={onSelectDay}/></section>

    <section className="mt-5 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm"><div><h2 className="text-[15px] font-semibold text-slate-900">代理与模型明细</h2><p className="mt-1 text-xs text-slate-500">第三方代理与自有 Agent 分开统计，方便后续增加不同代理适配器。</p></div><div className="mt-4 overflow-x-auto"><table className="w-full min-w-[640px] text-left text-xs"><thead className="border-b border-slate-100 text-slate-400"><tr><th className="pb-2.5 font-medium">来源</th><th className="pb-2.5 font-medium">模型</th><th className="pb-2.5 text-right font-medium">Token</th><th className="pb-2.5 text-right font-medium">输入 / 输出</th><th className="pb-2.5 text-right font-medium">轮次</th></tr></thead><tbody>{summary.providers.length > 0 ? summary.providers.map((provider) => <UsageProviderRow key={`${provider.provider_kind}-${provider.provider_id}-${provider.model || "default"}`} provider={provider}/>) : <tr><td colSpan={5} className="py-12 text-center text-slate-400">该时间范围内尚无已完成的对话用量。</td></tr>}</tbody></table></div></section>
  </>;
}

function UsageHeatmap({ activity, onSelectDay }: { activity: UsageActivityDay[]; onSelectDay: (date: string) => void }) {
  const max = useMemo(() => Math.max(0, ...activity.map((item) => item.total_tokens)), [activity]);
  const columns = Math.max(1, Math.ceil(activity.length / 7));
  const lastActivity = activity[activity.length - 1];
  return <div className="mt-5 overflow-hidden"><div className="grid w-full min-w-0 gap-1" style={{ gridTemplateRows: "repeat(7, minmax(0, 1fr))", gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`, gridAutoFlow: "column" }}>{activity.map((item) => <button key={item.date} type="button" onClick={() => onSelectDay(item.date)} title={`${formatDate(item.date)} · ${formatTokens(item.total_tokens)} Token · ${item.turn_count} 次，点击查看明细`} className={`aspect-square min-h-2 rounded-[3px] transition hover:brightness-90 hover:ring-2 hover:ring-inset hover:ring-slate-400 focus:outline-none focus:ring-2 focus:ring-inset focus:ring-blue-600 ${heatTone(item.total_tokens, max)}`}/>)}</div><div className="mt-2 flex justify-between text-[10px] text-slate-400"><span>{activity[0] ? formatDate(activity[0].date) : ""}</span><span>{lastActivity ? formatDate(lastActivity.date) : ""}</span></div></div>;
}

function UsageDayDetailDialog({ detail, loading, onClose }: { detail: UsageDayDetail | null; loading: boolean; onClose: () => void }) {
  return <div className="fixed inset-0 z-[100] flex items-center justify-center bg-slate-950/30 p-5 backdrop-blur-[2px]" role="presentation" onMouseDown={onClose}>
    <section className="w-full max-w-xl rounded-[22px] border border-white/80 bg-white p-5 shadow-[0_24px_80px_rgba(15,23,42,0.24)]" role="dialog" aria-modal="true" aria-label="每日 Token 明细" onMouseDown={(event) => event.stopPropagation()}>
      <div className="mb-1 flex items-start gap-3 border-b border-slate-100 pb-4"><span className="mt-0.5 grid h-9 w-9 place-items-center rounded-xl bg-blue-50 text-blue-600"><BarChart3 size={17}/></span><div className="min-w-0 flex-1"><h2 className="text-lg font-semibold tracking-tight text-slate-900">{detail ? `${formatFullDate(detail.date)} 的 Token 明细` : "加载每日 Token 明细"}</h2><p className="mt-1 text-xs leading-5 text-slate-500">按实际使用的代理与模型拆分，包含输入、输出和总 Token。</p></div><button type="button" onClick={onClose} className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 text-slate-400 transition hover:border-slate-300 hover:bg-slate-50 hover:text-slate-700" aria-label="关闭明细"><X size={17}/></button></div>
      {loading || !detail ? <div className="flex min-h-44 items-center justify-center gap-2 text-sm text-slate-500"><Loader2 size={16} className="animate-spin"/>正在读取该日明细…</div> : <><div className="mt-5 grid grid-cols-3 gap-3 rounded-xl bg-slate-50 p-3 text-center"><div><p className="text-lg font-semibold text-slate-900">{formatTokens(detail.total_tokens)}</p><p className="mt-1 text-[11px] text-slate-500">总 Token</p></div><div><p className="text-lg font-semibold text-slate-900">{formatTokens(detail.completion_tokens)}</p><p className="mt-1 text-[11px] text-slate-500">输出 Token</p></div><div><p className="text-lg font-semibold text-slate-900">{detail.turn_count}</p><p className="mt-1 text-[11px] text-slate-500">完成轮次</p></div></div><div className="mt-5 max-h-[45vh] overflow-auto rounded-xl border border-slate-100"><table className="w-full text-left text-xs"><thead className="sticky top-0 bg-white text-slate-400"><tr><th className="px-3 py-2.5 font-medium">代理</th><th className="px-3 py-2.5 font-medium">模型</th><th className="px-3 py-2.5 text-right font-medium">输入</th><th className="px-3 py-2.5 text-right font-medium">输出</th><th className="px-3 py-2.5 text-right font-medium">总计</th></tr></thead><tbody>{detail.providers.length > 0 ? detail.providers.map((provider) => <tr key={`${provider.provider_kind}-${provider.provider_id}-${provider.model || "default"}`} className="border-t border-slate-50"><td className="px-3 py-3 text-slate-700">{provider.provider_kind === "third_party" ? "第三方" : "自有"} · {provider.provider_id}</td><td className="px-3 py-3 text-slate-600">{provider.model || "默认模型"}</td><td className="px-3 py-3 text-right text-slate-600">{formatTokens(provider.prompt_tokens)}</td><td className="px-3 py-3 text-right font-medium text-emerald-700">{formatTokens(provider.completion_tokens)}</td><td className="px-3 py-3 text-right font-semibold text-slate-900">{formatTokens(provider.total_tokens)}</td></tr>) : <tr><td colSpan={5} className="px-3 py-10 text-center text-slate-400">该日没有已完成的对话用量。</td></tr>}</tbody></table></div></>}
    </section>
  </div>;
}

function UsageProviderRow({ provider }: { provider: UsageProviderSummary }) {
  const thirdParty = provider.provider_kind === "third_party";
  return <tr className="border-b border-slate-50 last:border-0"><td className="py-3"><span className={`inline-flex items-center gap-1.5 rounded-full px-2 py-1 text-[11px] ${thirdParty ? "bg-violet-50 text-violet-700" : "bg-blue-50 text-blue-700"}`}>{thirdParty ? "第三方" : "自有"} · {provider.provider_id}</span></td><td className="py-3 text-slate-600">{provider.model || "默认模型"}</td><td className="py-3 text-right font-medium text-slate-900">{formatTokens(provider.total_tokens)}</td><td className="py-3 text-right text-slate-500">{formatTokens(provider.prompt_tokens)} / {formatTokens(provider.completion_tokens)}</td><td className="py-3 text-right text-slate-500">{provider.turn_count}{provider.estimated_turn_count > 0 ? <span className="ml-1 text-[10px] text-amber-600">估</span> : null}</td></tr>;
}

function heatTone(value: number, max: number) {
  if (value <= 0 || max <= 0) return "bg-slate-100";
  const ratio = value / max;
  if (ratio > 0.72) return "bg-blue-600";
  if (ratio > 0.45) return "bg-blue-500";
  if (ratio > 0.2) return "bg-blue-300";
  return "bg-blue-100";
}

function formatTokens(value: number) {
  if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(value >= 10_000_000 ? 0 : 1)}M`;
  if (value >= 1_000) return `${(value / 1_000).toFixed(value >= 10_000 ? 0 : 1)}K`;
  return String(value);
}

function formatDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : `${date.getMonth() + 1}/${date.getDate()}`;
}

function formatFullDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : `${date.getFullYear()}年${date.getMonth() + 1}月${date.getDate()}日`;
}
