"use client";

import { useState } from "react";

export function ArchifyDemo() {
  const [loaded, setLoaded] = useState(false);
  return <section aria-label="Archify 交互示例" className="flex min-h-0 flex-1 flex-col">
    <div className="flex flex-wrap items-center justify-between gap-2 border-b bg-blue-50 px-5 py-3 text-xs text-slate-600">
      <p>固定架构示例 · 点击节点探索关系，试试路径追踪、缩放、主题切换和图片导出。</p>
      <a href="/archify/aiagent-demo.architecture.json" download className="text-blue-700 underline">下载示例 JSON</a>
      <p className="w-full text-slate-500">本地渲染验证，尚未接入 AI 生成；此图不是对当前项目的自动分析。</p>
    </div>
    {!loaded && <p role="status" className="px-5 py-2 text-xs text-slate-500">正在加载交互画布…</p>}
    <iframe title="AI 工作台 Archify 交互架构图" src="/archify/aiagent-demo.html" sandbox="allow-scripts allow-downloads" referrerPolicy="no-referrer" onLoad={() => setLoaded(true)} className="min-h-0 w-full flex-1 border-0 bg-slate-950" />
  </section>;
}
