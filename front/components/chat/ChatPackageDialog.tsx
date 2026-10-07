"use client";

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Check, File, Folder, Loader2, PackageOpen, ScanSearch, Sparkles, X } from "lucide-react";
import { getCodeTree, type CodeTree } from "@/lib/code-repository-api";
import type { CodeRepository } from "@/lib/code-repository-types";
import { buildPackagePrompt, normalizePackageTarget, normalizePackageTargets, suggestPackageTargets } from "@/lib/chat-packaging";

type PackageDraft = { selectedTargets: string[]; automatic: boolean; instructions: string };

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
  const [selectedTargets, setSelectedTargets] = useState<string[]>([]);
  const [automatic, setAutomatic] = useState(true);
  const [instructions, setInstructions] = useState("");
  const [draftReady, setDraftReady] = useState(false);
  const [detectedTargets, setDetectedTargets] = useState<string[]>([]);
  const [detectedProfiles, setDetectedProfiles] = useState<string[]>([]);
  const [detecting, setDetecting] = useState(false);
  const [hasDetected, setHasDetected] = useState(false);
  const dialogRef = useRef<HTMLElement>(null);

  const draftStorageKey = `aiagent:package-draft:${projectId}:${repository.name}`;

  useEffect(() => {
    setDraftReady(false);
    setDirectory("");
    setSelectedTargets([]);
    setAutomatic(true);
    setInstructions("");
    setDetectedTargets([]);
    setDetectedProfiles([]);
    setHasDetected(false);
    try {
      const raw = window.localStorage.getItem(draftStorageKey);
      if (raw) {
        const draft = JSON.parse(raw) as Partial<PackageDraft>;
        setSelectedTargets(Array.isArray(draft.selectedTargets) ? draft.selectedTargets.filter((path): path is string => typeof path === "string" && isTarget(path)) : []);
        setAutomatic(draft.automatic !== false);
        setInstructions(typeof draft.instructions === "string" ? draft.instructions.slice(0, 8000) : "");
      }
    } catch {
      // Local storage is optional; the dialog remains fully usable when it is unavailable.
    } finally {
      setDraftReady(true);
    }
  }, [draftStorageKey]);

  useEffect(() => {
    if (!draftReady) return;
    try {
      window.localStorage.setItem(draftStorageKey, JSON.stringify({ selectedTargets, automatic, instructions } satisfies PackageDraft));
    } catch {
      // Do not block packaging when storage is disabled or full.
    }
  }, [automatic, draftReady, draftStorageKey, instructions, selectedTargets]);

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
  const prompt = buildPackagePrompt(projectId, repository.name, { targetPaths: selectedTargets, automatic, instructions });
  const toggleTarget = (path: string) => setSelectedTargets((items) => items.includes(path) ? items.filter((item) => item !== path) : [...items, path]);
  const detectCandidates = () => {
    setDetecting(true);
    try {
      const inspectedPaths = [
        ...(repository.solution_files ?? []),
        ...(repository.configuration_files ?? []).filter((path) => path.toLowerCase().endsWith("package.json")),
        ...(tree?.files ?? []).map((file) => file.path),
      ];
      const candidates = suggestPackageTargets(inspectedPaths, repository.publish_target);
      setDetectedTargets(candidates);
      setSelectedTargets((items) => normalizePackageTargets([...items, ...candidates]));
      setDetectedProfiles((repository.configuration_files ?? []).filter((path) => path.toLowerCase().endsWith(".pubxml")));
      setHasDetected(true);
    } finally {
      setDetecting(false);
    }
  };
  const addDetected = (path: string) => setSelectedTargets((items) => normalizePackageTargets([...items, path]));
  const addAllDetected = () => setSelectedTargets((items) => normalizePackageTargets([...items, ...detectedTargets]));
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
          <p className="text-xs leading-5 text-slate-500">可多选 .sln / .slnx、工程文件或 JSON（如 package.json）作为候选。默认由 AI 探测、验证并判断真正需要打包的一个或多个入口。</p>
          <div className="overflow-hidden rounded-xl border border-slate-200">
            <div className="flex items-center gap-2 border-b border-slate-100 bg-slate-50 px-3 py-2 text-xs">
              <button type="button" onClick={() => setDirectory("")} className="shrink-0 text-blue-600">根目录</button>
              <span className="min-w-0 flex-1 truncate font-mono" title={directory}>/ {directory}</span>
              {directory && <button type="button" onClick={() => setDirectory(directory.split("/").slice(0, -1).join("/"))} className="shrink-0 text-blue-600">上一级</button>}
              <button type="button" disabled={loading} onClick={() => setReload((value) => value + 1)} className="text-blue-600 disabled:opacity-50">刷新</button>
              <button type="button" disabled={loading || detecting} onClick={detectCandidates} className="ml-auto inline-flex shrink-0 items-center gap-1 rounded-md border border-blue-200 bg-white px-2 py-1 text-blue-700 hover:bg-blue-50 disabled:opacity-50" title="根据代码库已识别文件探测并加入打包候选"><Sparkles size={13}/>{detecting ? "探测中" : "探测并加入"}</button>
            </div>
            <div className="h-48 overflow-y-auto p-2" aria-busy={loading}>
              {loading ? <p role="status" className="flex items-center gap-2 p-3 text-xs text-slate-500"><Loader2 size={14} className="animate-spin"/>正在读取目录…</p> : error ? <div role="alert" className="p-3 text-xs text-rose-700">{error}<button type="button" onClick={() => setReload((value) => value + 1)} className="ml-2 underline">重试</button></div> : <>
                {tree?.directories.map((item) => <button type="button" key={item.path} onClick={() => setDirectory(item.path.replace(/\\/g, "/"))} className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-xs hover:bg-slate-50"><Folder size={15} className="shrink-0 text-amber-500"/><span className="truncate">{item.name}</span></button>)}
                {files.map((file) => { const path = normalizePackageTarget(file.path); const selected = selectedTargets.includes(path); return <label key={path} className={`flex cursor-pointer items-center gap-2 rounded-lg px-3 py-2 text-xs ${selected ? "bg-blue-50 text-blue-700" : "hover:bg-slate-50"}`}>
                  <input type="checkbox" checked={selected} onChange={() => toggleTarget(path)}/><File size={14} className="shrink-0"/><span className="truncate" title={path}>{file.name}</span>
                </label>; })}
                {!files.length && <p className="p-3 text-xs text-slate-500">当前目录没有可选入口，可进入子目录查找。</p>}
              </>}
            </div>
          </div>
          {hasDetected && <div className="rounded-xl border border-violet-100 bg-violet-50/60 px-3 py-2.5 text-xs text-violet-900">
            <div className="flex items-center gap-2"><ScanSearch size={14} className="shrink-0 text-violet-600"/><span className="font-medium">已探测 {detectedTargets.length} 个候选入口</span><button type="button" disabled={!detectedTargets.length} onClick={addAllDetected} className="ml-auto rounded-md bg-violet-600 px-2 py-1 text-[11px] font-medium text-white hover:bg-violet-700 disabled:bg-violet-200">全部加入</button></div>
            <p className="mt-1 leading-5 text-violet-700">候选来自代码库识别结果，加入后仍由聊天 AI 检查项目结构并决定真正的构建入口。</p>
            {detectedTargets.length > 0 && <div className="mt-2 space-y-1">{detectedTargets.map((path) => { const selected = selectedTargets.includes(path); return <div key={path} className="flex items-center gap-2 rounded-md bg-white/70 px-2 py-1.5"><span className="min-w-0 flex-1 truncate font-mono text-[11px]" title={path}>{path}</span>{selected ? <span className="inline-flex shrink-0 items-center gap-1 text-[11px] text-emerald-700"><Check size={12}/>已加入</span> : <button type="button" onClick={() => addDetected(path)} className="shrink-0 text-[11px] font-medium text-violet-700 hover:underline">+ 加入</button>}</div>; })}</div>}
            {detectedProfiles.length > 0 && <p className="mt-2 break-all text-[11px] text-violet-700">发现发布配置：{detectedProfiles.join("、")}。填入聊天后，AI 会继续读取并参考它。</p>}
            {!detectedTargets.length && <p className="mt-2 text-[11px] text-amber-700">没有发现可识别的解决方案、工程或 package.json，请刷新目录后手动进入子目录检查。</p>}
          </div>}
          {selectedTargets.length > 0 && <div className="rounded-lg bg-blue-50 px-3 py-2 text-xs text-blue-800"><div className="font-medium">已保存 {selectedTargets.length} 个候选入口</div><div className="mt-1 space-y-1">{selectedTargets.map((path) => <div key={path} className="flex items-center gap-2"><span className="min-w-0 flex-1 truncate font-mono" title={path}>{path}</span><button type="button" onClick={() => toggleTarget(path)} className="shrink-0 underline">移除</button></div>)}</div></div>}
          <p className="break-all text-xs text-blue-700" aria-live="polite">{automatic ? "AI 将自动探测并判断入口" : selectedTargets.length ? `仅使用已选的 ${selectedTargets.length} 个入口` : "请至少选择一个入口，或开启 AI 自动判断"}</p>
          <label className="flex items-center gap-2 text-xs text-slate-600"><input type="checkbox" checked={automatic} onChange={(event) => setAutomatic(event.target.checked)}/>让 AI 自动探测并判断入口（推荐）</label>
          <p className="text-[11px] text-slate-400">选择、模式和补充要求会自动保存，下次打开此代码库时恢复。</p>
        </div>
        <label className="block text-sm font-medium text-slate-800">额外打包要求（可选）
          <textarea value={instructions} onChange={(event) => setInstructions(event.target.value)} rows={3} maxLength={8000} placeholder="例如：Release 模式，部署到 Windows x64；前端使用 build:prod，附上部署说明。" className="mt-2 w-full resize-y rounded-xl border border-slate-200 px-3 py-2 text-sm font-normal outline-none focus:border-blue-500"/>
        </label>
        <details className="rounded-xl border border-slate-200 p-3 text-xs text-slate-600"><summary className="cursor-pointer font-medium">预览完整打包提示词</summary><pre className="mt-3 max-h-48 overflow-y-auto whitespace-pre-wrap break-words font-sans leading-5">{prompt}</pre></details>
      </div>
      <footer className="flex items-center justify-end gap-2 border-t border-slate-100 p-4">
        <button type="button" onClick={onClose} className="rounded-lg border border-slate-200 px-4 py-2 text-xs text-slate-600">取消</button>
        <button type="button" disabled={!automatic && selectedTargets.length === 0} onClick={() => onApply(prompt)} className="rounded-lg bg-blue-600 px-4 py-2 text-xs font-medium text-white hover:bg-blue-700 disabled:bg-slate-300">填入聊天</button>
      </footer>
    </section>
  </div>, document.body);
}
