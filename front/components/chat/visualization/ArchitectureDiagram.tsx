"use client";

import { Children, isValidElement, useId, useMemo, useRef, useState, type ReactNode } from "react";
import { architecturePath, parseArchitecture, relatedNodes, type Architecture } from "@/lib/chat-architecture";

export function architectureSourceFromPre(children: ReactNode): string | null {
  const child = Children.toArray(children)[0];
  if (!isValidElement(child)) return null;
  const props = child.props as { className?: string; children?: ReactNode };
  return props.className?.split(" ").includes("language-aiagent-architecture") && typeof props.children === "string" ? props.children : null;
}

function download(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob), link = document.createElement("a");
  link.href = url; link.download = name; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function ArchitectureDiagram({ source }: { source: string }) {
  const result = useMemo(() => {
    try { return { graph: parseArchitecture(source), error: "" }; }
    catch (error) { return { graph: null, error: error instanceof SyntaxError ? "图形数据尚未完整或 JSON 格式有误" : (error as Error).message }; }
  }, [source]);
  return result.graph ? <ArchitectureCanvas key={source} graph={result.graph} /> : <div className="my-3 rounded-xl border border-amber-200 p-3 text-sm">
    <p role="status">{result.error}。可等待输出完成，或请 AI 按原格式修正。</p>
    <details className="mt-2"><summary>查看图形源数据</summary><pre className="max-h-64 overflow-auto whitespace-pre-wrap">{source.slice(0, 100_000)}</pre></details>
  </div>;
}

function ArchitectureCanvas({ graph }: { graph: Architecture }) {
  const marker = useId().replace(/:/g, ""), svgRef = useRef<SVGSVGElement>(null);
  const [selected, setSelected] = useState("");
  const [mode, setMode] = useState<"detail" | "upstream" | "downstream" | "path">("detail");
  const [destination, setDestination] = useState("");
  const [zoom, setZoom] = useState(1), [expanded, setExpanded] = useState(false);
  const [exportError, setExportError] = useState("");
  const dialogRef = useRef<HTMLDialogElement>(null);
  const viewportRef = useRef<HTMLDivElement>(null);
  const [dark, setDark] = useState(true);
  const [search, setSearch] = useState("");
  const [offsets, setOffsets] = useState<Record<string, { x: number; y: number }>>({});
  const drag = useRef<{ id: string | null; x: number; y: number; left: number; top: number; moved: boolean } | null>(null);
  const suppressClick = useRef(false);
  const colors = ["#60a5fa", "#a78bfa", "#34d399", "#fbbf24", "#fb7185", "#22d3ee"];
  const background = dark ? "#0b1220" : "#f8fafc";
  const foreground = dark ? "#e2e8f0" : "#0f172a";
  // Group columns are deterministic, including cycles and isolated nodes.
  const groups = [...new Set(graph.nodes.map((node) => node.group || "未分组"))];
  const columns = groups.length === 1 ? Math.min(4, Math.ceil(Math.sqrt(graph.nodes.length))) : groups.length;
  const positions = new Map(graph.nodes.map((node, index) => {
    const group = node.group || "未分组";
    const col = groups.length === 1 ? index % columns : groups.indexOf(group);
    const row = groups.length === 1 ? Math.floor(index / columns) : graph.nodes.filter((n) => (n.group || "未分组") === group).findIndex((n) => n.id === node.id);
    return [node.id, offsets[node.id] ?? { x: 50 + col * 270, y: 75 + row * 140 }];
  }));
  const width = Math.max(columns * 270 + 50, ...[...positions.values()].map((p) => p.x + 280)), height = Math.max(...[...positions.values()].map((p) => p.y)) + 140;
  const path = selected && destination ? architecturePath(graph, selected, destination) : [];
  const highlighted = selected ? mode === "upstream" || mode === "downstream" ? relatedNodes(graph, selected, mode) : mode === "path" && destination ? new Set(path) : new Set([selected, ...graph.edges.filter((e) => e.from === selected || e.to === selected).flatMap((e) => [e.from, e.to])]) : null;
  const node = graph.nodes.find((item) => item.id === selected);
  const buttonClass = "rounded border border-slate-200 bg-white px-2 py-1 text-xs text-slate-700 disabled:opacity-40";
  function choose(id: string) {
    if (suppressClick.current) { suppressClick.current = false; return; }
    if (mode === "path" && selected && !destination) setDestination(id);
    else { setSelected(id); setDestination(""); }
  }
  async function exportImage(format: "svg" | "png") {
    setExportError("");
    try {
      if (!svgRef.current) return;
      const clone = svgRef.current.cloneNode(true) as SVGSVGElement;
      clone.setAttribute("width", String(width)); clone.setAttribute("height", String(height));
      const blob = new Blob([new XMLSerializer().serializeToString(clone)], { type: "image/svg+xml;charset=utf-8" });
      if (format === "svg") { download(blob, "architecture.svg"); return; }
      const url = URL.createObjectURL(blob);
      try {
        const image = new Image();
        await new Promise<void>((resolve, reject) => { image.onload = () => resolve(); image.onerror = () => reject(new Error("图片导出失败")); image.src = url; });
        const canvas = document.createElement("canvas");
        const scale = Math.min(2, 4096 / width, 4096 / height);
        canvas.width = width * scale; canvas.height = height * scale;
        const context = canvas.getContext("2d");
        if (!context) throw new Error("浏览器无法导出图片");
        context.drawImage(image, 0, 0, canvas.width, canvas.height);
        const png = await new Promise<Blob>((resolve, reject) => canvas.toBlob((value) => value ? resolve(value) : reject(new Error("图片导出失败")), "image/png"));
        download(png, "architecture.png");
      } finally { URL.revokeObjectURL(url); }
    } catch (error) { setExportError((error as Error).message); }
  }
  const content = <>
    <header className="flex flex-wrap items-center gap-2 border-b border-slate-200 p-3">
      <strong className="mr-auto text-sm">{graph.title}</strong>
      <span className="text-xs text-slate-500">{graph.nodes.length} 个节点 · {graph.edges.length} 条关系</span>
      <button type="button" className={buttonClass} onClick={() => setDark(!dark)}>{dark ? "浅色画布" : "深色画布"}</button>
      <button type="button" className={buttonClass} onClick={() => { setOffsets({}); setZoom(1); viewportRef.current?.scrollTo(0, 0); }}>重置布局</button>
      <button type="button" className={buttonClass} onClick={() => setZoom((z) => Math.max(0.25, z - 0.25))}>缩小</button>
      <button type="button" className={buttonClass} onClick={() => setZoom(1)}>{Math.round(zoom * 100)}% · 重置</button>
      <button type="button" className={buttonClass} onClick={() => setZoom((z) => Math.min(3, z + 0.25))}>放大</button>
      <button type="button" className={buttonClass} onClick={() => download(new Blob([JSON.stringify(graph, null, 2)], { type: "application/json" }), "architecture.json")}>JSON</button>
      <button type="button" className={buttonClass} onClick={() => void exportImage("svg")}>SVG</button>
      <button type="button" className={buttonClass} onClick={() => void exportImage("png")}>PNG</button>
      <button type="button" className={buttonClass} onClick={() => { if (expanded) { dialogRef.current?.close(); setExpanded(false); } else { setExpanded(true); dialogRef.current?.showModal(); } }}>{expanded ? "关闭大图" : "展开大图"}</button>
    </header>
    <div className="flex flex-wrap gap-2 p-3">
      {([['detail', '节点详情'], ['upstream', '上游'], ['downstream', '下游'], ['path', '路径追踪']] as const).map(([value, label]) => <button key={value} type="button" aria-pressed={mode === value} className={`${buttonClass} ${mode === value ? "ring-2 ring-blue-400" : ""}`} onClick={() => { setMode(value); setDestination(""); }}>{label}</button>)}
      <button type="button" className={buttonClass} onClick={() => { setSelected(""); setDestination(""); }}>清除选择</button>
      <input aria-label="搜索节点" placeholder="搜索节点名称或说明" value={search} onChange={(event) => setSearch(event.target.value)} className="min-w-0 rounded border border-slate-200 px-2 py-1 text-xs text-slate-900" />
    </div>
    {search.trim() && <div className="flex max-h-24 flex-wrap gap-2 overflow-auto px-3 pb-2" aria-label="搜索结果">{graph.nodes.filter((n) => `${n.label} ${n.description} ${n.group}`.toLowerCase().includes(search.trim().toLowerCase())).map((n) => <button key={n.id} type="button" className={buttonClass} onClick={() => { choose(n.id); const p = positions.get(n.id)!; viewportRef.current?.scrollTo({ left: Math.max(0, p.x * zoom - 100), top: Math.max(0, p.y * zoom - 80), behavior: "smooth" }); }}>{n.label}</button>)}{!graph.nodes.some((n) => `${n.label} ${n.description} ${n.group}`.toLowerCase().includes(search.trim().toLowerCase())) && <span className="text-xs text-slate-500">没有匹配的节点</span>}</div>}
    <p className="px-3 pb-2 text-xs text-slate-500" aria-live="polite">{mode === "path" ? destination ? path.length ? `路径：${path.map((id) => graph.nodes.find((n) => n.id === id)?.label).join(" → ")}` : "两个节点间没有有向路径" : selected ? "请选择终点" : "请选择起点，再选择终点" : "点击节点查看说明和来源；拖动节点调整布局，拖动画布浏览。"}</p>
    <div ref={viewportRef} style={{ background }} className={`overflow-auto ${expanded ? "max-h-[58dvh]" : "max-h-[460px]"}`}>
      <svg ref={svgRef} xmlns="http://www.w3.org/2000/svg" role="img" aria-label={graph.title} width={width * zoom} height={height * zoom} viewBox={`0 0 ${width} ${height}`} style={{ maxWidth: "none", fontFamily: "sans-serif", touchAction: "none", userSelect: "none", cursor: "grab" }}
        onPointerDown={(event) => {
          if (event.button !== 0 || !event.isPrimary) return;
          const id = (event.target as Element).closest("[data-node-id]")?.getAttribute("data-node-id") ?? null;
          const p = id ? positions.get(id)! : { x: viewportRef.current?.scrollLeft ?? 0, y: viewportRef.current?.scrollTop ?? 0 };
          suppressClick.current = false;
          drag.current = { id, x: event.clientX, y: event.clientY, left: p.x, top: p.y, moved: false };
        }}
        onPointerMove={(event) => {
          const current = drag.current;
          if (!current) return;
          const dx = event.clientX - current.x, dy = event.clientY - current.y;
          if (!current.moved && Math.hypot(dx, dy) < 5) return;
          current.moved = true; suppressClick.current = true;
          event.currentTarget.setPointerCapture(event.pointerId);
          if (current.id) setOffsets((previous) => ({ ...previous, [current.id!]: { x: Math.min(12000, Math.max(20, current.left + dx / zoom)), y: Math.min(12000, Math.max(55, current.top + dy / zoom)) } }));
          else viewportRef.current?.scrollTo(current.left - dx, current.top - dy);
        }}
        onPointerLeave={() => { if (!drag.current?.moved) drag.current = null; }}
        onPointerUp={() => { drag.current = null; }} onPointerCancel={() => { drag.current = null; }} onLostPointerCapture={() => { drag.current = null; }}>
        <title>{graph.title}</title><rect width={width} height={height} fill={background} />
        <defs><pattern id={`${marker}-grid`} width="24" height="24" patternUnits="userSpaceOnUse"><circle cx="1" cy="1" r="1" fill={dark ? "#243247" : "#cbd5e1"} /></pattern></defs>
        <rect width={width} height={height} fill={`url(#${marker}-grid)`} />
        <defs><marker id={marker} markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 Z" fill="#64748b" /></marker></defs>
        {groups.length > 1 && groups.map((group, i) => <g key={group}><rect x={35 + i * 270} y="14" width="220" height="32" rx="8" fill={dark ? "#172236" : "#e2e8f0"} /><text x={50 + i * 270} y={35} fontSize="14" fill={colors[i % colors.length]}>{group.slice(0, 22)}</text></g>)}
        {graph.edges.map((edge, i) => {
          const a = positions.get(edge.from)!, b = positions.get(edge.to)!;
          const active = !highlighted || (mode === "path" && destination ? path.some((id, j) => id === edge.from && path[j + 1] === edge.to) : highlighted.has(edge.from) && highlighted.has(edge.to));
          const self = edge.from === edge.to;
          return <g key={i} opacity={active ? 1 : 0.15}><path d={self ? `M${a.x + 175},${a.y + 20} C${a.x + 260},${a.y - 55} ${a.x + 260},${a.y + 115} ${a.x + 175},${a.y + 60}` : `M${a.x + 190},${a.y + 40} C${a.x + 240},${a.y + 40} ${b.x - 50},${b.y + 40} ${b.x},${b.y + 40}`} fill="none" stroke="#64748b" strokeWidth="2" markerEnd={`url(#${marker})`} />
            <text x={self ? a.x + 205 : (a.x + 190 + b.x) / 2} y={self ? a.y - 5 : (a.y + b.y) / 2 + 30} textAnchor="middle" fontSize="11" fill={foreground} stroke={background} strokeWidth="4" paintOrder="stroke">{edge.label.slice(0, 24)}<title>{edge.label}</title></text></g>;
        })}
        {graph.nodes.map((item) => { const p = positions.get(item.id)!; return <g key={item.id} data-node-id={item.id} role="button" tabIndex={0} aria-label={`节点：${item.label}`} aria-pressed={selected === item.id} onClick={() => choose(item.id)} onKeyDown={(event) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); choose(item.id); } }} style={{ cursor: "pointer" }} opacity={!highlighted || highlighted.has(item.id) ? 1 : 0.25}>
          <rect x={p.x} y={p.y} width="190" height="80" rx="12" fill={dark ? selected === item.id ? "#1e3a5f" : "#152238" : selected === item.id ? "#dbeafe" : "white"} stroke={colors[groups.indexOf(item.group || "未分组") % colors.length]} strokeWidth={selected === item.id ? "3" : "1.5"} />
          <text x={p.x + 95} y={p.y + 34} textAnchor="middle" fontSize="14" fill={foreground}>{item.label.slice(0, 12)}</text>
          <text x={p.x + 95} y={p.y + 58} textAnchor="middle" fontSize="11" fill={dark ? "#94a3b8" : "#64748b"}>{item.group.slice(0, 18) || item.id.slice(0, 18)}</text><title>{item.label}</title>
        </g>; })}
      </svg>
    </div>
    {node && <aside className="max-h-48 space-y-1 overflow-auto border-t border-slate-200 p-3 text-sm"><strong>{node.label}</strong><p className="whitespace-pre-wrap break-words">{node.description || "暂无说明"}</p><p className="whitespace-pre-wrap break-words text-xs text-slate-500">来源（模型标注）：{node.source || "未提供"}</p><ul className="text-xs">{graph.edges.filter((edge) => edge.from === selected || edge.to === selected).map((edge, i) => <li key={i}>{graph.nodes.find((n) => n.id === edge.from)?.label} → {graph.nodes.find((n) => n.id === edge.to)?.label}：{edge.label}</li>)}</ul></aside>}
    {exportError && <p role="alert" className="p-3 text-sm text-red-600">{exportError}</p>}
  </>;
  return <section className="my-3 overflow-hidden rounded-xl border border-slate-200 bg-white text-slate-900">
    {!expanded && content}
    <dialog ref={dialogRef} aria-label={graph.title} onCancel={() => setExpanded(false)} onClose={() => setExpanded(false)} className="m-auto max-h-[94dvh] w-[95vw] max-w-none overflow-auto rounded-xl bg-white p-0 backdrop:bg-slate-900/40">{expanded && content}</dialog>
  </section>;
}
