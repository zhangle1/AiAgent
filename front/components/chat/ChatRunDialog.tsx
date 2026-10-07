"use client";

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Folder, Loader2, Play, X } from "lucide-react";
import type { CodeProject } from "@/lib/code-repository-types";
import { getCodeTree, type CodeTree } from "@/lib/code-repository-api";
import { prepareChatRuntime, stopChatRuntime } from "@/lib/chat-runtime-api";
import { buildRuntimePrompt } from "@/lib/chat-runtime";

export function ChatRunDialog({ project, initialRepository, onClose, onApply }: { project: CodeProject; initialRepository?: string; onClose: () => void; onApply: (prompt: string) => void }) {
  const [repository, setRepository] = useState(initialRepository || project.repositories[0]?.name || "");
  const [selected, setSelected] = useState<Record<string, string[]>>({ [initialRepository || project.repositories[0]?.name || ""]: [] });
  const [directory, setDirectory] = useState("");
  const [tree, setTree] = useState<CodeTree | null>(null);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [instructions, setInstructions] = useState("");
  const [pagePath, setPagePath] = useState("/");
  const [idleMinutes, setIdleMinutes] = useState(30);
  const alive = useRef(true);
  const dialog = useRef<HTMLElement>(null);
  useEffect(() => { alive.current = true; const previous = document.activeElement as HTMLElement | null; dialog.current?.focus(); return () => { alive.current = false; previous?.focus(); }; }, []);
  useEffect(() => {
    let active = true;
    setLoading(true); setTree(null); setError("");
    getCodeTree(repository, directory).then(value => { if (active) setTree(value); }).catch(reason => { if (active) setError(reason.message); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [repository, directory]);
  const toggle = (path: string) => setSelected(current => { const paths = current[repository] || []; return { ...current, [repository]: paths.includes(path) ? paths.filter(x => x !== path) : [...paths, path] }; });
  async function apply() {
    setBusy(true); setError("");
    try {
      const selections = Object.entries(selected).map(([repository_name, entry_paths]) => ({ repository_name, entry_paths }));
      const job = await prepareChatRuntime(project.id, selections, idleMinutes);
      if (!alive.current) { await stopChatRuntime(project.id, job.request_id); return; }
      onApply(buildRuntimePrompt(project.id, selections, `期望测试页面路径：${pagePath}\n${instructions}`, window.location.origin, job));
      window.dispatchEvent(new Event("aiagent:runtime-refresh"));
    } catch (reason) { if (alive.current) setError(reason instanceof Error ? reason.message : "创建运行请求失败"); }
    finally { if (alive.current) setBusy(false); }
  }
  return createPortal(<div className="fixed inset-0 z-[145] grid place-items-center bg-slate-950/50 p-3" onMouseDown={() => !busy && onClose()}>
    <section ref={dialog} tabIndex={-1} role="dialog" aria-modal="true" aria-labelledby="chat-run-title" className="flex max-h-[90dvh] w-full max-w-3xl flex-col overflow-hidden rounded-2xl bg-white text-slate-800 shadow-2xl" onMouseDown={event => event.stopPropagation()} onKeyDown={event => {
      if (event.key === "Escape" && !busy) onClose();
      if (event.key === "Tab") {
        const controls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),select,textarea'));
        const first = controls[0], last = controls.at(-1);
        if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog.current)) { event.preventDefault(); last?.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
      }
    }}>
      <header className="flex items-center gap-3 border-b p-4"><Play size={20} className="text-emerald-600"/><div className="flex-1"><h2 id="chat-run-title" className="font-semibold">配置 AI 运行</h2><p className="text-xs text-slate-500">多选前后端工程，由 AI 检查配置并启动</p></div><button aria-label="关闭运行配置" disabled={busy} onClick={onClose}><X size={20}/></button></header>
      <div className="space-y-4 overflow-y-auto p-4">
        <div className="flex flex-wrap gap-2">{project.repositories.map(repo => <label key={repo.name} className="flex items-center gap-2 rounded-lg border px-3 py-2 text-xs"><input type="checkbox" checked={repo.name in selected} onChange={event => setSelected(current => { const next = { ...current }; if (event.target.checked) next[repo.name] = []; else delete next[repo.name]; return next; })}/>{repo.display_name}</label>)}</div>
        <p className="text-xs text-slate-500">勾选需要运行的代码库；进入目录可多选工程或配置文件。未选具体文件时，AI 探测该代码库的入口。</p>
        <div className="overflow-hidden rounded-xl border">
          <div className="flex flex-wrap items-center gap-2 bg-slate-50 p-2 text-xs"><select aria-label="浏览代码库" value={repository} onChange={event => { setRepository(event.target.value); setDirectory(""); }}>{project.repositories.map(repo => <option key={repo.name} value={repo.name}>{repo.display_name}</option>)}</select><button onClick={() => setDirectory("")} className="text-blue-600">根目录</button><span className="min-w-0 flex-1 truncate">/{directory}</span>{directory && <button onClick={() => setDirectory(directory.split("/").slice(0, -1).join("/"))}>上一级</button>}</div>
          <div className="h-48 overflow-y-auto p-2">{loading ? <p className="p-3 text-xs"><Loader2 className="mr-2 inline animate-spin" size={14}/>读取目录…</p> : <>{tree?.directories.map(item => <button key={item.path} onClick={() => setDirectory(item.path.replace(/\\/g, "/"))} className="flex w-full items-center gap-2 rounded p-2 text-left text-xs hover:bg-slate-50"><Folder size={15} className="text-amber-500"/>{item.name}</button>)}{tree?.files.filter(item => /\.(csproj|slnx?|json|ya?ml|config)$/i.test(item.path)).map(item => { const path = item.path.replace(/\\/g, "/"); return <label key={path} className="flex items-center gap-2 rounded p-2 text-xs hover:bg-blue-50"><input type="checkbox" checked={selected[repository]?.includes(path) || false} onChange={() => toggle(path)}/>{item.name}</label>; })}</>}</div>
        </div>
        {Object.entries(selected).map(([name, paths]) => <p key={name} className="break-all text-xs text-blue-700">{name}：{paths.length ? paths.join("、") : "AI 自动探测入口"}</p>)}
        <div className="grid gap-3 sm:grid-cols-2"><label className="grid gap-1 text-xs">测试页面路径<input aria-label="测试页面路径" value={pagePath} maxLength={512} onChange={event => setPagePath(event.target.value)} className="rounded-lg border p-2" placeholder="/ 或 /login"/></label><label className="grid gap-1 text-xs">无测试窗口访问时自动关闭<select value={idleMinutes} onChange={event => setIdleMinutes(Number(event.target.value))} className="rounded-lg border p-2">{[5, 15, 30, 60, 120, 240].map(n => <option key={n} value={n}>{n} 分钟</option>)}</select></label></div>
        <label className="block text-sm">补充运行与页面配置要求<textarea value={instructions} onChange={event => setInstructions(event.target.value)} maxLength={8000} rows={4} className="mt-2 w-full rounded-xl border p-3 text-sm" placeholder="例如：先启动 API，再启动前端；调整 API 地址和代理；打开 /login 测试登录。不要填密码或密钥。"/></label>
        <p className="text-xs leading-5 text-slate-500">访问 IP/域名取自当前 AiAgent 地址，端口由 AI 判断。填写完成后将请求放入聊天，发送后 AI 才开始工作。请通过“新窗口测试”访问，以便系统准确续期；直接打开端口不会续期。</p>
        {error && <p role="alert" className="rounded-lg bg-rose-50 p-3 text-xs text-rose-700">{error}</p>}
      </div>
      <footer className="flex justify-end gap-2 border-t p-4"><button disabled={busy} onClick={onClose} className="rounded-lg border px-4 py-2 text-xs">取消</button><button disabled={busy || !Object.keys(selected).length || !pagePath.startsWith("/") || pagePath.startsWith("//")} onClick={() => void apply()} className="rounded-lg bg-emerald-600 px-4 py-2 text-xs text-white disabled:opacity-40">{busy ? "准备中…" : "填入聊天并准备运行"}</button></footer>
    </section>
  </div>, document.body);
}
