"use client";

import { Children, isValidElement, useEffect, useId, useState, type KeyboardEvent, type ReactNode } from "react";
import { Maximize2, X } from "lucide-react";

function readChildrenText(children: ReactNode): string {
  if (typeof children === "string" || typeof children === "number") return String(children);
  if (Array.isArray(children)) return children.map(readChildrenText).join("");
  return "";
}

export function mermaidSourceFromPre(children: ReactNode): string | null {
  const child = Children.toArray(children)[0];
  if (!isValidElement(child)) return null;
  const props = child.props as { className?: string; children?: ReactNode };
  if (!props.className?.split(" ").includes("language-mermaid")) return null;
  const chart = readChildrenText(props.children).trim();
  return chart || null;
}

export function MermaidDiagram({ chart }: { chart: string }) {
  const reactId = useId();
  const [svg, setSvg] = useState("");
  const [error, setError] = useState("");
  const [expanded, setExpanded] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setSvg("");
    setError("");
    async function render() {
      try {
        const mermaid = (await import("mermaid")).default;
        mermaid.initialize({ startOnLoad: false, securityLevel: "strict", theme: "neutral" });
        const id = `aiagent-mermaid-${reactId.replace(/[^a-zA-Z0-9_-]/g, "")}`;
        const result = await mermaid.render(id, chart);
        if (!cancelled) setSvg(result.svg);
      } catch (value) {
        if (!cancelled) setError(value instanceof Error ? value.message : "Mermaid 图表解析失败。");
      }
    }
    void render();
    return () => { cancelled = true; };
  }, [chart, reactId]);

  useEffect(() => {
    if (!expanded) return;
    const closeOnEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key === "Escape") setExpanded(false);
    };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [expanded]);

  if (error) {
    return <div className="my-4 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900"><p>Mermaid 图表解析失败：{error}</p><pre className="mt-2 overflow-x-auto rounded bg-amber-100/70 p-2 text-xs leading-5"><code>{chart}</code></pre></div>;
  }
  if (!svg) return <div className="my-4 flex min-h-24 items-center justify-center rounded-lg border border-zinc-200 bg-zinc-50 text-sm text-zinc-500">正在生成 Mermaid 图表…</div>;
  const expandOnKeyboard = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      setExpanded(true);
    }
  };
  return <>
    <div role="button" tabIndex={0} aria-label="点击全屏查看 Mermaid 图表" onClick={() => setExpanded(true)} onKeyDown={expandOnKeyboard} className="group relative my-4 cursor-zoom-in overflow-x-auto rounded-xl border border-zinc-200 bg-white p-3 outline-none transition hover:border-blue-300 focus-visible:ring-2 focus-visible:ring-blue-500">
      <div role="img" aria-label="Mermaid 图表" className="[&_svg]:h-auto [&_svg]:min-w-max [&_svg]:max-w-none" dangerouslySetInnerHTML={{ __html: svg }} />
      <span className="pointer-events-none absolute right-3 top-3 inline-flex items-center gap-1 rounded-md bg-slate-900/75 px-2 py-1 text-[11px] text-white opacity-0 shadow-sm transition group-hover:opacity-100 group-focus-visible:opacity-100"><Maximize2 size={13}/>点击放大</span>
    </div>
    {expanded && <div role="dialog" aria-modal="true" aria-label="Mermaid 图表全屏预览" onMouseDown={() => setExpanded(false)} className="fixed inset-0 z-[70] grid place-items-center bg-slate-950/80 p-4 backdrop-blur-sm">
      <div onMouseDown={(event) => event.stopPropagation()} className="flex h-full w-full max-w-[96vw] flex-col overflow-hidden rounded-2xl border border-white/15 bg-white shadow-2xl">
        <div className="flex shrink-0 items-center justify-between border-b border-zinc-200 px-4 py-3"><span className="text-sm font-semibold text-zinc-800">Mermaid 图表</span><button type="button" onClick={() => setExpanded(false)} className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-zinc-500 transition hover:bg-zinc-100 hover:text-zinc-900" aria-label="关闭全屏预览"><X size={17}/></button></div>
        <div role="img" aria-label="Mermaid 图表全屏预览" className="workspace-scroll min-h-0 flex-1 overflow-auto bg-zinc-50 p-6 [&_svg]:h-auto [&_svg]:min-w-max [&_svg]:max-w-none" dangerouslySetInnerHTML={{ __html: svg }} />
      </div>
    </div>}
  </>;
}
