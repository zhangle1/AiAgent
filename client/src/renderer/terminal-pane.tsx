import { useEffect, useRef } from 'react';
import { Terminal } from '@xterm/xterm';
function message(error: unknown) { return error instanceof Error ? error.message : 'Unexpected error.'; }

export function TerminalPane({ id, onClosed }: { id: string; onClosed: () => void }) {
  const host = useRef<HTMLDivElement>(null);
  useEffect(() => {
    let disposed = false;
    const terminal = new Terminal({ cursorBlink: true, fontSize: 14, theme: { background: '#101722', foreground: '#dbeafe' } });
    terminal.open(host.current!); terminal.focus();
    const input = terminal.onData(data => void window.aiagent.terminalWrite(id, data).catch(err => terminal.writeln(`\r\n[${message(err)}]`)));
    let timer = 0;
    const poll = async () => {
      try {
        const result = await window.aiagent.terminalPoll(id);
        if (disposed) return;
        if (result.data) terminal.write(result.data);
        if (result.ended) { terminal.writeln(`\r\n[${result.reason || 'Session ended.'}]`); return; }
      } catch (error) {
        if (!disposed) terminal.writeln(`\r\n[${message(error)}]`);
        return;
      }
      timer = window.setTimeout(poll, 100);
    };
    void poll();
    const resize = () => { const cols = Math.min(500, Math.max(10, Math.floor((host.current?.clientWidth || 800) / 8.5))); const rows = Math.min(200, Math.max(2, Math.floor((host.current?.clientHeight || 400) / 17))); terminal.resize(cols, rows); void window.aiagent.terminalResize(id, { cols, rows }).catch(() => undefined); };
    const observer = new ResizeObserver(resize); observer.observe(host.current!); resize();
    return () => { disposed = true; input.dispose(); observer.disconnect(); window.clearTimeout(timer); terminal.dispose(); void window.aiagent.terminalClose(id).catch(() => undefined); };
  }, [id]);
  return <section className="terminal"><div className="terminal-bar"><span>人工终端 · 输出不会自动发送给 AI</span><button onClick={onClosed}>关闭</button></div><div className="terminal-body" ref={host}/></section>;
}

