"use client";

import type { ArchitectureScope } from "@/lib/chat-architecture";
import { useState } from "react";
import { Network, Settings2 } from "lucide-react";
import type { CodeProject, SelectedGitCommit } from "@/lib/code-repository-types";
import { VisualizationDialog } from "./VisualizationDialog";
import { diagramTypes, type DiagramType } from "@/lib/chat-visualization";

export function VisualizationToolbar({ value, onChange, disabled, project, commits, onCommitsChange, scope, onScopeChange }: {
  value: DiagramType | null;
  onChange: (value: DiagramType | null) => void;
  disabled: boolean;
  project: CodeProject | null;
  scope?: ArchitectureScope | null;
  onScopeChange?: (scope: ArchitectureScope | null) => void;
  commits: SelectedGitCommit[];
  onCommitsChange: (commits: SelectedGitCommit[]) => void;
}) {
  const [open, setOpen] = useState(false);
  return <div className="mb-2 flex flex-wrap items-center gap-2 border-b border-slate-100 pb-2 text-xs">
    <div role="group" aria-label="输出形式" className="inline-flex rounded-lg bg-slate-100 p-0.5">
      <button type="button" disabled={disabled} aria-pressed={value === null} onClick={() => onChange(null)} className={`rounded-md px-3 py-1.5 disabled:opacity-50 ${value === null ? "bg-white text-slate-900 shadow-sm" : "text-slate-500"}`}>文字</button>
      <button type="button" disabled={disabled} aria-pressed={value !== null} aria-haspopup="dialog" onClick={() => setOpen(true)} className={`inline-flex items-center gap-1 rounded-md px-3 py-1.5 disabled:opacity-50 ${value !== null ? "bg-white text-blue-700 shadow-sm" : "text-slate-500"}`}><Network size={14} />可视化</button>
    </div>
    {value !== null && <>
      <button type="button" disabled={disabled} aria-haspopup="dialog" onClick={() => setOpen(true)} className="inline-flex items-center gap-2 rounded-lg border border-blue-200 bg-blue-50 px-3 py-1.5 text-blue-700 disabled:opacity-50"><Settings2 size={14} />图形：{diagramTypes.find((item) => item.id === value)?.label}{commits.length > 0 && ` · ${commits.length} 条 Git 记录`}</button>
      {value === "interactive" && scope && <span className="max-w-full truncate text-blue-700" title={scope.path}>范围：{scope.repository} / {scope.path || "根目录"}</span>}
      <span className="text-slate-500">点击选择图形与来源 · 可继续对话修改</span>
    </>}
    {open && !disabled && <VisualizationDialog scope={scope} project={project} value={value ?? "auto"} commits={commits} onClose={() => setOpen(false)} onApply={(type, selected, nextScope) => { onScopeChange?.(nextScope); onChange(type); onCommitsChange(selected); setOpen(false); }} />}
  </div>;
}
