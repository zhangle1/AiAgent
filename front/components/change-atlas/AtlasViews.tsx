"use client";

import { GitCommitHorizontal } from "lucide-react";
import { atlasCochange, atlasDay, atlasDays, atlasModuleStats, type AtlasModuleStat } from "@/lib/change-atlas";
import type { AtlasCommit, AtlasFile, AtlasSnapshot } from "@/lib/change-atlas-types";

import { AtlasTree } from "./AtlasTree";
import { AtlasModuleTree } from "./AtlasModuleTree";

export type AtlasView = "business" | "structure" | "timeline" | "people";
type Props = {
  snapshot: AtlasSnapshot; commits: AtlasCommit[]; timeCommits: AtlasCommit[]; stats: AtlasModuleStat[];
  perspective: "delivery" | "developer"; view: AtlasView; selected: string; authorId: string; until: string; search: string; labels: Record<string, string>;
  onModule: (id: string) => void; onFile: (file: AtlasFile) => void;
  onTime: (module: string, day: string) => void; onPerson: (module: string, author: string) => void;
};
const heat = (count: number) => count >= 5 ? "bg-blue-200/80" : count > 0 ? "bg-blue-50" : "bg-white";

export function AtlasViews(props: Props) {
  const { snapshot, stats, selected, labels } = props;
  const names = new Map(snapshot.repositories.map(repo => [repo.id, repo.name]));
  const label = (module: AtlasModuleStat) => labels[module.id] || module.name;
  const chosen = stats.find(module => module.id === selected);
  if (props.view === "business") return <div className="space-y-3">
    <AtlasModuleTree snapshot={snapshot} stats={stats} selected={selected} search={props.search} labels={labels} onModule={props.onModule}/>
    <p className="text-xs leading-6 text-slate-500">仓库 → 目录 → 模块；点击模块联动改动关系链与 AI 解读。目录分组不是已验证的业务边界。</p>
    {props.perspective === "delivery" && <div className="rounded-xl border border-blue-100 bg-blue-50 p-4"><h3 className="text-sm font-medium">交付关注 · {chosen ? label(chosen) : "选择模块"}</h3><p className="mt-2 text-xs leading-6">{chosen?.commits.length || 0} 次提交涉及 {chosen?.changedFiles.length || 0} 个文件。建议结合以下历史同改模块核对联动场景。</p><div className="mt-2 flex flex-wrap gap-2">{atlasCochange(snapshot, props.commits, selected).slice(0, 8).map(item => <button type="button" key={item.module.id} onClick={() => props.onModule(item.module.id)} className="rounded-lg border border-blue-200 bg-white px-3 py-2 text-xs text-blue-700">{chosen ? label(chosen) : "当前模块"} ↔ {labels[item.module.id] || item.module.name} · {item.count} 次同改</button>)}</div><p className="mt-2 text-[11px] text-slate-500">同改为非合并提交统计；上线影响和验收结果仍需核对。</p></div>}
  </div>;
  if (props.view === "structure") {
    const changed = new Set(chosen?.changedFiles.map(file => file.id));
    const files = snapshot.files.filter(file => file.module_id === selected && file.path.toLowerCase().includes(props.search.toLowerCase())).sort((a, b) => Number(changed.has(b.id)) - Number(changed.has(a.id)) || a.path.localeCompare(b.path));
    const repo = snapshot.repositories.find(item => item.id === chosen?.repository_id);
    return <div className="space-y-3" aria-label="所选模块目录结构">
      <p className="text-xs leading-6 text-slate-500">{repo?.name} · 结构固定在 <code>{repo?.head_sha.slice(0, 10) || "无提交"}</code>；目录可折叠，蓝色文件为范围内改动，点击读取差异。</p>
      <AtlasTree files={files.slice(0, 500)} changed={changed} onFile={props.onFile}/>
      {files.length === 0 && <p className="p-6 text-xs text-slate-500">没有匹配的文件。</p>}
      {files.length > 500 && <p className="text-xs text-slate-500">展示前 500 / {files.length} 个文件，优先保留改动文件；可输入路径搜索其余文件。</p>}
      <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs leading-6 text-amber-800">当前为目录包含结构，尚未解析函数调用和运行依赖。</p>
    </div>;
  }
  const top = stats.slice(0, 12);
  if (chosen && !top.some(module => module.id === selected)) top.splice(11, 1, chosen);
  const days = atlasDays(snapshot);
  if (props.view === "timeline") return <div className="space-y-3">
    <div className="overflow-x-auto"><table className="w-full min-w-[540px] border-separate border-spacing-1 text-xs" aria-label="模块每日提交矩阵"><thead><tr><th className="w-28 text-left font-normal text-slate-500">模块 / UTC 日期</th>{days.map(day => <th key={day} className="min-w-9 font-normal text-slate-500">{day.slice(5)}</th>)}</tr></thead><tbody>{top.map(module => <tr key={module.id}><th className="max-w-28 break-all py-2 text-left font-normal">{label(module)}<span className="block text-[11px] text-slate-400">{names.get(module.repository_id)}</span></th>{days.map(day => { const count = module.commits.filter(commit => atlasDay(commit.committed_at) === day).length; return <td key={day}><button type="button" aria-label={`${label(module)} ${day} ${count} 次提交`} aria-pressed={selected === module.id && props.until === day} onClick={() => props.onTime(module.id, day)} className={`flex min-h-10 w-full items-center justify-center gap-1 rounded-md border ${heat(count)} ${selected === module.id && props.until === day ? "border-blue-500 ring-1 ring-blue-400" : "border-transparent"}`}>{count ? <><GitCommitHorizontal size={13}/>{count}</> : <span className="text-slate-300">·</span>}</button></td>; })}</tr>)}</tbody></table></div>
    <p className="text-xs leading-6 text-slate-500">点击交点聚焦模块与日期；统计按 Git 提交时间 UTC 分桶。结构保持在当前快照，不表示还原了过去当天的远端状态。{stats.length > 12 && "展示活动最多的 12 个模块及当前选择。"}</p>
  </div>;
  const authors = Array.from(new Map(props.timeCommits.map(commit => [commit.author_id, commit.author])).entries()).sort((a, b) => a[1].localeCompare(b[1]));
  const visibleAuthors = authors.slice(0, 12);
  if (props.authorId && !visibleAuthors.some(([id]) => id === props.authorId)) { const author = authors.find(([id]) => id === props.authorId); if (author) visibleAuthors.splice(11, 1, author); }
  const allStats = new Map(atlasModuleStats(snapshot, props.timeCommits).map(module => [module.id, module]));
  const related = atlasCochange(snapshot, props.commits, selected).slice(0, 4);
  return <div className="space-y-4">
    <div className="overflow-x-auto"><table className="w-full min-w-[520px] border-separate border-spacing-1 text-xs" aria-label="Git 署名与模块交集"><thead><tr><th className="w-24 text-left font-normal text-slate-500">Git 署名</th>{top.map(module => <th key={module.id} className="max-w-24 break-all font-normal text-slate-500">{label(module)}</th>)}</tr></thead><tbody>{visibleAuthors.map(([authorId, author]) => <tr key={authorId}><th className="max-w-24 break-all py-3 text-left font-normal">{author}<span className="block text-[11px] text-slate-400">身份未绑定</span></th>{top.map(module => { const count = allStats.get(module.id)?.commits.filter(commit => commit.author_id === authorId).length || 0; return <td key={module.id}><button type="button" disabled={!count} aria-label={`${author} ${label(module)} ${count} 次提交`} aria-pressed={props.authorId === authorId && selected === module.id} onClick={() => props.onPerson(module.id, authorId)} className={`min-h-11 w-full rounded-md border disabled:cursor-default ${heat(count)} ${props.authorId === authorId && selected === module.id ? "border-blue-500 ring-1 ring-blue-400" : "border-transparent"}`}>{count || <span className="text-slate-300">·</span>}</button></td>; })}</tr>)}</tbody></table></div>
    <p className="text-xs leading-6 text-slate-500">点选人员和模块，其他视角会同步筛选。Git 署名尚未绑定平台身份，不据此判断岗位或 AI 生成归属。{authors.length > 12 && "这里只显示前 12 个署名，可在顶部人员选择中切换。"}</p>
    <div className="border-t border-slate-200 pt-3"><h3 className="text-xs font-medium">当前模块的历史共同修改</h3><div className="mt-2 flex flex-wrap gap-2">{related.map(item => <button type="button" key={item.module.id} onClick={() => props.onModule(item.module.id)} className="rounded-md bg-blue-50 px-2 py-1.5 text-xs text-blue-700">{labels[item.module.id] || item.module.name} · {item.count} 次同改</button>)}{related.length === 0 && <span className="text-xs text-slate-400">当前范围没有共同修改记录。</span>}</div><p className="mt-2 text-[11px] text-slate-500">同改按同一非合并提交计算，属于历史统计，不代表调用关系或已发生冲突。</p></div>
  </div>;
}
