"use client";

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Check, GitCommitHorizontal, X } from "lucide-react";
import { DiagramPreview } from "./DiagramPreview";
import { ArchitectureScopePicker } from "./ArchitectureScopePicker";
import type { ArchitectureScope } from "@/lib/chat-architecture";
import { ArchifyDemo } from "./ArchifyDemo";
import { diagramTypes, type DiagramType } from "@/lib/chat-visualization";
import { getProjectGitHistory } from "@/lib/code-repository-api";
import type { CodeProject, GitHistoryCommit, SelectedGitCommit } from "@/lib/code-repository-types";

export function VisualizationDialog({ project, value, commits, scope = null, onClose, onApply }: {
  project: CodeProject | null;
  value: DiagramType;
  commits: SelectedGitCommit[];
  scope?: ArchitectureScope | null;
  onClose: () => void;
  onApply: (value: DiagramType, commits: SelectedGitCommit[], scope: ArchitectureScope | null) => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [selectedScope, setSelectedScope] = useState(scope);
  const [type, setType] = useState(value);
  const [selected, setSelected] = useState(commits);
  const [repository, setRepository] = useState(project?.repositories[0]?.name ?? "");
  const [page, setPage] = useState(0);
  const [rows, setRows] = useState<GitHistoryCommit[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  const [historyOpen, setHistoryOpen] = useState(commits.length > 0);
  const [demoOpen, setDemoOpen] = useState(false);

  useEffect(() => {
    const dialog = dialogRef.current;
    const previous = document.activeElement as HTMLElement | null;
    dialog?.showModal();
    return () => { dialog?.close(); previous?.focus(); };
  }, []);

  useEffect(() => {
    if (!historyOpen || !project || !repository) return;
    let cancelled = false;
    setLoading(true); setError(""); setRows([]);
    getProjectGitHistory(project.id, repository, page * 50)
      .then((items) => { if (!cancelled) setRows(items); })
      .catch((err: unknown) => { if (!cancelled) setError(err instanceof Error ? err.message : "读取 Git 历史失败"); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [project, repository, page, revision, historyOpen]);

  function toggle(commit: GitHistoryCommit) {
    if (!project) return;
    setSelected((current) => current.some((item) => item.repository_name === repository && item.sha === commit.sha)
      ? current.filter((item) => item.repository_name !== repository || item.sha !== commit.sha)
      : current.length < 20 ? [...current, { ...commit, project_id: project.id, repository_name: repository }] : current);
  }

  return createPortal(<dialog ref={dialogRef} aria-labelledby="visualization-title" onCancel={(event) => { event.preventDefault(); if (demoOpen) setDemoOpen(false); else onClose(); }} className={`m-auto ${demoOpen ? "w-[calc(100vw-24px)] max-w-[1440px]" : "w-[min(760px,calc(100vw-24px))]"} max-h-[88dvh] overflow-hidden rounded-2xl bg-white p-0 text-slate-900 shadow-2xl backdrop:bg-slate-900/40`}>
    <div className={`flex max-h-[88dvh] flex-col ${demoOpen ? "h-[88dvh]" : ""}`}>
      <header className="flex shrink-0 items-center justify-between border-b px-5 py-4">
        <div><h2 id="visualization-title" className="text-lg font-semibold">{demoOpen ? "Archify 交互示例" : "选择图形与来源"}</h2><p className="mt-1 text-xs text-slate-500">{demoOpen ? "探索 AI 工作台的概念架构。" : "选好后，在输入框描述你想梳理的内容。"}</p></div>
        <button type="button" aria-label="关闭图形选择" onClick={onClose} className="rounded-lg p-2 hover:bg-slate-100"><X size={20} /></button>
      </header>
      {demoOpen ? <ArchifyDemo /> : <div className="min-h-0 overflow-y-auto px-5 py-4">
        <button type="button" onClick={() => setDemoOpen(true)} className="mb-4 flex w-full items-center justify-between rounded-xl border border-blue-200 bg-blue-50 p-3 text-left text-sm text-blue-800"><span><strong className="block">体验 Archify 交互示例</strong><span className="mt-1 block text-xs text-blue-600">点击节点、追踪路径、缩放和导出 · 固定示例</span></span><span aria-hidden="true">→</span></button>
        <div role="group" aria-label="图形类型" className="grid grid-cols-2 gap-2 sm:grid-cols-3">
          {diagramTypes.map((item) => <button key={item.id} type="button" aria-pressed={type === item.id} onClick={() => setType(item.id)} className={`relative rounded-xl border p-3 text-left transition-colors ${type === item.id ? "border-blue-500 bg-blue-50 ring-1 ring-blue-500" : "border-slate-200 hover:border-blue-300 hover:bg-slate-50"}`}>
            <DiagramPreview type={item.id} />
            <span className="block pr-5 text-sm font-medium">{item.label}</span>{type === item.id && <Check size={16} className="absolute right-3 top-3 text-blue-600" />}
            <span className="mt-1 block text-[11px] leading-5 text-slate-500">{descriptions[item.id]}</span>
          </button>)}
        </div>
        {type === "interactive" && <ArchitectureScopePicker project={project} value={selectedScope} onChange={setSelectedScope} />}
        <p className="mt-4 text-xs text-slate-500">默认使用当前对话与已选项目资料。</p>
        <button type="button" aria-expanded={historyOpen} onClick={() => setHistoryOpen(!historyOpen)} className="mt-3 flex w-full items-center gap-2 rounded-xl border border-slate-200 p-3 text-left text-sm font-medium"><GitCommitHorizontal size={18} />勾选 Git 历史<span className="ml-auto text-xs text-slate-500">已选 {selected.length}/20 · {historyOpen ? "收起" : "展开"}</span></button>
        {historyOpen && <section aria-label="Git 提交历史" className="mt-3 space-y-3">
          {!project ? <p className="text-sm text-slate-500">请先在聊天输入框下方选择项目，再选择 Git 历史。</p> : !project.repositories.length ? <p className="text-sm text-slate-500">当前项目没有代码库。</p> : <>
            <label className="flex items-center gap-2 text-sm">代码库<select aria-label="Git 历史代码库" value={repository} onChange={(event) => { setRepository(event.target.value); setPage(0); setRows([]); }} className="min-w-0 flex-1 rounded-lg border p-2">{project.repositories.map((repo) => <option key={repo.name} value={repo.name}>{repo.display_name || repo.name}</option>)}</select></label>
            <p className="text-xs text-slate-500">本地当前分支的提交记录，每页 50 条。发送所选摘要，不含代码差异。</p>
            {loading && <p role="status" className="py-4 text-sm text-slate-500">正在读取提交记录…</p>}
            {error && <div role="alert" className="text-sm text-red-600">{error}<button type="button" onClick={() => setRevision((r) => r + 1)} className="ml-2 underline">重试</button></div>}
            {!loading && !error && rows.length === 0 && <p className="py-3 text-sm text-slate-500">没有更多提交记录。</p>}
            <div className="max-h-64 overflow-y-auto rounded-lg border border-slate-100">
              {rows.map((commit) => {
                const checked = selected.some((item) => item.repository_name === repository && item.sha === commit.sha);
                return <label key={commit.sha} className={`flex cursor-pointer items-start gap-3 border-b border-slate-100 p-3 last:border-0 ${checked ? "bg-blue-50" : "hover:bg-slate-50"}`}>
                  <input type="checkbox" checked={checked} disabled={!checked && selected.length >= 20} onChange={() => toggle(commit)} className="mt-1" />
                  <span className="min-w-0"><span className="block break-words text-sm">{commit.subject}</span><span className="mt-1 block text-xs text-slate-500">{commit.sha.slice(0, 8)} · {commit.author} · {commit.date.slice(0, 10)}</span></span>
                </label>;
              })}
            </div>
            <div className="flex items-center justify-between text-xs"><button type="button" disabled={loading || page === 0} onClick={() => setPage(page - 1)} className="rounded border px-3 py-1.5 disabled:opacity-40">上一页</button><span>第 {page + 1} 页</span><button type="button" disabled={loading || !!error || rows.length < 50 || page >= 200} onClick={() => setPage(page + 1)} className="rounded border px-3 py-1.5 disabled:opacity-40">下一页</button></div>
          </>}
        </section>}
        {selected.length > 0 && <div className="mt-3 rounded-lg bg-slate-50 p-3"><div className="flex justify-between text-xs"><span>已选 {selected.length} 条提交（可跨仓库）</span><button type="button" onClick={() => setSelected([])} className="text-blue-600">清空</button></div><div className="mt-2 flex max-h-24 flex-wrap gap-1 overflow-y-auto">{selected.map((item) => <button type="button" key={`${item.repository_name}:${item.sha}`} title={item.subject} aria-label={`移除 ${item.repository_name} ${item.sha.slice(0, 8)}`} onClick={() => setSelected((current) => current.filter((c) => c.repository_name !== item.repository_name || c.sha !== item.sha))} className="rounded border bg-white px-2 py-1 text-xs">{item.repository_name} · {item.sha.slice(0, 8)} ×</button>)}</div></div>}
      </div>}
      <footer className="flex shrink-0 justify-end gap-2 border-t px-5 py-3">{demoOpen ? <button type="button" autoFocus onClick={() => setDemoOpen(false)} className="rounded-lg border px-4 py-2 text-sm">返回图形选择</button> : <><button type="button" onClick={onClose} className="rounded-lg border px-4 py-2 text-sm">取消</button><button type="button" onClick={() => onApply(type, selected, type === "interactive" ? selectedScope : null)} className="rounded-lg bg-blue-600 px-4 py-2 text-sm text-white">使用此配置</button></>}</footer>
    </div>
  </dialog>, document.body);
}

const descriptions: Record<DiagramType, string> = {
  interactive: "节点探索、上下游高亮、路径与导出", auto: "根据问题自动选择合适图形", architecture: "系统模块、层次与依赖", flowchart: "业务步骤、判断与分支", sequence: "接口调用与交互顺序", class: "类、接口与继承关系", er: "数据实体、字段与关联", state: "生命周期与状态变化", mindmap: "主题拆解与知识梳理", timeline: "事件与版本演进", gantt: "任务排期与依赖", git: "提交、分支与合并关系",
};
