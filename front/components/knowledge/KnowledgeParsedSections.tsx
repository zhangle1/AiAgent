"use client";

import type { KnowledgeResourceNode } from "@/lib/knowledge-types";

export function KnowledgeParsedSections({ sections, onOpen }: { sections: KnowledgeResourceNode[]; onOpen: (uri: string) => void }) {
  if (!sections.length) return null;
  return <details className="mb-3 rounded-lg border border-slate-200 bg-white p-3 text-xs">
    <summary className="cursor-pointer font-medium text-slate-700">解析章节 · {sections.length} 个文件</summary>
    <p className="my-2 leading-5 text-slate-500">按标题与段落组织，保留完整正文。章节是只读解析结果；重新解析请返回原始文件。</p>
    <ol className="max-h-64 space-y-1 overflow-auto" aria-label="解析章节列表">{sections.map(section => <li key={section.uri}><button type="button" onClick={() => onOpen(section.uri)} className="w-full break-all rounded px-2 py-1.5 text-left text-blue-700 hover:bg-blue-50">{decodeURI(section.uri.split("/_parsed/")[1] ?? section.uri)}</button></li>)}</ol>
  </details>;
}
