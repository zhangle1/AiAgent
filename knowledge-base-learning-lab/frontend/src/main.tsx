import { FormEvent, useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import "./styles.css";

type Resource = { id: string; title: string; source: string; status: string; node_count: number; created_at: string };
type Task = { id: string; resource_id: string; status: string; stage: string; progress: number; message: string };
type Hit = { resource_id: string; resource_title: string; level: number; title: string; excerpt: string; score: number; reason: string };
type LlmConfig = { mode: "rule" | "deepseek"; model: string; timeout_seconds: number; max_retries: number; api_key_configured: boolean; endpoint: string };

const initialContent = "知识库的写入链路需要先保存受控来源，再解析为 Markdown 与来源清单。检索必须先做权限过滤，然后执行关键词和向量召回。回答应返回文档、章节和页码证据。";

async function request<T>(url: string, options?: RequestInit): Promise<T> {
  const response = await fetch(url, options);
  if (!response.ok) throw new Error((await response.json().catch(() => null))?.detail ?? "请求失败");
  return response.json() as Promise<T>;
}

function App() {
  const [resources, setResources] = useState<Resource[]>([]);
  const [tasks, setTasks] = useState<Task[]>([]);
  const [hits, setHits] = useState<Hit[]>([]);
  const [query, setQuery] = useState("如何让知识库检索可追溯？");
  const [title, setTitle] = useState("新的架构笔记");
  const [source, setSource] = useState("manual://architecture-note");
  const [content, setContent] = useState(initialContent);
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [llmConfig, setLlmConfig] = useState<LlmConfig | null>(null);
  const [llmMode, setLlmMode] = useState<"rule" | "deepseek">("rule");
  const [llmModel, setLlmModel] = useState("deepseek-chat");
  const [apiKey, setApiKey] = useState("");

  const readyCount = useMemo(() => resources.filter((item) => item.status === "ready").length, [resources]);
  const refresh = async () => {
    const [nextResources, nextTasks] = await Promise.all([request<Resource[]>("/api/resources"), request<Task[]>("/api/tasks")]);
    setResources(nextResources); setTasks(nextTasks);
  };

  const loadLlmConfig = async () => {
    const config = await request<LlmConfig>("/api/llm/config");
    setLlmConfig(config); setLlmMode(config.mode); setLlmModel(config.model);
  };

  useEffect(() => { void refresh(); void loadLlmConfig(); const timer = window.setInterval(() => void refresh(), 1200); return () => window.clearInterval(timer); }, []);

  const submitResource = async (event: FormEvent) => {
    event.preventDefault(); setBusy(true); setNotice("");
    try {
      const config = await request<LlmConfig>("/api/llm/config", {
        method: "PUT", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ mode: llmMode, model: llmModel, timeout_seconds: 45, max_retries: 2, api_key: apiKey || undefined }),
      });
      setLlmConfig(config); setApiKey("");
      if (config.mode === "deepseek" && !config.api_key_configured) throw new Error("DeepSeek API Key 尚未写入 SQL Server 配置表。");
      await request<Resource>("/api/resources", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ title, source, content }) });
      setNotice("资源已进入异步索引队列，稍后将生成 L0/L1/L2 节点。"); await refresh();
    } catch (error) { setNotice(error instanceof Error ? error.message : "导入失败"); } finally { setBusy(false); }
  };

  const submitSearch = async (event: FormEvent) => {
    event.preventDefault(); setBusy(true); setNotice("");
    try { setHits(await request<Hit[]>("/api/search", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ query, limit: 6 }) })); }
    catch (error) { setNotice(error instanceof Error ? error.message : "检索失败"); } finally { setBusy(false); }
  };

  return <main>
    <header className="topbar"><div><p className="eyebrow">LEARNING MVP · FASTAPI + REACT</p><h1>Knowledge Lab</h1><p className="subtle">用可观察的方式理解“写入 → 分层 → 检索 → 证据”的知识库主链路。</p></div><div className="metric"><b>{readyCount}</b><span>已激活资源</span></div></header>
    <section className="flow"><span>受控资源</span><i>→</i><span>L0 摘要</span><i>→</i><span>L1 概览</span><i>→</i><span>L2 内容块</span><i>→</i><span>检索证据</span></section>
    {notice && <p className="notice">{notice}</p>}
    <div className="grid">
      <section className="panel import"><div className="panelHead"><div><p className="eyebrow">01 · INGEST</p><h2>添加学习资源</h2></div><span className="chip">异步索引</span></div>
        <form onSubmit={submitResource}><label>标题<input value={title} onChange={(event) => setTitle(event.target.value)} required /></label><label>来源<input value={source} onChange={(event) => setSource(event.target.value)} required /></label><label>正文<textarea value={content} onChange={(event) => setContent(event.target.value)} minLength={40} required /></label><fieldset className="llmSettings"><legend>摘要生成</legend><label>方式<select value={llmMode} onChange={(event) => setLlmMode(event.target.value as "rule" | "deepseek")}><option value="rule">规则模式（本地）</option><option value="deepseek">DeepSeek API</option></select></label>{llmMode === "deepseek" && <><label>模型<input value={llmModel} onChange={(event) => setLlmModel(event.target.value)} required /></label><label>DeepSeek API Key<input type="password" value={apiKey} onChange={(event) => setApiKey(event.target.value)} placeholder={llmConfig?.api_key_configured ? "已保存；留空则保持不变" : "首次保存需要填写"} /></label><small className={llmConfig?.api_key_configured ? "configured" : "notConfigured"}>{llmConfig?.api_key_configured ? "API Key 已加密保存至 SQL Server。" : "需要先启用 SQL Server 并提供 API Key。"}</small></>}</fieldset><button disabled={busy}>{busy ? "处理中…" : "写入并建立分层节点"}</button></form>
      </section>
      <section className="panel search"><div className="panelHead"><div><p className="eyebrow">02 · RETRIEVE</p><h2>检索与证据</h2></div><span className="chip blue">范围优先</span></div>
        <form className="searchForm" onSubmit={submitSearch}><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="输入问题" required /><button disabled={busy}>检索</button></form>
        <div className="hits">{hits.length === 0 ? <div className="empty">运行一次检索，查看命中的层级节点与来源证据。</div> : hits.map((hit, index) => <article className="hit" key={`${hit.resource_id}-${hit.level}-${index}`}><div className="hitTop"><span className={`level level${hit.level}`}>L{hit.level}</span><b>{hit.title}</b><em>{hit.score}</em></div><p>{hit.excerpt}</p><footer>{hit.resource_title} · {hit.reason}</footer></article>)}</div>
      </section>
    </div>
    <section className="panel operations"><div className="panelHead"><div><p className="eyebrow">03 · OBSERVE</p><h2>资源与任务</h2></div><span className="chip">轮询演示</span></div>
      <div className="tables"><div><h3>资源</h3>{resources.map((item) => <div className="row" key={item.id}><div><b>{item.title}</b><small>{item.source}</small></div><span className={`status ${item.status}`}>{item.status}</span><span>{item.node_count} nodes</span></div>)}</div><div><h3>索引任务</h3>{tasks.map((task) => <div className="task" key={task.id}><div><b>{task.stage}</b><small>{task.message}</small></div><div className="progress"><i style={{ width: `${task.progress}%` }} /></div><span>{task.progress}%</span></div>)}</div></div>
    </section>
  </main>;
}

createRoot(document.getElementById("root")!).render(<App />);
