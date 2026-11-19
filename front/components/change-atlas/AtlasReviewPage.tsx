"use client";

import { useEffect, useState } from "react";
import { readAtlasEvidence, readAtlasSnapshot } from "@/lib/change-atlas-api";
import type { AtlasEvidence, AtlasSnapshot } from "@/lib/change-atlas-types";
import { collaborationLink, collaborationTime } from "@/lib/chat-collaboration";

export function AtlasReviewPage({ projectId, snapshotId, commitId, fileId }: { projectId: number; snapshotId: string; commitId: string; fileId?: string }) {
  const [snapshot, setSnapshot] = useState<AtlasSnapshot | null>(null);
  const [evidence, setEvidence] = useState<AtlasEvidence | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(true);
  useEffect(() => {
    const controller = new AbortController(); let disposed = false;
    setBusy(true); setError(""); setSnapshot(null); setEvidence(null);
    async function load() {
      try {
        if (!Number.isSafeInteger(projectId) || projectId <= 0 || !snapshotId || !commitId) throw new Error("变更链接参数无效。");
        const value = await readAtlasSnapshot(projectId, snapshotId, controller.signal);
        const commit = value.commits.find(item => item.id === commitId);
        if (!commit || fileId && !commit.files.some(file => file.file_id === fileId)) throw new Error("当前快照中没有该提交或文件。");
        const detail = fileId ? await readAtlasEvidence(projectId, snapshotId, commitId, fileId, controller.signal) : null;
        if (!disposed) { setSnapshot(value); setEvidence(detail); }
      } catch (reason) { if (!disposed) setError(reason instanceof Error ? reason.message : "读取变更失败"); }
      finally { if (!disposed) setBusy(false); }
    }
    void load(); return () => { disposed = true; controller.abort(); };
  }, [projectId, snapshotId, commitId, fileId]);
  const commit = snapshot?.commits.find(item => item.id === commitId);
  return <main className="h-full overflow-y-auto bg-white p-6 text-slate-900"><div className="mx-auto max-w-5xl space-y-4"><h1 className="text-xl font-semibold">Git 变更查阅</h1><a href={`/change-atlas?project=${projectId}`} className="text-sm text-blue-600">打开项目变化地图</a><p className="text-xs text-slate-500">基于固定 Git 快照；需当前账号有项目访问权。快照过期后请重新选择变更。</p>{busy && <p role="status">正在读取变更…</p>}{error && <p role="alert" className="text-red-600">{error}</p>}{commit && snapshot && <><h2 className="break-words font-medium">{commit.subject}</h2><p className="break-all text-xs text-slate-500">{commit.sha} · {commit.author} · {collaborationTime(commit.committed_at)}</p>{snapshot.warnings.map((warning, index) => <p key={index} className="text-xs text-amber-700">{warning}</p>)}{commit.truncated && <p className="text-amber-700">该提交文件列表已截断。</p>}<ul className="space-y-2">{commit.files.map(change => { const file = snapshot.files.find(item => item.id === change.file_id); return file ? <li key={file.id}><a className="break-all text-sm text-blue-600" href={collaborationLink(window.location.origin, snapshot, commit, file.id)}>{file.path}</a> <span className="text-xs text-slate-500">+{change.added ?? "?"} / −{change.deleted ?? "?"}</span></li> : null; })}</ul></>}{evidence && <section><h2 className="mb-2 break-all font-medium">{evidence.path}</h2>{evidence.suppressed && <p className="text-amber-700">该文件差异受保护，未展示正文。</p>}{evidence.truncated && <p className="text-amber-700">差异已截断，不能视为完整审阅。</p>}<pre className="overflow-x-auto rounded-xl border bg-slate-50 p-4 text-xs">{evidence.patch || "没有可展示的文本差异。"}</pre></section>}</div></main>;
}
