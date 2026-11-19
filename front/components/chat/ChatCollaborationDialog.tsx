"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ArrowUpRight, GitCommitHorizontal, X } from "lucide-react";
import type { CodeProject } from "@/lib/code-repository-types";
import type { AtlasSnapshot } from "@/lib/change-atlas-types";
import { createAtlasSnapshot } from "@/lib/change-atlas-api";
import { collaborationCommits, collaborationDay, collaborationDays, collaborationLink, collaborationPrompt, collaborationRows, collaborationTime } from "@/lib/chat-collaboration";

const field = "h-9 min-w-0 rounded-lg border border-slate-200 bg-white px-2.5 text-sm";
const selectionLimit = 20;

export function ChatCollaborationDialog({ projects, initialProjectId, onClose, onInsert }: {
  projects: CodeProject[]; initialProjectId: number | null; onClose: () => void; onInsert: (prompt: string) => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const timelineViewport = useRef<HTMLDivElement>(null);
  const [projectId, setProjectId] = useState(initialProjectId ?? projects[0]?.id ?? 0);
  const [days, setDays] = useState(14);
  const [snapshot, setSnapshot] = useState<AtlasSnapshot | null>(null);
  const [busy, setBusy] = useState(Boolean(projectId));
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  const [author, setAuthor] = useState("");
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<Set<string>>(() => new Set());
  const [cell, setCell] = useState<{ rowKey: string; day: string } | null>(null);
  const [notice, setNotice] = useState("");

  useEffect(() => {
    if (!projectId && projects.length) setProjectId(initialProjectId ?? projects[0].id);
  }, [projectId, projects, initialProjectId]);

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    const element = dialog.current;
    element?.showModal();
    return () => { element?.close(); previous?.focus(); };
  }, []);

  useEffect(() => {
    if (!projectId) return;
    const controller = new AbortController();
    setBusy(true);
    setError("");
    createAtlasSnapshot(projectId, { days, ref: "HEAD", refresh_remote: false, history_only: true }, controller.signal)
      .then(value => { if (!controller.signal.aborted) setSnapshot(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "读取 Git 历史失败"); })
      .finally(() => { if (!controller.signal.aborted) setBusy(false); });
    return () => controller.abort();
  }, [projectId, days, revision]);

  useEffect(() => {
    const viewport = timelineViewport.current;
    if (snapshot && viewport) viewport.scrollLeft = viewport.scrollWidth;
  }, [snapshot]);

  const minDay = snapshot ? collaborationDay(snapshot.start_at) : "";
  const maxDay = snapshot ? collaborationDay(snapshot.created_at) : "";
  const from = start >= minDay && start <= maxDay ? start : minDay;
  const to = end && end >= minDay && end <= maxDay ? end : maxDay;
  const axis = useMemo(() => collaborationDays(from, to), [from, to]);
  const authors = useMemo(() => new Map(snapshot?.commits.map(commit => [commit.author_id, commit.author]) ?? []), [snapshot]);
  const commits = useMemo(() => snapshot ? collaborationCommits(snapshot, author, from, to, search) : [], [snapshot, author, from, to, search]);
  const rows = useMemo(() => snapshot ? collaborationRows(snapshot, commits) : [], [snapshot, commits]);
  const selectedCommits = useMemo(() => snapshot?.commits.filter(commit => selected.has(commit.id)) ?? [], [snapshot, selected]);
  const latestCommit = commits[0];
  const focusedRow = cell && rows.find(row => row.key === cell.rowKey && row.commitsByDay.has(cell.day));
  const activeRow = focusedRow || (latestCommit && rows.find(row => row.key === `${latestCommit.author_id}:${latestCommit.repository_id}`));
  const activeDay = focusedRow && cell ? cell.day : latestCommit && collaborationDay(latestCommit.committed_at);
  const activeCommits = activeDay ? activeRow?.commitsByDay.get(activeDay) ?? [] : [];
  const project = projects.find(item => item.id === projectId);
  const hiddenSelected = selected.size - commits.reduce((count, commit) => count + Number(selected.has(commit.id)), 0);

  function resetHistory() {
    setSnapshot(null);
    setBusy(true);
    setAuthor("");
    setStart("");
    setEnd("");
    setCell(null);
    setSelected(new Set());
    setNotice("");
  }

  function toggle(id: string) {
    setNotice("");
    setSelected(current => {
      const next = new Set(current);
      if (!next.delete(id) && next.size < selectionLimit) next.add(id);
      return next;
    });
  }

  function selectedPrompt() {
    return snapshot ? collaborationPrompt(window.location.origin, project?.display_name ?? "", snapshot, selectedCommits) : "";
  }

  async function copyPrompt() {
    try { await navigator.clipboard.writeText(selectedPrompt()); setNotice("已复制"); }
    catch { setNotice("无法复制，请使用「插入输入框」。"); }
  }

  return createPortal(
    <dialog ref={dialog} aria-labelledby="collaboration-title" onCancel={event => { event.preventDefault(); onClose(); }}
      className="m-auto w-[min(1440px,calc(100vw-24px))] max-w-none rounded-2xl bg-white p-0 text-slate-900 shadow-xl backdrop:bg-slate-900/40">
      <div className="flex h-[min(820px,92dvh)] flex-col">
        <header className="flex shrink-0 items-center justify-between border-b border-slate-200 px-4 py-3 sm:px-6">
          <div>
            <h2 id="collaboration-title" className="text-lg font-semibold">协作 · 提交时间轴</h2>
            <p className="mt-0.5 text-xs text-slate-500">选中日期查看提交，勾选后加入聊天。时间均为北京时间。</p>
          </div>
          <button type="button" aria-label="关闭协作选择" onClick={onClose} className="rounded-lg p-2 hover:bg-slate-100"><X size={20} /></button>
        </header>

        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-4 py-4 sm:px-6">
          <div className="grid grid-cols-2 gap-x-3 gap-y-2 sm:grid-cols-3 xl:grid-cols-6">
            <label className="flex min-w-0 flex-col gap-1 text-xs">项目
              <select className={field} value={projectId} onChange={event => { resetHistory(); setProjectId(Number(event.target.value)); }}>
                {!projects.length && <option value={0}>暂无可访问项目</option>}
                {projects.map(item => <option key={item.id} value={item.id}>{item.display_name}</option>)}
              </select>
            </label>
            <label className="flex min-w-0 flex-col gap-1 text-xs">历史范围
              <select className={field} value={days} onChange={event => { resetHistory(); setDays(Number(event.target.value)); }}>
                {[7, 14, 30].map(day => <option key={day} value={day}>最近 {day} 天</option>)}
              </select>
            </label>
            <label className="flex min-w-0 flex-col gap-1 text-xs">人员（Git 署名）
              <select className={field} value={author} onChange={event => { setAuthor(event.target.value); setCell(null); }}>
                <option value="">全部人员</option>
                {[...authors].map(([id, name]) => <option key={id} value={id}>{name}</option>)}
              </select>
            </label>
            <label className="flex min-w-0 flex-col gap-1 text-xs">开始日期
              <input type="date" className={field} value={from} min={minDay} max={to} onChange={event => { setStart(event.target.value); setCell(null); }} />
            </label>
            <label className="flex min-w-0 flex-col gap-1 text-xs">结束日期
              <input type="date" className={field} value={to} min={from} max={maxDay} onChange={event => { setEnd(event.target.value); setCell(null); }} />
            </label>
            <label className="flex min-w-0 flex-col gap-1 text-xs">搜索提交
              <input className={field} value={search} onChange={event => { setSearch(event.target.value); setCell(null); }} placeholder="提交说明、SHA 或路径" />
            </label>
          </div>

          {busy && <p role="status" className="py-8 text-center text-sm text-slate-500">正在读取提交历史…</p>}
          {error && <p role="alert" className="text-sm text-red-600">{error} <button type="button" onClick={() => setRevision(value => value + 1)} className="underline">重试</button></p>}
          {snapshot?.warnings.map((warning, index) => <p key={index} className="text-xs text-amber-700">{warning}</p>)}
          {snapshot && !rows.length && <p className="py-8 text-center text-sm text-slate-500">当前筛选没有提交，可调整日期、人员或搜索条件。</p>}

          {rows.length > 0 && <>
            <div ref={timelineViewport} className="max-h-[42dvh] overflow-auto rounded-xl border border-slate-200">
              <table aria-label="人员与仓库提交时间轴" className="w-full border-separate border-spacing-0 text-xs">
                <thead className="sticky top-0 z-20">
                  <tr>
                    <th scope="col" className="sticky left-0 z-10 min-w-32 border-b border-r border-slate-200 bg-slate-50 px-3 py-2 text-left sm:min-w-44">人员 / 仓库</th>
                    {axis.map(day => <th key={day} scope="col" title={day} className="min-w-14 border-b border-slate-200 bg-slate-50 px-1 py-2 font-medium tabular-nums text-slate-600">{day.slice(5)}</th>)}
                  </tr>
                </thead>
                <tbody>
                  {rows.map(row => <tr key={row.key}>
                    <th scope="row" className="sticky left-0 z-10 border-b border-r border-slate-100 bg-white px-3 py-2 text-left align-middle">
                      <span title={row.author} className="block max-w-28 truncate text-sm sm:max-w-40">{row.author}</span>
                      <span title={row.repository} className="block max-w-28 truncate font-normal text-slate-500 sm:max-w-40">{row.repository}</span>
                    </th>
                    {axis.map(day => {
                      const entries = row.commitsByDay.get(day);
                      const active = activeRow?.key === row.key && activeDay === day;
                      const hasSelection = entries?.some(commit => selected.has(commit.id));
                      return <td key={day} className="border-b border-slate-100 px-1 py-1.5 text-center">
                        {entries ? <button type="button" aria-pressed={active} aria-label={`${row.author} · ${row.repository} · ${day} · ${entries.length} 次提交`}
                          onClick={() => setCell({ rowKey: row.key, day })}
                          className={`inline-flex h-8 w-full items-center justify-center gap-0.5 rounded-md tabular-nums hover:bg-blue-100 ${active ? "bg-blue-100 text-blue-700 ring-1 ring-inset ring-blue-500" : hasSelection ? "bg-blue-100 font-semibold text-blue-800" : "bg-blue-50 text-blue-800"}`}>
                          <GitCommitHorizontal size={13} aria-hidden="true" />{entries.length}
                        </button> : <span className="text-slate-300" aria-hidden="true">·</span>}
                      </td>;
                    })}
                  </tr>)}
                </tbody>
              </table>
            </div>
            {snapshot && activeCommits.length > 0 && (
              <section aria-label="当日提交" className="overflow-hidden rounded-xl border border-slate-200">
                <h3 className="border-b border-slate-200 bg-slate-50 px-3 py-2 text-sm font-semibold">
                  {activeRow?.author} · {activeRow?.repository} <span className="font-normal text-slate-600">· {activeDay} · {activeCommits.length} 次提交</span>
                </h3>
                <ul className="divide-y divide-slate-100">
                  {activeCommits.map(commit => <li key={commit.id} className="flex items-center gap-2 px-3 py-2.5 sm:gap-4">
                    <label className="flex min-w-0 flex-1 cursor-pointer items-start gap-2.5">
                      <input type="checkbox" className="mt-1 h-4 w-4 shrink-0 accent-blue-600" checked={selected.has(commit.id)}
                        disabled={!selected.has(commit.id) && selected.size >= selectionLimit} onChange={() => toggle(commit.id)} />
                      <span className="min-w-0">
                        <span className="block break-words text-sm font-medium">{commit.subject || "无提交说明"}</span>
                        <span className="block text-xs tabular-nums text-slate-500">{commit.sha.slice(0, 8)} · {collaborationTime(commit.committed_at).slice(11)} · {commit.files.length} 个文件{commit.truncated ? "（已截断）" : ""}</span>
                      </span>
                    </label>
                    <a href={collaborationLink(window.location.origin, snapshot, commit)} target="_blank" rel="noreferrer"
                      className="inline-flex shrink-0 items-center gap-1 rounded-md px-1.5 py-1 text-xs text-blue-700 hover:bg-blue-50">
                      查看变更<ArrowUpRight size={13} aria-hidden="true" />
                    </a>
                  </li>)}
                </ul>
              </section>
            )}
          </>}
        </div>

        <footer className="flex shrink-0 flex-wrap items-center gap-2 border-t border-slate-200 px-4 py-3 text-sm sm:gap-3 sm:px-6">
          <span className="text-xs text-slate-600">已选 {selected.size}/{selectionLimit}{hiddenSelected > 0 ? `（${hiddenSelected} 项已隐藏）` : ""}</span>
          {selected.size > 0 && <button type="button" onClick={() => { setSelected(new Set()); setNotice(""); }} className="text-xs text-blue-700">清空</button>}
          {notice && <span role="status" className="text-xs text-slate-500">{notice}</span>}
          <span className="ml-auto" />
          <button type="button" disabled={!selected.size || busy} onClick={() => void copyPrompt()} className="rounded-lg border border-slate-200 px-3 py-2 disabled:opacity-40">复制所选</button>
          <button type="button" disabled={!selected.size || busy} onClick={() => onInsert(selectedPrompt())} className="rounded-lg bg-blue-600 px-3 py-2 text-white disabled:opacity-40">插入输入框</button>
        </footer>
      </div>
    </dialog>, document.body);
}
