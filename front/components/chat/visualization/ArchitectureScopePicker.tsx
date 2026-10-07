"use client";

import { useEffect, useState } from "react";
import { getCodeTree, type CodeTree } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";
import { normalizeArchitecturePath, type ArchitectureScope } from "@/lib/chat-architecture";

export function ArchitectureScopePicker({ project, value, onChange }: { project: CodeProject | null; value: ArchitectureScope | null; onChange: (scope: ArchitectureScope | null) => void }) {
  const [repository, setRepository] = useState(value?.repository || project?.repositories[0]?.name || "");
  const [directory, setDirectory] = useState("");
  const [tree, setTree] = useState<CodeTree | null>(null), [error, setError] = useState("");
  const [loading, setLoading] = useState(false), [revision, setRevision] = useState(0);
  useEffect(() => {
    if (!project || !repository) return;
    let active = true;
    setLoading(true); setError(""); setTree(null);
    getCodeTree(repository, directory).then((result) => { if (active) setTree(result); }).catch((reason) => { if (active) setError(reason instanceof Error ? reason.message : "读取目录失败"); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [project, repository, directory, revision]);
  function select(path: string) {
    if (!project) return;
    try { onChange({ projectId: project.id, repository, path: normalizeArchitecturePath(path) }); }
    catch (reason) { setError((reason as Error).message); }
  }
  return <section aria-label="架构分析范围" className="mt-4 space-y-2 rounded-xl border p-3 text-sm">
    <strong>代码分析范围（可选）</strong>
    <p className="text-xs text-slate-500">不指定时使用当前对话与已选资料；指定后由 AI 读取该范围，并标注代码证据。</p>
    {!project?.repositories.length ? <p className="text-xs text-slate-500">当前项目未关联代码库。</p> : <>
      <label className="flex gap-2">代码库<select aria-label="架构分析代码库" className="min-w-0 flex-1 rounded border" value={repository} onChange={(event) => { setRepository(event.target.value); setDirectory(""); onChange(null); }}>{project.repositories.map((repo) => <option key={repo.name} value={repo.name}>{repo.display_name || repo.name}</option>)}</select></label>
      <div className="flex flex-wrap items-center gap-2 text-xs"><button type="button" onClick={() => setDirectory("")}>根目录</button><span className="min-w-0 break-all">/ {directory}</span>{directory && <button type="button" onClick={() => setDirectory(directory.split("/").slice(0, -1).join("/"))}>上一级</button>}<button type="button" disabled={loading || !!error || !tree} onClick={() => select(directory)} className="ml-auto text-blue-600">选择当前目录</button></div>
      <div className="max-h-40 overflow-auto rounded border p-2" aria-busy={loading}>
        {loading ? <p role="status">正在读取目录…</p> : error ? <p role="alert">{error}<button type="button" onClick={() => setRevision((r) => r + 1)} className="ml-2 text-blue-600">重试</button></p> : <>
          {tree?.directories.map((dir) => <button type="button" key={dir.path} className="block w-full truncate p-1 text-left" onClick={() => { try { setDirectory(normalizeArchitecturePath(dir.path)); } catch (reason) { setError((reason as Error).message); } }}>📁 {dir.name}</button>)}
          {tree?.files.filter((file) => /\.(slnx?|csproj|fsproj|vbproj|vcxproj|json)$/i.test(file.path)).map((file) => <button type="button" key={file.path} className="block w-full truncate p-1 text-left text-blue-700" onClick={() => select(file.path)}>选择 {file.name}</button>)}
        </>}
      </div>
    </>}
    <p className="break-all text-xs text-blue-700" aria-live="polite">{value ? `已选：${value.repository} / ${value.path || "（根目录）"}` : "使用当前对话与已选资料"}</p>
    {value && <button type="button" className="text-xs text-blue-600" onClick={() => onChange(null)}>清除代码范围</button>}
  </section>;
}
