"use client";

import { useEffect, useRef, useState } from "react";
import { History, Loader2 } from "lucide-react";
import { getAdminSession, getAdminSessions, getAdminUsers, type AdminSession, type AdminUser } from "@/lib/admin-api";
import { getAuthStatus } from "@/lib/auth-api";
import type { SessionDetail } from "@/lib/session-api";
import { MarkdownMessage } from "@/components/chat/MarkdownMessage";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";

const control = "h-10 min-w-0 rounded-xl border border-slate-200 bg-white px-3 text-sm outline-none focus:border-blue-400 focus:ring-2 focus:ring-blue-100";
const time = (value: string) => new Date(value).toLocaleString("zh-CN", { hour12: false });

export function AdminSessionsPage() {
  const [allowed, setAllowed] = useState<boolean | null>(null);
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [userId, setUserId] = useState("");
  const [sessions, setSessions] = useState<AdminSession[]>([]);
  const [selected, setSelected] = useState<AdminSession | null>(null);
  const [detail, setDetail] = useState<SessionDetail | null>(null);
  const [keyword, setKeyword] = useState("");
  const [ascending, setAscending] = useState(false);
  const [reverseMessages, setReverseMessages] = useState(false);
  const [loading, setLoading] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState("");
  const requestId = useRef(0);
  useEffect(() => { let active = true; void getAuthStatus().then(s => { if (active) setAllowed(s.is_admin === true); }).catch(() => { if (active) setAllowed(false); }); return () => { active = false; }; }, []);
  useEffect(() => { if (!allowed) return; let active = true; void getAdminUsers().then(v => { if (active) setUsers(v); }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, [allowed]);
  useEffect(() => {
    if (!allowed) return;
    let active = true;
    requestId.current++;
    setSelected(null); setDetail(null); setDetailLoading(false); setSessions([]); setLoading(true); setError("");
    void getAdminSessions(userId || undefined).then(v => { if (active) setSessions(v); }).catch(e => { if (active) setError(e.message); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; requestId.current++; };
  }, [allowed, userId]);
  const open = async (session: AdminSession) => {
    const id = ++requestId.current;
    setSelected(session); setDetail(null); setDetailLoading(true); setError("");
    try { const result = await getAdminSession(session.user_id, session.id); if (requestId.current === id) setDetail(result); }
    catch (e) { if (requestId.current === id) setError(e instanceof Error ? e.message : "读取会话失败"); }
    finally { if (requestId.current === id) setDetailLoading(false); }
  };
  const person = (id: string, fallback: string) => { const u = users.find(u => u.id === id); return u?.alias ? `${u.alias}（${u.username}）` : u?.username || fallback; };
  const query = keyword.trim().toLocaleLowerCase();
  const visible = sessions.filter(s => [s.title, s.project_name, s.last_message, person(s.user_id, s.username)].some(v => v?.toLocaleLowerCase().includes(query))).sort((a, b) => (Date.parse(a.updated_at) - Date.parse(b.updated_at) || a.id.localeCompare(b.id)) * (ascending ? 1 : -1));
  const messages = detail ? (reverseMessages ? [...detail.messages].reverse() : detail.messages) : [];
  if (allowed === null) return <p className="p-10 text-center text-slate-500">正在检查管理权限…</p>;
  if (!allowed) return <p className="rounded-xl bg-amber-50 p-8 text-center text-amber-700">没有管理权限</p>;
  return <section>
    <SettingsPageHeader title="历史会话" description="按人员查找会话，阅读完整聊天记录。管理员只读查看。" action={null} />
    {error && <p role="alert" className="mb-4 rounded-xl bg-amber-50 p-4 text-sm text-amber-700">{error}</p>}
    <div className="mb-5 grid gap-3 sm:grid-cols-[1fr_1fr_auto]">
      <select aria-label="筛选人员" className={control} value={userId} onChange={e => setUserId(e.target.value)}><option value="">全部人员</option>{users.map(u => <option key={u.id} value={u.id}>{person(u.id, u.username)}</option>)}</select>
      <input aria-label="搜索会话" className={control} placeholder="搜索标题、项目、人员或末条消息" value={keyword} onChange={e => setKeyword(e.target.value)} />
      <button className={control} onClick={() => setAscending(v => !v)}>更新时间：{ascending ? "最早优先 ↑" : "最新优先 ↓"}</button>
    </div>
    <div className="grid items-start gap-5 lg:grid-cols-[minmax(260px,0.75fr)_minmax(0,1.65fr)]">
      <aside className="min-w-0 overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
        <div className="border-b border-slate-100 p-4"><h2 className="text-sm font-semibold">会话列表 <span className="ml-2 text-xs font-normal text-slate-400">{visible.length} 条</span></h2><p className="mt-1 text-xs text-slate-400">所选人员最近 100 条，排序和搜索作用于这些会话</p></div>
        <div aria-busy={loading} className="max-h-[72vh] divide-y divide-slate-100 overflow-y-auto">
          {loading ? <p role="status" className="p-8 text-center text-sm text-slate-400">正在加载会话…</p> : visible.length === 0 ? <p className="p-8 text-center text-sm text-slate-400">没有符合条件的会话</p> : visible.map(s => <button key={`${s.user_id}:${s.id}`} aria-pressed={selected?.id === s.id && selected.user_id === s.user_id} onClick={() => void open(s)} className={`block w-full border-l-2 p-4 text-left transition ${selected?.id === s.id && selected.user_id === s.user_id ? "border-blue-500 bg-blue-50" : "border-transparent hover:bg-slate-50"}`}>
            <h3 className="line-clamp-2 text-sm font-medium leading-6 text-slate-800">{s.title || "未命名会话"}</h3><p className="mt-1 truncate text-xs text-slate-500">{person(s.user_id, s.username)} · {s.message_count} 条消息 · {s.project_name || "未关联项目"}</p><p className="mt-2 line-clamp-2 break-words text-xs leading-5 text-slate-400">{s.last_message}</p><time className="mt-2 block text-[11px] text-slate-400">{time(s.updated_at)}</time>
          </button>)}
        </div>
      </aside>
      <article aria-busy={detailLoading} className="min-w-0 overflow-hidden rounded-2xl border border-slate-200 bg-slate-50/70 shadow-sm">
        {selected && <header className="border-b border-slate-200 bg-white p-5"><h2 className="break-words text-base font-semibold text-slate-900">{selected.title}</h2><p className="mt-2 text-xs leading-5 text-slate-500">{person(selected.user_id, selected.username)} · {selected.project_name || "未关联项目"} · {detail?.messages.length ?? selected.message_count} 条消息</p><button className="mt-3 rounded-lg border border-slate-200 px-3 py-1.5 text-xs text-slate-600 hover:bg-slate-50" onClick={() => setReverseMessages(v => !v)}>消息顺序：{reverseMessages ? "最新在前 ↓" : "最早在前 ↑"}</button></header>}
        <div key={`${selected?.user_id}:${selected?.id}:${reverseMessages}`} className="max-h-[75vh] min-h-[440px] space-y-6 overflow-y-auto p-4 sm:p-6">
          {detailLoading ? <p role="status" className="flex justify-center gap-2 p-12 text-sm text-slate-400"><Loader2 size={18} className="animate-spin" />正在加载聊天记录…</p> : detail ? messages.length === 0 ? <p className="p-12 text-center text-sm text-slate-400">此会话暂无消息</p> : messages.map(m => <div key={m.id} className={`flex min-w-0 flex-col ${m.role === "user" ? "items-end" : "items-start"}`}>
            <p className="mb-2 text-[11px] text-slate-400">{m.role === "user" ? person(selected!.user_id, selected!.username) : "AI 助手"} · {time(m.created_at)}</p>
            <div className={m.role === "user" ? "max-w-[92%] whitespace-pre-wrap break-words rounded-2xl rounded-tr-md bg-blue-600 px-4 py-3 text-sm leading-7 text-white" : "w-full min-w-0 rounded-2xl rounded-tl-md border border-slate-200 bg-white px-4 py-4 text-sm leading-7 shadow-sm sm:px-5"}>
              {m.role === "user" ? m.content : <MarkdownMessage content={m.content} />}
            </div>
          </div>) : <div className="flex min-h-[390px] flex-col items-center justify-center text-slate-400"><History size={30} strokeWidth={1.3} /><p className="mt-3 text-sm">选择一个会话查看聊天记录</p></div>}
        </div>
      </article>
    </div>
  </section>;
}
