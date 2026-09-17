import { useEffect, useRef, useState } from 'react';
import type { FileEntry } from '../shared/contracts';
import { TerminalPane } from './terminal-pane';

type Message = { id: number; role: 'user' | 'assistant'; text: string; attachment?: string; state?: string };
const errorText = (error: unknown) => error instanceof Error ? error.message : '操作失败，请重试';

export function App() {
  const [user, setUser] = useState('');
  const [status, setStatus] = useState('');
  const [busy, setBusy] = useState(false);
  const [available, setAvailable] = useState(true);
  const [workspace, setWorkspace] = useState('');
  const [directory, setDirectory] = useState('');
  const [entries, setEntries] = useState<FileEntry[]>([]);
  const [truncated, setTruncated] = useState(false);
  const [selected, setSelected] = useState('');
  const [content, setContent] = useState('');
  const [preview, setPreview] = useState(false);
  const [prompt, setPrompt] = useState('');
  const [messages, setMessages] = useState<Message[]>([]);
  const [running, setRunning] = useState(false);
  const [terminal, setTerminal] = useState('');
  const [ssh, setSsh] = useState(false);
  const [address, setAddress] = useState('http://127.0.0.1:5000');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [allowHttp, setAllowHttp] = useState(true);
  const scroll = useRef<HTMLDivElement>(null);
  const follow = useRef(true);
  const sending = useRef(false);
  const stopped = useRef(false);
  const active = useRef<number | null>(null);
  const sequence = useRef(0);
  const [away, setAway] = useState(false);
  useEffect(() => window.aiagent.onAnalysisDelta(text => {
    const id = active.current;
    if (id !== null) setMessages(items => items.map(item => item.id === id ? { ...item, text: item.text + text } : item));
  }), []);
  useEffect(() => { if (follow.current && scroll.current) scroll.current.scrollTop = scroll.current.scrollHeight; }, [messages]);
  const login = async (event: React.FormEvent) => {
    event.preventDefault(); setBusy(true); setStatus('');
    try { const result = await window.aiagent.login({ address, username, password, allowHttp }); setUser(result.username); setAvailable(result.codexAvailable); setPassword(''); }
    catch (error) { setStatus(errorText(error)); } finally { setBusy(false); }
  };
  const list = async (path: string) => {
    const page = await window.aiagent.list(path); setEntries(page.entries); setDirectory(path); setTruncated(page.truncated);
  };
  const choose = async () => {
    setBusy(true); setStatus('');
    try {
      const result = await window.aiagent.chooseWorkspace();
      if (result) { setWorkspace(result.name); setEntries([]); setDirectory(''); setSelected(''); setContent(''); setPreview(false); setMessages([]); setPrompt(''); await list(''); }
    } catch (error) { setStatus(errorText(error)); } finally { setBusy(false); }
  };
  const open = async (item: FileEntry) => {
    setBusy(true); setStatus('');
    try {
      if (item.directory) await list(item.path);
      else { const text = await window.aiagent.read(item.path); setSelected(item.path); setContent(text); setPreview(true); }
    } catch (error) { setStatus(errorText(error)); } finally { setBusy(false); }
  };
  const send = async (event?: React.FormEvent) => {
    event?.preventDefault();
    if (sending.current || !prompt.trim() || !available) return;
    sending.current = true; stopped.current = false; setRunning(true); setStatus(''); follow.current = true; setAway(false);
    const question = prompt.trim(); const id = ++sequence.current; active.current = id;
    setMessages(items => [...items, { id: -id, role: 'user', text: question, attachment: selected }, { id, role: 'assistant', text: '' }]); setPrompt('');
    // The delegate is stateless. Keep the explicitly shared conversation bounded.
    const history = messages.filter(item => !item.state).map(item => `${item.role}: ${item.text}`).join('\n\n');
    const request = history ? `Previous conversation (reference only):\n${history.slice(-Math.max(0, 19000 - question.length))}\n\nCurrent request:\n${question}` : question;
    try {
      const result = await window.aiagent.analyze({ prompt: request, context: content });
      setMessages(items => items.map(item => item.id === id ? { ...item, text: result.answer, state: stopped.current ? '已停止' : undefined } : item));
    } catch (error) {
      setMessages(items => items.map(item => item.id === id ? { ...item, state: stopped.current ? '已停止' : `回复失败：${errorText(error)}` } : item));
    } finally { active.current = null; sending.current = false; setRunning(false); }
  };
  const disconnect = async () => {
    try { await window.aiagent.disconnect(); setUser(''); setWorkspace(''); setEntries([]); setContent(''); setSelected(''); setMessages([]); setPrompt(''); setTerminal(''); setSsh(false); setStatus(''); }
    catch (error) { setStatus(errorText(error)); }
  };
  if (!user) return <main className="login"><div className="brand-mark">A</div><h1>连接你的工作空间</h1><p>AiAgent Desktop · 从一个问题开始</p><form onSubmit={login}><label>平台地址<input value={address} onChange={e => setAddress(e.target.value)} required/></label><label>用户名<input autoComplete="username" value={username} onChange={e => setUsername(e.target.value)} required/></label><label>密码<input type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} required/></label><label className="check"><input type="checkbox" checked={allowHttp} onChange={e => setAllowHttp(e.target.checked)}/> 允许 HTTP（本地开发）</label><button className="primary" disabled={busy}>{busy ? '正在连接…' : '登录工作台 →'}</button></form><p className="muted">文件保留在本机，仅发送你明确选择的文本。</p>{status && <p role="alert" className="notice">{status}</p>}</main>;
  return <main className="app-shell">
    <aside className="sidebar">
      <div className="brand"><span className="brand-mark">A</span><strong>AiAgent</strong><span className="desktop-label">DESKTOP</span></div>
      <button className="new-chat" disabled={running} onClick={() => { setMessages([]); setPrompt(''); setStatus(''); }}>＋ 新对话</button>
      <div className="section-heading"><span>项目文件夹</span><button aria-label="选择项目文件夹" title="选择项目文件夹" disabled={running || busy} onClick={() => void choose()}>＋</button></div>
      {workspace ? <button className="project active" disabled={busy || running} onClick={() => void list('').catch(error => setStatus(errorText(error)))}><span>▱</span><strong>{workspace}</strong><span className="dot"/></button> : <button className="folder-empty" disabled={busy || running} onClick={() => void choose()}>▱ 打开项目文件夹<span>浏览文件，附带上下文</span></button>}
      <div className="file-browser">
        {directory && <button className="file parent" disabled={busy || running} onClick={() => { setBusy(true); void list(directory.split('/').slice(0, -1).join('/')).catch(error => setStatus(errorText(error))).finally(() => setBusy(false)); }}>← 上一级 <span>{directory}</span></button>}
        {entries.map(item => <button title={item.path} disabled={busy || running} className={`file ${selected === item.path ? 'selected' : ''}`} key={item.path} onClick={() => void open(item)}><span className="file-icon">{item.directory ? '▸' : '≡'}</span>{item.name}</button>)}
        {workspace && !entries.length && <p className="muted file-hint">此目录没有可显示的文件</p>}
        {truncated && <p className="muted file-hint">目录较大，仅显示部分文件。</p>}
      </div>
      <div className="sidebar-bottom"><span className="section-label">工具</span><button onClick={() => void window.aiagent.openLocal().then(setTerminal).catch(error => setStatus(errorText(error)))}>⌘ 本地终端</button><button onClick={() => setSsh(true)}>⇄ SSH 连接</button><div className="account"><span className="avatar">{user.slice(0, 1).toUpperCase()}</span><span>{user}<small>已连接平台</small></span><button title="退出登录" aria-label="退出登录" disabled={running} onClick={() => void disconnect()}>↪</button></div></div>
    </aside>
    <section className="chat-panel">
      <header className="chat-header"><div><span className="muted">▱</span><strong>{workspace || '自由对话'}</strong><span className="header-divider">/</span><span className="muted">{messages.length ? '当前对话' : '新对话'}</span></div><span className="connection"><i className={running ? 'pulse' : ''}/>{running ? '正在生成' : '已连接'}</span></header>
      <div ref={scroll} className="conversation" role="log" aria-label="对话记录" onScroll={() => { const node = scroll.current!; follow.current = node.scrollHeight - node.scrollTop - node.clientHeight < 80; setAway(!follow.current); }}>
        {!messages.length ? <div className="welcome"><div className="welcome-symbol">✳</div><p className="eyebrow">YOUR LOCAL WORKSPACE</p><h1>今天，一起完成什么？</h1><p>聊一个想法，梳理代码，或从项目中的一个文件开始。</p><div className="suggestions">{['帮我梳理这段代码的逻辑', '检查潜在问题并给出改进建议', '为这个功能设计实施步骤'].map((text, index) => <button key={text} onClick={() => setPrompt(text)}><span>{['⌘', '◎', '☷'][index]}</span>{text}<b>↗</b></button>)}</div></div> : <div className="message-list">{messages.map(item => <article className={`message ${item.role}`} key={item.id}><div className="message-meta"><span className={`avatar ${item.role === 'assistant' ? 'ai' : ''}`}>{item.role === 'assistant' ? 'A' : user.slice(0, 1)}</span><strong>{item.role === 'assistant' ? 'AiAgent' : '你'}</strong>{item.id === active.current && <span className="muted">正在回复…</span>}</div>{item.attachment && <div className="attachment-label">▤ {item.attachment}</div>}<div className="message-text">{item.text || (item.state ? '' : <span className="thinking">正在思考<span>···</span></span>)}</div>{item.state && <p className="message-state">{item.state}</p>}</article>)}</div>}
      </div>
      <div className="composer-area">
        {away && <button className="jump" onClick={() => { follow.current = true; scroll.current!.scrollTop = scroll.current!.scrollHeight; setAway(false); }}>↓ 回到最新回复</button>}
        {status && <div className="notice" role="alert">{status}<button aria-label="关闭提示" onClick={() => setStatus('')}>×</button></div>}
        {!available && <div className="notice">平台 Codex 暂不可用，请在平台配置后重新登录。</div>}
        <form className="composer" onSubmit={send}>
          {selected && <div className="attachment"><button type="button" title="预览即将发送的文件" onClick={() => setPreview(!preview)}>▤ {selected}</button><span>{content.length.toLocaleString()} 字符</span><button type="button" disabled={running} aria-label="移除附带文件" onClick={() => { setSelected(''); setContent(''); setPreview(false); }}>×</button></div>}
          <textarea aria-label="聊天输入" maxLength={12000} value={prompt} onChange={e => setPrompt(e.target.value)} onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) { e.preventDefault(); void send(); } }} placeholder="输入问题或描述任务…"/>
          <div className="composer-toolbar"><span className="muted">{selected ? '附带所选文件文本' : '仅发送对话文本'}</span><div><span className="model-label">Codex <span>⌄</span></span>{running ? <button type="button" className="send stop" aria-label="停止生成" onClick={() => { stopped.current = true; void window.aiagent.cancelAnalysis().catch(error => setStatus(errorText(error))); }}>■</button> : <button className="send" aria-label="发送消息" disabled={!prompt.trim() || !available}>↑</button>}</div></div>
        </form><p className="composer-hint">Enter 发送 · Shift + Enter 换行<span>文件与终端不会自动上传</span></p>
      </div>
    </section>
    {preview && selected && <section className="preview"><header><strong>文件预览</strong><button aria-label="关闭文件预览" onClick={() => setPreview(false)}>×</button></header><p title={selected}>{selected}</p><pre>{content || '（空文件）'}</pre><footer>此文件文本将随下一条消息发送<button onClick={() => setPreview(false)}>完成</button></footer></section>}
    {ssh && <div className="modal-backdrop"><section className="ssh-dialog" role="dialog" aria-modal="true" aria-label="SSH 连接"><header><h2>SSH 连接</h2><button aria-label="关闭 SSH 配置" onClick={() => setSsh(false)}>×</button></header><p className="muted">手动操作远程终端，密码不会保存。</p><form onSubmit={event => { event.preventDefault(); const data = new FormData(event.currentTarget); setBusy(true); void window.aiagent.openSsh({ host: String(data.get('host')), port: Number(data.get('port')), username: String(data.get('username')), password: String(data.get('password')), fingerprint: String(data.get('fingerprint')) }).then(id => { setTerminal(id); setSsh(false); }).catch(error => setStatus(errorText(error))).finally(() => setBusy(false)); }}><label>主机<input name="host" required/></label><label>端口<input name="port" type="number" min="1" max="65535" defaultValue="22" required/></label><label>用户名<input name="username" required/></label><label>密码<input name="password" type="password" required/></label><label>主机指纹<input name="fingerprint" placeholder="SHA256:…" required/></label><button className="primary" disabled={busy}>打开终端</button>{status && <p role="alert" className="notice">{status}</p>}</form></section></div>}
    {terminal && <TerminalPane id={terminal} onClosed={() => setTerminal('')}/>}
  </main>;
}
