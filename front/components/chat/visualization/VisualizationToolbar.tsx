"use client";

import { useEffect, useRef, useState } from "react";
import { Network, Settings2, X } from "lucide-react";
import type { CodeProject, SelectedGitCommit } from "@/lib/code-repository-types";
import { VisualizationDialog } from "./VisualizationDialog";
import { diagramTypes, mermaidDiagramTypes, isInteractiveDiagram, type DiagramType } from "@/lib/chat-visualization";

export function VisualizationToolbar({ value, onChange, disabled, project, commits, onCommitsChange, activation = 0, compact = false }: {
  activation?: number; compact?: boolean;
  value: DiagramType | null;
  onChange: (value: DiagramType | null) => void;
  disabled: boolean;
  project: CodeProject | null;
  commits: SelectedGitCommit[];
  onCommitsChange: (commits: SelectedGitCommit[]) => void;
}) {
  const [open, setOpen] = useState(false);
  const seen = useRef(activation);
  useEffect(() => { if (seen.current === activation) return; seen.current = activation; if (!disabled) setOpen(true); }, [activation, disabled]);
  if (compact) return <>
    {value && <div className="mb-2 inline-flex max-w-full items-center rounded-lg border border-blue-100 bg-blue-50 text-xs text-blue-700">
      <button type="button" disabled={disabled} onClick={() => setOpen(true)} className="inline-flex min-w-0 items-center gap-1.5 px-2 py-1.5"><Network size={13} className="shrink-0" /><span className="truncate">{[...diagramTypes, ...mermaidDiagramTypes].find(item => item.id === value)?.label}{commits.length > 0 && ` · ${commits.length} 条 Git 记录`}</span></button>
      <button type="button" aria-label="取消可视化" disabled={disabled} onClick={() => { onChange(null); onCommitsChange([]); }} className="rounded p-1.5 hover:bg-blue-100"><X size={13} /></button>
    </div>}
    {open && !disabled && <VisualizationDialog project={project} value={value ?? "interactive"} commits={commits} onClose={() => setOpen(false)} onApply={(type, selected) => { onChange(type); onCommitsChange(selected); setOpen(false); }} />}
  </>;
  return <div className="mb-2 flex flex-wrap items-center gap-2 border-b border-slate-100 pb-2 text-xs">
    <div role="group" aria-label="输出形式" className="inline-flex rounded-lg bg-slate-100 p-0.5">
      <button type="button" disabled={disabled} aria-pressed={value === null} onClick={() => onChange(null)} className={`rounded-md px-3 py-1.5 disabled:opacity-50 ${value === null ? "bg-white text-slate-900 shadow-sm" : "text-slate-500"}`}>文字</button>
      <button type="button" disabled={disabled} aria-pressed={value !== null} aria-haspopup="dialog" onClick={() => setOpen(true)} className={`inline-flex items-center gap-1 rounded-md px-3 py-1.5 disabled:opacity-50 ${value !== null ? "bg-white text-blue-700 shadow-sm" : "text-slate-500"}`}><Network size={14} />可视化</button>
    </div>
    {value !== null && <>
      <button type="button" disabled={disabled} aria-haspopup="dialog" onClick={() => setOpen(true)} className="inline-flex items-center gap-2 rounded-lg border border-blue-200 bg-blue-50 px-3 py-1.5 text-blue-700 disabled:opacity-50"><Settings2 size={14} />{isInteractiveDiagram(value) ? "交互图形" : "Mermaid"}：{[...diagramTypes, ...mermaidDiagramTypes].find((item) => item.id === value)?.label}{commits.length > 0 && ` · ${commits.length} 条 Git 记录`}</button>
      <span className="text-slate-500">发送后在回复中查看图形 · 可继续对话修改</span>
    </>}
    {open && !disabled && <VisualizationDialog project={project} value={value ?? "interactive"} commits={commits} onClose={() => setOpen(false)} onApply={(type, selected) => { onChange(type); onCommitsChange(selected); setOpen(false); }} />}
  </div>;
}
