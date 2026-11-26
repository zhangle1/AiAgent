"use client";

import { useLayoutEffect, useRef } from "react";
import { ChevronLeft, ChevronRight, GitCommitHorizontal } from "lucide-react";
import type { CollaborationRow } from "@/lib/chat-collaboration";

export type CollaborationCell = { rowKey: string | null; day: string };

export function ChatCollaborationTimeline({ axis, rows, active, selected, onNavigate }: {
  axis: string[];
  rows: CollaborationRow[];
  active: CollaborationCell;
  selected: Set<string>;
  onNavigate: (cell: CollaborationCell) => void;
}) {
  const viewport = useRef<HTMLDivElement>(null);
  const laneHeader = useRef<HTMLTableCellElement>(null);
  const dayHeader = useRef<HTMLTableCellElement>(null);
  const dayIndex = axis.indexOf(active.day);

  useLayoutEffect(() => {
    const scroll = viewport.current;
    const lane = laneHeader.current;
    const column = dayHeader.current;
    if (!scroll || !lane || !column) return;
    const { left, right } = column.getBoundingClientRect();
    const visibleLeft = lane.getBoundingClientRect().right;
    const visibleRight = scroll.getBoundingClientRect().left + scroll.clientWidth;
    if (left < visibleLeft) scroll.scrollLeft += left - visibleLeft;
    else if (right > visibleRight) scroll.scrollLeft += right - visibleRight;
  }, [active.day, axis]);

  const dayButton = "rounded-md border border-slate-200 p-1.5 text-slate-600 hover:bg-slate-100 disabled:opacity-40";
  function chooseDay(day: string) {
    onNavigate({ day, rowKey: null });
  }

  return <section aria-label="浏览提交日期" className="space-y-2">
    <div className="flex flex-wrap items-center gap-2">
      <button type="button" aria-label="前一天" disabled={dayIndex <= 0} onClick={() => chooseDay(axis[dayIndex - 1])} className={dayButton}><ChevronLeft size={16} /></button>
      <label className="sr-only" htmlFor="collaboration-jump-day">跳转日期</label>
      <input id="collaboration-jump-day" type="date" value={active.day} min={axis[0]} max={axis.at(-1)}
        className="h-8 min-w-0 rounded-md border border-slate-200 bg-white px-2 text-xs"
        onChange={event => { if (axis.includes(event.target.value)) chooseDay(event.target.value); }} />
      <button type="button" aria-label="后一天" disabled={dayIndex < 0 || dayIndex >= axis.length - 1} onClick={() => chooseDay(axis[dayIndex + 1])} className={dayButton}><ChevronRight size={16} /></button>
      <button type="button" disabled={dayIndex === axis.length - 1} onClick={() => chooseDay(axis[axis.length - 1])}
        className="rounded-md px-2 py-1 text-xs text-blue-700 hover:bg-blue-50 disabled:text-slate-500">最近一天</button>
    </div>
    <div ref={viewport} tabIndex={0} aria-label="可横向滚动的提交时间轴" className="max-h-[43dvh] overflow-auto rounded-xl border border-slate-200">
      <table aria-label="人员与仓库提交时间轴" className="w-full border-separate border-spacing-0 text-xs">
        <thead className="sticky top-0 z-20">
          <tr>
            <th ref={laneHeader} scope="col" className="sticky left-0 z-10 min-w-32 border-b border-r border-slate-200 bg-slate-50 px-3 py-2 text-left sm:min-w-48">人员 / 仓库</th>
            {axis.map(day => <th key={day} ref={active.day === day ? dayHeader : undefined} scope="col"
              className="min-w-16 border-b border-slate-200 bg-slate-50 p-0.5 sm:min-w-[72px]">
              <button type="button" aria-pressed={active.day === day && !active.rowKey} aria-label={`查看 ${day} 的全部提交`} onClick={() => chooseDay(day)}
                className={`w-full rounded px-1 py-1.5 font-medium tabular-nums hover:bg-blue-100 ${active.day === day ? "bg-blue-100 text-blue-800" : "text-slate-600"}`}>{day.slice(5)}</button>
            </th>)}
          </tr>
        </thead>
        <tbody>
          {rows.map(row => <tr key={row.key}>
            <th scope="row" className="sticky left-0 z-10 border-b border-r border-slate-100 bg-white px-3 py-2 text-left align-middle">
              <span title={row.author} className="block max-w-28 truncate text-sm sm:max-w-44">{row.author}</span>
              <span title={row.repository} className="block max-w-28 truncate font-normal text-slate-500 sm:max-w-44">{row.repository}</span>
            </th>
            {axis.map(day => {
              const entries = row.commitsByDay.get(day);
              const opened = active.rowKey === row.key && active.day === day;
              const hasSelection = entries?.some(commit => selected.has(commit.id));
              return <td key={day} className={`border-b border-slate-100 px-1 py-1.5 text-center ${active.day === day ? "bg-blue-50/50" : ""}`}>
                {entries ? <button type="button" aria-pressed={opened} aria-label={`${row.author} · ${row.repository} · ${day} · ${entries.length} 次提交`}
                  onClick={() => onNavigate({ rowKey: row.key, day })}
                  className={`inline-flex h-8 w-full items-center justify-center gap-0.5 rounded-md tabular-nums hover:bg-blue-100 ${opened ? "bg-blue-100 text-blue-700 ring-1 ring-inset ring-blue-500" : hasSelection ? "bg-blue-100 font-semibold text-blue-800" : "bg-blue-50 text-blue-800"}`}>
                  <GitCommitHorizontal size={13} aria-hidden="true" />{entries.length}
                </button> : <span className="text-slate-300" aria-hidden="true">·</span>}
              </td>;
            })}
          </tr>)}
        </tbody>
      </table>
    </div>
  </section>;
}
