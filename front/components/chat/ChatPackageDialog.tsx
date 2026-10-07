"use client";

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { File, Folder, Loader2, PackageOpen, X } from "lucide-react";
import { getCodeTree, type CodeTree } from "@/lib/code-repository-api";
import type { CodeRepository } from "@/lib/code-repository-types";
import { buildPackagePrompt, normalizePackageTarget } from "@/lib/chat-packaging";

function isTarget(path: string) {
  try { normalizePackageTarget(path); return true; } catch { return false; }
}

export function ChatPackageDialog({ projectId, repository, onClose, onApply }: {
  projectId: number; repository: CodeRepository; onClose: () => void; onApply: (prompt: string) => void;
}) {
  const [directory, setDirectory] = useState("");
  const [tree, setTree] = useState<CodeTree | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [reload, setReload] = useState(0);
  const [target, setTarget] = useState("");
  const [automatic, setAutomatic] = useState(false);
  const [instructions, setInstructions] = useState("");
  const dialogRef = useRef<HTMLElement>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setTree(null);
    setError("");
    getCodeTree(repository.name, directory).then((value) => {
      if (active) setTree(value);
    }).catch((reason) => {
      if (active) setError(reason instanceof Error ? reason.message : "读取文件失败，请重试。");
    }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [repository.name, directory, reload]);

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialogRef.current?.focus();
    return () => { previous?.focus(); };
  }, []);

  const files = tree?.files.filter((file) => isTarget(file.path)) ?? [];
  const prompt = buildPackagePrompt(projectId, repository.name, { targetPath: automatic ? undefined : target, instructions });
  if (typeof document === "undefined") return null;
  return createPortal(<div className="fixed inset-0 z-[140] grid place-items-center bg-slate-950/50 p-3 sm:p-5" onMouseDown={onClose}>
    <section ref={dialogRef} tabIndex={-1} role="dialog" aria-modal="true" aria-labelledby="package-dialog-title" onMouseDown={(event) => event.stopPropagation()} onKeyDown={(event) => {
      if (event.key === "Escape") { event.stopPropagation(); onClose(); }
      if (event.key === "Tab") {
        const controls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled), input, textarea, summary, [tabindex="0"]'));
        const first = controls[0]; const last = controls.at(-1);
        if (event.shiftKey && (document.activeElement === first || document.activeElement === dialogRef.current)) { event.preventDefault(); last?.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
      }
    }} className="flex max-h-[90dvh] w-full max-w-2xl flex-col overflow-hidden rounded-2xl bg-white shadow-2xl outline-none">
      <header className="flex items-center gap-3 border-b border-slate-100 p-4">
        <PackageOpen size={22} className="shrink-0 text-blue-600"/>
        <div className="min-w-0 flex-1"><h2 id="package-dialog-title" className="font-semibold text-slate-900">配置打包</h2><p className="truncate text-xs text-slate-500">{repository.display_name} · 从代码库根目录选择入口</p></div>
        <button type="button" onClick={onClose} aria-label="关闭打包配置" className="rounded-lg p-2 text-slate-500 hover:bg-slate-100"><X size={18}/></button>
      </header>
      <div className="space-y-4 overflow-y-auto p-4">
        <div className="space-y-2">
          <p className="text-sm font-medium text-slate-800">打包入口</p>
          <p className="text-xs leading-5 text-slate-500">选择一个 .sln / .slnx、工程文件或 JSON（如 package.json）。多个解决方案时，只打包选定入口及其必要依赖。</p>
          <div className="overflow-hidden rounded-xl border border-slate-200">
            <div className="flex items-center gap-2 border-b border-slate-100 bg-slate-50 px-3 py-2 text-xs">
              <button type="button" onClick={() => setDirectory("")} className="shrink-0 text-blue-600">根目录</button>
              <span className="min-w-0 flex-1 truncate font-mono" title={directory}>/ {directory}</span>
              {directory && <button type="button" onClick={() => setDirectory(directory.split("/").slice(0, -1).join("/"))} className="shrink-0 text-blue-600">上一级</button>}
              <button type="button" disabled={loading} onClick={() => setReload((value) => value + 1)} className="text-blue-600 disabled:opacity-50">刷新</button>
            </div>
            <div className="h-48 overflow-y-auto p-2" aria-busy={loading}>
              {loading ? <p role="status" className="flex items-center gap-2 p-3 text-xs text-slate-500"><Loader2 size={14} className="animate-spin"/>正在读取目录…</p> : error ? <div role="alert" className="p-3 text-xs text-rose-700">{error}<button type="button" onClick={() => setReload((value) => value + 1)} className="ml-2 underline">重试</button></div> : <>
                {tree?.directories.map((item) => <button type="button" key={item.path} onClick={() => setDirectory(item.path.replace(/\\/g, "/"))} className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-xs hover:bg-slate-50"><Folder size={15} className="shrink-0 text-amber-500"/><span className="truncate">{item.name}</span></button>)}
                {files.map((file) => { const path = normalizePackageTarget(file.path); return <label key={path} className={`flex cursor-pointer items-center gap-2 rounded-lg px-3 py-2 text-xs ${!automatic && target === path ? "bg-blue-50 text-blue-700" : "hover:bg-slate-50"}`}>
                  <input type="radio" name="package-target" checked={!automatic && target === path} onChange={() => { setTarget(path); setAutomatic(false); }}/><File size={14} className="shrink-0"/><span className="truncate" title={path}>{file.name}</span>
                </label>; })}
                {!files.length && <p className="p-3 text-xs text-slate-500">当前目录没有可选入口，可进入子目录查找。</p>}
              </>}
            </div>
          </div>
          <p className="break-all text-xs text-blue-700" aria-live="polite">{automatic ? "由 AI 检查代码库并确定入口" : target ? `已选：${target}` : "尚未选择打包入口"}</p>
          <label className="flex items-center gap-2 text-xs text-slate-600"><input type="checkbox" checked={automatic} onChange={(event) => setAutomatic(event.target.checked)}/>由 AI 自动判断入口（可选）</label>
        </div>
        <label className="block text-sm font-medium text-slate-800">额外打包要求（可选）
          <textarea value={instructions} onChange={(event) => setInstructions(event.target.value)} rows={3} maxLength={8000} placeholder="例如：Release 模式，部署到 Windows x64；前端使用 build:prod，附上部署说明。" className="mt-2 w-full resize-y rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal outline-none focus:border-blue-500"/>
        </label>
        <details className="rounded-xl border border-slate-200 p-3 text-xs text-slate-600"><summary className="cursor-pointer font-medium">预览完整打包提示词</summary><pre className="mt-3 max-h-48 overflow-y-auto whitespace-pre-wrap break-words font-sans leading-5">{prompt}</pre></details>
      </div>
      <footer className="flex items-center justify-end gap-2 border-t border-slate-100 p-4">
        <button type="button" onClick={onClose} className="rounded-lg border border-slate-200 px-4 py-2 text-xs text-slate-600">取消</button>
        <button type="button" disabled={!automatic && !target} onClick={() => onApply(prompt)} className="rounded-lg bg-blue-600 px-4 py-2 text-xs font-medium text-white hover:bg-blue-700 disabled:bg-slate-300">填入聊天</button>
      </footer>
    </section>
  </div>, document.body);
}
