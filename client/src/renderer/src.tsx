import { StrictMode, useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Terminal } from '@xterm/xterm';
import '@xterm/xterm/css/xterm.css';
import './style.css';

function message(error: unknown) { return error instanceof Error ? error.message : 'Unexpected error.'; }

function TerminalPane({ id, onClosed }: { id: string; onClosed: () => void }) {
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
  return <section className="terminal"><div className="terminal-bar"><span>Human terminal — never sent to AI automatically</span><button onClick={onClosed}>Close</button></div><div className="terminal-body" ref={host}/></section>;
}

function App() {
  const [status, setStatus] = useState('Sign in to start.'); const [loggedIn, setLoggedIn] = useState(false); const [workspace, setWorkspace] = useState('');
  const [entries, setEntries] = useState<{ name: string; path: string; directory: boolean }[]>([]); const [selected, setSelected] = useState(''); const [content, setContent] = useState('');
  const [prompt, setPrompt] = useState(''); const [answer, setAnswer] = useState(''); const [terminal, setTerminal] = useState(''); const [analyzing, setAnalyzing] = useState(false);
  const [address, setAddress] = useState('http://127.0.0.1:5000'); const [username, setUsername] = useState(''); const [password, setPassword] = useState(''); const [allowHttp, setAllowHttp] = useState(true);
  const [sshHost, setSshHost] = useState(''); const [sshPort, setSshPort] = useState('22'); const [sshUser, setSshUser] = useState(''); const [sshPassword, setSshPassword] = useState(''); const [sshFingerprint, setSshFingerprint] = useState('');
  const login = async (event: React.FormEvent) => { event.preventDefault(); try { const result = await window.aiagent.login({ address, username, password, allowHttp }); setLoggedIn(true); setPassword(''); setStatus(`Signed in as ${result.username}${result.codexAvailable ? '' : '; remote Codex is unavailable.'}`); } catch (err) { setStatus(message(err)); } };
  const choose = async () => { try { const result = await window.aiagent.chooseWorkspace(); if (!result) return; setWorkspace(result.name); const page = await window.aiagent.list(''); setEntries(page.entries); setSelected(''); setContent(''); } catch (err) { setStatus(message(err)); } };
  const open = async (item: { path: string; directory: boolean }) => { if (item.directory) { const page = await window.aiagent.list(item.path); setEntries(page.entries); return; } try { setSelected(item.path); setContent(await window.aiagent.read(item.path)); } catch (err) { setStatus(message(err)); } };
  const analyze = async () => { setAnalyzing(true); try { setStatus('Analyzing the explicitly shared excerpt…'); setAnswer((await window.aiagent.analyze({ prompt, context: content })).answer); setStatus('Analysis complete.'); } catch (err) { setStatus(message(err)); } finally { setAnalyzing(false); } };
  if (!loggedIn) return <main className="login"><h1>AiAgent Terminal</h1><p>Local files and terminals stay on this computer. Only the file excerpt you select is sent for remote analysis.</p><form onSubmit={login}><label>Backend address<input value={address} onChange={e => setAddress(e.target.value)} required/></label><label>Username<input value={username} onChange={e => setUsername(e.target.value)} required/></label><label>Password<input type="password" value={password} onChange={e => setPassword(e.target.value)} required/></label><label className="check"><input type="checkbox" checked={allowHttp} onChange={e => setAllowHttp(e.target.checked)}/> Allow HTTP for local development</label><button>Sign in</button></form><p className="status">{status}</p></main>;
  const openSsh = async (event: React.FormEvent) => { event.preventDefault(); try { const id = await window.aiagent.openSsh({ host: sshHost, port: Number(sshPort), username: sshUser, password: sshPassword, fingerprint: sshFingerprint }); setSshPassword(''); setTerminal(id); } catch (err) { setStatus(message(err)); } };
  return <main className="app"><header><h1>AiAgent Terminal</h1><span>{status}</span><button onClick={() => void window.aiagent.disconnect().then(() => { setLoggedIn(false); setTerminal(''); })}>Disconnect</button></header><div className="grid"><aside><button onClick={() => void choose()}>Authorize folder</button><p>{workspace || 'No folder authorized'}</p><form className="ssh" onSubmit={openSsh}><strong>SSH terminal</strong><input placeholder="Host" value={sshHost} onChange={e => setSshHost(e.target.value)} required/><input placeholder="Port" inputMode="numeric" value={sshPort} onChange={e => setSshPort(e.target.value)} required/><input placeholder="Username" value={sshUser} onChange={e => setSshUser(e.target.value)} required/><input placeholder="Password (not stored)" type="password" value={sshPassword} onChange={e => setSshPassword(e.target.value)} required/><input placeholder="SHA256: host fingerprint" value={sshFingerprint} onChange={e => setSshFingerprint(e.target.value)} required/><button>Open SSH terminal</button></form>{entries.map(item => <button className="file" key={item.path} onClick={() => void open(item)}>{item.directory ? '▸ ' : '• '}{item.name}</button>)}</aside><section className="editor"><h2>{selected || 'Select a UTF-8 text file'}</h2><textarea value={content} readOnly placeholder="Selected content appears here. Nothing is shared until Analyze is used."/><div className="actions"><button onClick={() => void window.aiagent.openLocal().then(setTerminal).catch(err => setStatus(message(err)))}>Open local terminal</button>{analyzing ? <button onClick={() => void window.aiagent.cancelAnalysis()}>Cancel analysis</button> : <button disabled={!content || !prompt.trim()} onClick={() => void analyze()}>Analyze shared excerpt</button>}</div><textarea value={prompt} onChange={e => setPrompt(e.target.value)} placeholder="Ask the remote AI about the selected excerpt…"/><article>{answer}</article></section></div>{terminal && <TerminalPane id={terminal} onClosed={() => setTerminal('')}/>}</main>;
}
createRoot(document.getElementById('root')!).render(<StrictMode><App/></StrictMode>);
