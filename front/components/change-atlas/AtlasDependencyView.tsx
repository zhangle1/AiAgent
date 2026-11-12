"use client";

import { useEffect, useMemo, useState } from 'react';
import { readAtlasImports } from '@/lib/change-atlas-api';
import { selectImportNeighborhood } from '@/lib/atlas-imports';
import type { AtlasSnapshot, AtlasImportGraph, AtlasImportEdge, AtlasCommit } from '@/lib/change-atlas-types';

export function AtlasDependencyView({ projectId, snapshot, commits }: { projectId: number; snapshot: AtlasSnapshot; commits: AtlasCommit[] }) {
  const [repository, setRepository] = useState(snapshot.repositories[0]?.id || 0);
  const [graph, setGraph] = useState<AtlasImportGraph | null>(null);
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  const [seed, setSeed] = useState(''), [search, setSearch] = useState('');
  const [reverse, setReverse] = useState(false), [depth, setDepth] = useState(1);
  const [edge, setEdge] = useState<AtlasImportEdge | null>(null);
  const [reload, setReload] = useState(0);
  const [enabled, setEnabled] = useState(false);
  useEffect(() => {
    if (!enabled || !repository) return;
    const controller = new AbortController(); let disposed = false;
    setGraph(null); setError(''); setBusy(true); setEdge(null);
    readAtlasImports(projectId, snapshot.id, repository, controller.signal).then(value => {
      if (disposed) return;
      setGraph(value); setSeed(value.edges[0]?.source || value.files[0]?.id || '');
    }).catch(reason => { if (!disposed) setError(reason instanceof Error ? reason.message : '关系读取失败'); }).finally(() => { if (!disposed) setBusy(false); });
    return () => { disposed = true; controller.abort(); };
  }, [projectId, snapshot.id, repository, reload, enabled]);
  useEffect(() => { setEdge(null); }, [seed, reverse, depth]);
  const active = useMemo(() => new Set(commits.flatMap(commit => commit.files.map(file => file.file_id))), [commits]);
  const local = useMemo(() => graph ? selectImportNeighborhood(graph, seed, reverse, depth) : null, [graph, seed, reverse, depth]);
  const files = new Map(graph?.files.map(file => [file.id, file]));
  const field = 'rounded border border-slate-200 bg-white p-2 text-xs';
  if (!enabled) return <section className="rounded-xl border border-slate-200 p-4"><button type="button" className="text-sm text-blue-700" onClick={() => setEnabled(true)}>打开依赖探索 / 候选影响分析</button><p className="mt-2 text-xs text-slate-500">按需读取固定版本 JS/TS 相对导入，保留原地图操作。</p></section>;
  return <section aria-label="文件依赖探索" className="space-y-4 rounded-xl border border-slate-200 bg-white p-4">
    <header><h2 className="text-sm font-semibold">依赖探索 · 候选影响分析</h2><p className="mt-2 text-xs text-slate-500">从文件出发核查导入关系；箭头始终为导入方 → 被导入方。日期与人员仅改变活动标记。</p></header>
    <button type="button" className={field} onClick={() => { setEnabled(false); setGraph(null); }}>关闭并取消读取</button>
    <div className="flex flex-wrap gap-2"><select aria-label="依赖代码库" className={field} value={repository} onChange={e => setRepository(Number(e.target.value))}>{snapshot.repositories.map(repo => <option key={repo.id} value={repo.id}>{repo.name}</option>)}</select><button className={field} onClick={() => setReverse(!reverse)}>{reverse ? '影响分析：反向查使用方' : '依赖探索：查看导入目标'}</button><select aria-label="关系跳数" className={field} value={depth} onChange={e => setDepth(Number(e.target.value))}><option value={1}>1 跳</option><option value={2}>2 跳</option></select><button className={field} onClick={() => setReload(x => x + 1)}>重新读取</button></div>
    {busy && <p role="status" className="text-xs">正在读取固定版本导入候选… 切换页面将取消读取。</p>}{error && <p role="alert" className="text-xs text-red-700">{error}</p>}
    {graph && <><p className="break-all font-mono text-xs text-slate-500">{graph.head_sha} · 已扫描 {graph.scanned} · 跳过/未覆盖 {graph.skipped} · 未解析相对导入 {graph.unresolved}</p><details className="text-xs leading-6 text-amber-800"><summary>覆盖范围与未知项（词法候选）</summary>{graph.warnings.map(w => <p key={w}>{w}</p>)}</details>
    <div className="grid min-w-0 gap-3 lg:grid-cols-[220px_minmax(0,1fr)]"><div className="min-w-0"><input aria-label="搜索依赖文件" placeholder="搜索真实相对路径" className={`${field} w-full`} value={search} onChange={e => setSearch(e.target.value)}/><div className="mt-2 max-h-96 overflow-auto">{graph.files.filter(f => f.path.toLowerCase().includes(search.toLowerCase())).slice(0, 300).map(f => <button key={f.id} aria-pressed={seed === f.id} onClick={() => setSeed(f.id)} className={`block w-full break-all rounded p-2 text-left text-xs ${seed === f.id ? 'bg-blue-50 text-blue-700' : 'hover:bg-slate-50'}`}>{active.has(f.id) ? '● ' : ''}{f.path}</button>)}<p className="p-2 text-xs text-slate-500">列表最多 300 项，请搜索缩小范围。</p></div></div>
    <div className="min-w-0 space-y-3"><div className="max-h-[460px] overflow-auto rounded border border-slate-200"><svg role="img" aria-label="局部导入关系图" width="760" height={Math.max(160, (local?.files.length || 0) * 56)} viewBox={`0 0 760 ${Math.max(160, (local?.files.length || 0) * 56)}`}><defs><marker id="imports-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse"><path d="M 0 0 L 10 5 L 0 10 z" fill="#94a3b8"/></marker></defs>{local?.edges.map(e => { const from = local.files.findIndex(f => f.id === e.source), to = local.files.findIndex(f => f.id === e.target); return <path key={e.id} d={`M 590 ${from * 56 + 28} C 740 ${from * 56 + 28},740 ${to * 56 + 28},590 ${to * 56 + 28}`} fill="none" stroke={edge?.id === e.id ? '#2563eb' : '#94a3b8'} strokeWidth="3" markerEnd="url(#imports-arrow)" onClick={() => setEdge(e)} className="cursor-pointer"><title>{files.get(e.source)?.path} → {files.get(e.target)?.path}，点击查看证据</title></path>; })}{local?.files.map((f, i) => <g key={f.id} onClick={() => setSeed(f.id)} className="cursor-pointer"><rect x="12" y={i * 56 + 8} width="578" height="40" rx="6" fill={f.id === seed ? '#eff6ff' : '#f8fafc'} stroke={active.has(f.id) ? '#2563eb' : '#cbd5e1'}/><text x="24" y={i * 56 + 33} fontSize="11" fill="#334155">{f.path.length > 80 ? '…' + f.path.slice(-79) : f.path}</text><title>{f.path}</title></g>)}</svg></div>
    {local?.truncated && <p className="text-xs text-amber-700">局部关系已截断（60 节点 / 120 边），请减少跳数或更换起点。</p>}
    {!local?.edges.length && <p className="text-xs text-slate-500">未找到已覆盖的关系，不能据此判断没有依赖。删除文件需要基线图。</p>}
    <div className="max-h-48 overflow-auto">{local?.edges.map(e => <button key={e.id} className="block w-full break-all border-b p-2 text-left text-xs text-blue-700" onClick={() => setEdge(e)}>{files.get(e.source)?.path} → {files.get(e.target)?.path} · L{e.line}</button>)}</div>
    {edge && <aside aria-label="导入关系证据" className="rounded bg-slate-50 p-3 text-xs leading-6"><p className="break-all">{files.get(edge.source)?.path} · L{edge.line} · 词法候选</p><pre className="overflow-auto whitespace-pre-wrap break-all">{edge.text}</pre><p>核查清单：确认导入可解析、使用方行为与相关测试。候选路径不代表实际运行影响。</p></aside>}</div></div></>}
  </section>;
}
