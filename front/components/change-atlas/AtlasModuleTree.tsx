"use client";

import { Folder, GitCommitHorizontal } from "lucide-react";
import type { AtlasModuleStat } from "@/lib/change-atlas";
import type { AtlasSnapshot } from "@/lib/change-atlas-types";

type Branch = { name: string; path: string; module?: AtlasModuleStat; children: Branch[] };
export function AtlasModuleTree({ snapshot, stats, selected, search, labels, onModule }: { snapshot: AtlasSnapshot; stats: AtlasModuleStat[]; selected: string; search: string; labels: Record<string, string>; onModule: (id: string) => void }) {
  function render(nodes: Branch[]) {
    return [...nodes].sort((a, b) => a.name.localeCompare(b.name)).map(node => <div key={node.path} className="min-w-0">
      {node.module && <button type="button" data-atlas-module={node.module.id} aria-pressed={selected === node.module.id} onClick={() => onModule(node.module!.id)} className={`my-1 flex w-full min-w-0 flex-wrap items-center gap-2 rounded-lg border p-3 text-left ${selected === node.module.id ? "border-blue-400 bg-blue-50 ring-1 ring-blue-200" : "border-slate-200 bg-white hover:border-blue-300"}`}><Folder size={16} className="shrink-0 text-blue-500"/><span className="min-w-0 flex-1 break-all text-sm font-medium">{labels[node.module.id] || node.name}{labels[node.module.id] && <span className="ml-2 text-[10px] font-normal text-blue-600">AI 名称候选</span>}</span><span className="flex items-center gap-1 text-xs text-slate-500"><GitCommitHorizontal size={13}/>{node.module.commits.length} 提交 · {node.module.changedFiles.length} 文件 · {node.module.authors} 署名</span></button>}
      {node.children.length > 0 && <details open><summary className="cursor-pointer py-2 text-xs text-slate-500">{node.module ? "子目录" : node.name}</summary><div className="ml-2 border-l border-slate-200 pl-4">{render(node.children)}</div></details>}
    </div>);
  }
  return <div aria-label="模块变化地图" className="max-h-[600px] space-y-3 overflow-auto">{snapshot.repositories.map(repo => {
    const roots: Branch[] = [];
    const modules = stats.filter(module => module.repository_id === repo.id && `${module.name} ${module.directory} ${labels[module.id] || ""} ${repo.name}`.toLowerCase().includes(search.toLowerCase()));
    for (const module of modules) {
      const parts = module.directory === "." ? ["根目录"] : module.directory.split("/").filter(Boolean);
      let children = roots;
      parts.forEach((name, index) => { const path = parts.slice(0, index + 1).join("/"); let branch = children.find(item => item.path === path); if (!branch) { branch = { name, path, children: [] }; children.push(branch); } if (index === parts.length - 1) branch.module = module; children = branch.children; });
    }
    return modules.length > 0 ? <details key={repo.id} open className="rounded-xl border border-slate-200 p-3"><summary className="cursor-pointer text-sm font-semibold"><Folder size={17} className="mx-2 inline text-blue-600"/>{repo.name}<span className="ml-2 text-[11px] font-normal text-slate-500">{repo.ref} · {modules.length} 模块</span></summary><div className="ml-3 mt-3 border-l border-slate-200 pl-4">{render(roots)}</div></details> : null;
  })}{!stats.some(module => `${module.name} ${module.directory} ${labels[module.id] || ""} ${snapshot.repositories.find(repo => repo.id === module.repository_id)?.name}`.toLowerCase().includes(search.toLowerCase())) && <p className="p-8 text-center text-xs text-slate-500">没有匹配的模块，请调整关键词。</p>}</div>;
}
