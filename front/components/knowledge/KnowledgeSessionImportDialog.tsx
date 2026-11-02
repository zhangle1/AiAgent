"use client";

import { useEffect, useMemo, useState } from "react";
import { Check, Download, Loader2, Search, X } from "lucide-react";
import { getPlatformSessionImportOptions, importPlatformSessions } from "@/lib/knowledge-api";
import type { PlatformChatSession, PlatformSessionImportResult } from "@/lib/knowledge-types";

export function KnowledgeSessionImportDialog({ onClose, onImported }: { onClose: () => void; onImported: (result: PlatformSessionImportResult) => Promise<void> }) {
  const [sessions, setSessions] = useState<PlatformChatSession[]>([]);
  const [canImportAll, setCanImportAll] = useState(false);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [keyword, setKeyword] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    let disposed = false;
    void getPlatformSessionImportOptions().then(value => {
      if (disposed) return;
      setSessions(value.sessions);
      setCanImportAll(value.can_import_all);
    }).catch(ex => { if (!disposed) setError(ex instanceof Error ? ex.message : "平台会话读取失败"); }).finally(() => { if (!disposed) setLoading(false); });
    return () => { disposed = true; };
  }, []);

  const visible = useMemo(() => {
    const query = keyword.trim().toLocaleLowerCase();
    return query ? sessions.filter(item => [item.title, item.last_message, item.username, item.user_alias].filter(Boolean).some(value => value!.toLocaleLowerCase().includes(query))) : sessions;
  }, [keyword, sessions]);
  const allVisibleSelected = visible.length > 0 && visible.every(item => selected.has(item.id));

  function toggle(id: string) {
    setSelected(current => { const next = new Set(current); next.has(id) ? next.delete(id) : next.add(id); return next; });
  }
  function toggleVisible() {
    setSelected(current => {
      const next = new Set(current);
      if (allVisibleSelected) visible.forEach(item => next.delete(item.id)); else visible.forEach(item => next.add(item.id));
      return next;
    });
  }
  async function submit() {
    if (selected.size === 0) { setError("请至少勾选一个平台会话。"); return; }
    setBusy(true); setError("");
    try { await onImported(await importPlatformSessions([...selected])); }
    catch (ex) { setError(ex instanceof Error ? ex.message : "平台会话导入失败"); }
    finally { setBusy(false); }
  }

  return <div role="dialog" aria-modal="true" aria-label="导入平台会话" className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/35 p-4 backdrop-blur-[2px]">
    <section className="flex max-h-[calc(100vh-2rem)] w-full max-w-3xl flex-col overflow-hidden rounded-2xl bg-white shadow-2xl">
      <header className="flex shrink-0 items-start justify-between border-b border-slate-100 px-6 py-5"><div><h2 className="text-lg font-semibold text-slate-900">导入平台会话</h2><p className="mt-1 text-sm text-slate-500">将聊天中的用户与 AI 对话追加到人员工作区的会话原文中，不自动解析或调用模型。</p></div><button type="button" onClick={onClose} disabled={busy} className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="关闭"><X size={18} /></button></header>
      <div className="min-h-0 overflow-y-auto p-6">
        {canImportAll && <p className="mb-3 rounded-lg border border-violet-100 bg-violet-50 px-3 py-2 text-xs text-violet-700">管理员可选择所有人员的聊天会话，导入后仍按原所属人员保存。</p>}
        <div className="flex flex-wrap items-center gap-2"><label className="relative min-w-0 flex-1"><Search size={14} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" /><input value={keyword} onChange={event => setKeyword(event.target.value)} placeholder={canImportAll ? "搜索标题、人员或内容" : "搜索标题或内容"} className="h-9 w-full rounded-lg border border-slate-200 pl-9 pr-3 text-sm outline-none focus:border-blue-400" /></label><button type="button" onClick={toggleVisible} disabled={loading || visible.length === 0} className="h-9 rounded-lg border border-slate-200 px-3 text-xs text-slate-600 hover:border-blue-300 disabled:opacity-50">{allVisibleSelected ? "取消全选" : "全选当前"}</button></div>
        {error && <p role="alert" className="mt-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
        {loading ? <div className="flex min-h-56 items-center justify-center text-sm text-slate-400"><Loader2 size={17} className="mr-2 animate-spin" />正在读取平台会话…</div> : visible.length === 0 ? <div className="flex min-h-56 items-center justify-center rounded-xl border border-dashed border-slate-200 text-sm text-slate-400">{sessions.length ? "没有符合条件的会话" : "暂无可导入的平台会话"}</div> : <div className="mt-3 divide-y divide-slate-100 rounded-xl border border-slate-200">{visible.map(session => <label key={session.id} className="flex cursor-pointer items-start gap-3 px-3 py-3 hover:bg-slate-50"><input type="checkbox" checked={selected.has(session.id)} onChange={() => toggle(session.id)} className="mt-1 h-4 w-4 rounded border-slate-300 text-blue-600" /><span className="min-w-0 flex-1"><span className="flex flex-wrap items-center gap-2"><strong className="truncate text-sm font-medium text-slate-800">{session.title}</strong>{canImportAll && <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] text-slate-500">{session.user_alias || session.username}</span>}</span><span className="mt-1 block text-xs text-slate-500">{session.message_count} 条消息 · {new Date(session.updated_at).toLocaleString()}</span><span className="mt-1 block line-clamp-2 whitespace-pre-wrap text-xs leading-5 text-slate-400">{session.last_message || "暂无消息"}</span></span>{selected.has(session.id) && <Check size={16} className="mt-1 shrink-0 text-blue-600" />}</label>)}</div>}
      </div>
      <footer className="flex shrink-0 items-center justify-between gap-3 border-t border-slate-100 px-6 py-4"><span className="text-xs text-slate-400">已选择 {selected.size} 个会话</span><div className="flex gap-2"><button type="button" onClick={onClose} disabled={busy} className="h-9 rounded-lg border border-slate-200 px-4 text-sm text-slate-600 hover:bg-slate-50">取消</button><button type="button" onClick={() => void submit()} disabled={busy || loading || selected.size === 0} className="inline-flex h-9 items-center gap-2 rounded-lg bg-blue-600 px-4 text-sm font-medium text-white hover:bg-blue-700 disabled:bg-slate-300">{busy && <Loader2 size={15} className="animate-spin" />}<Download size={15} />导入选中会话</button></div></footer>
    </section>
  </div>;
}
