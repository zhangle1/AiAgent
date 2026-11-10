"use client";
import type { ChatSkill } from "@/lib/chat-skill-types";
import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Check, Target, X } from "lucide-react";

export function ChatSkillPicker({ items, selectedIds, activeIndex, onSelect, onPreview, onActiveIndexChange, anchor, onProjects, onDismiss, previewOpen, goalOption }: {
  goalOption?: { selected: boolean; disabledReason?: string; onSelect: () => void };
  items: ChatSkill[]; activeIndex: number; onSelect: (skill: ChatSkill) => void;
  onPreview: (skill: ChatSkill) => void; onActiveIndexChange: (index: number) => void;
  anchor: HTMLDivElement | null; onProjects: () => void; selectedIds: string[];
  onDismiss: () => void; previewOpen: boolean;
}) {
  const offset = goalOption ? 1 : 0;
  const menu = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ left: 16, bottom: 80, width: 380, maxHeight: 360 });
  useEffect(() => {
    if (previewOpen) return;
    const pointerDown = (event: PointerEvent) => {
      if (event.target instanceof Node && !menu.current?.contains(event.target) && !anchor?.contains(event.target)) onDismiss();
    };
    const keyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || event.isComposing) return;
      event.preventDefault(); event.stopPropagation(); onDismiss(); anchor?.focus();
    };
    document.addEventListener("pointerdown", pointerDown, true);
    document.addEventListener("keydown", keyDown, true);
    return () => { document.removeEventListener("pointerdown", pointerDown, true); document.removeEventListener("keydown", keyDown, true); };
  }, [anchor, onDismiss, previewOpen]);
  useEffect(() => {
    menu.current?.querySelectorAll<HTMLElement>('[role="option"]')[activeIndex]?.scrollIntoView({ block: "nearest" });
  }, [activeIndex]);
  useLayoutEffect(() => {
    const target = anchor?.closest("form") ?? anchor;
    const update = () => {
      const rect = target?.getBoundingClientRect();
      const width = Math.min(480, window.innerWidth - 16);
      if (rect) setPosition({ left: Math.max(8, Math.min(rect.left, window.innerWidth - width - 8)), bottom: Math.max(8, window.innerHeight - rect.top + 8), width, maxHeight: Math.min(360, window.innerHeight * 0.55, Math.max(0, rect.top - 16)) });
    };
    const observer = new ResizeObserver(update);
    if (target) observer.observe(target);
    update(); window.addEventListener("resize", update); window.addEventListener("scroll", update, true);
    return () => { observer.disconnect(); window.removeEventListener("resize", update); window.removeEventListener("scroll", update, true); };
  }, [anchor]);
  return createPortal(<div ref={menu} style={{ position: "fixed", ...position }} className="z-[80] flex flex-col overflow-hidden rounded-xl border border-slate-200 bg-white shadow-xl" role="listbox" aria-multiselectable="true" aria-label="主动调用技能">
    <div className="flex shrink-0 items-center justify-between gap-2 border-b border-slate-100 px-3 py-2">
      <div className="text-xs text-slate-500"><span className="font-medium text-slate-700">技能与目标 · 技能可多选</span>{selectedIds.length > 0 && <span className="ml-2 text-blue-600">已选 {selectedIds.length} 项</span>}<p className="mt-1 text-[10px]">↑↓ 选择，Enter / Tab 添加 · Esc 收起</p></div>
      <button type="button" aria-label="收起技能菜单" onClick={() => { onDismiss(); anchor?.focus(); }} className="rounded-md p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700"><X size={15} /></button>
    </div>
    <div className="min-h-0 overflow-y-auto p-2">
    {goalOption && <button type="button" role="option" aria-selected={goalOption.selected} disabled={Boolean(goalOption.disabledReason)} onMouseDown={event => event.preventDefault()} onMouseEnter={() => onActiveIndexChange(0)} onClick={goalOption.onSelect} className={`mb-1 flex w-full items-start gap-2 rounded-lg px-2 py-2 text-left disabled:opacity-50 ${activeIndex === 0 ? "bg-blue-50" : "hover:bg-slate-50"}`}><Target size={16} className="mt-0.5 shrink-0 text-blue-600" /><span><span className="block text-sm text-slate-800">目标模式 <span className="text-xs text-slate-400">/goal · 目标</span></span><span className="block text-xs text-slate-500">{goalOption.disabledReason || "设置目标与验收标准，发送后持续推进"}</span></span></button>}
    {items.length === 0 && !goalOption && <div className="p-2 text-xs text-slate-500">没有匹配技能或目标，可继续使用下方项目搜索。</div>}
    {items.map((skill, index) => <div key={skill.id} className={`flex items-center gap-2 rounded-lg ${index + offset === activeIndex ? "bg-blue-50" : ""}`}>
      <button type="button" role="option" aria-selected={selectedIds.includes(skill.id)} onMouseDown={(e) => e.preventDefault()} onMouseEnter={() => onActiveIndexChange(index + offset)} onClick={() => onSelect(skill)} className="flex min-w-0 flex-1 items-start gap-2 px-2 py-2 text-left">
        <span aria-hidden="true" className={`mt-0.5 grid h-4 w-4 shrink-0 place-items-center rounded border ${selectedIds.includes(skill.id) ? "border-blue-600 bg-blue-600 text-white" : "border-slate-300"}`}>{selectedIds.includes(skill.id) && <Check size={12} />}</span>
        <span className="min-w-0"><span className="block text-sm text-slate-800">{skill.name} <span className="text-xs text-slate-400">/{skill.id} · {skill.category}</span></span>
        <span className="block text-xs text-slate-500">{skill.description}</span></span>
      </button>
      <button type="button" className="shrink-0 px-2 text-xs text-blue-600" onClick={() => onPreview(skill)}>预览</button>
    </div>)}
    </div>
    <div className="flex shrink-0 items-center justify-between border-t border-slate-100 px-3 py-2"><button type="button" className="py-1 text-xs text-blue-600" onMouseDown={(event) => event.preventDefault()} onClick={onProjects}>搜索项目 / 文档 →</button><button type="button" onClick={() => { onDismiss(); anchor?.focus(); }} className="rounded-md bg-blue-600 px-3 py-1.5 text-xs text-white hover:bg-blue-700">完成</button></div>
  </div>, document.body);
}

export function ChatSkillPreview({ skill, onClose }: { skill: ChatSkill; onClose: () => void }) {
  return <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/30 p-4" onClick={onClose}>
    <section role="dialog" aria-modal="true" aria-label={`${skill.name}提示词预览`} className="max-h-[80vh] w-full max-w-xl overflow-y-auto rounded-xl bg-white p-5 shadow-xl" onClick={(event) => event.stopPropagation()} onKeyDown={(event) => { if (event.key === "Escape") onClose(); }}>
      <div className="flex justify-between gap-4"><h2 className="font-semibold">{skill.name} · /{skill.id}</h2><button type="button" autoFocus onClick={onClose}>关闭</button></div>
      <p className="my-3 text-sm text-slate-600">示例：{skill.example}</p>
      <p className="whitespace-pre-wrap text-sm leading-7">{skill.prompt}</p>
      <p className="mt-4 text-xs text-slate-500">中文流程改编 · {skill.source.license} · 版本 {skill.version.slice(0, 12)}</p>
      <a href={skill.source.url} target="_blank" rel="noreferrer" className="text-xs text-blue-600">查看固定版本来源：{skill.source.repository}</a>
    </section>
  </div>;
}
