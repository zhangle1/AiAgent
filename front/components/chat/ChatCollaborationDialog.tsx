"use client";

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { X } from "lucide-react";
import type { CodeProject } from "@/lib/code-repository-types";
import type { AtlasSnapshot } from "@/lib/change-atlas-types";
import { createAtlasSnapshot } from "@/lib/change-atlas-api";
import { collaborationCommits, collaborationLink, collaborationPrompt } from "@/lib/chat-collaboration";

const field = "min-w-0 rounded-lg border bg-white p-2 text-sm";
export function ChatCollaborationDialog({ projects, initialProjectId, onClose, onInsert }: {
  projects: CodeProject[]; initialProjectId: number | null; onClose: () => void; onInsert: (prompt: string) => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [projectId, setProjectId] = useState(initialProjectId ?? projects[0]?.id ?? 0);
  const [days, setDays] = useState(7);
  const [snapshot, setSnapshot] = useState<AtlasSnapshot | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  const [author, setAuthor] = useState("");
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<Record<string, string>>({});
  const [notice, setNotice] = useState("");
  useEffect(() => { if (!projectId && projects.length) setProjectId(initialProjectId ?? projects[0].id); }, [projectId, projects, initialProjectId]);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    const element = dialog.current; element?.showModal();
    return () => { element?.close(); previous?.focus(); };
  }, []);
  useEffect(() => {
    if (!projectId) return;
    const controller = new AbortController(); let disposed = false;
    setSnapshot(null); setBusy(true); setError(""); setSelected({}); setAuthor(""); setStart(""); setEnd(""); setNotice("");
    createAtlasSnapshot(projectId, { days, ref: "HEAD", refresh_remote: false }, controller.signal)
      .then(value => { if (!disposed) setSnapshot(value); })
      .catch(reason => { if (!disposed) setError(reason instanceof Error ? reason.message : "读取 Git 历史失败"); })
      .finally(() => { if (!disposed) setBusy(false); });
    return () => { disposed = true; controller.abort(); };
  }, [projectId, days, revision]);
  const commits = snapshot ? collaborationCommits(snapshot, author, start, end, search) : [];
  const references = Object.values(selected);
  const project = projects.find(item => item.id === projectId);
  const prompt = collaborationPrompt(project?.display_name ?? "", references);
  function toggle(key: string, text: string) {
    setNotice("");
    setSelected(current => { const next = { ...current }; if (key in next) delete next[key]; else if (Object.keys(next).length < 20) next[key] = text; return next; });
  }
  async function copy(text: string) {
    try { await navigator.clipboard.writeText(text); setNotice("已复制，可粘贴到输入框。"); }
    catch { setNotice("复制失败，请使用“插入输入框”或手动复制链接。"); }
  }
  return createPortal(<dialog ref={dialog} aria-labelledby="collaboration-title" onCancel={event => { event.preventDefault(); onClose(); }} className="m-auto w-[min(900px,calc(100vw-24px))] rounded-2xl bg-white p-0 text-slate-900 shadow-xl backdrop:bg-slate-900/40">
    <div className="flex max-h-[88dvh] flex-col">
      <header className="flex items-center justify-between border-b p-4"><div><h2 id="collaboration-title" className="text-lg font-semibold">协作 · Git 历史树</h2><p className="mt-1 text-xs text-slate-500">按项目、Git 署名和日期查阅改动，选择链接加入对话。</p></div><button type="button" aria-label="关闭协作选择" onClick={onClose} className="rounded p-2"><X size={20}/></button></header>
      <div className="min-h-0 space-y-3 overflow-y-auto p-4">
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          <label className="flex flex-col gap-1 text-xs">项目<select className={field} value={projectId} onChange={event => { setProjectId(Number(event.target.value)); setSnapshot(null); setSelected({}); }}>{!projects.length && <option value={0}>暂无可访问项目</option>}{projects.map(item => <option key={item.id} value={item.id}>{item.display_name}</option>)}</select></label>
          <label className="flex flex-col gap-1 text-xs">历史范围<select className={field} value={days} onChange={event => { setDays(Number(event.target.value)); setSnapshot(null); setSelected({}); }}>{[7, 14, 30].map(day => <option key={day} value={day}>最近 {day} 天</option>)}</select></label>
          <label className="flex flex-col gap-1 text-xs">人员（Git 署名）<select className={field} value={author} onChange={event => setAuthor(event.target.value)}><option value="">全部人员</option>{Array.from(new Map(snapshot?.commits.map(commit => [commit.author_id, commit.author]) ?? [])).map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label>
          <label className="flex flex-col gap-1 text-xs">开始日期（UTC）<input type="date" className={field} value={start} min={snapshot?.start_at.slice(0, 10)} max={end || snapshot?.created_at.slice(0, 10)} onChange={event => setStart(event.target.value)}/></label>
          <label className="flex flex-col gap-1 text-xs">结束日期（UTC）<input type="date" className={field} value={end} min={start || snapshot?.start_at.slice(0, 10)} max={snapshot?.created_at.slice(0, 10)} onChange={event => setEnd(event.target.value)}/></label>
          <label className="flex flex-col gap-1 text-xs">Git 改动<input className={field} value={search} onChange={event => setSearch(event.target.value)} placeholder="提交说明、SHA 或文件路径"/></label>
        </div>
        <p className="text-xs leading-5 text-slate-500">默认读取已有本地 HEAD，不拉取远端。Git 署名不代表平台身份。筛选仅覆盖当前快照已载入记录；链接需登录且快照有效期为 24 小时。</p>
        {busy && <p role="status">正在读取 Git 历史…</p>}
        {error && <p role="alert" className="text-red-600">{error} <button type="button" onClick={() => setRevision(value => value + 1)} className="underline">重试</button></p>}
        {snapshot?.warnings.map((warning, index) => <p key={index} className="text-xs text-amber-700">{warning}</p>)}
        {snapshot && !commits.length && <p className="py-6 text-center text-sm text-slate-500">当前筛选没有提交，可调整日期、人员或搜索条件。</p>}
        {snapshot?.repositories.map(repository => {
          const rows = commits.filter(commit => commit.repository_id === repository.id);
          if (!rows.length) return null;
          return <details key={repository.id} open className="rounded-xl border"><summary className="cursor-pointer bg-slate-50 p-3 text-sm font-medium">{repository.name} · {rows.length} 条提交</summary><div className="space-y-2 p-3">{rows.map(commit => {
            const url = collaborationLink(window.location.origin, snapshot, commit);
            const key = commit.id;
            return <details key={key} className="rounded-lg border p-2"><summary className="cursor-pointer break-words text-sm">{commit.subject} <span className="text-xs text-slate-500">{commit.sha.slice(0, 8)} · {commit.author} · {commit.committed_at.slice(0, 10)} · {commit.files.length} 文件{commit.truncated ? "（已截断）" : ""}</span></summary>
              <div className="mt-2 flex flex-wrap items-center gap-3 text-xs"><label><input type="checkbox" checked={key in selected} disabled={!(key in selected) && references.length >= 20} onChange={() => toggle(key, `- ${commit.sha} · ${commit.author} · ${JSON.stringify(commit.subject)}\n  ${url}`)}/> 选择提交</label><a href={url} target="_blank" rel="noreferrer" className="text-blue-600">查看变更</a><button type="button" onClick={() => void copy(url)} className="text-blue-600">复制链接</button></div>
              <ul className="mt-2 space-y-2 border-l pl-3">{commit.files.map(change => {
                const file = snapshot.files.find(item => item.id === change.file_id); if (!file) return null;
                const fileKey = `${key}:${file.id}`, fileUrl = collaborationLink(window.location.origin, snapshot, commit, file.id);
                return <li key={file.id} className="flex flex-wrap items-center gap-2 text-xs"><label className="min-w-0 flex-1 break-all"><input type="checkbox" checked={fileKey in selected} disabled={!(fileKey in selected) && references.length >= 20} onChange={() => toggle(fileKey, `- ${JSON.stringify(file.path)} · ${commit.sha}\n  ${fileUrl}`)}/> {file.path} <span className="text-green-700">+{change.added ?? "?"}</span> <span className="text-red-600">−{change.deleted ?? "?"}</span></label><a href={fileUrl} target="_blank" rel="noreferrer" className="text-blue-600">差异</a><button type="button" onClick={() => void copy(fileUrl)} className="text-blue-600">复制链接</button></li>;
              })}</ul>
            </details>;
          })}</div></details>;
        })}
      </div>
      <footer className="flex shrink-0 flex-wrap items-center gap-3 border-t p-4 text-sm"><span>已选 {references.length}/20（含隐藏项）</span><button type="button" onClick={() => setSelected({})} className="text-blue-600">清空</button><span role="status" className="text-xs text-slate-500">{notice}</span><button type="button" disabled={!references.length || busy} onClick={() => void copy(prompt)} className="ml-auto rounded-lg border px-3 py-2 disabled:opacity-40">复制所选</button><button type="button" disabled={!references.length || busy} onClick={() => onInsert(prompt)} className="rounded-lg bg-blue-600 px-3 py-2 text-white disabled:opacity-40">插入输入框</button></footer>
    </div>
  </dialog>, document.body);
}
