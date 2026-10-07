"use client";

import { useEffect, useState } from "react";
import { FolderTree, RefreshCw } from "lucide-react";
import { getKnowledgeContextTree } from "@/lib/knowledge-api";
import type { KnowledgeContextNode } from "@/lib/knowledge-types";

const layerLabels: Record<number, string> = { 0: "L0 摘要", 1: "L1 概览", 2: "L2 详情" };

export function KnowledgeContextTree({ name }: { name: string }) {
  const [nodes, setNodes] = useState<KnowledgeContextNode[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  async function load() {
    setLoading(true);
    setError("");
    try {
      setNodes(await getKnowledgeContextTree(name));
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "资源树加载失败");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void load(); }, [name]);

  return <section className="mb-4 rounded-lg border border-[var(--border)] bg-white p-4">
    <div className="flex items-center justify-between gap-3">
      <div>
        <h2 className="flex items-center gap-2 text-[14px] font-semibold"><FolderTree size={16} />上下文资源树</h2>
        <p className="mt-1 text-[11px] text-[var(--muted-foreground)]">按 OpenViking 的 URI 和 L0 / L1 / L2 分层浏览资料。</p>
      </div>
      <button type="button" onClick={() => void load()} disabled={loading} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-[var(--border)] px-2.5 text-[12px] hover:border-blue-300 disabled:opacity-50"><RefreshCw size={13} className={loading ? "animate-spin" : ""} />刷新</button>
    </div>
    {error && <p className="mt-3 rounded-md bg-red-50 px-3 py-2 text-[12px] text-red-700">{error}</p>}
    {!error && loading && <p className="mt-3 text-[12px] text-[var(--muted-foreground)]">正在读取资源树…</p>}
    {!error && !loading && nodes.length === 0 && <p className="mt-3 text-[12px] text-[var(--muted-foreground)]">上传源文件后，这里会显示 raw 资源和知识页。</p>}
    {!error && !loading && nodes.length > 0 && <div className="mt-3 grid gap-1.5">{nodes.map((node) => <div key={node.id} className="flex min-w-0 items-center gap-2 rounded-md bg-slate-50 px-2.5 py-2 text-[12px]">
      <span className="shrink-0 rounded bg-white px-1.5 py-0.5 text-[10px] font-semibold text-slate-600">{layerLabels[node.layer] ?? `L${node.layer}`}</span>
      <span className="min-w-0 flex-1 truncate font-medium">{node.name}</span>
      <code className="hidden max-w-[48%] truncate text-[10px] text-slate-500 md:block">{node.uri}</code>
      <span className="shrink-0 text-[10px] text-slate-500">{node.status}</span>
    </div>)}</div>}
  </section>;
}
