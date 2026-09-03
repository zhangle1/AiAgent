"use client";

import { type PointerEvent as ReactPointerEvent, useCallback, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "next/navigation";
import { KnowledgeChatHome, type EmbeddedPrototypeFile } from "@/components/chat/KnowledgeChatHome";
import { getCodeProjects } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";
import { createPromptTemplate, deletePromptTemplate, listPromptTemplates, updatePromptTemplate } from "@/lib/prompt-template-api";
import type { PromptTemplate, PromptTemplateSaveRequest } from "@/lib/prompt-template-types";
import { decodePrototypeHtml, encodePrototypeHtml, extractPrototypeHtml, securePrototypePreview } from "@/lib/prototype-preview";
import { ChevronLeft, ChevronRight, Code2, Download, ExternalLink, FileCode2, FolderOpen, GripVertical, Loader2, Maximize2, Minimize2, Monitor, Pencil, Plus, RefreshCw, Search, Share2, Smartphone, Tablet, Trash2, X } from "lucide-react";

type Viewport = "desktop" | "tablet" | "mobile";
type StreamCompleteDetail = { projectId?: number; content?: string };

const PROTOTYPE_TAG = "prototype-html";
const PROTOTYPE_ORDER_KEY = "aiagent:prototype-file-order";

export function PrototypeStudio() {
  const search = useSearchParams();
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [projectId, setProjectId] = useState(search.get("project") ?? "");
  const [assets, setAssets] = useState<PromptTemplate[]>([]);
  const [activeId, setActiveId] = useState<number | null>(Number(search.get("prototype")) || null);
  const [query, setQuery] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [viewport, setViewport] = useState<Viewport>("desktop");
  const [showSource, setShowSource] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);
  const [draggedId, setDraggedId] = useState<number | null>(null);
  const [contextMenu, setContextMenu] = useState<{ asset: PromptTemplate; x: number; y: number } | null>(null);
  const [renameTarget, setRenameTarget] = useState<PromptTemplate | null>(null);
  const [renameValue, setRenameValue] = useState("");
  const [leftOpen, setLeftOpen] = useState(true);
  const [rightOpen, setRightOpen] = useState(true);
  const [leftWidth, setLeftWidth] = useState(272);
  const [rightWidth, setRightWidth] = useState(520);
  const [previewFullscreen, setPreviewFullscreen] = useState(false);

  const project = projects.find((item) => String(item.id) === projectId) ?? null;
  const active = assets.find((item) => item.id === activeId) ?? null;
  const visibleAssets = assets.filter((item) => !query.trim() || item.name.toLowerCase().includes(query.trim().toLowerCase()));
  const embeddedPrototypeFiles = useMemo<EmbeddedPrototypeFile[]>(() => assets.map((item) => ({ id: item.id, name: item.name, html: decodePrototypeHtml(item.body) })).filter((item) => item.html), [assets]);

  const loadAssets = useCallback(async () => {
    try {
      const items = await listPromptTemplates({ stage: "design" });
      const storedOrder = typeof window === "undefined" ? [] : readPrototypeOrder();
      const order = new Map(storedOrder.map((id, index) => [id, index]));
      setAssets(items.filter((item) => item.tags.includes(PROTOTYPE_TAG)).sort((left, right) => (order.get(left.id) ?? Number.MAX_SAFE_INTEGER) - (order.get(right.id) ?? Number.MAX_SAFE_INTEGER) || new Date(right.updated_at).getTime() - new Date(left.updated_at).getTime()));
    } catch (value) { setError(messageOf(value, "原型文件加载失败。")); }
  }, []);

  useEffect(() => {
    void Promise.all([getCodeProjects(), loadAssets()]).then(([items]) => {
      setProjects(items);
      if (!projectId && items[0]) setProjectId(String(items[0].id));
    }).catch((value) => setError(messageOf(value, "初始化原型工作台失败。"))).finally(() => setLoading(false));
  }, []); // 初次加载

  useEffect(() => {
    if (activeId && !assets.some((item) => item.id === activeId)) setActiveId(null);
  }, [activeId, assets]);

  useEffect(() => {
    const syncFullscreen = () => setPreviewFullscreen(Boolean(document.fullscreenElement));
    document.addEventListener("fullscreenchange", syncFullscreen);
    return () => document.removeEventListener("fullscreenchange", syncFullscreen);
  }, []);

  useEffect(() => {
    const receivePrototype = (event: Event) => {
      const detail = (event as CustomEvent<StreamCompleteDetail>).detail;
      if (project && detail?.projectId && detail.projectId !== project.id) return;
      const html = extractPrototypeHtml(detail?.content ?? "");
      if (!html) return;
      setSaving(true);
      const current = active;
      const payload: PromptTemplateSaveRequest = {
        name: current?.name ?? nameFromHtml(html),
        description: current?.description ?? "通过 AI 协作生成的 HTML 界面原型",
        stage: "design",
        tags: [PROTOTYPE_TAG],
        body: encodePrototypeHtml(html),
        variables: [],
        project_id: project?.id ?? null,
        visibility: current?.visibility ?? "personal",
      };
      const save = current ? updatePromptTemplate(current.id, payload) : createPromptTemplate(payload);
      void save.then((asset) => {
        setAssets((items) => [asset, ...items.filter((item) => item.id !== asset.id)]);
        setActiveId(asset.id);
        setShowSource(false);
      }).catch((value) => setError(messageOf(value, "原型保存失败。"))).finally(() => setSaving(false));
    };
    window.addEventListener("aiagent:chat-stream-complete", receivePrototype);
    return () => window.removeEventListener("aiagent:chat-stream-complete", receivePrototype);
  }, [active, project]);

  const activeHtml = useMemo(() => active ? decodePrototypeHtml(active.body) : "", [active]);
  const assistantInstruction = useMemo(() => {
    const context = active && activeHtml ? `当前正在编辑原型“${active.name}”。以下是当前 HTML，请按用户要求修改后返回完整替换版本：\n${activeHtml}` : "当前没有已选原型，请根据用户需求新建一个界面。";
    return `你是 HTML 原型设计助手。${context}\n如果用户用 / 引用了其他原型文件，则以被引用文件为唯一修改对象，忽略上述默认编辑文件。请只返回一个完整、可独立预览的 HTML 文档，必须用 \`\`\`html 代码块包裹；不要写入代码库、不要调用文件工具、不要输出额外说明。`;
  }, [active, activeHtml]);
  const preview = useMemo(() => active ? securePrototypePreview(activeHtml || incompletePrototypeDocument()) : "", [active, activeHtml]);
  const shareUrl = typeof window === "undefined" || !active ? "" : `${window.location.origin}/prototype-share?prototype=${active.id}`;

  function persistAssetOrder(next: PromptTemplate[]) {
    if (typeof window !== "undefined") window.localStorage.setItem(PROTOTYPE_ORDER_KEY, JSON.stringify(next.map((item) => item.id)));
  }

  function moveAsset(sourceId: number, targetId: number) {
    if (sourceId === targetId) return;
    setAssets((items) => {
      const sourceIndex = items.findIndex((item) => item.id === sourceId);
      const targetIndex = items.findIndex((item) => item.id === targetId);
      if (sourceIndex < 0 || targetIndex < 0) return items;
      const next = [...items];
      const [source] = next.splice(sourceIndex, 1);
      next.splice(targetIndex, 0, source);
      persistAssetOrder(next);
      return next;
    });
  }

  function downloadAsset(asset: PromptTemplate) {
    const anchor = document.createElement("a");
    const objectUrl = URL.createObjectURL(new Blob([decodePrototypeHtml(asset.body)], { type: "text/html;charset=utf-8" }));
    anchor.href = objectUrl;
    anchor.download = `${asset.name}.html`;
    anchor.click();
    URL.revokeObjectURL(objectUrl);
  }

  async function renameAsset() {
    const name = renameValue.trim().replace(/\.html$/i, "");
    if (!renameTarget || !name) return;
    try {
      const updated = await updatePromptTemplate(renameTarget.id, { name, description: renameTarget.description, stage: "design", tags: renameTarget.tags, body: renameTarget.body, variables: renameTarget.variables, project_id: renameTarget.project_id, visibility: renameTarget.visibility });
      setAssets((items) => items.map((item) => item.id === updated.id ? updated : item));
      setRenameTarget(null);
    } catch (value) { setError(messageOf(value, "重命名失败。")); }
  }

  async function removeAsset(asset: PromptTemplate) {
    try {
      await deletePromptTemplate(asset.id);
      setAssets((items) => {
        const next = items.filter((item) => item.id !== asset.id);
        persistAssetOrder(next);
        return next;
      });
      if (activeId === asset.id) setActiveId(null);
    } catch (value) { setError(messageOf(value, "删除失败。")); }
  }

  function startResize(side: "left" | "right", event: ReactPointerEvent<HTMLDivElement>) {
    event.preventDefault();
    const startX = event.clientX;
    const startWidth = side === "left" ? leftWidth : rightWidth;
    const update = (moveEvent: PointerEvent) => {
      const delta = moveEvent.clientX - startX;
      const next = Math.max(240, Math.min(720, side === "left" ? startWidth + delta : startWidth - delta));
      if (side === "left") setLeftWidth(next);
      else setRightWidth(next);
    };
    const stop = () => {
      window.removeEventListener("pointermove", update);
      window.removeEventListener("pointerup", stop);
    };
    window.addEventListener("pointermove", update);
    window.addEventListener("pointerup", stop);
  }

  async function togglePreviewFullscreen() {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else await document.documentElement.requestFullscreen();
    } catch { setPreviewFullscreen((value) => !value); }
  }

  return <div className="flex h-[100dvh] min-h-[640px] min-w-0 flex-col overflow-hidden bg-slate-100 text-slate-900">
    <header className="flex h-14 shrink-0 items-center gap-3 border-b border-slate-200 bg-white px-4">
      <span className="grid h-8 w-8 place-items-center rounded-lg bg-violet-100 text-violet-700"><Code2 size={17} /></span>
      <div className="min-w-0"><h1 className="text-sm font-semibold">界面原型</h1><p className="text-[10px] text-slate-400">描述功能，生成 HTML 原型；可预览、迭代和分享</p></div>
      <div className="ml-auto flex items-center gap-2"><select aria-label="原型关联项目" value={projectId} onChange={(event) => setProjectId(event.target.value)} className="h-9 max-w-48 rounded-lg border border-slate-200 bg-white px-2 text-xs outline-none"><option value="">不关联项目</option>{projects.map((item) => <option key={item.id} value={item.id}>{item.display_name}</option>)}</select><button type="button" onClick={() => { setActiveId(null); setShowSource(false); }} className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-violet-200 bg-violet-50 px-3 text-xs font-medium text-violet-700 hover:bg-violet-100"><Plus size={14} />新界面</button><button type="button" disabled={!active} onClick={() => setShareOpen(true)} className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-slate-900 px-3 text-xs font-medium text-white disabled:opacity-40"><Share2 size={14} />分享</button></div>
    </header>
    <div className="flex min-h-0 flex-1 overflow-hidden">
      {!previewFullscreen && <aside style={{ width: leftOpen ? leftWidth : 42 }} className="hidden min-h-0 shrink-0 flex-col border-r border-slate-200 bg-white transition-[width] duration-150 lg:flex">
        {!leftOpen ? <button type="button" onClick={() => setLeftOpen(true)} title="展开原型文件夹" className="grid h-12 w-full place-items-center border-b border-slate-100 text-violet-600 hover:bg-violet-50"><ChevronRight size={17} /></button> : <>
        <div className="flex h-12 items-center gap-2 border-b border-slate-100 px-3"><FolderOpen size={16} className="text-violet-600" /><div className="min-w-0 flex-1"><b className="block text-xs">我的原型文件夹</b><span className="block text-[10px] text-slate-400">{assets.length} 个 HTML 界面</span></div><button type="button" onClick={() => void loadAssets()} className="grid h-7 w-7 place-items-center rounded-md text-slate-400 hover:bg-slate-100" title="刷新"><RefreshCw size={14} /></button><button type="button" onClick={() => setLeftOpen(false)} title="收起原型文件夹" className="grid h-7 w-7 place-items-center rounded text-slate-400 hover:bg-violet-50 hover:text-violet-600"><ChevronLeft size={15} /></button></div>
        <div className="m-3 flex h-8 items-center gap-2 rounded-lg bg-slate-100 px-2"><Search size={13} className="text-slate-400" /><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="搜索界面名称" className="min-w-0 flex-1 bg-transparent text-xs outline-none" /></div>
        <div className="workspace-scroll min-h-0 flex-1 overflow-auto px-2 pb-3">
          {loading ? <Empty text="正在加载原型…" loading /> : visibleAssets.length ? visibleAssets.map((item) => (
            <div key={item.id} draggable onDragStart={() => setDraggedId(item.id)} onDragEnd={() => setDraggedId(null)} onDragOver={(event) => event.preventDefault()} onDrop={(event) => { event.preventDefault(); if (draggedId) moveAsset(draggedId, item.id); setDraggedId(null); }} onContextMenu={(event) => { event.preventDefault(); setContextMenu({ asset: item, x: event.clientX, y: event.clientY }); }} className={`group flex w-full items-center gap-1 rounded-lg px-1 py-0.5 text-xs ${draggedId === item.id ? "opacity-45" : ""}`}>
              <GripVertical size={14} className="shrink-0 cursor-grab text-slate-300 group-hover:text-violet-400" aria-hidden="true" />
              <button type="button" onClick={() => { setActiveId(item.id); setShowSource(false); }} className={`flex min-w-0 flex-1 items-center gap-2 rounded-lg px-1 py-2 text-left ${activeId === item.id ? "bg-violet-50 text-violet-700" : "text-slate-600 hover:bg-slate-100"}`}><FileCode2 size={15} className="shrink-0" /><span className="min-w-0 flex-1"><b className="block truncate font-medium">{item.name}.html</b><span className="mt-0.5 block truncate text-[10px] text-slate-400">{new Date(item.updated_at).toLocaleString()}</span></span></button>
            </div>
          )) : <Empty text="还没有原型。直接在右侧描述你想做的界面。" />}
        </div>
        <p className="border-t border-slate-100 px-3 py-3 text-[10px] leading-4 text-slate-400">拖拽可排序；右键可重命名、下载或删除。输入 / 可把左侧 HTML 引用给 AI 修改。</p>
        </>}
      </aside>}
      {!previewFullscreen && leftOpen && <div onPointerDown={(event) => startResize("left", event)} className="hidden w-1 shrink-0 cursor-col-resize bg-slate-100 hover:bg-violet-300 lg:block" aria-label="调整文件夹宽度" />}
      <section className="flex min-h-0 min-w-0 flex-1 flex-col"><div className="flex h-11 items-center gap-2 border-b border-slate-200 bg-white px-3"><div className="flex rounded-lg bg-slate-100 p-0.5"><button type="button" onClick={() => setShowSource(false)} className={`rounded-md px-3 py-1.5 text-[11px] ${!showSource ? "bg-white shadow-sm" : "text-slate-500"}`}>预览</button><button type="button" disabled={!active} onClick={() => setShowSource(true)} className={`rounded-md px-3 py-1.5 text-[11px] ${showSource ? "bg-white shadow-sm" : "text-slate-500"}`}>HTML 源码</button></div><span className="min-w-0 flex-1 truncate text-center font-mono text-[10px] text-slate-400">{active ? `${active.name}.html` : "描述一个功能，生成第一份界面原型"}</span><div className="flex gap-1">{([["desktop", Monitor], ["tablet", Tablet], ["mobile", Smartphone]] as const).map(([value, Icon]) => <button key={value} type="button" onClick={() => setViewport(value)} className={`grid h-7 w-7 place-items-center rounded ${viewport === value ? "bg-violet-100 text-violet-700" : "text-slate-400"}`}><Icon size={13} /></button>)}<button type="button" onClick={() => void togglePreviewFullscreen()} title={previewFullscreen ? "退出全屏预览" : "全屏预览"} className="grid h-7 w-7 place-items-center rounded text-slate-400 hover:bg-violet-100 hover:text-violet-700">{previewFullscreen ? <Minimize2 size={14} /> : <Maximize2 size={14} />}</button></div></div><div className="workspace-scroll min-h-0 flex-1 overflow-auto p-5">{!active ? <div className="grid h-full place-items-center"><div className="max-w-sm text-center"><FolderOpen className="mx-auto text-violet-400" size={42} /><h2 className="mt-4 text-base font-semibold">描述你想要的界面</h2><p className="mt-2 text-xs leading-5 text-slate-500">例如“做一个客户订单看板”。右侧聊天返回 HTML 后，它会自动保存到左侧文件夹。</p></div></div> : showSource ? <pre className="mx-auto min-h-full max-w-5xl overflow-auto rounded-xl bg-slate-950 p-5 text-xs leading-5 text-slate-100"><code>{activeHtml}</code></pre> : <div className={`mx-auto h-full min-h-[560px] overflow-hidden rounded-xl border border-slate-200 bg-white shadow-xl ${viewport === "mobile" ? "w-[390px]" : viewport === "tablet" ? "w-[768px]" : "w-full"}`}><iframe title={active.name} sandbox="allow-scripts" srcDoc={preview} className="h-full w-full border-0" /></div>}</div></section>
      {!previewFullscreen && rightOpen && <div onPointerDown={(event) => startResize("right", event)} className="hidden w-1 shrink-0 cursor-col-resize bg-slate-100 hover:bg-violet-300 lg:block" aria-label="调整聊天宽度" />}
      {!previewFullscreen && <aside style={{ width: rightOpen ? rightWidth : 42 }} className="flex min-h-0 shrink-0 flex-col border-l border-slate-200 bg-white transition-[width] duration-150">{!rightOpen ? <button type="button" onClick={() => setRightOpen(true)} title="展开原型协作聊天" className="grid h-12 w-full place-items-center border-b border-slate-100 text-violet-600 hover:bg-violet-50"><ChevronLeft size={17} /></button> : <><div className="flex h-10 items-center gap-2 border-b border-slate-100 px-3"><Code2 size={14} className="text-violet-600" /><b className="min-w-0 flex-1 truncate text-xs">原型协作聊天</b><span className="truncate text-[10px] text-slate-400">{saving ? "正在保存 HTML…" : active ? `编辑：${active.name}` : "将创建新界面"}</span><button type="button" onClick={() => setRightOpen(false)} title="收起原型协作聊天" className="grid h-7 w-7 place-items-center rounded text-slate-400 hover:bg-violet-50 hover:text-violet-600"><ChevronRight size={15} /></button></div><div className="min-h-0 flex-1 overflow-hidden"><KnowledgeChatHome key={projectId || "standalone"} embedded embeddedProjectId={project ? project.id : null} embeddedProjectLocked embeddedMessagePrefix={assistantInstruction} embeddedPrototypeFiles={embeddedPrototypeFiles} onEmbeddedPrototypeReferenceSelect={(id) => { setActiveId(id); setShowSource(false); }} /></div></>}</aside>}
    </div>
    {contextMenu && <div className="fixed z-[60] w-36 overflow-hidden rounded-xl border border-slate-200 bg-white p-1 shadow-xl" style={{ left: contextMenu.x, top: contextMenu.y }} onMouseLeave={() => setContextMenu(null)}><button type="button" onClick={() => { setRenameTarget(contextMenu.asset); setRenameValue(contextMenu.asset.name); setContextMenu(null); }} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-xs hover:bg-slate-50"><Pencil size={13} />重命名</button><button type="button" onClick={() => { downloadAsset(contextMenu.asset); setContextMenu(null); }} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-xs hover:bg-slate-50"><Download size={13} />下载 HTML</button><button type="button" onClick={() => { const asset = contextMenu.asset; setContextMenu(null); void removeAsset(asset); }} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-xs text-rose-600 hover:bg-rose-50"><Trash2 size={13} />删除</button></div>}
    {renameTarget && <RenameDialog value={renameValue} onChange={setRenameValue} onClose={() => setRenameTarget(null)} onSave={() => void renameAsset()} />}
    {error && <div className="fixed bottom-4 left-1/2 z-50 flex max-w-lg -translate-x-1/2 items-center gap-3 rounded-xl bg-rose-600 px-4 py-3 text-xs text-white shadow-xl">{error}<button type="button" onClick={() => setError("")}><X size={14} /></button></div>}{shareOpen && active && <ShareDialog url={shareUrl} asset={active} onClose={() => setShareOpen(false)} onShared={(asset) => setAssets((items) => items.map((item) => item.id === asset.id ? asset : item))} />}
  </div>;
}

function ShareDialog({ url, asset, onClose, onShared }: { url: string; asset: PromptTemplate; onClose: () => void; onShared: (asset: PromptTemplate) => void }) {
  const [copied, setCopied] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const copyLink = async () => {
    setBusy(true);
    setError("");
    const copyAttempt = copyText(url);
    try {
      const next = asset.visibility === "team" ? asset : await updatePromptTemplate(asset.id, { name: asset.name, description: asset.description, stage: "design", tags: asset.tags, body: asset.body, variables: asset.variables, project_id: asset.project_id, visibility: "team" });
      onShared(next);
      if (!await copyAttempt) throw new Error("浏览器未允许自动复制，请选中上方链接后手动复制。");
      setCopied(true);
    } catch (value) {
      setError(messageOf(value, "分享链接复制失败。"));
    } finally {
      setBusy(false);
    }
  };

  const download = () => {
    const anchor = document.createElement("a");
    const objectUrl = URL.createObjectURL(new Blob([decodePrototypeHtml(asset.body)], { type: "text/html;charset=utf-8" }));
    anchor.href = objectUrl;
    anchor.download = `${asset.name}.html`;
    anchor.click();
    URL.revokeObjectURL(objectUrl);
  };

  return <div className="fixed inset-0 z-[70] grid place-items-center bg-slate-950/45 p-4" onClick={onClose}><section onClick={(event) => event.stopPropagation()} className="w-full max-w-lg rounded-2xl bg-white p-5 shadow-2xl"><div className="flex"><h2 className="font-semibold">分享 HTML 原型</h2><button type="button" onClick={onClose} className="ml-auto"><X size={17} /></button></div><p className="mt-2 text-xs leading-5 text-slate-500">获得链接的人可直接打开当前原型，无需登录；之后继续编辑会自动更新分享版本。</p><div className="mt-4 flex rounded-lg bg-slate-100 p-2"><input readOnly value={url} onFocus={(event) => event.currentTarget.select()} className="min-w-0 flex-1 bg-transparent text-xs outline-none" /><button type="button" disabled={busy} onClick={() => void copyLink()} className="ml-2 shrink-0 rounded bg-white px-3 py-1.5 text-xs disabled:opacity-50">{busy ? "处理中…" : copied ? "已复制" : "复制链接"}</button></div>{error && <p className="mt-2 text-xs text-rose-600">{error}</p>}<div className="mt-4 flex justify-end"><button type="button" onClick={download} className="inline-flex items-center gap-1 rounded-lg border px-3 py-2 text-xs"><ExternalLink size={13} />下载 HTML</button></div></section></div>;
}
function RenameDialog({ value, onChange, onClose, onSave }: { value: string; onChange: (value: string) => void; onClose: () => void; onSave: () => void }) { return <div className="fixed inset-0 z-[70] grid place-items-center bg-slate-950/45 p-4" onClick={onClose}><form onSubmit={(event) => { event.preventDefault(); onSave(); }} onClick={(event) => event.stopPropagation()} className="w-full max-w-sm rounded-2xl bg-white p-5 shadow-2xl"><h2 className="text-sm font-semibold">重命名 HTML 原型</h2><input autoFocus value={value} onChange={(event) => onChange(event.target.value)} onFocus={(event) => event.currentTarget.select()} className="mt-4 h-10 w-full rounded-lg border border-slate-200 px-3 text-sm outline-none focus:border-violet-400" /><p className="mt-2 text-[11px] text-slate-400">扩展名 .html 会自动保留。</p><div className="mt-5 flex justify-end gap-2"><button type="button" onClick={onClose} className="rounded-lg border px-3 py-2 text-xs">取消</button><button type="submit" disabled={!value.trim()} className="rounded-lg bg-violet-600 px-3 py-2 text-xs text-white disabled:opacity-40">保存</button></div></form></div>; }
function Empty({ text, loading = false }: { text: string; loading?: boolean }) { return <div className="grid h-40 place-items-center px-5 text-center text-xs leading-5 text-slate-400">{loading ? <Loader2 size={16} className="animate-spin" /> : text}</div>; }
function nameFromHtml(html: string) { const title = html.match(/<title[^>]*>([\s\S]*?)<\/title>/i)?.[1].replace(/<[^>]+>/g, "").trim(); return (title || "新界面原型").slice(0, 120); }
function readPrototypeOrder(): number[] { try { const value = JSON.parse(window.localStorage.getItem(PROTOTYPE_ORDER_KEY) ?? "[]"); return Array.isArray(value) ? value.filter((item): item is number => typeof item === "number") : []; } catch { return []; } }
function incompletePrototypeDocument() { return "<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><title>原型需要重新生成</title><style>body{margin:0;display:grid;min-height:100vh;place-items:center;background:#f8fafc;color:#0f172a;font-family:system-ui,-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif}.card{max-width:420px;padding:36px;border:1px solid #e2e8f0;border-radius:18px;background:#fff;box-shadow:0 18px 50px rgba(15,23,42,.08);text-align:center}h1{margin:0;font-size:20px}p{margin:14px 0 0;color:#64748b;font-size:14px;line-height:1.7}</style></head><body><main class=\"card\"><h1>这份原型需要重新生成</h1><p>右侧描述一次界面需求，生成完成后会自动替换为可预览的 HTML。</p></main></body></html>"; }
function messageOf(value: unknown, fallback: string) { return value instanceof Error ? value.message : fallback; }
function copyText(value: string) { if (navigator.clipboard?.writeText) return navigator.clipboard.writeText(value).then(() => true).catch(() => copyTextFallback(value)); return Promise.resolve(copyTextFallback(value)); }
function copyTextFallback(value: string) { const textarea = document.createElement("textarea"); textarea.value = value; textarea.style.cssText = "position:fixed;opacity:0;pointer-events:none"; document.body.appendChild(textarea); textarea.select(); const copied = document.execCommand("copy"); textarea.remove(); return copied; }
