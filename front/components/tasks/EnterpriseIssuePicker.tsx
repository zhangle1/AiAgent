"use client";

import { useEffect, useMemo, useState } from "react";
import { ChevronLeft, ChevronRight, ExternalLink, ImageIcon, Link2, Loader2, RefreshCw, Search, X } from "lucide-react";
import type { CodeProject } from "@/lib/code-repository-types";
import { enterpriseAttachmentUrl, getEnterpriseIssue, linkEnterpriseIssue, listEnterpriseIssues, type EnterpriseIssue, type EnterpriseIssueDetail, type ProjectTask } from "@/lib/project-task-api";

type Props = { projects: CodeProject[]; initialProjectId?: number | null; onClose: () => void; onLinked: (task: ProjectTask) => void };
type IssueState = "open" | "progressing" | "closed" | "rejected" | "";

export function EnterpriseIssuePicker({ projects, initialProjectId, onClose, onLinked }: Props) {
  const [projectId, setProjectId] = useState<number | "">(initialProjectId ?? projects[0]?.id ?? "");
  const [items, setItems] = useState<EnterpriseIssue[]>([]);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [state, setState] = useState<IssueState>("open");
  const [query, setQuery] = useState("");
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detail, setDetail] = useState<EnterpriseIssueDetail | null>(null);
  const [linking, setLinking] = useState(false);
  const [error, setError] = useState("");

  const selected = useMemo(() => items.find((item) => item.number === detail?.number) ?? null, [detail?.number, items]);
  const load = async (nextPage = 1) => {
    setLoading(true); setError("");
    try { const result = await listEnterpriseIssues({ page: nextPage, state: state || undefined, query }); setItems(result.items); setPage(result.page); setHasMore(result.has_more); setDetail(null); }
    catch (value) { setError(message(value, "无法读取 yun_kun 企业工作项。")); }
    finally { setLoading(false); }
  };
  useEffect(() => { void load(); }, []);

  const select = async (issue: EnterpriseIssue) => {
    setDetailLoading(true); setError("");
    try { setDetail(await getEnterpriseIssue(issue.number)); }
    catch (value) { setError(message(value, "无法读取工作项详情。")); }
    finally { setDetailLoading(false); }
  };
  const link = async () => {
    if (!projectId || !detail) return;
    setLinking(true); setError("");
    try { const result = await linkEnterpriseIssue({ projectId, number: detail.number }); onLinked(result.task); }
    catch (value) { setError(message(value, "关联工作项失败。")); }
    finally { setLinking(false); }
  };

  return <div className="fixed inset-0 z-[70] bg-slate-950/45 p-3 sm:p-6"><div className="mx-auto flex h-[min(900px,calc(100dvh-24px))] w-full max-w-[1400px] flex-col overflow-hidden rounded-2xl bg-white shadow-2xl"><header className="flex items-start justify-between gap-4 border-b border-slate-200 px-5 py-4 sm:px-7"><div><p className="text-xs font-semibold tracking-[.14em] text-blue-600">GITEE ENTERPRISE · YUN_KUN</p><h2 className="mt-1 text-xl font-semibold text-slate-950">选择企业工作项并关联本地项目</h2><p className="mt-1 text-sm text-slate-500">点击左侧工作项查看完整内容与图片附件；确认后才写入所选 AiAgent 项目。</p></div><button onClick={onClose} className="grid h-9 w-9 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="关闭"><X/></button></header><div className="grid min-h-0 flex-1 lg:grid-cols-[420px_minmax(0,1fr)]"><aside className="flex min-h-0 flex-col border-b border-slate-200 lg:border-b-0 lg:border-r"><div className="space-y-3 border-b border-slate-100 p-4"><div className="grid grid-cols-[1fr_130px] gap-2"><label className="text-xs font-medium text-slate-600">状态<select value={state} onChange={(event) => setState(event.target.value as IssueState)} className="mt-1 h-9 w-full rounded-lg border border-slate-200 bg-white px-2 text-sm"><option value="open">开启</option><option value="progressing">进行中</option><option value="closed">已关闭</option><option value="rejected">已拒绝</option><option value="">接口默认</option></select></label><label className="text-xs font-medium text-slate-600">AiAgent 项目<select value={projectId} onChange={(event) => setProjectId(Number(event.target.value))} className="mt-1 h-9 w-full rounded-lg border border-slate-200 bg-white px-2 text-sm"><option value="">选择项目</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.display_name}</option>)}</select></label></div><div className="flex gap-2"><label className="relative min-w-0 flex-1"><Search size={15} className="pointer-events-none absolute left-2.5 top-2.5 text-slate-400"/><input value={query} onChange={(event) => setQuery(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") void load(1); }} placeholder="编号、标题、负责人" className="h-9 w-full rounded-lg border border-slate-200 pl-8 pr-2 text-sm"/></label><button onClick={() => void load(1)} className="inline-flex h-9 items-center gap-1 rounded-lg border border-slate-200 px-2.5 text-xs text-slate-700 hover:bg-slate-50"><RefreshCw size={13}/>查询</button></div></div><div className="min-h-0 flex-1 overflow-y-auto p-2">{loading ? <div className="grid h-40 place-items-center text-slate-400"><Loader2 className="animate-spin"/></div> : items.length ? items.map((issue) => <button key={issue.number} onClick={() => void select(issue)} className={`w-full rounded-xl p-3 text-left ${selected?.number === issue.number ? "bg-blue-50 ring-1 ring-blue-200" : "hover:bg-slate-50"}`}><div className="flex items-start justify-between gap-2"><p className="min-w-0 flex-1 truncate text-sm font-medium text-slate-900">#{issue.number} · {issue.title}</p><span className="shrink-0 rounded bg-slate-100 px-1.5 py-0.5 text-[11px] text-slate-600">{issue.status ?? "—"}</span></div><p className="mt-1 truncate text-xs text-slate-500">{[issue.work_item_type, issue.assignee && `负责人：${issue.assignee}`].filter(Boolean).join(" · ") || "未填写负责人"}</p></button>) : <div className="grid h-40 place-items-center px-5 text-center text-sm text-slate-400">没有可选择的企业工作项</div>}</div><footer className="flex items-center justify-between border-t border-slate-100 p-3 text-xs text-slate-500"><button disabled={loading || page <= 1} onClick={() => void load(page - 1)} className="inline-flex items-center gap-1 rounded px-2 py-1.5 hover:bg-slate-100 disabled:opacity-40"><ChevronLeft size={14}/>上一页</button><span>第 {page} 页</span><button disabled={loading || !hasMore} onClick={() => void load(page + 1)} className="inline-flex items-center gap-1 rounded px-2 py-1.5 hover:bg-slate-100 disabled:opacity-40">下一页<ChevronRight size={14}/></button></footer></aside><section className="min-h-0 overflow-y-auto bg-slate-50/50"><div className="mx-auto max-w-4xl p-5 sm:p-7">{error && <div className="mb-4 rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">{error}</div>}{detailLoading ? <div className="grid h-72 place-items-center text-slate-400"><Loader2 className="animate-spin"/></div> : detail ? <IssueDetail detail={detail} projectName={projects.find((project) => project.id === projectId)?.display_name} linking={linking} canLink={Boolean(projectId)} onLink={() => void link()}/> : <div className="grid min-h-72 place-items-center rounded-2xl border border-dashed border-slate-200 bg-white px-6 text-center"><div><Search className="mx-auto text-slate-300" size={30}/><h3 className="mt-3 font-medium text-slate-700">从左侧选择一条企业工作项</h3><p className="mt-1 text-sm text-slate-500">此处会展示任务正文、处理人、标签，以及 Gitee 返回的附件和正文图片。</p></div></div>}</div></section></div></div></div>;
}

function IssueDetail({ detail, projectName, linking, canLink, onLink }: { detail: EnterpriseIssueDetail; projectName?: string; linking: boolean; canLink: boolean; onLink: () => void }) {
  const images = detail.attachments.filter((attachment) => attachment.is_image);
  const files = detail.attachments.filter((attachment) => !attachment.is_image);
  return <article className="rounded-2xl border border-slate-200 bg-white shadow-sm"><header className="border-b border-slate-100 p-5"><div className="flex flex-wrap items-start justify-between gap-3"><div><p className="font-mono text-xs text-slate-500">#{detail.number}</p><h3 className="mt-1 text-xl font-semibold text-slate-950">{detail.title}</h3></div>{detail.external_url && <a href={detail.external_url} target="_blank" rel="noreferrer" className="inline-flex h-9 items-center gap-1 rounded-lg border border-slate-200 px-3 text-sm text-slate-700 hover:bg-slate-50"><ExternalLink size={14}/>Gitee 原链接</a>}</div><dl className="mt-4 grid gap-x-6 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-3"><Meta label="状态" value={detail.status}/><Meta label="类型" value={detail.work_item_type}/><Meta label="负责人" value={detail.assignee}/><Meta label="创建人" value={detail.creator}/><Meta label="优先级" value={detail.priority}/><Meta label="标签" value={detail.labels}/></dl></header><div className="space-y-6 p-5"><section><h4 className="text-sm font-semibold text-slate-800">任务详情</h4><div className="mt-2 whitespace-pre-wrap break-words rounded-xl bg-slate-50 p-4 text-sm leading-7 text-slate-700">{detail.description || "Gitee 未返回任务正文。"}</div></section>{images.length > 0 && <section><h4 className="flex items-center gap-2 text-sm font-semibold text-slate-800"><ImageIcon size={16}/>图片附件（{images.length}）</h4><div className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-3">{images.map((attachment) => <a key={attachment.url} href={enterpriseAttachmentUrl(attachment.url)} target="_blank" rel="noreferrer" className="overflow-hidden rounded-xl border border-slate-200 bg-slate-50"><img src={enterpriseAttachmentUrl(attachment.url)} alt={attachment.name} className="aspect-video w-full object-cover"/><span className="block truncate px-3 py-2 text-xs text-slate-600">{attachment.name}</span></a>)}</div></section>}{files.length > 0 && <section><h4 className="text-sm font-semibold text-slate-800">文件附件</h4><div className="mt-2 flex flex-wrap gap-2">{files.map((attachment) => <a key={attachment.url} href={enterpriseAttachmentUrl(attachment.url)} target="_blank" rel="noreferrer" className="inline-flex max-w-full items-center gap-1 rounded-lg border border-slate-200 px-3 py-2 text-sm text-blue-700 hover:bg-blue-50"><ExternalLink size={14}/><span className="truncate">{attachment.name}</span></a>)}</div></section>}<section className="rounded-xl bg-blue-50 p-4"><p className="text-sm text-blue-900">关联目标：<strong>{projectName ?? "请先在左侧选择 AiAgent 项目"}</strong></p><button onClick={onLink} disabled={!canLink || linking} className="mt-3 inline-flex h-10 items-center gap-2 rounded-lg bg-blue-600 px-4 text-sm font-medium text-white hover:bg-blue-700 disabled:bg-slate-300">{linking ? <Loader2 size={16} className="animate-spin"/> : <Link2 size={16}/>}{linking ? "关联中…" : "关联此工作项"}</button></section></div></article>;
}

function Meta({ label, value }: { label: string; value?: string | null }) { return <div><dt className="text-xs text-slate-400">{label}</dt><dd className="mt-0.5 text-slate-700">{value || "—"}</dd></div>; }
function message(value: unknown, fallback: string) { return value instanceof Error && value.message.trim() ? value.message : fallback; }
