"use client";
import type { ChatSkill } from "@/lib/chat-skill-types";
import { useLayoutEffect, useState } from "react";
import { createPortal } from "react-dom";

export function ChatSkillPicker({ items, activeIndex, onSelect, onPreview, onActiveIndexChange, anchor, onProjects }: {
  items: ChatSkill[]; activeIndex: number; onSelect: (skill: ChatSkill) => void;
  onPreview: (skill: ChatSkill) => void; onActiveIndexChange: (index: number) => void;
  anchor: HTMLDivElement | null; onProjects: () => void;
}) {
  const [position, setPosition] = useState({ left: 16, bottom: 80, width: 380 });
  useLayoutEffect(() => {
    const update = () => {
      const rect = anchor?.getBoundingClientRect();
      const width = Math.min(480, window.innerWidth - 16);
      if (rect) setPosition({ left: Math.max(8, Math.min(rect.left, window.innerWidth - width - 8)), bottom: Math.max(8, window.innerHeight - rect.top + 8), width });
    };
    update(); window.addEventListener("resize", update); window.addEventListener("scroll", update, true);
    return () => { window.removeEventListener("resize", update); window.removeEventListener("scroll", update, true); };
  }, [anchor]);
  return createPortal(<div style={{ position: "fixed", ...position, maxHeight: "min(360px, 55vh)" }} className="z-[80] overflow-y-auto rounded-xl border border-slate-200 bg-white p-2 shadow-xl" role="listbox" aria-label="主动调用技能">
    <div className="px-2 py-1 text-xs text-slate-500">技能 · 仅明确选择后启用 · ↑↓ 选择，Enter / Tab 确认</div>
    {items.length === 0 && <div className="p-2 text-xs text-slate-500">没有匹配技能，可继续使用下方项目搜索。</div>}
    {items.map((skill, index) => <div key={skill.id} className={`flex items-center gap-2 rounded-lg ${index === activeIndex ? "bg-blue-50" : ""}`}>
      <button type="button" role="option" aria-selected={index === activeIndex} onMouseDown={(e) => e.preventDefault()} onMouseEnter={() => onActiveIndexChange(index)} onClick={() => onSelect(skill)} className="min-w-0 flex-1 px-2 py-2 text-left">
        <div className="text-sm text-slate-800">{skill.name} <span className="text-xs text-slate-400">/{skill.id} · {skill.category}</span></div>
        <div className="text-xs text-slate-500">{skill.description}</div>
      </button>
      <button type="button" className="shrink-0 px-2 text-xs text-blue-600" onClick={() => onPreview(skill)}>预览</button>
    </div>)}
    <button type="button" className="w-full border-t p-2 text-left text-xs text-blue-600" onMouseDown={(event) => event.preventDefault()} onClick={onProjects}>搜索项目 / 文档 →</button>
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
