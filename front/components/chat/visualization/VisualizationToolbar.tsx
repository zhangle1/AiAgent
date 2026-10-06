"use client";

import { Network } from "lucide-react";
import { diagramTypes, type DiagramType } from "@/lib/chat-visualization";

export function VisualizationToolbar({ value, onChange, disabled }: {
  value: DiagramType | null;
  onChange: (value: DiagramType | null) => void;
  disabled: boolean;
}) {
  return <div className="mb-2 flex flex-wrap items-center gap-2 border-b border-slate-100 pb-2 text-xs">
    <div role="group" aria-label="输出形式" className="inline-flex rounded-lg bg-slate-100 p-0.5">
      <button type="button" disabled={disabled} aria-pressed={value === null} onClick={() => onChange(null)} className={`rounded-md px-3 py-1.5 disabled:opacity-50 ${value === null ? "bg-white text-slate-900 shadow-sm" : "text-slate-500"}`}>文字</button>
      <button type="button" disabled={disabled} aria-pressed={value !== null} onClick={() => onChange(value ?? "auto")} className={`inline-flex items-center gap-1 rounded-md px-3 py-1.5 disabled:opacity-50 ${value !== null ? "bg-white text-blue-700 shadow-sm" : "text-slate-500"}`}><Network size={14} />可视化</button>
    </div>
    {value !== null && <>
      <label className="inline-flex items-center gap-1 text-slate-600">图形
        <select aria-label="图形类型" value={value} disabled={disabled} onChange={(event) => onChange(event.target.value as DiagramType)} className="rounded-md border border-slate-200 bg-white px-2 py-1.5 text-slate-800">
          {diagramTypes.map((item) => <option key={item.id} value={item.id}>{item.label}</option>)}
        </select>
      </label>
      <span className="text-slate-500">基于当前对话和已选资料 · 可继续对话修改</span>
    </>}
  </div>;
}
