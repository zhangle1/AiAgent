"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { PsdFiles } from "./PsdFiles";
import { chatPrototypeOutputs, PROTOTYPE_DESIGN_INSTRUCTION, type ChatPrototypeOutput } from "@/lib/chat-prototype";
import { decodePrototypeHtml, encodePrototypeHtml, securePrototypePreview } from "@/lib/prototype-preview";
import { createPromptTemplate, listPromptTemplates, updatePromptTemplate } from "@/lib/prompt-template-api";
import type { PromptTemplate } from "@/lib/prompt-template-types";
import type { CodeProjectMarkdownDocument } from "@/lib/code-repository-types";
import { getProjectPrototypeHtml } from "@/lib/code-repository-api";

export function ChatPrototypePanel({ projectId, messages, onAdjust, requestedDocument, requestedOutput }: {
  projectId: number | null; messages: { id: string; content: string }[]; onAdjust?: (prompt: string) => void; requestedDocument?: CodeProjectMarkdownDocument | null; requestedOutput?: ChatPrototypeOutput | null;
}) {
  const outputs = useMemo(() => chatPrototypeOutputs(messages), [messages]);
  const [assets, setAssets] = useState<PromptTemplate[]>([]);
  const [selected, setSelected] = useState("");
  const [html, setHtml] = useState("");
  const [name, setName] = useState("");
  const [notes, setNotes] = useState("");
  const [source, setSource] = useState(false);
  const [width, setWidth] = useState(1440);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const alive = useRef(true);
  useEffect(() => {
    if (!requestedOutput) return;
    setSelected(requestedOutput.id); setName(requestedOutput.name); setHtml(requestedOutput.html); setNotes(""); setError(""); setNotice("");
  }, [requestedOutput]);
  useEffect(() => {
    if (!requestedDocument || !projectId) return;
    const controller = new AbortController();
    setError(""); setSelected(""); setHtml(""); setNotes("");
    void getProjectPrototypeHtml(projectId, requestedDocument.repository_name, requestedDocument.path, controller.signal).then(html => {
      if (controller.signal.aborted) return;
      setSelected("document"); setName(requestedDocument.name.replace(/\.html?$/i, "")); setHtml(html);
    }).catch(error => { if (!controller.signal.aborted) setError(error instanceof Error ? error.message : "读取原型失败"); });
    return () => controller.abort();
  }, [requestedDocument, projectId]);
  useEffect(() => {
    alive.current = true;
    let cancelled = false;
    void listPromptTemplates({ stage: "design" }).then(items => {
      if (!cancelled) setAssets(items.filter(item => item.tags.includes("prototype-html") && (item.project_id ?? null) === projectId));
    }).catch(error => { if (!cancelled) setError(error instanceof Error ? error.message : "原型加载失败"); });
    return () => { cancelled = true; alive.current = false; };
  }, [projectId]);
  const activeAsset = assets.find(item => `saved:${item.id}` === selected);
  const preview = useMemo(() => html ? securePrototypePreview(html) : "", [html]);
  const button = "rounded border border-slate-200 bg-white px-2 py-1 text-xs hover:bg-violet-50 disabled:opacity-40";

  function choose(id: string) {
    setSelected(id); setError(""); setNotice("");
    const asset = assets.find(item => `saved:${item.id}` === id);
    const output = outputs.find(item => item.id === id);
    setHtml(asset ? decodePrototypeHtml(asset.body) : output?.html ?? "");
    setName(asset?.name ?? output?.name ?? "");
    setNotes(asset?.description ?? "");
  }

  async function save() {
    setBusy(true); setError(""); setNotice("");
    try {
      const payload = { name: name.trim() || "界面原型", body: encodePrototypeHtml(html), description: notes, stage: "design" as const, tags: activeAsset?.tags ?? ["prototype-html"], variables: activeAsset?.variables ?? [], project_id: projectId, visibility: activeAsset?.visibility ?? "personal" as const };
      const asset = activeAsset ? await updatePromptTemplate(activeAsset.id, payload) : await createPromptTemplate(payload);
      if (!alive.current) return;
      setAssets(items => [asset, ...items.filter(item => item.id !== asset.id)]);
      setSelected(`saved:${asset.id}`); setNotice("HTML 与注解已保存，可在原型工作台继续编辑。");
    } catch (error) { if (alive.current) setError(error instanceof Error ? error.message : "保存失败"); }
    finally { if (alive.current) setBusy(false); }
  }

  function download() {
    const url = URL.createObjectURL(new Blob([html], { type: "text/html;charset=utf-8" }));
    const link = document.createElement("a"); link.href = url; link.download = `${name || "prototype"}.html`; link.click();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  return <section className="flex min-h-0 flex-1 flex-col overflow-hidden bg-slate-50" aria-label="原型设计">
    <div className="flex flex-wrap items-center gap-2 border-b bg-white p-3">
      <select aria-label="选择原型产出" className="min-w-0 flex-1 rounded border p-1 text-sm" value={selected} disabled={busy} onChange={event => {
        if (selected && !window.confirm("切换原型将丢弃未保存的编辑，是否继续？")) return;
        choose(event.target.value);
      }}>
        <option value="">选择聊天产出或已保存原型</option>
        {selected === "document" && <option value="document">项目文件 · {name}</option>}
        <optgroup label="当前会话 HTML 产出">{outputs.map(output => <option key={output.id} value={output.id}>{output.name}</option>)}</optgroup>
        <optgroup label="当前项目已保存原型">{assets.map(asset => <option key={asset.id} value={`saved:${asset.id}`}>{asset.name}</option>)}</optgroup>
      </select>
      <a className={button} href={`/prototype-studio${projectId ? `?project=${projectId}` : ""}`} target="_blank" rel="noreferrer">完整工作台</a>
    </div>
    {error && <p role="alert" className="p-2 text-xs text-rose-700">{error}</p>}
    {notice && <p role="status" className="p-2 text-xs text-green-700">{notice}</p>}
    {!selected ? <div className="p-6 text-sm leading-7 text-slate-500">在聊天「+」中启用设计模式，发送界面需求。完整 HTML 产出会列在这里；选择后可以预览、编辑源码、保存注解和生成 PSD。</div> : <>
      <div className="flex flex-wrap items-center gap-2 p-2">
        <input aria-label="原型名称" disabled={busy} value={name} onChange={event => setName(event.target.value)} className="min-w-0 flex-1 rounded border px-2 py-1 text-xs" />
        <button className={button} onClick={() => setSource(value => !value)}>{source ? "预览" : "编辑 HTML"}</button>
        <select aria-label="预览尺寸" className={button} value={width} onChange={event => setWidth(Number(event.target.value))}><option value={1440}>桌面</option><option value={768}>平板</option><option value={390}>移动端</option></select>
        <button className={button} disabled={busy || !html.trim()} onClick={() => void save()}>{busy ? "保存中…" : "保存原型与注解"}</button>
        <button className={button} disabled={!html.trim()} onClick={download}>下载 HTML</button>
      </div>
      <div className="min-h-[180px] flex-1 overflow-auto bg-slate-200 p-2">
        {source ? <textarea aria-label="HTML 源码" disabled={busy} value={html} onChange={event => setHtml(event.target.value)} className="h-full min-h-[280px] w-full resize-none bg-white p-3 font-mono text-xs" spellCheck={false} /> : <iframe title="原型预览" sandbox="" srcDoc={preview} style={{ width, height: "100%", minHeight: 400 }} className="border-0 bg-white" />}
      </div>
      <div className="max-h-[38%] shrink-0 overflow-auto border-t bg-white p-2">
        <label className="block text-xs text-slate-600">注解与调整要求<textarea disabled={busy} value={notes} onChange={event => setNotes(event.target.value)} placeholder="例如：导航栏改为深色，缩小右侧卡片间距…" className="mt-1 block w-full rounded border p-2 text-xs" rows={2}/></label>
        <button className={`${button} my-2`} disabled={!onAdjust || busy || !html.trim()} onClick={() => onAdjust?.(`${PROTOTYPE_DESIGN_INSTRUCTION}\n原型名称：${name}\n调整要求：${notes || "请根据下一条需求调整"}\n\n\`\`\`html\n${html}\n\`\`\``)}>带入聊天调整原型</button>
        <PsdFiles key={selected} name={name || "prototype"} html={html}/>
      </div>
    </>}
  </section>;
}
