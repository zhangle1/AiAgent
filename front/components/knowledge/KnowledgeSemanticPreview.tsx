"use client";

import { useState } from "react";
import ReactMarkdown from "react-markdown";
import type { KnowledgeResourceRead } from "@/lib/knowledge-types";

export function KnowledgeSemanticPreview({ resource, onOpen }: { resource: KnowledgeResourceRead | null; onOpen: (uri: string) => void }) {
  const [level, setLevel] = useState<"abstract" | "overview">("overview");
  const content = level === "abstract" ? resource?.abstract_content : resource?.overview_content;
  function download() {
    if (!content) return;
    const url = URL.createObjectURL(new Blob([content], { type: "text/markdown;charset=utf-8" }));
    const link = document.createElement("a"); link.href = url; link.download = `.${level}.md`; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  return <section className="mb-6 rounded-lg border border-slate-200 bg-white p-4">
    <div className="mb-3 flex items-center gap-2 text-xs">
      {(["abstract", "overview"] as const).map(value => <button key={value} type="button" onClick={() => setLevel(value)} className={`rounded px-3 py-1.5 ${level === value ? "bg-blue-50 text-blue-700" : "text-slate-500"}`}>{value === "abstract" ? "L0 · 目录摘要" : "L1 · 目录概览"}</button>)}
      <button type="button" disabled={!content} onClick={download} className="ml-auto text-blue-700 disabled:text-slate-400">下载 .{level}.md</button>
    </div>
    {content ? <>
      <p className="mb-3 text-[11px] text-slate-400">AI 生成 · {resource?.model ?? "配置模型"} · {resource?.semantic_generated_at ? new Date(resource.semantic_generated_at).toLocaleString() : ""}</p>
      <article className="max-h-[480px] overflow-auto text-sm leading-7"><ReactMarkdown
        urlTransform={url => url.startsWith("viking://") ? url : ""}
        components={{ img: () => null, a: ({ href, children }) => href?.startsWith("viking://") ? <button type="button" className="text-blue-600 underline" onClick={() => onOpen(href)}>{children}</button> : <span>{children}</span> }}
      >{content}</ReactMarkdown></article>
    </> : <p className="py-4 text-xs text-slate-500">尚未生成，或资料已变化。请对目录中的文件执行「重新解析」，任务完成后刷新查看 L0/L1。</p>}
  </section>;
}
