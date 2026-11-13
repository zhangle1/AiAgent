"use client";
import { useEffect, useRef, useState } from "react";
import { ontologyApi } from "@/lib/ontology-api";
import type { OntologyGraph, OntologyModel, OntologyPlugin } from "@/lib/ontology-types";

export function OntologyGraphEditor({ graph, plugins, onChange }: { graph: OntologyGraph; plugins: OntologyPlugin[]; onChange: (g: OntologyGraph) => void }) {
  const [selected, setSelected] = useState("");
  const [source, setSource] = useState("");
  const [target, setTarget] = useState("");
  const node = graph.nodes.find(n => n.id === selected);
  const positions = new Map(graph.nodes.map((n, i) => [n.id, { x: 30 + (i % 3) * 215, y: 35 + Math.floor(i / 3) * 125 }]));
  return <section className="space-y-3">
    <div className="flex flex-wrap gap-2">{plugins.map(p => <button className="rounded border px-3 py-1" key={p.id} onClick={() => { const id = crypto.randomUUID(); onChange({ ...graph, nodes: [...graph.nodes, { id, kind: p.id, label: p.label }] }); setSelected(id); }}>＋ {p.label}</button>)}</div>
    <div className="max-h-80 overflow-auto rounded-xl border bg-slate-50"><svg aria-label="ETL 流程画布" width="680" height={Math.max(230, Math.ceil(graph.nodes.length / 3) * 125 + 20)}>
      <defs><marker id="ontology-arrow" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8" fill="#64748b"/></marker></defs>
      {graph.edges.map(e => { const a = positions.get(e.source), b = positions.get(e.target); if (!a || !b) return null; return <path key={`${e.source}-${e.target}`} d={`M${a.x + 175},${a.y + 35} C${a.x + 210},${a.y + 35} ${b.x - 35},${b.y + 35} ${b.x},${b.y + 35}`} fill="none" stroke="#64748b" strokeWidth="2" markerEnd="url(#ontology-arrow)"/>; })}
      {graph.nodes.map(n => { const p = positions.get(n.id)!; return <g key={n.id} transform={`translate(${p.x},${p.y})`} role="button" tabIndex={0} aria-label={n.label} onClick={() => setSelected(n.id)} onKeyDown={e => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); setSelected(n.id); } }} className="cursor-pointer"><rect width="175" height="70" rx="12" fill="white" stroke={selected === n.id ? "#2563eb" : "#cbd5e1"} strokeWidth="2"/><text x="12" y="25" fontSize="11" fill="#64748b">{plugins.find(p => p.id === n.kind)?.label ?? n.kind}</text><text x="12" y="50" fontSize="14">{n.label.slice(0, 12)}</text></g>; })}
    </svg></div>
    {node && <div className="flex gap-2"><input aria-label="节点名称" maxLength={100} value={node.label} onChange={e => onChange({ ...graph, nodes: graph.nodes.map(n => n.id === node.id ? { ...n, label: e.target.value } : n) })} className="min-w-0 flex-1 rounded border p-2"/><button onClick={() => { onChange({ nodes: graph.nodes.filter(n => n.id !== node.id), edges: graph.edges.filter(e => e.source !== node.id && e.target !== node.id) }); setSelected(""); }}>删除节点</button></div>}
    <div className="flex flex-wrap gap-2">{([source, target] as const).map((value, i) => <select aria-label={i ? "目标节点" : "来源节点"} key={i} value={value} onChange={e => (i ? setTarget : setSource)(e.target.value)} className="max-w-48 rounded border p-2"><option value="">{i ? "目标节点" : "来源节点"}</option>{graph.nodes.map(n => <option key={n.id} value={n.id}>{n.label}</option>)}</select>)}<button disabled={!source || !target || source === target || graph.edges.some(e => e.source === source && e.target === target)} onClick={() => onChange({ ...graph, edges: [...graph.edges, { source, target }] })}>连接节点</button></div>
    {graph.edges.map(e => <div className="flex items-center gap-2 text-xs" key={`${e.source}-${e.target}`}><span>{graph.nodes.find(n => n.id === e.source)?.label} → {graph.nodes.find(n => n.id === e.target)?.label}</span><button aria-label="删除连线" onClick={() => onChange({ ...graph, edges: graph.edges.filter(x => x !== e) })}>删除</button></div>)}
  </section>;
}

export function OntologyWorkbench({ projectId, onClose, onInsert }: { projectId: number | null; onClose: () => void; onInsert: (text: string) => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const alive = useRef(true);
  const sequence = useRef(0);
  const [models, setModels] = useState<{ id: string; name: string }[]>([]);
  const [plugins, setPlugins] = useState<OntologyPlugin[]>([]);
  const [model, setModel] = useState<OntologyModel | null>(null);
  const [name, setName] = useState("");
  const [password, setPassword] = useState("");
  const [preview, setPreview] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [dirty, setDirty] = useState(false);
  useEffect(() => { alive.current = true; const element = dialog.current!; const focus = document.activeElement as HTMLElement | null; element.showModal(); return () => { alive.current = false; sequence.current++; element.close(); focus?.focus(); }; }, []);
  async function run(task: () => Promise<void>) { setBusy(true); setError(""); try { await task(); } catch (e) { if (alive.current) setError(e instanceof Error ? e.message : "操作失败"); } finally { if (alive.current) setBusy(false); } }
  useEffect(() => { if (!projectId) return; void run(async () => { const [items, catalog] = await Promise.all([ontologyApi.list(projectId), ontologyApi.plugins(projectId)]); if (alive.current) { setModels(items); setPlugins(catalog); } }); }, [projectId]);
  async function load(id: string) { const current = ++sequence.current; setPassword(""); setPreview(""); setModel(null); setDirty(false); if (!id || !projectId) return; await run(async () => { const next = await ontologyApi.get(projectId, id); if (alive.current && current === sequence.current) setModel(next); }); }
  function edit(next: OntologyModel) { setModel(next); setPreview(""); setDirty(true); }
  return <dialog ref={dialog} onCancel={onClose} className="m-auto max-h-[90dvh] w-[min(1000px,95vw)] overflow-y-auto rounded-2xl border bg-white p-5 text-slate-800 shadow-xl backdrop:bg-slate-950/40">
    <header className="mb-4 flex items-center justify-between"><h2 className="text-lg font-semibold">本体模型 · 项目 {projectId ?? "未选择"}</h2><button aria-label="关闭本体模型" onClick={onClose}>关闭</button></header>
    {!projectId ? <p>请先选择项目。</p> : <div className="space-y-4">
      <p className="text-xs text-slate-500">配置 MES 连接并编排流程。当前保存流程定义，尚不执行 ETL，也不验证数据库连通。</p>
      <div className="flex flex-wrap gap-2"><select disabled={busy} aria-label="企业模型" value={model?.id ?? ""} onChange={e => void load(e.target.value)} className="rounded border p-2"><option value="">选择企业模型</option>{models.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}</select><input aria-label="新模型名称" value={name} maxLength={100} onChange={e => setName(e.target.value)} placeholder="企业模型名称" className="rounded border p-2"/><button disabled={busy || !name.trim()} onClick={() => void run(async () => { const next = await ontologyApi.create(projectId, name); const items = await ontologyApi.list(projectId); if (alive.current) { setModels(items); setModel(next); setPassword(""); setPreview(""); setDirty(false); setName(""); } })}>新建模型</button><button disabled={busy || !model} onClick={() => model && void load(model.id)}>重新加载（丢弃修改）</button></div>
      {model && <><div className="grid gap-5 md:grid-cols-[260px_1fr]"><fieldset disabled={busy} className="space-y-3"><legend className="mb-2 font-medium">MES 数据库配置</legend><label className="block text-xs">数据库类型<select value={model.connection.type} onChange={e => edit({ ...model, connection: { ...model.connection, type: e.target.value } })} className="mt-1 block w-full rounded border p-2">{["sqlserver", "mysql", "postgresql", "oracle"].map(t => <option key={t}>{t}</option>)}</select></label>{([['host','数据库地址'],['port','端口'],['database','数据库名'],['username','账号']] as const).map(([key,label]) => <label key={key} className="block text-xs">{label}<input value={model.connection[key]} type={key === 'port' ? 'number' : 'text'} maxLength={256} onChange={e => edit({ ...model, connection: { ...model.connection, [key]: key === 'port' ? Number(e.target.value) : e.target.value } })} className="mt-1 w-full rounded border p-2"/></label>)}<label className="block text-xs">密码{model.has_password ? '（已保存，留空保留）' : ''}<input autoComplete="new-password" type="password" value={password} maxLength={4096} onChange={e => { setPassword(e.target.value); setPreview(""); setDirty(true); }} className="mt-1 w-full rounded border p-2"/></label><p className="text-xs text-slate-500">加密保存在服务端；不会填入聊天。</p></fieldset><fieldset disabled={busy} className="min-w-0"><legend className="mb-3 font-medium">ETL 流程定义 · v{model.version}</legend><OntologyGraphEditor key={model.id} graph={model.graph} plugins={plugins} onChange={graph => edit({ ...model, graph })}/></fieldset></div>
      <div className="flex flex-wrap gap-4"><button disabled={busy} className="rounded bg-blue-600 px-4 py-2 text-white disabled:opacity-40" onClick={() => void run(async () => { const saved = await ontologyApi.connection(projectId, model, model.connection, password); if (alive.current) { setModel(saved); setPassword(""); setDirty(false); setPreview(""); } })}>保存配置与流程</button><button disabled={busy || dirty} onClick={() => void run(async () => { const context = await ontologyApi.context(projectId, model.id); if (alive.current) setPreview(context.text); })}>获取聊天上下文</button></div>{dirty && <p className="text-xs text-amber-700">有未保存修改；获取上下文前请先保存。</p>}
      <details className="rounded border p-3"><summary>LLM API 使用说明</summary><p className="mt-2 break-all text-xs">通过宿主已认证 HTTP 调用 GET /api/v1/ontology/{projectId}/plugins 和 /models/{model.id}；以 graph、version 调用 PUT /models/{model.id}/graph 组装流程。GET /models/{model.id}/context 获取脱敏上下文。修改后点击重新加载。</p></details>
      {preview && <div className="space-y-2"><textarea aria-label="上下文预览" readOnly value={preview} className="h-40 w-full rounded border p-3 text-xs"/><button onClick={() => { onInsert(preview); onClose(); }} className="rounded bg-blue-600 px-4 py-2 text-white">填入聊天草稿</button></div>}</>}
      {error && <p role="alert" className="text-sm text-red-600">{error}</p>}{busy && <p role="status" className="text-xs">正在处理…</p>}
    </div>}
  </dialog>;
}
