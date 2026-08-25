"use client";

import { useEffect, useMemo, useState } from "react";
import { Heart, LayoutTemplate, Loader2, Play, Search, Star, X } from "lucide-react";
import { getCodeProjects } from "@/lib/code-repository-api";
import { listPromptTemplates, setPromptTemplateFavorited, setPromptTemplateLiked, usePromptTemplate } from "@/lib/prompt-template-api";
import type { PromptTemplate, PromptTemplateStage } from "@/lib/prompt-template-types";
import type { CodeProject } from "@/lib/code-repository-types";

const stages: Array<{ id: "all" | PromptTemplateStage; label: string }> = [
  { id: "all", label: "全部阶段" }, { id: "requirements", label: "需求评审" }, { id: "design", label: "方案设计" },
  { id: "development", label: "开发实现" }, { id: "code-understanding", label: "代码理解" }, { id: "testing", label: "测试验证" }, { id: "delivery", label: "交付验收" },
];

export function PromptTemplateCanvasPicker({ onClose, onUsed }: { onClose: () => void; onUsed: (result: { template: PromptTemplate; project_id?: number | null; rendered_content: string }) => void }) {
  const [templates, setTemplates] = useState<PromptTemplate[]>([]);
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [query, setQuery] = useState("");
  const [stage, setStage] = useState<"all" | PromptTemplateStage>("all");
  const [template, setTemplate] = useState<PromptTemplate | null>(null);
  const [values, setValues] = useState<Record<string, string>>({});
  const [projectId, setProjectId] = useState<number | null>(null);
  const [using, setUsing] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void Promise.all([listPromptTemplates(), getCodeProjects()])
      .then(([nextTemplates, nextProjects]) => { if (!cancelled) { setTemplates(nextTemplates); setProjects(nextProjects); } })
      .catch((value) => { if (!cancelled) setError(value instanceof Error ? value.message : "加载模板市场失败，请稍后重试。"); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []);
  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === "Escape" && !using) template ? setTemplate(null) : onClose(); };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [onClose, template, using]);

  const visible = useMemo(() => templates.filter((item) => {
    const needle = query.trim().toLowerCase();
    return (stage === "all" || item.stage === stage) && (!needle || [item.name, item.description, ...item.tags].join(" ").toLowerCase().includes(needle));
  }), [query, stage, templates]);
  const replace = (next: PromptTemplate) => setTemplates((items) => items.map((item) => item.id === next.id ? next : item));
  const select = (next: PromptTemplate) => { setTemplate(next); setValues(Object.fromEntries(next.variables.map((item) => [item.key, item.default_value ?? ""]))); setProjectId(next.project_id ?? null); };
  const toggleLike = async (item: PromptTemplate) => { try { replace(await setPromptTemplateLiked(item.id, !item.liked_by_me)); } catch (value) { setError(value instanceof Error ? value.message : "点赞失败，请稍后重试。"); } };
  const toggleFavorite = async (item: PromptTemplate) => { try { replace(await setPromptTemplateFavorited(item.id, !item.favorited_by_me)); } catch (value) { setError(value instanceof Error ? value.message : "收藏失败，请稍后重试。"); } };
  const confirm = async () => {
    if (!template) return;
    setUsing(true);
    try { onUsed(await usePromptTemplate(template.id, { project_id: projectId, variables: values })); }
    catch (value) { setError(value instanceof Error ? value.message : "使用模板失败，请稍后重试。"); }
    finally { setUsing(false); }
  };
  const applicableProjects = template?.project_id ? projects.filter((item) => item.id === template.project_id) : projects;

  return <div className="fixed inset-0 z-[80] flex items-end bg-slate-950/45 p-0 backdrop-blur-[2px] sm:items-center sm:justify-center sm:p-4" role="presentation" onMouseDown={onClose}>
    <section className="flex max-h-[min(46rem,100dvh)] w-full flex-col rounded-t-3xl bg-white shadow-[0_24px_80px_rgba(15,23,42,0.28)] sm:max-w-4xl sm:rounded-2xl" role="dialog" aria-modal="true" aria-labelledby="canvas-template-picker-title" onMouseDown={(event) => event.stopPropagation()}>
      <header className="flex items-start gap-3 border-b border-slate-100 px-4 py-4 sm:px-5"><span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-blue-50 text-blue-600"><LayoutTemplate size={20}/></span><div className="min-w-0 flex-1"><h2 id="canvas-template-picker-title" className="text-base font-semibold text-slate-900">选择 Prompt 模板</h2><p className="mt-1 text-xs leading-5 text-slate-500">选择并填写变量后，会沿用原有逻辑在新聊天草稿中预填模板内容。</p></div><button type="button" onClick={onClose} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="关闭"><X size={17}/></button></header>
      {error && <div role="alert" className="mx-4 mt-3 rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700 sm:mx-5">{error}</div>}
      {!template ? <><div className="grid gap-2 border-b border-slate-100 p-4 sm:grid-cols-[minmax(0,1fr)_auto] sm:px-5"><label className="flex h-10 items-center gap-2 rounded-lg border border-slate-200 px-3 text-slate-400"><Search size={15}/><input autoFocus value={query} onChange={(event) => setQuery(event.target.value)} placeholder="搜索模板名称、用途或标签" className="w-full bg-transparent text-sm text-slate-800 outline-none"/></label><div className="flex max-w-full gap-1 overflow-x-auto">{stages.map((item) => <button key={item.id} type="button" onClick={() => setStage(item.id)} className={`whitespace-nowrap rounded-lg px-2.5 py-2 text-xs font-medium ${stage === item.id ? "bg-blue-50 text-blue-700" : "bg-slate-50 text-slate-500 hover:bg-slate-100"}`}>{item.label}</button>)}</div></div><div className="workspace-scroll min-h-0 flex-1 overflow-y-auto p-4 sm:p-5">{loading ? <div className="grid h-48 place-items-center"><Loader2 className="animate-spin text-blue-600"/></div> : visible.length ? <div className="grid gap-3 sm:grid-cols-2">{visible.map((item) => <article key={item.id} className="rounded-xl border border-slate-200 p-4 transition hover:border-blue-200 hover:bg-blue-50/30"><button type="button" onClick={() => select(item)} className="w-full text-left"><div className="flex items-start justify-between gap-3"><h3 className="font-semibold text-slate-800">{item.name}</h3><span className="shrink-0 text-xs text-slate-400">已用 {item.use_count}</span></div><p className="mt-1 line-clamp-2 text-xs leading-5 text-slate-500">{item.description}</p><div className="mt-3 flex flex-wrap gap-1">{item.tags.map((tag) => <span key={tag} className="rounded bg-slate-100 px-1.5 py-0.5 text-[11px] text-slate-500">{tag}</span>)}</div></button><div className="mt-3 flex justify-end gap-1 border-t border-slate-100 pt-2"><button type="button" onClick={() => void toggleLike(item)} className={`inline-flex h-7 items-center gap-1 rounded-md px-2 text-xs ${item.liked_by_me ? "bg-rose-50 text-rose-600" : "text-slate-500 hover:bg-slate-100"}`}><Heart size={13} fill={item.liked_by_me ? "currentColor" : "none"}/>{item.like_count}</button><button type="button" onClick={() => void toggleFavorite(item)} className={`grid h-7 w-7 place-items-center rounded-md ${item.favorited_by_me ? "bg-amber-50 text-amber-500" : "text-slate-500 hover:bg-slate-100"}`} aria-label="收藏"><Star size={13} fill={item.favorited_by_me ? "currentColor" : "none"}/></button><button type="button" onClick={() => select(item)} className="inline-flex h-7 items-center gap-1 rounded-md bg-blue-50 px-2 text-xs font-medium text-blue-600 hover:bg-blue-100"><Play size={12} fill="currentColor"/>使用</button></div></article>)}</div> : <p className="py-14 text-center text-sm text-slate-400">没有符合条件的模板。</p>}</div></> : <><div className="workspace-scroll min-h-0 flex-1 overflow-y-auto p-4 sm:p-5"><button type="button" onClick={() => setTemplate(null)} className="mb-4 text-xs font-medium text-blue-600 hover:underline">← 返回模板列表</button><h3 className="text-base font-semibold text-slate-900">使用模板 · {template.name}</h3><p className="mt-1 text-xs text-slate-500">确认变量后会在新聊天中创建草稿，不会自动发送。</p><label className="mt-5 block text-xs font-medium text-slate-600"><span className="mb-1.5 block">项目上下文</span><select value={projectId ?? ""} onChange={(event) => setProjectId(event.target.value ? Number(event.target.value) : null)} className="h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-sm">{!template.project_id && <option value="">不带项目上下文</option>}{applicableProjects?.map((item) => <option key={item.id} value={item.id}>{item.display_name}</option>)}</select></label>{template.variables.map((item) => <label key={item.key} className="mt-4 block text-xs font-medium text-slate-600"><span className="mb-1.5 block">{item.label}{item.required && <span className="ml-1 text-rose-500">*</span>}</span>{item.type === "textarea" ? <textarea value={values[item.key] ?? ""} onChange={(event) => setValues((current) => ({ ...current, [item.key]: event.target.value }))} placeholder={item.description ?? undefined} className="min-h-24 w-full rounded-lg border border-slate-200 px-3 py-2 text-sm"/> : item.type === "select" ? <select value={values[item.key] ?? ""} onChange={(event) => setValues((current) => ({ ...current, [item.key]: event.target.value }))} className="h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-sm">{!item.required && <option value="">请选择</option>}{item.options.map((option) => <option key={option} value={option}>{option}</option>)}</select> : <input value={values[item.key] ?? ""} onChange={(event) => setValues((current) => ({ ...current, [item.key]: event.target.value }))} placeholder={item.description ?? undefined} className="h-10 w-full rounded-lg border border-slate-200 px-3 text-sm"/>}</label>)}</div><footer className="flex justify-end gap-2 border-t border-slate-100 p-4 sm:px-5"><button type="button" onClick={() => setTemplate(null)} disabled={using} className="h-10 rounded-lg px-4 text-sm font-medium text-slate-600 hover:bg-slate-100 disabled:opacity-50">取消</button><button type="button" onClick={() => void confirm()} disabled={using} className="inline-flex h-10 items-center gap-2 rounded-lg bg-blue-600 px-4 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50">{using ? <Loader2 size={16} className="animate-spin"/> : <Play size={14} fill="currentColor"/>}确认并新开会话</button></footer></>}</section>
  </div>;
}
