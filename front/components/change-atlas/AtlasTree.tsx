"use client";

import { FileCode2, Folder } from "lucide-react";
import { atlasFileTree, type AtlasTreeNode } from "@/lib/change-atlas";
import type { AtlasFile } from "@/lib/change-atlas-types";

export function AtlasTree({ files, changed, onFile }: { files: AtlasFile[]; changed: Set<string>; onFile: (file: AtlasFile) => void }) {
  const tree = atlasFileTree(files);
  function render(nodes: AtlasTreeNode[]) {
    return nodes.map(node => node.file ? <button type="button" key={node.path} data-atlas-file={node.file.id} onClick={() => onFile(node.file!)} title={node.file.path} className={`flex w-full min-w-0 items-center gap-2 rounded-md px-2 py-2 text-left text-xs hover:bg-blue-50 ${changed.has(node.file.id) ? "bg-blue-50 font-medium text-blue-700" : "text-slate-600"}`}><FileCode2 size={14} className="shrink-0"/><span className="min-w-0 break-all font-mono">{node.name}</span><span className="ml-auto shrink-0 text-[10px]">{!node.file.at_head ? "已删除" : changed.has(node.file.id) ? "有改动" : ""}</span></button> : <details key={node.path} open className="min-w-0"><summary className="cursor-pointer rounded-md px-2 py-2 text-xs font-medium text-slate-700 hover:bg-slate-50"><Folder size={14} className="mx-1 inline text-blue-500"/>{node.name}</summary><div className="ml-3 min-w-0 border-l border-slate-200 pl-3">{render(node.children)}</div></details>);
  }
  return <div aria-label="文件目录树" className="max-h-[560px] min-w-0 overflow-auto rounded-xl border border-slate-200 bg-white p-3">{render(tree)}</div>;
}
