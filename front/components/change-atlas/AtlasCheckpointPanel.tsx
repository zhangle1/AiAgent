"use client";

import { useEffect, useRef, useState } from "react";
import { atlasCheckpoint } from "@/lib/change-atlas-api";
import type { AtlasCheckpoint } from "@/lib/change-atlas-types";

export function AtlasCheckpointPanel({ projectId, snapshotId, commitId }: { projectId: number; snapshotId: string; commitId: string }) {
  const [checkpoint, setCheckpoint] = useState<AtlasCheckpoint>();
  const [sessionId, setSessionId] = useState("");
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState("");
  const controller = useRef<AbortController | null>(null);

  useEffect(() => {
    const request = new AbortController();
    controller.current = request;
    atlasCheckpoint(projectId, snapshotId, commitId, request.signal).then(value => {
      if (request.signal.aborted) return;
      setCheckpoint(value);
      setSessionId(value.session?.id ?? "");
    }).catch(reason => {
      if (!request.signal.aborted) setError(reason instanceof Error ? reason.message : "读取检查点失败。");
    }).finally(() => { if (!request.signal.aborted) setBusy(false); });
    return () => request.abort();
  }, [projectId, snapshotId, commitId]);

  async function save() {
    const signal = controller.current?.signal;
    setBusy(true);
    setError("");
    try {
      const value = await atlasCheckpoint(projectId, snapshotId, commitId, signal, { session_id: sessionId || null });
      if (!signal?.aborted) {
        setCheckpoint(value);
        setSessionId(value.session?.id ?? "");
      }
    } catch (reason) {
      if (!signal?.aborted) setError(reason instanceof Error ? reason.message : "保存检查点失败。");
    } finally { if (!signal?.aborted) setBusy(false); }
  }

  const delivery = checkpoint?.delivery;
  return <section className="space-y-3 rounded-lg border border-slate-200 p-4" aria-labelledby="atlas-checkpoint-title">
    <h3 id="atlas-checkpoint-title" className="font-medium">会话检查点</h3>
    {error && <p role="alert" className="text-sm text-red-600">{error}</p>}
    {busy && !checkpoint && <p role="status" className="text-sm text-slate-500">读取检查点…</p>}
    {checkpoint && <>
      {checkpoint.session && <a className="block break-words text-sm text-blue-700 underline" href={`/chat?session=${encodeURIComponent(checkpoint.session.id)}`}>
        打开关联会话 · {checkpoint.session.title}
      </a>}
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <label htmlFor="atlas-checkpoint-session">关联本人会话</label>
        <select id="atlas-checkpoint-session" className="min-w-0 max-w-full rounded border border-slate-300 px-2 py-1.5" disabled={busy} value={sessionId} onChange={event => setSessionId(event.target.value)}>
          <option value="">不关联</option>
          {checkpoint.sessions.map(session => <option value={session.id} key={session.id}>{session.title}</option>)}
        </select>
        <button type="button" className="rounded bg-blue-600 px-3 py-1.5 font-medium text-white disabled:opacity-50" disabled={busy || sessionId === (checkpoint.session?.id ?? "")} onClick={() => void save()}>保存关联</button>
      </div>
      <p className="break-words text-sm text-slate-600">{delivery
        ? `交付校验：${delivery.validation_status === "passed" ? "通过" : delivery.validation_status === "failed" ? "失败" : "无结果"}${delivery.task_title ? ` · 任务：${delivery.task_title}` : ""}`
        : "暂无该提交的交付校验记录。"}</p>
    </>}
  </section>;
}
