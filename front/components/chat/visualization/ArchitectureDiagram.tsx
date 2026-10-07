"use client";

import { Children, isValidElement, useEffect, useId, useMemo, useRef, useState, type ReactNode } from "react";
import { architectureHtml } from "@/lib/architecture-export";
import { architecturePath, architectureTypes, parseArchitecture, relatedNodes, type Architecture } from "@/lib/chat-architecture";
import { diagramRoutes, fitDiagram, layoutArchitecture, NODE_WIDTH, NODE_HEIGHT, wrapDiagramText } from "@/lib/architecture-layout";

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
  const [dark, setDark] = useState(false);
  const [autoFit, setAutoFit] = useState(true);
  const [search, setSearch] = useState("");
  const [offsets, setOffsets] = useState<Record<string, { x: number; y: number }>>({});
  const drag = useRef<{ id: string | null; x: number; y: number; left: number; top: number; moved: boolean } | null>(null);
  const suppressClick = useRef(false);
  const colors = dark ? ["#60a5fa", "#a78bfa", "#34d399", "#fbbf24", "#fb7185", "#22d3ee"] : ["#0284c7", "#8b5cf6", "#059669", "#d97706", "#e0527c", "#0891b2"];
  const background = dark ? "#0b1220" : "#f6fbfc";
  const foreground = dark ? "#e2e8f0" : "#0f172a";
  const groups = [...new Set(graph.nodes.map((node) => node.group || "未分组"))];
  const layout = useMemo(() => layoutArchitecture(graph, offsets), [graph, offsets]);
  const { positions, width, height } = layout;
  const routes = useMemo(() => diagramRoutes(graph, layout), [graph, layout]);
  const type = graph.diagramType ?? "architecture";
  useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport || !autoFit) return;
    const fit = () => { setZoom(fitDiagram(width, height, viewport.clientWidth, viewport.clientHeight)); viewport.scrollTo(0, 0); };
    const observer = new ResizeObserver(fit); observer.observe(viewport); fit();
    return () => observer.disconnect();
  }, [width, height, expanded, autoFit]);
  const path = selected && destination ? architecturePath(graph, selected, destination) : [];
  const highlighted = selected ? mode === "upstream" || mode === "downstream" ? relatedNodes(graph, selected, mode) : mode === "path" && destination ? new Set(path) : new Set([selected, ...graph.edges.filter((e) => e.from === selected || e.to === selected).flatMap((e) => [e.from, e.to])]) : null;
  const node = graph.nodes.find((item) => item.id === selected);
  const buttonClass = "rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-xs text-slate-600 transition hover:border-cyan-300 hover:bg-cyan-50 focus-visible:outline-cyan-500 disabled:opacity-40";
  function choose(id: string) {
    if (suppressClick.current) { suppressClick.current = false; return; }
    if (mode === "path" && selected && !destination) setDestination(id);
    else { setSelected(id); setDestination(""); }
  }
  async function exportImage(format: "svg" | "png" | "html") {
    setExportError("");
    try {
      if (!svgRef.current) return;
      const clone = svgRef.current.cloneNode(true) as SVGSVGElement;
      clone.setAttribute("width", String(width)); clone.setAttribute("height", String(height));
      clone.style.cursor = "default";
      // Export the whole graph even while the reader is tracing a subset.
      clone.querySelectorAll("[data-node-id], [data-edge-index]").forEach((element) => {
        element.setAttribute("opacity", "1");
        element.removeAttribute("tabindex");
        element.removeAttribute("role");
        element.removeAttribute("aria-pressed");
      });
      if (format === "html") {
        const html = architectureHtml(graph, new XMLSerializer().serializeToString(clone), dark);
        download(new Blob([html], { type: "text/html;charset=utf-8" }), "diagram.html"); return;
      }
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
    <header className="flex flex-wrap items-center gap-2 border-b border-cyan-100 bg-gradient-to-r from-cyan-50/60 to-white p-4">
      <div className="mr-auto min-w-0"><div className="mb-1 text-[10px] font-semibold tracking-[0.16em] text-cyan-600">{architectureTypes[type]} / {graph.nodes.length} 个节点 · {graph.edges.length} 条关系</div><strong className="text-base font-semibold">{graph.title}</strong></div>
      <button type="button" className={buttonClass} onClick={() => setDark(!dark)}>{dark ? "浅色画布" : "深色画布"}</button>
      <button type="button" className={buttonClass} onClick={() => { setOffsets({}); setAutoFit(true); }}>重置布局</button>
      <button type="button" className={buttonClass} onClick={() => { setAutoFit(true); const v = viewportRef.current; if (v) { setZoom(fitDiagram(width, height, v.clientWidth, v.clientHeight)); v.scrollTo(0, 0); } }}>适配窗口</button>
      <button type="button" className={buttonClass} onClick={() => { setAutoFit(false); setZoom((z) => Math.max(0.03, z - 0.15)); }}>缩小</button>
      <button type="button" className={buttonClass} onClick={() => { setAutoFit(false); setZoom(1); }}>{Math.round(zoom * 100)}% · 重置</button>
      <button type="button" className={buttonClass} onClick={() => { setAutoFit(false); setZoom((z) => Math.min(3, z + 0.25)); }}>放大</button>
      <button type="button" className={buttonClass} onClick={() => download(new Blob([JSON.stringify(graph, null, 2)], { type: "application/json" }), "architecture.json")}>JSON</button>
      <button type="button" className={buttonClass} onClick={() => void exportImage("svg")}>SVG</button>
      <button type="button" className={buttonClass} onClick={() => void exportImage("png")}>PNG</button>
      <button type="button" title="下载可独立打开的静态图形网页" className={buttonClass} onClick={() => void exportImage("html")}>HTML</button>
      <button type="button" className={buttonClass} onClick={() => { if (expanded) { dialogRef.current?.close(); setExpanded(false); } else { setExpanded(true); dialogRef.current?.showModal(); } }}>{expanded ? "关闭大图" : "展开大图"}</button>
    </header>
    <div className="flex flex-wrap gap-2 p-3">
      {([['detail', '节点详情'], ['upstream', '上游'], ['downstream', '下游'], ['path', '路径追踪']] as const).map(([value, label]) => <button key={value} type="button" aria-pressed={mode === value} className={`${buttonClass} ${mode === value ? "ring-2 ring-blue-400" : ""}`} onClick={() => { setMode(value); setDestination(""); }}>{label}</button>)}
      <button type="button" className={buttonClass} onClick={() => { setSelected(""); setDestination(""); }}>清除选择</button>
      <input aria-label="搜索节点" placeholder="搜索节点名称或说明" value={search} onChange={(event) => setSearch(event.target.value)} className="min-w-0 rounded border border-slate-200 px-2 py-1 text-xs text-slate-900" />
    </div>
    {search.trim() && <div className="flex max-h-24 flex-wrap gap-2 overflow-auto px-3 pb-2" aria-label="搜索结果">{graph.nodes.filter((n) => `${n.label} ${n.description} ${n.group}`.toLowerCase().includes(search.trim().toLowerCase())).map((n) => <button key={n.id} type="button" className={buttonClass} onClick={() => { choose(n.id); const p = positions.get(n.id)!; viewportRef.current?.scrollTo({ left: Math.max(0, p.x * zoom - 100), top: Math.max(0, p.y * zoom - 80), behavior: "smooth" }); }}>{n.label}</button>)}{!graph.nodes.some((n) => `${n.label} ${n.description} ${n.group}`.toLowerCase().includes(search.trim().toLowerCase())) && <span className="text-xs text-slate-500">没有匹配的节点</span>}</div>}
    <p className="px-3 pb-2 text-xs text-slate-500" aria-live="polite">{mode === "path" ? destination ? path.length ? `路径：${path.map((id) => graph.nodes.find((n) => n.id === id)?.label).join(" → ")}` : "两个节点间没有有向路径" : selected ? "请选择终点" : "请选择起点，再选择终点" : "点击节点查看说明和来源；拖动节点调整布局，拖动画布浏览。"}</p>
    <div ref={viewportRef} style={{ background }} className={`relative overflow-auto ${expanded ? "h-[58dvh]" : "h-[440px]"}`}>
      <svg ref={svgRef} xmlns="http://www.w3.org/2000/svg" role="img" aria-label={graph.title} width={width * zoom} height={height * zoom} viewBox={`0 0 ${width} ${height}`} style={{ display: "block", margin: "16px auto", flexShrink: 0, maxWidth: "none", fontFamily: "system-ui, sans-serif", touchAction: "none", userSelect: "none", cursor: "grab" }}
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
          if (current.id) { setAutoFit(false); setOffsets((previous) => ({ ...previous, [current.id!]: { x: Math.min(12000, Math.max(32, current.left + dx / zoom)), y: Math.min(12000, Math.max(56, current.top + dy / zoom)) } })); }
          else viewportRef.current?.scrollTo(current.left - dx, current.top - dy);
        }}
        onPointerLeave={() => { if (!drag.current?.moved) drag.current = null; }}
        onPointerUp={() => { drag.current = null; }} onPointerCancel={() => { drag.current = null; }} onLostPointerCapture={() => { drag.current = null; }}>
        <title>{graph.title}</title><rect width={width} height={height} fill={background} />
        <defs><pattern id={`${marker}-grid`} width="24" height="24" patternUnits="userSpaceOnUse"><path d="M24 0H0V24" fill="none" stroke={dark ? "#19283d" : "#e8f1f4"} strokeWidth="0.6" /></pattern></defs>
        <rect width={width} height={height} fill={`url(#${marker}-grid)`} />
        <defs>{colors.map((color, i) => <marker key={color} id={`${marker}-${i}`} markerWidth="7" markerHeight="7" refX="6" refY="3.5" orient="auto"><path d="M0,0 L7,3.5 L0,7 Z" fill={color} /></marker>)}</defs>
        {layout.boundaries.map((boundary, i) => <g key={boundary.label} data-boundary={boundary.label}>
          <rect x={boundary.x} y={boundary.y} width={boundary.width} height={boundary.height} rx="12" fill={colors[i % colors.length]} fillOpacity={dark ? ".06" : ".035"} stroke={colors[i % colors.length]} strokeOpacity=".45" strokeDasharray="6 4" />
          <text x={boundary.x + 18} y={boundary.y + 27} fontSize="12" fontWeight="600" fill={colors[i % colors.length]}>{boundary.label.slice(0, 30)}<title>{boundary.label}</title></text>
        </g>)}
        {type === "sequence" && graph.nodes.map((item) => { const p = positions.get(item.id)!; return <line key={item.id} data-lifeline={item.id} x1={p.x + NODE_WIDTH / 2} x2={p.x + NODE_WIDTH / 2} y1={p.y + NODE_HEIGHT} y2={height - 40} stroke={dark ? "#475569" : "#b1c4d0"} strokeDasharray="5 6" />; })}
        {graph.edges.map((edge, i) => {
          const active = !highlighted || (mode === "path" && destination ? path.some((id, j) => id === edge.from && path[j + 1] === edge.to) : highlighted.has(edge.from) && highlighted.has(edge.to));
          const route = routes[i], group = graph.nodes.find((n) => n.id === edge.from)!.group || "未分组";
          const colorIndex = groups.indexOf(group) % colors.length;
          const label = `${type === "sequence" ? `${i + 1}. ` : ""}${edge.label}`;
          const lines = wrapDiagramText(label, 26), visible = lines.slice(0, 2);
          return <g key={i} data-edge-index={i} opacity={active ? 1 : 0.12}>
            <path d={route.d} fill="none" stroke={colors[colorIndex]} strokeOpacity={selected ? 1 : .65} strokeWidth={selected && active ? 2 : 1.4} strokeLinejoin="round" strokeDasharray={edge.style === "dashed" ? "5 4" : undefined} markerEnd={`url(#${marker}-${colorIndex})`} />
            {label && <text x={route.label.x} y={route.label.y} textAnchor="middle" fontSize="10" fontWeight="500" fill={dark ? "#bdcedb" : "#527184"} stroke={background} strokeWidth="5" paintOrder="stroke" strokeLinejoin="round">{visible.map((line, j) => <tspan key={j} x={route.label.x} dy={j ? 13 : 0}>{line}{j === 1 && lines.length > 2 ? "…" : ""}</tspan>)}<title>{label}</title></text>}
          </g>;
        })}
        {graph.nodes.map((item, index) => { const p = positions.get(item.id)!, color = colors[groups.indexOf(item.group || "未分组") % colors.length];
          const lines = wrapDiagramText(item.label, item.kind === "decision" ? 16 : 22);
          const fill = dark ? selected === item.id ? "#1e3a5f" : "#152238" : selected === item.id ? "#e0f2fe" : "#ffffff";
          return <g key={item.id} data-node-id={item.id} role="button" tabIndex={0} aria-label={`节点：${item.label}`} aria-pressed={selected === item.id} onClick={() => choose(item.id)} onKeyDown={(event) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); choose(item.id); } }} style={{ cursor: "pointer" }} opacity={!highlighted || highlighted.has(item.id) ? 1 : 0.25}>
          {item.kind === "decision" ? <path d={`M${p.x + NODE_WIDTH / 2},${p.y} L${p.x + NODE_WIDTH},${p.y + 40} L${p.x + NODE_WIDTH / 2},${p.y + 80} L${p.x},${p.y + 40} Z`} fill={fill} stroke={color} strokeWidth={selected === item.id ? 2.5 : 1.3} /> : <>
            <rect x={p.x} y={p.y + 3} width={NODE_WIDTH} height={NODE_HEIGHT} rx={type === "lifecycle" || item.kind === "start" || item.kind === "end" ? 32 : 8} fill={dark ? "#000000" : "#29485d"} opacity=".06" />
            <rect x={p.x} y={p.y} width={NODE_WIDTH} height={NODE_HEIGHT} rx={type === "lifecycle" || item.kind === "start" || item.kind === "end" ? 32 : 8} fill={fill} stroke={color} strokeOpacity={selected === item.id ? 1 : .6} strokeWidth={selected === item.id ? 2.5 : 1.2} />
            {item.kind === "store" && <path d={`M${p.x},${p.y + 13} Q${p.x + 92},${p.y + 32} ${p.x + 184},${p.y + 13} M${p.x},${p.y + 65} Q${p.x + 92},${p.y + 84} ${p.x + 184},${p.y + 65}`} fill="none" stroke={color} strokeOpacity=".5" />}
            <circle cx={p.x + 14} cy={p.y + 14} r={item.kind === "end" ? 5 : 3} fill={item.kind === "end" ? fill : color} stroke={color} strokeWidth="1.5" />
          </>}
          <text x={p.x + NODE_WIDTH / 2} y={p.y + (lines.length > 1 ? 30 : 36)} textAnchor="middle" fontSize="12" fontWeight="600" fill={foreground}>{lines.slice(0, 2).map((line, i) => <tspan key={i} x={p.x + NODE_WIDTH / 2} dy={i ? 16 : 0}>{line}{i === 1 && lines.length > 2 ? "…" : ""}</tspan>)}</text>
          {item.kind !== "decision" && <text x={p.x + NODE_WIDTH / 2} y={p.y + 65} textAnchor="middle" fontSize="9" letterSpacing=".4" fill={dark ? "#94a3b8" : "#8193a0"}>{type === "workflow" ? `${String(index + 1).padStart(2, "0")} · ` : ""}{item.group.slice(0, 18) || item.id.slice(0, 18)}</text>}<title>{item.label}</title>
        </g>; })}
      </svg>
    </div>
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 border-t border-cyan-100 bg-slate-50/70 px-4 py-2 text-[10px] text-slate-500"><span className="uppercase tracking-widest">图例</span>{groups.map((group, i) => <span key={group} className="inline-flex items-center gap-1.5"><i className="h-2 w-2 rounded-sm border" style={{ borderColor: colors[i % colors.length], background: `${colors[i % colors.length]}18` }} />{group}</span>)}<span className="ml-auto">{type === "sequence" ? "从上到下为消息顺序 · 虚线表示返回消息" : "箭头表示有向关系"}</span></div>
    {node && <aside className="max-h-48 space-y-1 overflow-auto border-t border-slate-200 p-3 text-sm"><strong>{node.label}</strong><p className="whitespace-pre-wrap break-words">{node.description || "暂无说明"}</p><p className="whitespace-pre-wrap break-words text-xs text-slate-500">来源（模型标注）：{node.source || "未提供"}</p><ul className="text-xs">{graph.edges.filter((edge) => edge.from === selected || edge.to === selected).map((edge, i) => <li key={i}>{graph.nodes.find((n) => n.id === edge.from)?.label} → {graph.nodes.find((n) => n.id === edge.to)?.label}：{edge.label}</li>)}</ul></aside>}
    {exportError && <p role="alert" className="p-3 text-sm text-red-600">{exportError}</p>}
  </>;
  return <section className="my-4 overflow-hidden rounded-2xl border border-cyan-200 bg-white text-slate-900 shadow-sm">
    {!expanded && content}
    <dialog ref={dialogRef} aria-label={graph.title} onCancel={() => setExpanded(false)} onClose={() => setExpanded(false)} className="m-auto max-h-[94dvh] w-[95vw] max-w-none overflow-auto rounded-xl bg-white p-0 backdrop:bg-slate-900/40">{expanded && content}</dialog>
  </section>;
}
