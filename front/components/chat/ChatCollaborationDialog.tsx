"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Eye, Sparkles, Undo2, X } from "lucide-react";
import { ChatCollaborationReview } from "./ChatCollaborationReview";
import { ChatCollaborationTimeline, type CollaborationCell } from "./ChatCollaborationTimeline";
import type { CodeProject } from "@/lib/code-repository-types";
import type { AtlasCommit, AtlasSnapshot } from "@/lib/change-atlas-types";
import { createAtlasSnapshot } from "@/lib/change-atlas-api";
import { collaborationCommits, collaborationDay, collaborationDays, collaborationPrompt, collaborationRows, collaborationTime, type CollaborationAction } from "@/lib/chat-collaboration";

const field = "h-9 min-w-0 rounded-lg border border-slate-200 bg-white px-2.5 text-sm";
const selectionLimit = 20;
const rowAction = "inline-flex items-center gap-1 rounded-md px-2 py-1.5 text-xs text-blue-700 hover:bg-blue-50";

export function ChatCollaborationDialog({ projects, initialProjectId, onClose, onInsert }: {
  projects: CodeProject[]; initialProjectId: number | null; onClose: () => void; onInsert: (prompt: string) => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const reviewSection = useRef<HTMLElement>(null);
  const [openedAt] = useState(() => Date.now());
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
  const [cell, setCell] = useState<CollaborationCell | null>(null);
  const [viewedId, setViewedId] = useState("");
  const [action, setAction] = useState<CollaborationAction>("summary");
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
    // Let Strict Mode clean up its replayed effect before sending a POST.
    const pending = window.setTimeout(() => {
      createAtlasSnapshot(projectId, { days, ref: "HEAD", refresh_remote: false, history_only: true }, controller.signal)
        .then(value => { if (!controller.signal.aborted) setSnapshot(value); })
        .catch(reason => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "读取 Git 历史失败"); })
        .finally(() => { if (!controller.signal.aborted) setBusy(false); });
    }, 0);
    return () => {
      window.clearTimeout(pending);
      controller.abort();
    };
  }, [projectId, days, revision]);

  const minDay = collaborationDay(snapshot?.start_at ?? new Date(openedAt - (days - 1) * 86_400_000).toISOString());
  const maxDay = collaborationDay(snapshot?.created_at ?? new Date(openedAt).toISOString());
  const from = start >= minDay && start <= maxDay ? start : minDay;
  const to = end >= from && end <= maxDay ? end : maxDay;
  const axis = useMemo(() => collaborationDays(from, to), [from, to]);
  const authors = useMemo(() => new Map(snapshot?.commits.map(commit => [commit.author_id, commit.author]) ?? []), [snapshot]);
  const commits = useMemo(() => snapshot ? collaborationCommits(snapshot, author, from, to, search) : [], [snapshot, author, from, to, search]);
  const rows = useMemo(() => snapshot ? collaborationRows(snapshot, commits) : [], [snapshot, commits]);
  const selectedCommits = useMemo(() => snapshot?.commits.filter(commit => selected.has(commit.id)) ?? [], [snapshot, selected]);
  const latestCommit = commits[0];
  const active = cell && axis.includes(cell.day) ? cell : {
    rowKey: latestCommit ? `${latestCommit.author_id}:${latestCommit.repository_id}` : null,
    day: latestCommit ? collaborationDay(latestCommit.committed_at) : to,
  };
  const activeRow = active.rowKey ? rows.find(row => row.key === active.rowKey) : undefined;
  const activeCommits = useMemo(() => activeRow ? activeRow.commitsByDay.get(active.day) ?? []
    : commits.filter(commit => collaborationDay(commit.committed_at) === active.day), [activeRow, active.day, commits]);
  const viewedCommit = viewedId ? activeCommits.find(commit => commit.id === viewedId) : undefined;
  const reviewCommits = useMemo(() => viewedCommit ? [viewedCommit] : activeCommits, [viewedCommit, activeCommits]);
  const repositoryNames = useMemo(() => new Map(snapshot?.repositories.map(repository => [repository.id, repository.name]) ?? []), [snapshot]);
  const project = projects.find(item => item.id === projectId);
  const hiddenSelected = selected.size - commits.reduce((count, commit) => count + Number(selected.has(commit.id)), 0);

  function resetHistory() {
    setSnapshot(null);
    setBusy(true);
    setAuthor("");
    setStart("");
    setEnd("");
    setSearch("");
    setCell(null);
    setViewedId("");
    setAction("summary");
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

  function navigate(next: CollaborationCell | null) {
    setCell(next);
    setViewedId("");
  }

  function viewCommit(commit: AtlasCommit) {
    setViewedId(commit.id);
    reviewSection.current?.scrollIntoView({ block: "start" });
  }

  function selectedPrompt(targets = selectedCommits, intent = action) {
    return snapshot ? collaborationPrompt(window.location.origin, project?.display_name ?? "", snapshot, targets, intent) : "";
  }

  async function copyPrompt() {
    try { await navigator.clipboard.writeText(selectedPrompt()); setNotice("已复制"); }
    catch { setNotice("无法复制，请使用「插入输入框」。"); }
  }

  return createPortal(
    <dialog ref={dialog} aria-labelledby="collaboration-title" onCancel={event => { event.preventDefault(); onClose(); }}
      className="m-auto w-[min(1760px,calc(100vw-16px))] max-w-none rounded-2xl bg-white p-0 text-slate-900 shadow-xl backdrop:bg-slate-900/40">
      <div className="flex h-[min(1020px,96dvh)] flex-col">
        <header className="flex shrink-0 items-center justify-between border-b border-slate-200 px-4 py-3 sm:px-6">
          <div>
            <h2 id="collaboration-title" className="text-lg font-semibold">协作 · 提交时间轴</h2>
            <p className="mt-0.5 text-xs text-slate-500">点击日期或提交数查看记录。时间均为北京时间。</p>
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
              <select className={field} value={author} onChange={event => { setAuthor(event.target.value); navigate(null); }}>
                <option value="">全部人员</option>
                {[...authors].map(([id, name]) => <option key={id} value={id}>{name}</option>)}
              </select>
            </label>
            <label className="flex min-w-0 flex-col gap-1 text-xs">开始日期
              <input type="date" className={field} value={from} min={minDay} max={to} onChange={event => { setStart(event.target.value); navigate(null); }} />
            </label>
            <label className="flex min-w-0 flex-col gap-1 text-xs">结束日期
              <input type="date" className={field} value={to} min={from} max={maxDay} onChange={event => { setEnd(event.target.value); navigate(null); }} />
            </label>
            <label className="flex min-w-0 flex-col gap-1 text-xs">搜索提交
              <input className={field} value={search} onChange={event => { setSearch(event.target.value); navigate(null); }} placeholder="提交说明、SHA 或路径" />
            </label>
          </div>

          {busy && <p role="status" className="py-8 text-center text-sm text-slate-500">正在读取提交历史…</p>}
          {error && <p role="alert" className="text-sm text-red-600">{error} <button type="button" onClick={() => setRevision(value => value + 1)} className="underline">重试</button></p>}
          {snapshot?.warnings.map((warning, index) => <p key={index} className="text-xs text-amber-700">{warning}</p>)}
          <ChatCollaborationTimeline axis={axis} rows={rows} active={active} selected={selected} onNavigate={navigate} />
          {snapshot && !rows.length && <p className="text-sm text-slate-500">当前筛选没有提交，可调整日期、人员或搜索条件。</p>}

          {snapshot && <section aria-label="当日提交" className="overflow-hidden rounded-xl border border-slate-200">
            <h3 className="border-b border-slate-200 bg-slate-50 px-3 py-2 text-sm font-semibold">
              {activeRow && <>{activeRow.author} · {activeRow.repository} <span className="font-normal text-slate-500">· </span></>}
              {active.day} <span className="font-normal text-slate-600">· {activeCommits.length} 次提交</span>
            </h3>
            {!activeCommits.length && <p className="px-3 py-3 text-sm text-slate-500">当天没有符合条件的提交。</p>}
            <ul className="divide-y divide-slate-100">
              {activeCommits.map(commit => <li key={commit.id} className="flex flex-wrap items-center gap-x-4 gap-y-1 px-3 py-2.5">
                <label className="flex min-w-0 flex-1 basis-full cursor-pointer items-start gap-2.5 sm:basis-auto">
                  <input type="checkbox" className="mt-1 h-4 w-4 shrink-0 accent-blue-600" checked={selected.has(commit.id)}
                    disabled={!selected.has(commit.id) && selected.size >= selectionLimit} onChange={() => toggle(commit.id)} />
                  <span className="min-w-0">
                    <span className="block break-words text-sm font-medium">{commit.subject || "无提交说明"}</span>
                    <span className="block text-xs tabular-nums text-slate-500">
                      {commit.sha.slice(0, 8)} · {collaborationTime(commit.committed_at).slice(11)} · {commit.files.length} 个文件{commit.truncated ? "（已截断）" : ""}
                      {!activeRow && <> · {commit.author} · {repositoryNames.get(commit.repository_id)}</>}
                    </span>
                  </span>
                </label>
                <div className="ml-5 flex shrink-0 flex-wrap gap-1 sm:ml-0">
                  <button type="button" className={rowAction} aria-pressed={viewedId === commit.id} onClick={() => viewCommit(commit)}>
                    <Eye size={14} aria-hidden="true" />查看变更
                  </button>
                  <button type="button" className={rowAction} onClick={() => onInsert(selectedPrompt([commit], "summary"))}>
                    <Sparkles size={14} aria-hidden="true" />AI 解读
                  </button>
                  <button type="button" className={rowAction} onClick={() => onInsert(selectedPrompt([commit], "revert"))}
                    aria-label="拟定撤回方案" title="在聊天中草拟撤回方案">
                    <Undo2 size={14} aria-hidden="true" />撤回
                  </button>
                </div>
              </li>)}
            </ul>
          </section>}

          {snapshot && reviewCommits.length > 0 && <section ref={reviewSection} aria-label="变更查阅" className="space-y-2 rounded-xl border border-slate-200 p-3">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <p className="min-w-0 break-words text-xs text-slate-600">{viewedCommit
                ? `${viewedCommit.sha.slice(0, 8)} · ${viewedCommit.subject || "无提交说明"}`
                : `${active.day} · 当前范围 ${reviewCommits.length} 次提交`}</p>
              {viewedCommit && <button type="button" className="text-xs text-blue-700 hover:underline" onClick={() => setViewedId("")}>查看当天全部</button>}
            </div>
            <ChatCollaborationReview key={`${snapshot.id}:${active.day}:${active.rowKey}:${reviewCommits.map(commit => commit.id).join(",")}`}
              snapshot={snapshot} commits={reviewCommits} />
          </section>}
        </div>

        <footer className="flex shrink-0 flex-wrap items-center gap-2 border-t border-slate-200 px-4 py-3 text-sm sm:gap-3 sm:px-6">
          <span className="text-xs text-slate-600">已选 {selected.size}/{selectionLimit}{hiddenSelected > 0 ? `（${hiddenSelected} 项已隐藏）` : ""}</span>
          {selected.size > 0 && <button type="button" onClick={() => { setSelected(new Set()); setNotice(""); }} className="text-xs text-blue-700">清空</button>}
          {notice && <span role="status" className="text-xs text-slate-500">{notice}</span>}
          <select aria-label="Agent 操作" className={field + " ml-auto"} value={action} onChange={event => { setAction(event.target.value as CollaborationAction); setNotice(""); }}>
            <option value="summary">总结改动</option>
            <option value="risk">检查风险</option>
            <option value="revert">拟定撤回方案</option>
          </select>
          <div className="flex w-full justify-end gap-2 sm:w-auto">
            <button type="button" disabled={!selected.size || busy} onClick={() => void copyPrompt()} className="rounded-lg border border-slate-200 px-3 py-2 disabled:opacity-40">复制所选</button>
            <button type="button" disabled={!selected.size || busy} onClick={() => onInsert(selectedPrompt())} className="rounded-lg bg-blue-600 px-3 py-2 text-white disabled:opacity-40">插入输入框</button>
          </div>
        </footer>
      </div>
    </dialog>, document.body);
}
