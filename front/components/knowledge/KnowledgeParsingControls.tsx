"use client";

import type { KnowledgeParsingOptions } from "@/lib/knowledge-types";

const defaults: KnowledgeParsingOptions = { split_content: true, max_section_tokens: 2048, max_section_chars: 6000, min_section_tokens: 512 };

export function KnowledgeParsingControls({ value = defaults, onChange, disabled = false }: { value?: KnowledgeParsingOptions; onChange: (value: KnowledgeParsingOptions) => void; disabled?: boolean }) {
  return <fieldset disabled={disabled} className="space-y-3 rounded-lg border border-slate-200 p-3 text-xs disabled:opacity-60">
    <legend className="px-1 font-medium text-slate-700">文档结构解析</legend>
    <label className="flex items-center gap-2"><input type="checkbox" checked={value.split_content} onChange={e => onChange({ ...value, split_content: e.target.checked })} />按标题和段落拆分章节</label>
    <p className="leading-5 text-slate-500">用于组织解析文件，独立于模型上下文窗口。关闭后保存单个 Markdown；完整正文始终保留。单行超长表格优先保留整行。</p>
    {value.split_content && <div className="grid gap-3 sm:grid-cols-3">{([
      ["max_section_tokens", "章节 token 估值", 1, 1000000], ["max_section_chars", "章节字符上限", 2, 4000000], ["min_section_tokens", "短章节合并阈值", 0, value.max_section_tokens],
    ] as const).map(([key, label, min, max]) => <label key={key}>{label}<input aria-label={label} type="number" step={1} required min={min} max={max} value={value[key]} onChange={e => onChange({ ...value, [key]: Number(e.target.value) })} className="mt-1 w-full rounded border border-slate-200 bg-white px-2 py-1.5" /></label>)}</div>}
  </fieldset>;
}
