"use client";

import { type RefObject, useEffect, useState } from "react";

type NavigationMessage = { id: string; role: string; content: string };
const MAX_MARKERS = 8;

export function ChatMessageNavigation({ messages, scrollRef }: {
  messages: NavigationMessage[];
  scrollRef: RefObject<HTMLDivElement | null>;
}) {
  const questions = messages.filter(message => message.role === "user");
  const [activeId, setActiveId] = useState("");
  const [dismissed, setDismissed] = useState(false);

  useEffect(() => {
    const container = scrollRef.current;
    if (!container) return;
    let frame = 0;
    const update = () => {
      const top = container.getBoundingClientRect().top + 48;
      const elements = Array.from(container.querySelectorAll<HTMLElement>("[data-chat-question]"));
      let current = elements[0];
      for (const element of elements) {
        if (element.getBoundingClientRect().top > top) break;
        current = element;
      }
      setActiveId(current?.dataset.chatQuestion ?? "");
    };
    const schedule = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(update);
    };
    const observer = new ResizeObserver(schedule);
    observer.observe(container);
    if (container.firstElementChild) observer.observe(container.firstElementChild);
    container.addEventListener("scroll", schedule, { passive: true });
    update();
    return () => {
      cancelAnimationFrame(frame);
      observer.disconnect();
      container.removeEventListener("scroll", schedule);
    };
  }, [messages, scrollRef]);

  if (!questions.length) return null;

  const markerCount = Math.min(questions.length, MAX_MARKERS);
  const markerIndexes = Array.from({ length: markerCount }, (_, slot) =>
    Math.round(slot * (questions.length - 1) / Math.max(1, markerCount - 1)),
  );
  const activeIndex = questions.findIndex(question => question.id === activeId);

  return <nav aria-label="会话导航" className="pointer-events-none absolute inset-y-3 left-0 z-20 flex w-8 flex-col justify-center">
    <div className="workspace-scroll pointer-events-auto flex max-h-full flex-col gap-1 overflow-y-auto py-1">
      {markerIndexes.map((index, slot) => {
        const question = questions[index];
        const nextIndex = markerIndexes[slot + 1] ?? questions.length;
        const active = activeIndex >= index && activeIndex < nextIndex;
        const preview = question.content.trim().replace(/\s+/g, " ") || "附件消息";
        return <div key={question.id} className="group shrink-0" onMouseEnter={() => setDismissed(false)}>
          <button type="button" aria-label={`跳转到第 ${index + 1} 条提问：${preview.slice(0, 80)}`} aria-current={active ? "step" : undefined}
            onKeyDown={event => { if (event.key === "Escape") setDismissed(true); }}
            onFocus={() => setDismissed(false)}
            onClick={() => {
              const container = scrollRef.current;
              const target = Array.from(container?.querySelectorAll<HTMLElement>("[data-chat-question]") ?? []).find(element => element.dataset.chatQuestion === question.id);
              if (!container || !target) return;
              container.scrollTo({ top: container.scrollTop + target.getBoundingClientRect().top - container.getBoundingClientRect().top - 16, behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth" });
              setActiveId(question.id);
              setDismissed(true);
            }}
            className="flex h-7 w-8 items-center justify-start rounded-md px-1 focus-visible:outline focus-visible:-outline-offset-2 focus-visible:outline-blue-500">
            <span aria-hidden="true" className={`h-[3px] rounded-full motion-safe:transition-all group-hover:w-6 group-hover:bg-blue-500 group-focus-within:bg-blue-500 ${active ? "w-6 bg-slate-700" : "w-3 bg-slate-300"}`} />
          </button>
          {!dismissed && <div className="pointer-events-none absolute left-10 top-1/2 hidden w-64 max-w-[65vw] -translate-y-1/2 rounded-xl border border-slate-200 bg-white/95 p-3 text-xs leading-5 text-slate-600 shadow-lg backdrop-blur group-hover:block group-focus-within:block">
            <p className="mb-1 font-semibold text-slate-800">第 {index + 1} 条提问</p>
            <p className="line-clamp-4 break-words">{preview.slice(0, 300)}</p>
          </div>}
        </div>;
      })}
    </div>
  </nav>;
}
