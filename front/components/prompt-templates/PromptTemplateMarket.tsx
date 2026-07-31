"use client";

import { type ChangeEvent, type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { Download, FileUp, Heart, LayoutTemplate, Loader2, Play, Plus, Save, Search, Star, Trash2, X } from "lucide-react";
import { createPromptTemplate, deletePromptTemplate, listPromptTemplates, setPromptTemplateFavorited, setPromptTemplateLiked, updatePromptTemplate, usePromptTemplate } from "@/lib/prompt-template-api";
import type { PromptTemplate, PromptTemplateSaveRequest, PromptTemplateStage, PromptTemplateVariable, PromptTemplateVisibility } from "@/lib/prompt-template-types";
import { getCodeProjects } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";

const stages: Array<{ id: "all" | PromptTemplateStage; label: string }> = [
  { id: "all", label: "全部阶段" }, { id: "requirements", label: "需求评审" }, { id: "design", label: "方案设计" },
  { id: "development", label: "开发实现" }, { id: "code-understanding", label: "代码理解" }, { id: "testing", label: "测试验证" }, { id: "delivery", label: "交付验收" },
];
const visibilityLabels: Record<PromptTemplateVisibility, string> = { personal: "仅自己", project: "项目成员", team: "团队成员" };
const stageLabels: Record<PromptTemplateStage, string> = Object.fromEntries(stages.filter((item) => item.id !== "all").map((item) => [item.id, item.label])) as Record<PromptTemplateStage, string>;
const variablePattern = /\$\{([A-Za-z_][A-Za-z0-9_]*)\}/g;

type ViewTab = "all" | "favorites" | "mine";

function blankTemplate(): PromptTemplate {
  const now = new Date().toISOString();
  return { id: 0, name: "未命名模板", description: "说明这个模板适合解决什么问题。", stage: "development", tags: [], body: "你是项目助手。请基于 ${task} 输出可执行的结果，并标明证据、假设与未知项。", variables: [{ key: "task", label: "任务描述", type: "textarea", required: true, default_value: "", description: "请输入要完成的工作", options: [] }], visibility: "personal", author_name: "我", created_by_me: true, liked_by_me: false, favorited_by_me: false, like_count: 0, use_count: 0, created_at: now, updated_at: now };
}

function toRequest(template: PromptTemplate): PromptTemplateSaveRequest {
  return { name: template.name, description: template.description, stage: template.stage, tags: template.tags, body: template.body, variables: template.variables, project_id: template.project_id ?? null, visibility: template.visibility };
}

function extractKeys(body: string): string[] {
  return [...new Set(Array.from(body.matchAll(variablePattern)).map((item) => item[1]))];
}

function syncVariables(template: PromptTemplate): PromptTemplate {
  const keys = extractKeys(template.body);
  const existing = new Map(template.variables.map((item) => [item.key, item]));
  return { ...template, variables: keys.map((key) => existing.get(key) ?? { key, label: key.replaceAll("_", " "), type: "text", required: false, default_value: "", description: "从正文识别的变量", options: [] }) };
}

export function PromptTemplateMarket() {
  const router = useRouter();
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [templates, setTemplates] = useState<PromptTemplate[]>([]);
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [stage, setStage] = useState<"all" | PromptTemplateStage>("all");
  const [tab, setTab] = useState<ViewTab>("all");
  const [query, setQuery] = useState("");
  const [editing, setEditing] = useState<PromptTemplate | null>(null);
  const [usingTemplate, setUsingTemplate] = useState<PromptTemplate | null>(null);
  const [usingValues, setUsingValues] = useState<Record<string, string>>({});
  const [useProjectId, setUseProjectId] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);

  const refresh = async () => {
    setLoading(true);
    setError(null);
    try {
      const [nextTemplates, nextProjects] = await Promise.all([listPromptTemplates(), getCodeProjects()]);
      setTemplates(nextTemplates);
      setProjects(nextProjects);
    } catch (value) {
      setError(value instanceof Error ? value.message : "加载模板市场失败。");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void refresh(); }, []);

  const visibleTemplates = useMemo(() => templates.filter((item) => {
    if (stage !== "all" && item.stage !== stage) return false;
    if (tab === "favorites" && !item.favorited_by_me) return false;
    if (tab === "mine" && !item.created_by_me) return false;
    const needle = query.trim().toLowerCase();
    return !needle || [item.name, item.description, ...item.tags].join(" ").toLowerCase().includes(needle);
  }), [query, stage, tab, templates]);

  function replaceTemplate(next: PromptTemplate) {
    setTemplates((items) => items.some((item) => item.id === next.id) ? items.map((item) => item.id === next.id ? next : item) : [next, ...items]);
  }

  async function toggleLiked(template: PromptTemplate) {
    try { replaceTemplate(await setPromptTemplateLiked(template.id, !template.liked_by_me)); }
    catch (value) { setError(value instanceof Error ? value.message : "点赞失败。"); }
  }

  async function toggleFavorited(template: PromptTemplate) {
    try { replaceTemplate(await setPromptTemplateFavorited(template.id, !template.favorited_by_me)); }
    catch (value) { setError(value instanceof Error ? value.message : "收藏失败。"); }
  }

  function openUse(template: PromptTemplate) {
    setUsingTemplate(template);
    setUsingValues(Object.fromEntries(template.variables.map((item) => [item.key, item.default_value ?? ""])));
    setUseProjectId(template.project_id ?? null);
  }

  async function confirmUse() {
    if (!usingTemplate) return;
    setSaving(true);
    try {
      const result = await usePromptTemplate(usingTemplate.id, { project_id: useProjectId, variables: usingValues });
      const handoffId = `${result.template.id}-${Date.now()}`;
      sessionStorage.setItem("aiagent:pending-template-turn", JSON.stringify({ handoff_id: handoffId, template_id: result.template.id, template_name: result.template.name, project_id: result.project_id ?? null, content: result.rendered_content }));
      setUsingTemplate(null);
      const query = new URLSearchParams({ template_handoff: handoffId });
      if (result.project_id) query.set("project", String(result.project_id));
      router.push(`/chat?${query.toString()}`);
    } catch (value) {
      setError(value instanceof Error ? value.message : "使用模板失败。");
    } finally { setSaving(false); }
  }

  async function saveTemplate() {
    if (!editing) return;
    setSaving(true);
    try {
      const payload = toRequest(syncVariables(editing));
      const saved = editing.id ? await updatePromptTemplate(editing.id, payload) : await createPromptTemplate(payload);
      replaceTemplate(saved);
      setEditing(saved);
      setError(null);
    } catch (value) {
      setError(value instanceof Error ? value.message : "保存模板失败。");
    } finally { setSaving(false); }
  }

  async function removeTemplate() {
    if (!editing?.id || !window.confirm(`确定删除模板“${editing.name}”吗？`)) return;
    setSaving(true);
    try {
      await deletePromptTemplate(editing.id);
      setTemplates((items) => items.filter((item) => item.id !== editing.id));
      setEditing(null);
    } catch (value) { setError(value instanceof Error ? value.message : "删除模板失败。"); }
    finally { setSaving(false); }
  }

  function exportMarkdown(template: PromptTemplate) {
    const frontmatter = [
      "---", "aiagent-template: 1", `id: ${template.id}`, `name: ${JSON.stringify(template.name)}`, "version: 1.0.0", `stage: ${template.stage}`,
      `tags: [${template.tags.join(", ")}]`, `visibility: ${template.visibility}`, `projectId: ${template.project_id ?? ""}`, "variables:",
      ...template.variables.flatMap((item) => [`  - key: ${item.key}`, `    label: ${JSON.stringify(item.label)}`, `    type: ${item.type}`, `    required: ${item.required}`, `    defaultValue: ${JSON.stringify(item.default_value ?? "")}`, ...(item.options.length ? [`    options: [${item.options.join(", ")}]`] : [])]),
      "---", "", template.body, "",
    ].join("\n");
    const link = document.createElement("a");
    link.href = URL.createObjectURL(new Blob([frontmatter], { type: "text/markdown;charset=utf-8" }));
    link.download = `${template.name.replace(/[\\/:*?"<>|]/g, "-") || "prompt-template"}.md`;
    link.click();
    URL.revokeObjectURL(link.href);
  }

  function handleImport(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    if (file.size > 200 * 1024) { setError("导入文件不能超过 200KB。"); return; }
    const reader = new FileReader();
    reader.onload = () => {
      try { setEditing(parseTemplateMarkdown(String(reader.result ?? ""))); }
      catch (value) { setError(value instanceof Error ? value.message : "导入 MD 失败。"); }
    };
    reader.readAsText(file, "utf-8");
  }

  return <main className="min-h-screen bg-slate-50 text-slate-900">
    <div className="mx-auto max-w-[1500px] px-8 py-7">
      <div className="rounded-xl border border-slate-200 bg-white px-4 py-2 text-xs text-slate-500">⌂ 工作台　/　模板市场</div>
      <section className="mt-7 flex flex-wrap items-end justify-between gap-4"><div><div className="flex items-center gap-2"><LayoutTemplate className="text-blue-600" size={24}/><h1 className="font-serif text-3xl font-semibold tracking-tight">Prompt 模板市场</h1></div><p className="mt-2 text-sm text-slate-500">按研发阶段发现、复用和沉淀团队的高质量提问模板。</p></div><div className="flex gap-2"><button type="button" onClick={() => fileInputRef.current?.click()} className="inline-flex h-9 items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 text-sm font-medium text-slate-700 hover:bg-slate-50"><FileUp size={15}/>导入 MD</button><button type="button" onClick={() => setEditing(blankTemplate())} className="inline-flex h-9 items-center gap-2 rounded-lg bg-blue-600 px-3 text-sm font-medium text-white shadow-sm hover:bg-blue-700"><Plus size={16}/>创建模板</button><input ref={fileInputRef} type="file" accept=".md,text/markdown" className="hidden" onChange={handleImport}/></div></section>
      <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-3"><div className="flex flex-wrap items-center gap-3"><label className="flex h-9 min-w-[260px] flex-1 items-center gap-2 rounded-lg border border-slate-200 px-3 text-slate-400"><Search size={15}/><input value={query} onChange={(event) => setQuery(event.target.value)} className="w-full border-0 bg-transparent text-sm text-slate-800 outline-none" placeholder="搜索模板名称、用途或标签"/></label><div className="flex max-w-full gap-1 overflow-x-auto">{stages.map((item) => <button type="button" key={item.id} onClick={() => setStage(item.id)} className={`whitespace-nowrap rounded-lg px-3 py-2 text-xs font-medium ${stage === item.id ? "bg-blue-50 text-blue-700" : "bg-slate-50 text-slate-500 hover:bg-slate-100"}`}>{item.label}</button>)}</div></div></section>
      <div className="mt-6 flex gap-6 border-b border-slate-200">{([{ id: "all", label: "全部模板", count: templates.length }, { id: "favorites", label: "已收藏", count: templates.filter((item) => item.favorited_by_me).length }, { id: "mine", label: "我创建的", count: templates.filter((item) => item.created_by_me).length }] as Array<{ id: ViewTab; label: string; count: number }>).map((item) => <button key={item.id} type="button" onClick={() => setTab(item.id)} className={`relative pb-3 text-sm font-medium ${tab === item.id ? "text-blue-600" : "text-slate-500 hover:text-slate-800"}`}>{item.label} ({item.count}){tab === item.id && <span className="absolute inset-x-0 -bottom-px h-0.5 bg-blue-600"/>}</button>)}</div>
      {error && <div className="mt-4 flex items-center justify-between rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700"><span>{error}</span><button type="button" onClick={() => setError(null)} aria-label="关闭错误提示"><X size={16}/></button></div>}
      {loading ? <div className="grid min-h-72 place-items-center text-slate-400"><Loader2 className="animate-spin" size={24}/></div> : visibleTemplates.length ? <section className="mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-3">{visibleTemplates.map((template) => <TemplateCard key={template.id} template={template} onOpen={() => setEditing(template)} onUse={() => openUse(template)} onLike={() => void toggleLiked(template)} onFavorite={() => void toggleFavorited(template)}/>)}</section> : <div className="mt-5 rounded-2xl border border-dashed border-slate-300 bg-white py-20 text-center text-sm text-slate-400">没有匹配的模板。试试调整筛选条件，或创建一个新模板。</div>}
    </div>
    {editing && <TemplateEditor template={editing} projects={projects} saving={saving} onChange={setEditing} onClose={() => setEditing(null)} onSave={() => void saveTemplate()} onDelete={() => void removeTemplate()} onExport={() => exportMarkdown(editing)} onUse={() => openUse(editing)}/>} 
    {usingTemplate && <UseTemplateDialog template={usingTemplate} projects={projects} values={usingValues} projectId={useProjectId} saving={saving} onChangeValue={(key, value) => setUsingValues((current) => ({ ...current, [key]: value }))} onChangeProject={setUseProjectId} onClose={() => setUsingTemplate(null)} onConfirm={() => void confirmUse()}/>} 
  </main>;
}

function TemplateCard({ template, onOpen, onUse, onLike, onFavorite }: { template: PromptTemplate; onOpen: () => void; onUse: () => void; onLike: () => void; onFavorite: () => void }) {
  return <article className="flex min-h-60 flex-col rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:shadow-md"><div className="flex items-start justify-between gap-3"><span className="rounded-md bg-blue-50 px-2 py-1 text-[11px] font-semibold text-blue-700">{stageLabels[template.stage]}</span><span className="text-xs text-slate-400">{visibilityLabels[template.visibility]}</span></div><button type="button" onClick={onOpen} className="mt-3 text-left"><h2 className="text-base font-semibold text-slate-800 hover:text-blue-700">{template.name}</h2><p className="mt-1 line-clamp-2 text-sm leading-5 text-slate-500">{template.description}</p></button><div className="mt-3 flex flex-wrap gap-1.5">{template.tags.map((tag) => <span key={tag} className="rounded-md bg-slate-100 px-2 py-0.5 text-[11px] text-slate-500">{tag}</span>)}</div><div className="mt-auto flex items-center justify-between border-t border-slate-100 pt-4 text-xs text-slate-400"><span className="truncate">{template.author_name} · 已用 {template.use_count}</span><div className="flex items-center gap-1"><button type="button" onClick={onLike} className={`inline-flex h-7 items-center gap-1 rounded-md px-1.5 ${template.liked_by_me ? "bg-red-50 text-red-600" : "hover:bg-slate-100"}`} aria-label="点赞"><Heart size={14} fill={template.liked_by_me ? "currentColor" : "none"}/>{template.like_count}</button><button type="button" onClick={onFavorite} className={`grid h-7 w-7 place-items-center rounded-md ${template.favorited_by_me ? "bg-amber-50 text-amber-500" : "hover:bg-slate-100"}`} aria-label="收藏"><Star size={14} fill={template.favorited_by_me ? "currentColor" : "none"}/></button><button type="button" onClick={onUse} className="grid h-7 w-7 place-items-center rounded-md bg-blue-50 text-blue-600 hover:bg-blue-100" aria-label="一键使用"><Play size={13} fill="currentColor"/></button></div></div></article>;
}

function TemplateEditor({ template, projects, saving, onChange, onClose, onSave, onDelete, onExport, onUse }: { template: PromptTemplate; projects: CodeProject[]; saving: boolean; onChange: (template: PromptTemplate) => void; onClose: () => void; onSave: () => void; onDelete: () => void; onExport: () => void; onUse: () => void }) {
  const preview = renderPreview(template.body, template.variables);
  const update = <K extends keyof PromptTemplate>(key: K, value: PromptTemplate[K]) => onChange({ ...template, [key]: value });
  const updateBody = (body: string) => onChange(syncVariables({ ...template, body }));
  const addVariable = () => { const key = `field_${template.variables.length + 1}`; update("variables", [...template.variables, { key, label: "新属性", type: "text", required: false, default_value: "", description: "", options: [] }]); };
  const updateVariable = (index: number, patch: Partial<PromptTemplateVariable>) => update("variables", template.variables.map((item, current) => current === index ? { ...item, ...patch } : item));
  return <Modal><div className="flex min-h-[78vh] max-h-[calc(100vh-40px)] w-[min(1240px,calc(100vw-32px))] flex-col overflow-hidden rounded-2xl bg-white shadow-2xl"><header className="flex flex-wrap items-center gap-3 border-b border-slate-200 px-5 py-4"><div className="min-w-0 flex-1"><h2 className="truncate text-lg font-semibold">{template.name || "模板详情"}</h2><p className="mt-0.5 text-xs text-slate-400">左侧预览与编辑，右侧配置属性和项目范围</p></div><button type="button" onClick={onExport} className="inline-flex h-9 items-center gap-2 rounded-lg border border-slate-200 px-3 text-sm text-slate-600 hover:bg-slate-50"><Download size={15}/>导出 MD</button><button type="button" onClick={onSave} disabled={saving} className="inline-flex h-9 items-center gap-2 rounded-lg border border-slate-200 px-3 text-sm font-medium text-slate-700 disabled:opacity-50"><Save size={15}/>{saving ? "保存中" : "保存草稿"}</button><button type="button" onClick={onUse} disabled={!template.id} className="inline-flex h-9 items-center gap-2 rounded-lg bg-blue-600 px-3 text-sm font-medium text-white disabled:cursor-not-allowed disabled:opacity-50"><Play size={14} fill="currentColor"/>一键使用</button><button type="button" onClick={onClose} className="grid h-9 w-9 place-items-center rounded-lg text-slate-500 hover:bg-slate-100" aria-label="关闭"><X size={19}/></button></header><div className="grid min-h-0 flex-1 grid-cols-[minmax(0,2fr)_minmax(330px,1fr)] overflow-auto"><div className="grid min-h-0 grid-rows-2 border-r border-slate-200"><section className="min-h-0 border-b border-slate-200 p-5"><PanelTitle title="预览" subtitle="变量以标签高亮"/><div className="h-[calc(100%-30px)] overflow-auto rounded-xl border border-slate-200 bg-slate-50 p-4 font-mono text-[13px] leading-6 text-slate-700">{preview}</div></section><section className="min-h-0 p-5"><PanelTitle title="编辑文字区" subtitle="使用 ${field} 增加变量"/><textarea value={template.body} onChange={(event) => updateBody(event.target.value)} className="h-[calc(100%-30px)] w-full resize-none rounded-xl border border-slate-200 p-3 font-mono text-[13px] leading-6 outline-none transition focus:border-blue-400 focus:ring-4 focus:ring-blue-50" spellCheck={false}/></section></div><aside className="overflow-y-auto bg-slate-50/60 p-5"><h3 className="mb-4 font-semibold">模板配置</h3><Field label="名称"><input value={template.name} onChange={(event) => update("name", event.target.value)} maxLength={120}/></Field><Field label="摘要"><textarea value={template.description} onChange={(event) => update("description", event.target.value)} maxLength={320}/></Field><div className="grid grid-cols-2 gap-3"><Field label="研发阶段"><select value={template.stage} onChange={(event) => update("stage", event.target.value as PromptTemplateStage)}>{stages.filter((item) => item.id !== "all").map((item) => <option value={item.id} key={item.id}>{item.label}</option>)}</select></Field><Field label="可见范围"><select value={template.visibility} onChange={(event) => update("visibility", event.target.value as PromptTemplateVisibility)}><option value="personal">仅自己</option><option value="project">项目成员</option><option value="team">团队成员</option></select></Field></div><Field label="选择项目"><select value={template.project_id ?? ""} onChange={(event) => update("project_id", event.target.value ? Number(event.target.value) : null)}><option value="">不绑定项目</option>{projects.map((project) => <option value={project.id} key={project.id}>{project.display_name}</option>)}</select></Field><Field label="标签（用逗号分隔）"><input value={template.tags.join(", ")} onChange={(event) => update("tags", event.target.value.split(/[，,]/).map((item) => item.trim()).filter(Boolean))}/></Field><div className="mt-5 flex items-center justify-between border-t border-slate-200 pt-4"><span className="text-sm font-semibold">属性 / 变量</span><span className="text-xs text-slate-400">{template.variables.length} 个</span></div><div className="mt-2 space-y-2">{template.variables.map((item, index) => <VariableEditor key={`${item.key}-${index}`} item={item} onChange={(patch) => updateVariable(index, patch)} onRemove={() => update("variables", template.variables.filter((_, current) => current !== index))}/>)}</div><button type="button" onClick={addVariable} className="mt-2 w-full rounded-lg border border-dashed border-blue-300 bg-blue-50/50 px-3 py-2 text-sm font-medium text-blue-700 hover:bg-blue-50"><Plus size={15} className="mr-1 inline"/>新增属性</button>{template.id > 0 && template.created_by_me && <button type="button" onClick={onDelete} disabled={saving} className="mt-5 inline-flex items-center gap-2 text-sm text-red-600 hover:text-red-700"><Trash2 size={14}/>删除此模板</button>}</aside></div><footer className="flex justify-end gap-2 border-t border-slate-200 px-5 py-3"><button type="button" onClick={onClose} className="h-9 rounded-lg border border-slate-200 px-4 text-sm text-slate-600 hover:bg-slate-50">取消</button><button type="button" onClick={onSave} disabled={saving} className="h-9 rounded-lg bg-blue-600 px-4 text-sm font-medium text-white disabled:opacity-50">{saving ? "保存中" : "保存草稿"}</button></footer></div></Modal>;
}

function VariableEditor({ item, onChange, onRemove }: { item: PromptTemplateVariable; onChange: (patch: Partial<PromptTemplateVariable>) => void; onRemove: () => void }) {
  return <div className="rounded-xl border border-slate-200 bg-white p-3"><div className="flex items-center justify-between gap-2"><input value={item.key} onChange={(event) => onChange({ key: event.target.value.replace(/[^A-Za-z0-9_]/g, "") })} className="min-w-0 flex-1 rounded-md border border-slate-200 px-2 py-1 text-xs font-mono text-blue-700 outline-none focus:border-blue-400" placeholder="field_name"/><button type="button" onClick={onRemove} className="text-xs text-red-500 hover:text-red-700">移除</button></div><input value={item.label} onChange={(event) => onChange({ label: event.target.value })} className="mt-2 w-full rounded-md border border-slate-200 px-2 py-1.5 text-xs outline-none focus:border-blue-400" placeholder="显示名称"/><div className="mt-2 grid grid-cols-2 gap-2"><select value={item.type} onChange={(event) => onChange({ type: event.target.value as PromptTemplateVariable["type"] })} className="rounded-md border border-slate-200 px-2 py-1.5 text-xs"><option value="text">短文本</option><option value="textarea">多行文本</option><option value="select">选择</option></select><label className="flex items-center gap-1 text-xs text-slate-600"><input type="checkbox" checked={item.required} onChange={(event) => onChange({ required: event.target.checked })}/>必填</label></div><input value={item.default_value ?? ""} onChange={(event) => onChange({ default_value: event.target.value })} className="mt-2 w-full rounded-md border border-slate-200 px-2 py-1.5 text-xs outline-none focus:border-blue-400" placeholder="默认值"/>{item.type === "select" && <input value={item.options.join(", ")} onChange={(event) => onChange({ options: event.target.value.split(/[，,]/).map((value) => value.trim()).filter(Boolean) })} className="mt-2 w-full rounded-md border border-slate-200 px-2 py-1.5 text-xs outline-none focus:border-blue-400" placeholder="选项，用逗号分隔"/>}</div>;
}

function UseTemplateDialog({ template, projects, values, projectId, saving, onChangeValue, onChangeProject, onClose, onConfirm }: { template: PromptTemplate; projects: CodeProject[]; values: Record<string, string>; projectId: number | null; saving: boolean; onChangeValue: (key: string, value: string) => void; onChangeProject: (id: number | null) => void; onClose: () => void; onConfirm: () => void }) {
  const applicableProjects = template.project_id ? projects.filter((project) => project.id === template.project_id) : projects;
  return <Modal><div className="w-[min(560px,calc(100vw-32px))] rounded-2xl bg-white shadow-2xl"><header className="flex items-start gap-3 border-b border-slate-200 px-5 py-4"><div className="flex-1"><h2 className="font-semibold">使用模板 · {template.name}</h2><p className="mt-1 text-xs text-slate-500">确认默认属性后，将为你新开一个聊天会话。</p></div><button type="button" onClick={onClose} className="grid h-8 w-8 place-items-center rounded-lg text-slate-500 hover:bg-slate-100" aria-label="关闭"><X size={18}/></button></header><div className="max-h-[65vh] overflow-y-auto p-5"><Field label="项目上下文"><select value={projectId ?? ""} onChange={(event) => onChangeProject(event.target.value ? Number(event.target.value) : null)}>{!template.project_id && <option value="">不带项目上下文</option>}{applicableProjects.map((project) => <option value={project.id} key={project.id}>{project.display_name}</option>)}</select></Field>{template.variables.map((item) => <Field key={item.key} label={<>{item.label}{item.required && <span className="ml-1 text-red-500">*</span>}</>}>{item.type === "textarea" ? <textarea value={values[item.key] ?? ""} onChange={(event) => onChangeValue(item.key, event.target.value)} placeholder={item.description ?? undefined}/> : item.type === "select" ? <select value={values[item.key] ?? ""} onChange={(event) => onChangeValue(item.key, event.target.value)}>{!item.required && <option value="">请选择</option>}{item.options.map((option) => <option value={option} key={option}>{option}</option>)}</select> : <input value={values[item.key] ?? ""} onChange={(event) => onChangeValue(item.key, event.target.value)} placeholder={item.description ?? undefined}/>}</Field>)}</div><footer className="flex justify-end gap-2 border-t border-slate-200 px-5 py-3"><button type="button" onClick={onClose} className="h-9 rounded-lg border border-slate-200 px-4 text-sm text-slate-600">取消</button><button type="button" disabled={saving} onClick={onConfirm} className="inline-flex h-9 items-center gap-2 rounded-lg bg-blue-600 px-4 text-sm font-medium text-white disabled:opacity-50">{saving ? <Loader2 size={15} className="animate-spin"/> : <Play size={14} fill="currentColor"/>}确认并新开会话</button></footer></div></Modal>;
}

function Field({ label, children }: { label: ReactNode; children: ReactNode }) { return <label className="mb-3 block text-xs font-medium text-slate-600"><span className="mb-1.5 block">{label}</span><span className="block [&_input]:w-full [&_input]:rounded-lg [&_input]:border [&_input]:border-slate-200 [&_input]:px-3 [&_input]:py-2 [&_input]:text-sm [&_select]:w-full [&_select]:rounded-lg [&_select]:border [&_select]:border-slate-200 [&_select]:bg-white [&_select]:px-3 [&_select]:py-2 [&_select]:text-sm [&_textarea]:w-full [&_textarea]:rounded-lg [&_textarea]:border [&_textarea]:border-slate-200 [&_textarea]:px-3 [&_textarea]:py-2 [&_textarea]:text-sm">{children}</span></label>; }
function PanelTitle({ title, subtitle }: { title: string; subtitle: string }) { return <div className="mb-2 flex items-center justify-between text-xs"><span className="font-semibold text-slate-600">{title}</span><span className="text-slate-400">{subtitle}</span></div>; }
function Modal({ children }: { children: ReactNode }) { return <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/40 p-4">{children}</div>; }

function renderPreview(body: string, variables: PromptTemplateVariable[]): ReactNode[] {
  const map = new Map(variables.map((item) => [item.key, item.label]));
  const content: ReactNode[] = [];
  let index = 0;
  for (const match of body.matchAll(variablePattern)) {
    content.push(body.slice(index, match.index));
    content.push(<span key={`${match.index}-${match[1]}`} className="rounded border border-blue-200 bg-blue-50 px-1.5 py-0.5 text-blue-700">{map.get(match[1]) ?? match[1]}</span>);
    index = (match.index ?? 0) + match[0].length;
  }
  content.push(body.slice(index));
  return content;
}

function parseTemplateMarkdown(source: string): PromptTemplate {
  const match = source.match(/^---\s*\n([\s\S]*?)\n---\s*\n?([\s\S]*)$/);
  if (!match) throw new Error("导入文件缺少以 --- 包围的 front matter。");
  const front = match[1];
  const read = (key: string) => front.match(new RegExp(`^${key}:\\s*(.+)$`, "m"))?.[1]?.trim().replace(/^"|"$/g, "") ?? "";
  const stage = read("stage") as PromptTemplateStage;
  if (!stages.some((item) => item.id === stage)) throw new Error("导入文件的 stage 无效。");
  const body = match[2].trim();
  const blocks = front.match(/  - key:[\s\S]*?(?=\n  - key:|$)/g) ?? [];
  const variables = blocks.map((block) => {
    const get = (key: string) => block.match(new RegExp(`^\\s*${key}:\\s*(.+)$`, "m"))?.[1]?.trim().replace(/^"|"$/g, "") ?? "";
    const type = get("type");
    return { key: get("key"), label: get("label") || get("key"), type: ["text", "textarea", "select"].includes(type) ? type as PromptTemplateVariable["type"] : "text", required: get("required") === "true", default_value: get("defaultValue"), description: "", options: get("options").replace(/^\[|\]$/g, "").split(",").map((item) => item.trim()).filter(Boolean) };
  });
  const draft = blankTemplate();
  return syncVariables({ ...draft, name: read("name") || "导入的模板", description: "由 Markdown 导入，请补充模板摘要。", stage, tags: read("tags").replace(/^\[|\]$/g, "").split(",").map((item) => item.trim()).filter(Boolean), visibility: (["personal", "project", "team"].includes(read("visibility")) ? read("visibility") : "personal") as PromptTemplateVisibility, project_id: Number(read("projectId")) || null, body, variables: variables.filter((item) => item.key) });
}
