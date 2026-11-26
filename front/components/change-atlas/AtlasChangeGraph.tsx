"use client";

import { useId, useState } from "react";
import type { AtlasCommit, AtlasFile } from "@/lib/change-atlas-types";

export function AtlasChangeGraph({ commits, files, onEvidence, historyOnly = false, formatDay }: {
  commits: AtlasCommit[];
  files: AtlasFile[];
  onEvidence: (commit: AtlasCommit, file: AtlasFile) => void;
  historyOnly?: boolean;
  formatDay?: (timestamp: string) => string;
}) {
  const arrowId = useId();
  const [zoom, setZoom] = useState(1);
  const visible = commits.slice(0, 8);
  const fileMap = new Map(files.map(file => [file.id, file]));
  const targets = Array.from(new Set(visible.flatMap(commit => commit.files.map(change => change.file_id)))).map(id => fileMap.get(id)).filter((file): file is AtlasFile => !!file).slice(0, 16);
  const isAbsent = (file: AtlasFile) => !historyOnly && !file.at_head;
  const height = Math.max(180, visible.length * 82 + 40, targets.length * 64 + 40);
  return <section aria-label="提交文件改动关系图" className="space-y-2">
    <div className="flex flex-wrap items-center gap-2"><h3 className="mr-auto text-sm font-medium">改动关系链</h3><button type="button" aria-label="缩小关系图" onClick={() => setZoom(value => Math.max(.6, value - .2))} className="rounded border px-2">−</button><button type="button" aria-label="重置关系图缩放" onClick={() => setZoom(1)} className="text-xs text-slate-500">{Math.round(zoom * 100)}%</button><button type="button" aria-label="放大关系图" onClick={() => setZoom(value => Math.min(1.6, value + .2))} className="rounded border px-2">+</button></div>
    <p className="text-xs leading-6 text-slate-500">提交 → 改动文件。点击文件或连线查看对应差异。{!historyOnly && "红色虚线表示文件在快照版本中已不存在。"}</p>
    {!visible.length ? <p className="p-6 text-xs text-slate-500">当前范围无提交，调整日期或人员后查看关系链。</p> : <div className="max-h-[540px] overflow-auto rounded-xl border border-slate-200 bg-slate-50" tabIndex={0} aria-label="可滚动改动关系画布"><svg width={780 * zoom} height={height * zoom} viewBox={`0 0 780 ${height}`} className="block" role="group" aria-label="提交到文件的改动连线">
      <defs><marker id={arrowId} viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse"><path d="M 0 0 L 10 5 L 0 10 z" fill="#60a5fa"/></marker></defs>
      <text x="20" y="22" fill="#64748b" fontSize="12">已载入提交（最近 {visible.length} 次）</text><text x="450" y="22" fill="#64748b" fontSize="12">改动文件（{targets.length} 个）</text>
      {visible.flatMap((commit, index) => commit.files.map(change => { const target = targets.findIndex(file => file.id === change.file_id); if (target < 0) return null; const file = targets[target]; const d = `M 300 ${60 + index * 82} C 370 ${60 + index * 82}, 380 ${60 + target * 64}, 450 ${60 + target * 64}`; return <g key={`${commit.id}:${file.id}`} role="button" tabIndex={0} aria-label={`${commit.subject} → ${file.path}`} onClick={() => onEvidence(commit, file)} onKeyDown={event => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); onEvidence(commit, file); } }} className="cursor-pointer"><path d={d} fill="none" stroke={isAbsent(file) ? "#f87171" : "#93c5fd"} strokeWidth="2" strokeDasharray={isAbsent(file) ? "5 4" : undefined} markerEnd={`url(#${arrowId})`}/><path d={d} fill="none" stroke="transparent" strokeWidth="16"><title>{commit.sha.slice(0, 8)} · {file.path} · {change.added === null ? "二进制" : `+${change.added} −${change.deleted}`}</title></path><circle cx="300" cy={60 + index * 82} r="5" fill="#3b82f6" stroke="white" strokeWidth="2"/></g>; }))}
      {visible.map((commit, index) => <g key={commit.id}><rect x="20" y={36 + index * 82} width="280" height="56" rx="8" fill="white" stroke="#cbd5e1"/><text x="32" y={58 + index * 82} fontSize="12" fill="#0f172a">{commit.subject.slice(0, 26)}<title>{commit.subject}</title></text><text x="32" y={78 + index * 82} fontSize="11" fill="#64748b">{commit.sha.slice(0, 8)} · {commit.author.slice(0, 16)} · {formatDay ? formatDay(commit.committed_at) : commit.committed_at.slice(5, 10)}</text></g>)}
      {targets.map((file, index) => { const commit = visible.find(item => item.files.some(change => change.file_id === file.id))!; return <g key={file.id} role="button" tabIndex={0} aria-label={`查看文件改动 ${file.path}`} onClick={() => onEvidence(commit, file)} onKeyDown={event => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); onEvidence(commit, file); } }} className="cursor-pointer"><title>{file.path} · 点击查看最近一次差异</title><rect x="450" y={36 + index * 64} width="310" height="48" rx="8" fill={isAbsent(file) ? "#fff1f2" : "#eff6ff"} stroke={isAbsent(file) ? "#fda4af" : "#93c5fd"}/><text x="462" y={56 + index * 64} fontSize="12" fill="#1d4ed8">{file.path.split("/").pop()!.slice(0, 37)}</text><text x="462" y={73 + index * 64} fontSize="10" fill="#64748b">{file.path.slice(0, Math.max(0, file.path.lastIndexOf("/"))).slice(-48)}</text></g>; })}
    </svg></div>}
    {(commits.length > 8 || new Set(visible.flatMap(commit => commit.files.map(change => change.file_id))).size > 16) && <p className="text-[11px] text-slate-500">画布最多展示最近 8 次提交和 16 个文件。可在下方列表查看其余已载入文件。</p>}
  </section>;
}
