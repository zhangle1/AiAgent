"use client";

import { useEffect, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { getKnowledgePages } from "@/lib/knowledge-api";
import type { KnowledgePage } from "@/lib/knowledge-types";

export function KnowledgePages({ name, onSource }: { name: string; onSource: (id: number) => void }) {
  const [pages, setPages] = useState<KnowledgePage[]>([]);
  const [selected, setSelected] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let disposed = false;
    setLoading(true); setError(""); setPages([]);
    getKnowledgePages(name).then((rows) => { if (!disposed) setPages(rows); })
      .catch((ex) => { if (!disposed) setError(ex instanceof Error ? ex.message : String(ex)); })
      .finally(() => { if (!disposed) setLoading(false); });
    return () => { disposed = true; };
  }, [name, revision]);
  const key = (page: KnowledgePage) => `${page.id}:${page.page_index}`;
  const page = pages.find((p) => key(p) === selected) ?? pages[0];
  return <div className="grid h-[calc(100vh-230px)] min-h-[420px] grid-cols-1 md:grid-cols-[280px_1fr]">
    <aside className="overflow-auto border-r p-4">
      <div className="mb-4 flex items-center justify-between text-sm font-semibold"><span>知识 · {pages.length}</span><button onClick={() => setRevision((v) => v + 1)} className="text-xs text-blue-600">刷新</button></div>
      {loading && <p role="status" className="text-sm text-zinc-500">正在读取知识…</p>}
      {error && <p role="alert" className="text-sm text-red-600">{error}</p>}
      {!loading && !error && pages.length === 0 && <p className="text-sm leading-6 text-zinc-500">还没有提炼结果。请在「原始文件」中选择资料，点击「提炼知识」。</p>}
      {pages.map((p) => <button key={key(p)} onClick={() => setSelected(key(p))} className={`mb-1 block w-full rounded-lg p-3 text-left text-sm ${page === p ? "bg-blue-50 text-blue-700" : "hover:bg-zinc-50"}`}><span className="block font-medium">{p.title}</span><span className="mt-1 block truncate text-xs text-zinc-500">来源：{p.source_name}</span></button>)}
    </aside>
    <article className="min-w-0 overflow-auto p-6">
      {page && <><header className="mb-6 border-b pb-4"><h2 className="text-xl font-semibold">{page.title}</h2><div className="mt-3 flex flex-wrap items-center gap-3 text-xs text-zinc-500"><span>{page.review_status === "draft" ? "待核对草稿" : page.review_status}</span><span>{page.generator} · {page.model || "默认模型"}</span>{page.document_id != null && <button className="text-blue-600" onClick={() => onSource(page.document_id!)}>查看原始文件：{page.source_name}</button>}</div></header><div className="space-y-4 break-words text-sm leading-7 [&_h1]:text-xl [&_h2]:text-lg [&_h2]:font-semibold [&_h3]:font-semibold [&_ul]:list-disc [&_ul]:pl-5 [&_ol]:list-decimal [&_ol]:pl-5 [&_pre]:overflow-auto [&_pre]:bg-zinc-50 [&_pre]:p-3 [&_blockquote]:border-l-2 [&_blockquote]:pl-4"><ReactMarkdown remarkPlugins={[remarkGfm]} skipHtml components={{ img: ({ alt }) => <span>[图片：{alt || "未显示"}]</span> }}>{page.content || "暂无正文"}</ReactMarkdown></div></>}
    </article>
  </div>;
}
