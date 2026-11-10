"use client";

import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Network, Paperclip, Sparkles, Target } from "lucide-react";

export function ChatComposerMenu({ anchor, onClose, onFiles, filesEnabled, onSkills, onGoal, goalDisabledReason, onVisualization }: {
  anchor: HTMLButtonElement; onClose: () => void; onFiles: () => void; filesEnabled: boolean;
  onSkills?: () => void; onGoal?: () => void; goalDisabledReason?: string; onVisualization?: () => void;
}) {
  const menu = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ left: 8, bottom: 80, width: 320, maxHeight: 360 });
  useLayoutEffect(() => {
    const target = anchor.closest("form") ?? anchor;
    const update = () => {
      const rect = target.getBoundingClientRect();
      const top = rect.top >= 180 ? rect.top : anchor.getBoundingClientRect().top;
      const width = Math.min(360, window.innerWidth - 16);
      setPosition({ left: Math.max(8, Math.min(rect.left, window.innerWidth - width - 8)), bottom: Math.max(8, window.innerHeight - top + 8), width, maxHeight: Math.max(0, Math.min(360, top - 16)) });
    };
    const observer = new ResizeObserver(update); observer.observe(target); update();
    window.addEventListener("resize", update); window.addEventListener("scroll", update, true);
    return () => { observer.disconnect(); window.removeEventListener("resize", update); window.removeEventListener("scroll", update, true); };
  }, [anchor]);
  useEffect(() => {
    menu.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus();
    const pointer = (event: PointerEvent) => { if (event.target instanceof Node && !menu.current?.contains(event.target) && !anchor.contains(event.target)) onClose(); };
    const key = (event: KeyboardEvent) => {
      if (event.isComposing) return;
      if (event.key === "Tab") { onClose(); return; }
      if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); onClose(); anchor.focus(); }
      if (["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
        event.preventDefault();
        const items = Array.from(menu.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? []);
        const current = items.indexOf(document.activeElement as HTMLButtonElement);
        const index = event.key === "Home" ? 0 : event.key === "End" ? items.length - 1 : (current + (event.key === "ArrowUp" ? -1 : 1) + items.length) % items.length;
        items[index]?.focus();
      }
    };
    document.addEventListener("pointerdown", pointer, true); document.addEventListener("keydown", key, true);
    return () => { document.removeEventListener("pointerdown", pointer, true); document.removeEventListener("keydown", key, true); };
  }, [anchor, onClose]);
  const item = "flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-left text-sm text-slate-700 hover:bg-slate-50 focus-visible:bg-blue-50 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-45";
  return createPortal(<div ref={menu} style={{ position: "fixed", ...position }} role="menu" aria-label="添加到聊天" className="z-[80] overflow-y-auto rounded-xl border border-slate-200 bg-white p-1.5 shadow-xl">
    <p className="px-3 pb-1 pt-2 text-[11px] text-slate-400">添加</p>
    <button type="button" role="menuitem" disabled={!filesEnabled} onClick={onFiles} className={item}><Paperclip size={17} className="shrink-0" /><span>文件和图片<span className="block text-[11px] text-slate-400">{filesEnabled ? "选择附件上传" : "选择 Codex 本地后可上传"}</span></span></button>
    {onGoal && <button type="button" role="menuitem" disabled={Boolean(goalDisabledReason)} onClick={onGoal} className={item}><Target size={17} className="shrink-0" /><span>目标模式<span className="block text-[11px] text-slate-400">{goalDisabledReason || "设置目标与验收标准"}</span></span></button>}
    {onSkills && <button type="button" role="menuitem" onClick={onSkills} className={item}><Sparkles size={17} className="shrink-0" /><span>技能<span className="block text-[11px] text-slate-400">选择一个或多个技能，也可输入 / 搜索</span></span></button>}
    {onVisualization && <button type="button" role="menuitem" onClick={onVisualization} className={item}><Network size={17} className="shrink-0" /><span>可视化<span className="block text-[11px] text-slate-400">交互图形、Mermaid 与 Git 来源</span></span></button>}
  </div>, document.body);
}
