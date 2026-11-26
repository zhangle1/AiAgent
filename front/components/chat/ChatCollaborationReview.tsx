"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { ArrowUpRight, X } from "lucide-react";
import { AtlasChangeGraph } from "@/components/change-atlas/AtlasChangeGraph";
import { readAtlasEvidence } from "@/lib/change-atlas-api";
import type { AtlasCommit, AtlasEvidence, AtlasFile, AtlasSnapshot } from "@/lib/change-atlas-types";
import { collaborationLink, collaborationTime, collaborationDay } from "@/lib/chat-collaboration";

type EvidencePreview = { commit: AtlasCommit; file: AtlasFile; result?: AtlasEvidence; error?: string };

export function ChatCollaborationReview({ snapshot, commits }: { snapshot: AtlasSnapshot; commits: AtlasCommit[] }) {
  const [preview, setPreview] = useState<EvidencePreview | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);
  const evidenceSection = useRef<HTMLElement>(null);
  const previewCommitId = preview?.commit.id;
  const previewFileId = preview?.file.id;
  const files = useMemo(() => {
    const ids = new Set(commits.flatMap(commit => commit.files.map(change => change.file_id)));
    return snapshot.files.filter(file => ids.has(file.id));
  }, [snapshot, commits]);
  const fileMap = useMemo(() => new Map(files.map(file => [file.id, file])), [files]);

  useEffect(() => () => request.current?.abort(), []);
  useEffect(() => {
    if (previewCommitId && previewFileId) evidenceSection.current?.scrollIntoView({ block: "nearest" });
  }, [previewCommitId, previewFileId]);

  async function showEvidence(commit: AtlasCommit, file: AtlasFile) {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setPreview({ commit, file });
    try {
      const result = await readAtlasEvidence(snapshot.project_id, snapshot.id, commit.id, file.id, controller.signal);
      if (!controller.signal.aborted) setPreview({ commit, file, result });
    } catch (reason) {
      if (!controller.signal.aborted) {
        setPreview({ commit, file, error: reason instanceof Error ? reason.message : "读取差异失败" });
      }
    }
  }

  return <div className="space-y-3">
    <div className={`grid min-w-0 gap-3 ${preview ? "xl:grid-cols-[minmax(0,1fr)_minmax(0,0.8fr)]" : ""}`}>
      <AtlasChangeGraph commits={commits} files={files} historyOnly formatDay={timestamp => collaborationDay(timestamp).slice(5)}
        onEvidence={(commit, file) => void showEvidence(commit, file)} />
      {preview && <section ref={evidenceSection} aria-label="提交差异" className="min-w-0 rounded-lg border border-slate-200">
        <header className="flex items-start gap-2 border-b border-slate-200 bg-slate-50 p-3">
          <div className="min-w-0 flex-1 break-all text-xs"><strong>{preview.file.path}</strong><p className="mt-1 text-slate-500">{preview.commit.sha.slice(0, 8)} · {collaborationTime(preview.commit.committed_at)}</p></div>
          <button type="button" aria-label="关闭提交差异" className="rounded p-1 hover:bg-slate-200" onClick={() => { request.current?.abort(); setPreview(null); }}><X size={16} /></button>
        </header>
        {preview.error ? <p role="alert" className="p-3 text-sm text-red-700">{preview.error} <button type="button" className="underline" onClick={() => void showEvidence(preview.commit, preview.file)}>重试</button></p>
          : preview.result ? <>
            {preview.result.suppressed ? <p className="p-3 text-xs text-amber-800">该文件差异受保护。</p>
              : <pre className="max-h-[40dvh] overflow-auto p-3 text-xs">{preview.result.patch || "没有可展示的文本差异。"}</pre>}
            {preview.result.truncated && <p className="px-3 pb-3 text-xs text-amber-800">差异已截断，仅展示部分内容。</p>}
          </> : <p role="status" className="p-3 text-xs text-slate-600">正在读取差异…</p>}
      </section>}
    </div>
    {commits.map(commit => <details key={commit.id} name={`collaboration-files-${snapshot.id}`} onToggle={event => {
      const open = event.currentTarget.open;
      setExpanded(current => open ? commit.id : current === commit.id ? null : current);
    }} className="rounded-lg border border-slate-200">
      <summary className="cursor-pointer break-words p-2.5 text-xs font-medium">{commit.sha.slice(0, 8)} · {commit.subject || "无提交说明"} · {commit.files.length} 个文件{commit.truncated ? "（已截断）" : ""}</summary>
      {expanded === commit.id && <div className="max-h-64 space-y-1.5 overflow-auto border-t border-slate-100 p-2.5 text-xs">
        <a href={collaborationLink(window.location.origin, snapshot, commit)} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-blue-700 hover:underline">在新页查阅 <ArrowUpRight size={12} aria-hidden="true" /></a>
        {commit.files.map(change => {
          const file = fileMap.get(change.file_id);
          return file && <button key={file.id} type="button" onClick={() => void showEvidence(commit, file)}
            className="block w-full break-all rounded px-1.5 py-1 text-left text-blue-700 hover:bg-blue-50">
            {file.path}<span className="ml-2 text-slate-500">{change.added == null && change.deleted == null ? "二进制" : `+${change.added ?? "?"} / −${change.deleted ?? "?"}`}</span>
          </button>;
        })}
      </div>}
    </details>)}
  </div>;
}
