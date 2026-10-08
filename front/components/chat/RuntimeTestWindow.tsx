"use client";

import { useEffect, useState } from "react";
import { listChatRuntimeJobs, stopChatRuntime, visitChatRuntime } from "@/lib/chat-runtime-api";
import { runtimeAccessUrl, runtimeJobAccessUrl } from "@/lib/chat-runtime";
import type { ChatRuntimeJob } from "@/lib/chat-runtime-types";

export function RuntimeTestWindow() {
  const [job, setJob] = useState<ChatRuntimeJob | null>(null);
  const [error, setError] = useState("");
  const [runId, setRunId] = useState("");
  const [pagePath, setPagePath] = useState("/");
  const [pathDraft, setPathDraft] = useState("/");
  const [origin, setOrigin] = useState("");
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const projectId = Number(params.get("project_id")), requestId = params.get("request_id") || "";
    setOrigin(window.location.origin);
    if (!Number.isSafeInteger(projectId) || projectId <= 0 || !/^[a-f0-9]{32}$/.test(requestId)) { setError("运行链接无效"); return; }
    let active = true; let timer: ReturnType<typeof setTimeout>;
    async function poll() {
      try {
        const jobs = await listChatRuntimeJobs(projectId);
        const next = jobs.find(item => item.request_id === requestId);
        if (!next) throw new Error("运行请求不存在、已过期或服务器已重启，请重新运行。");
        if (!active) return;
        setJob(next);
        if (params.get("mode") !== "manage") {
          const destination = runtimeJobAccessUrl(window.location.origin, next);
          if (destination) { window.location.replace(destination); return; }
        }
        if (document.visibilityState === "visible" && ["starting", "running"].includes(next.status)) await visitChatRuntime(projectId, requestId);
        if (active) setError("");
      } catch (reason) { if (active) setError(reason instanceof Error ? reason.message : "无法连接运行服务"); }
      finally { if (active) timer = setTimeout(poll, 10000); }
    }
    void poll();
    const visibility = () => { if (document.visibilityState === "visible") void visitChatRuntime(projectId, requestId).catch(() => {}); };
    document.addEventListener("visibilitychange", visibility);
    return () => { active = false; clearTimeout(timer); document.removeEventListener("visibilitychange", visibility); };
  }, []);
  const run = job?.runs.find(item => item.run_id === runId) || job?.runs.find(item => item.role === "frontend") || job?.runs[0];
  useEffect(() => {
    const target = job?.targets.find(item => item.repository_name === run?.repository_name && item.entry_path === run?.entry_path);
    setPagePath(target?.page_path || "/"); setPathDraft(target?.page_path || "/");
  // Only a different service should reset the user's selected route.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [run?.run_id]);
  let url = "";
  try { if (run && origin) url = runtimeAccessUrl(origin, run.port, pagePath); } catch { /* Invalid path stays in editor. */ }
  const mixedContent = origin.startsWith("https:");
  async function stop() {
    if (!job) return; setBusy(true);
    try { await stopChatRuntime(job.project_id, job.request_id); setJob({ ...job, status: "stopped" }); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "关闭失败"); }
    finally { setBusy(false); }
  }
  return <main className="flex h-[100dvh] min-h-0 flex-col bg-slate-50 text-slate-800">
    <header className="space-y-2 border-b bg-white p-3"><div className="flex flex-wrap items-center gap-3"><strong className="text-sm">项目功能测试</strong><span className="flex-1 text-xs text-slate-500">{job?.message || "正在探测运行状态…"}</span>{job && <button disabled={busy || ["stopped", "failed", "expired"].includes(job.status)} onClick={() => void stop()} className="text-xs text-rose-600 disabled:opacity-40">关闭整组服务</button>}</div>
      {run && <div className="flex flex-wrap items-center gap-2 text-xs"><select aria-label="测试服务" value={run.run_id} onChange={event => setRunId(event.target.value)} className="max-w-full rounded border p-2">{job?.runs.map(item => <option key={item.run_id} value={item.run_id}>{item.repository_name} / {item.entry_path} · :{item.port}</option>)}</select><form className="flex min-w-0 flex-1 gap-2" onSubmit={event => { event.preventDefault(); try { runtimeAccessUrl(origin, run.port, pathDraft); setPagePath(pathDraft); setError(""); setReload(value => value + 1); } catch { setError("页面路径必须以 / 开头，不能使用外部地址。"); } }}><input aria-label="页面路径" className="min-w-20 flex-1 rounded border px-2" value={pathDraft} onChange={event => setPathDraft(event.target.value)}/><button className="rounded border px-3 py-2">打开页面</button></form><button onClick={() => setReload(value => value + 1)} className="rounded border p-2">刷新</button>{url && <a href={url} target="_blank" rel="noopener noreferrer" className="text-blue-600">直接打开端口 ↗</a>}</div>}
      <p className="break-all text-[11px] text-slate-500">{url} · 此窗口可见时自动续期；关闭或隐藏后 {job?.idle_minutes ?? 30} 分钟自动关闭前后端。直接打开端口不续期。</p>
      {error && <p role="alert" className="text-xs text-rose-600">{error}</p>}
    </header>
    {job?.status === "running" && url ? mixedContent ? <div className="p-6 text-sm">当前 AiAgent 使用 HTTPS，开发服务使用 HTTP，浏览器会阻止嵌入。请点击“直接打开端口”，并保持本测试窗口可见以续期。</div> : <iframe key={`${run?.run_id}:${reload}:${url}`} src={url} title="项目测试页面" className="min-h-0 flex-1 border-0" sandbox="allow-scripts allow-same-origin allow-forms allow-downloads allow-popups" referrerPolicy="no-referrer"/> : <p className="p-6 text-sm">{job?.status === "waiting" ? "等待 AI 分析配置并提交启动清单。" : job?.status === "starting" ? "正在启动前后端并检查 HTTP 就绪状态…" : job?.status === "running" ? "没有可打开的页面。" : "服务尚未就绪或已关闭。"}</p>}
    <footer className="border-t bg-white px-3 py-2 text-[11px] text-slate-500">页面无法显示时，可直接打开端口检查（部分项目禁止 iframe）。外网访问还需要服务器防火墙或端口映射放行对应端口。</footer>
  </main>;
}
