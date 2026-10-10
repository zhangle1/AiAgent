"use client";

import { useEffect, useRef, useState, type CSSProperties, type PointerEvent, type ReactNode } from "react";
import styles from "./knowledge-workspace.module.css";

const DEFAULT = { directory: 248, terminal: 480 };
const STORAGE_KEY = "knowledge-workspace-panel-widths";
type Widths = typeof DEFAULT;
type Side = keyof Widths;

function fit(widths: Widths, available: number): Widths {
  if (available < 980) return widths;
  const directory = Math.max(200, Math.min(widths.directory, available - 280 - 300 - 12));
  const terminal = Math.max(300, Math.min(widths.terminal, available - directory - 280 - 12));
  return { directory, terminal };
}

export function KnowledgeWorkspacePanels({ directory, preview, terminal }: { directory: ReactNode; preview: ReactNode; terminal: ReactNode }) {
  const wrapper = useRef<HTMLDivElement>(null);
  const drag = useRef<{ side: Side; x: number; widths: Widths } | null>(null);
  const [widths, setWidths] = useState(DEFAULT);
  const [available, setAvailable] = useState(0);
  const [ready, setReady] = useState(false);
  const [dragging, setDragging] = useState(false);
  const fitted = fit(widths, available);

  useEffect(() => {
    try {
      const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? "null");
      if (saved && Number.isFinite(saved.directory) && Number.isFinite(saved.terminal)) setWidths(fit(saved, wrapper.current?.clientWidth ?? 0));
    } catch { /* Browser storage is optional. */ }
    setReady(true);
    const element = wrapper.current;
    if (!element) return;
    const observer = new ResizeObserver(() => setAvailable(element.clientWidth));
    observer.observe(element);
    setAvailable(element.clientWidth);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!ready || dragging) return;
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(widths)); } catch { /* Resizing still works without storage. */ }
  }, [widths, ready, dragging]);

  function start(side: Side, event: PointerEvent<HTMLDivElement>) {
    if (event.button !== 0) return;
    event.preventDefault();
    event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = { side, x: event.clientX, widths: fitted };
    setDragging(true);
  }
  function move(event: PointerEvent<HTMLDivElement>) {
    const initial = drag.current;
    if (!initial) return;
    const delta = (event.clientX - initial.x) * (initial.side === "directory" ? 1 : -1);
    const max = available - 280 - 12 - initial.widths[initial.side === "directory" ? "terminal" : "directory"];
    setWidths({ ...initial.widths, [initial.side]: Math.max(initial.side === "directory" ? 200 : 300, Math.min(max, initial.widths[initial.side] + delta)) });
  }
  function stop() { drag.current = null; setDragging(false); }

  function separator(side: Side) {
    const minimum = side === "directory" ? 200 : 300;
    const maximum = Math.max(minimum, available - 280 - 12 - fitted[side === "directory" ? "terminal" : "directory"]);
    return <div role="separator" aria-orientation="vertical" aria-label={side === "directory" ? "调整目录与预览宽度" : "调整预览与 Agent 终端宽度"}
      aria-valuemin={minimum} aria-valuemax={Math.round(maximum)} aria-valuenow={Math.round(fitted[side])} tabIndex={0}
      className={styles.separator} onPointerDown={event => start(side, event)} onPointerMove={move}
      onPointerUp={stop} onPointerCancel={stop} onLostPointerCapture={stop}
      onDoubleClick={() => setWidths(current => fit({ ...current, [side]: DEFAULT[side] }, available))}
      onKeyDown={event => {
        if (!["ArrowLeft", "ArrowRight", "Home"].includes(event.key)) return;
        event.preventDefault();
        const delta = (event.key === "ArrowRight" ? 24 : -24) * (side === "directory" ? 1 : -1);
        setWidths({ ...fitted, [side]: event.key === "Home" ? Math.min(maximum, DEFAULT[side]) : Math.max(minimum, Math.min(maximum, fitted[side] + delta)) });
      }} title="左右拖动调整宽度，双击恢复默认；也可使用方向键" />;
  }

  return <div ref={wrapper} className={styles.panelsWrapper}>
    <div className={`${styles.panels} ${dragging ? styles.dragging : ""}`} style={{ "--directory-width": `${fitted.directory}px`, "--terminal-width": `${fitted.terminal}px` } as CSSProperties}>
      {directory}{separator("directory")}{preview}{separator("terminal")}{terminal}
    </div>
  </div>;
}
