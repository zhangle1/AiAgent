"use client";

import { useId, useState } from "react";
import { Check, ChevronDown, Search } from "lucide-react";
import type { CodeProject } from "@/lib/code-repository-types";

export function SearchableProjectSelect({ projects, value, disabled, onChange }: {
  projects: CodeProject[];
  value: number | "";
  disabled: boolean;
  onChange: (value: number) => void;
}) {
  const id = useId();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const selected = projects.find((project) => project.id === value);
  const matches = projects.filter((project) => project.display_name.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()));

  return <div className="relative min-w-0" onBlur={(event) => {
    if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false);
  }} onKeyDown={(event) => {
    if (event.key === "Escape" && open) {
      event.stopPropagation();
      setOpen(false);
      document.getElementById(id)?.focus();
    }
  }}>
    <label htmlFor={id} className="text-xs font-medium text-slate-600">AiAgent 项目</label>
    <button id={id} type="button" disabled={disabled} aria-expanded={open && !disabled} aria-controls={`${id}-projects`}
      onClick={() => { setQuery(""); setOpen(!open); }}
      title={selected?.display_name}
      className="mt-1 flex h-9 w-full items-center justify-between gap-2 rounded-lg border border-slate-200 bg-white px-2 text-left text-sm text-slate-700 disabled:opacity-50">
      <span className="truncate">{selected?.display_name ?? "选择项目"}</span><ChevronDown size={14} className="shrink-0"/>
    </button>
    {open && !disabled && <div id={`${id}-projects`} className="absolute right-0 top-full z-20 mt-1 w-full min-w-[240px] max-w-[calc(100vw-64px)] rounded-xl border border-slate-200 bg-white p-2 shadow-lg">
      <label className="relative block">
        <Search size={14} className="pointer-events-none absolute left-2 top-2.5 text-slate-400"/>
        <input autoFocus aria-label="搜索 AiAgent 项目" placeholder="搜索项目名称…" value={query} onChange={(event) => setQuery(event.target.value)}
          className="h-9 w-full rounded-lg border border-slate-200 pl-7 pr-2 text-sm"/>
      </label>
      <p className="px-2 py-2 text-xs text-slate-400" aria-live="polite">{matches.length} 个项目</p>
      <div className="max-h-60 overflow-y-auto" aria-label="项目搜索结果">
        {matches.map((project) => <button key={project.id} type="button" aria-pressed={project.id === value}
          onClick={() => { onChange(project.id); setOpen(false); document.getElementById(id)?.focus(); }}
          className="flex w-full items-center gap-2 rounded-lg px-2 py-2 text-left text-sm text-slate-700 hover:bg-blue-50 focus:bg-blue-50">
          <span className="min-w-0 flex-1 break-words">{project.display_name}</span>
          {project.id === value && <Check size={14} className="shrink-0 text-blue-600"/>}
        </button>)}
        {!matches.length && <p className="px-2 py-4 text-center text-sm text-slate-400">{projects.length ? "没有匹配的项目" : "暂无可用项目"}</p>}
      </div>
    </div>}
  </div>;
}
