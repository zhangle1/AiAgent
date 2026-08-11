"use client";

import { useEffect, useState } from "react";
import { Download, FileText, ImageIcon, Loader2, RefreshCw, Search, ShieldCheck } from "lucide-react";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";
import { getAdminUploads, getAdminUsers, adminChatUploadContentUrl, type AdminUser } from "@/lib/admin-api";
import { getAuthStatus } from "@/lib/auth-api";
import { getChatUploadText, getMyChatUploads, myChatUploadContentUrl, type ChatUploadFile } from "@/lib/chat-api";

export function MyUploadsPage() {
  return <UploadLibraryPage admin={false} />;
}

export function AdminUploadsPage() {
  const [allowed, setAllowed] = useState<boolean | null>(null);
  useEffect(() => { void getAuthStatus().then((status) => setAllowed(status.is_admin === true)).catch(() => setAllowed(false)); }, []);
  if (allowed === null) return <Loading />;
  if (!allowed) return <section className="rounded-2xl border border-amber-200 bg-amber-50 p-8 text-center"><ShieldCheck size={28} className="mx-auto text-amber-600" /><h1 className="mt-3 text-lg font-semibold text-amber-900">没有管理权限</h1><p className="mt-2 text-sm text-amber-700">上传管理只对管理员开放。</p></section>;
  return <UploadLibraryPage admin />;
}

function UploadLibraryPage({ admin }: { admin: boolean }) {
  const [items, setItems] = useState<ChatUploadFile[]>([]);
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [userId, setUserId] = useState("");
  const [keyword, setKeyword] = useState("");
  const [kind, setKind] = useState("");
  const [selected, setSelected] = useState<ChatUploadFile | null>(null);
  const [text, setText] = useState("");
  const [message, setMessage] = useState("");
  const [loading, setLoading] = useState(true);

  const load = async () => {
    setLoading(true);
    setMessage("");
    try {
      const [nextItems, nextUsers] = await Promise.all([
        admin ? getAdminUploads({ userId: userId || undefined, keyword: keyword || undefined, kind: kind || undefined }) : getMyChatUploads({ keyword: keyword || undefined, kind: kind || undefined }),
        admin ? getAdminUsers() : Promise.resolve([]),
      ]);
      setItems(nextItems);
      setUsers(nextUsers);
      setSelected((current) => nextItems.find((item) => item.id === current?.id) ?? nextItems[0] ?? null);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "读取上传记录失败。");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, [admin, userId, kind]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (!selected || selected.kind !== "extracted_text") { setText(""); return; }
    const ownerId = admin ? (selected.uploader_id || userId) : undefined;
    if (admin && !ownerId) { setText("无法确定附件所属用户。"); return; }
    void getChatUploadText(selected.id, ownerId).then(setText).catch((error) => setText(error instanceof Error ? error.message : "读取文本提取失败。"));
  }, [admin, selected, userId]);

  const contentUrl = selected ? (admin ? adminChatUploadContentUrl(selected.id, selected.uploader_id || userId) : myChatUploadContentUrl(selected.id)) : "";
  return <section>
    <SettingsPageHeader title={admin ? "上传管理" : "我的上传"} description={admin ? "按用户、类型或文件名筛选并只读查看聊天附件与服务端生成的文本提取。" : "查看在聊天中已发送并保存的图片、文档和文本提取；附件实际路径不会显示或写入聊天记录。"} action={null} />
    {message && <p className="mb-4 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-700">{message}</p>}
    <div className="mb-5 flex flex-wrap gap-3 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
      {admin && <select value={userId} onChange={(event) => setUserId(event.target.value)} className="h-10 rounded-lg border border-slate-200 bg-white px-3 text-sm"><option value="">全部用户</option>{users.map((user) => <option key={user.id} value={user.id}>{user.username}</option>)}</select>}
      <select value={kind} onChange={(event) => setKind(event.target.value)} className="h-10 rounded-lg border border-slate-200 bg-white px-3 text-sm"><option value="">全部类型</option><option value="image">图片</option><option value="document">原始文档</option><option value="extracted_text">文本提取</option></select>
      <div className="flex min-w-[220px] flex-1"><input value={keyword} onChange={(event) => setKeyword(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") void load(); }} placeholder="按文件名搜索" className="h-10 min-w-0 flex-1 rounded-l-lg border border-slate-200 px-3 text-sm outline-none focus:border-blue-400" /><button type="button" onClick={() => void load()} className="inline-flex h-10 items-center gap-1 rounded-r-lg border border-l-0 border-slate-200 px-3 text-sm text-slate-600 hover:bg-slate-50"><Search size={15} />搜索</button></div>
      <button type="button" onClick={() => void load()} className="inline-flex h-10 items-center gap-2 rounded-lg border border-slate-200 px-3 text-sm text-slate-600 hover:bg-slate-50"><RefreshCw size={15} />刷新</button>
    </div>
    <div className="grid gap-5 xl:grid-cols-[minmax(0,0.8fr)_minmax(380px,1.2fr)]">
      <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm"><div className="border-b border-slate-100 px-5 py-4 text-sm font-semibold">上传索引 <span className="ml-2 text-xs font-normal text-slate-400">{items.length} 项</span></div><div className="max-h-[620px] divide-y divide-slate-100 overflow-y-auto">{loading ? <Loading /> : items.length === 0 ? <p className="px-5 py-16 text-center text-sm text-slate-400">暂无已保存的上传记录</p> : items.map((item) => <button key={item.id} type="button" onClick={() => setSelected(item)} className={`w-full px-5 py-4 text-left hover:bg-slate-50 ${selected?.id === item.id ? "bg-blue-50/70" : ""}`}><div className="flex items-center gap-3"><span className="text-blue-600">{item.kind === "image" ? <ImageIcon size={18} /> : <FileText size={18} />}</span><span className="min-w-0 flex-1"><span className="block truncate text-sm font-medium text-slate-800">{item.file_name}</span><span className="mt-1 block text-xs text-slate-500">{item.kind === "extracted_text" ? "文本提取" : item.kind === "image" ? "图片" : "原始文档"} · {formatSize(item.size_bytes)}{admin && item.uploader_id ? ` · ${users.find((user) => user.id === item.uploader_id)?.username || item.uploader_id}` : ""}</span></span><span className="shrink-0 text-[11px] text-slate-400">{new Date(item.created_at).toLocaleDateString()}</span></div></button>)}</div></div>
      <div className="min-h-[500px] overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">{selected ? <><div className="flex items-center justify-between gap-3 border-b border-slate-100 px-5 py-4"><div className="min-w-0"><h2 className="truncate text-[15px] font-semibold">{selected.file_name}</h2><p className="mt-1 text-xs text-slate-500">{selected.content_type} · {formatSize(selected.size_bytes)}</p></div><a href={contentUrl} target="_blank" rel="noreferrer" className="inline-flex shrink-0 items-center gap-1.5 rounded-lg border border-slate-200 px-3 py-2 text-xs text-slate-600 hover:bg-slate-50"><Download size={14} />打开/下载</a></div><div className="p-5">{selected.kind === "image" ? <img src={contentUrl} alt={selected.file_name} className="max-h-[560px] max-w-full rounded-lg border border-slate-100 object-contain" /> : selected.kind === "extracted_text" ? <pre className="max-h-[560px] overflow-auto whitespace-pre-wrap break-words rounded-xl bg-slate-50 p-4 text-xs leading-6 text-slate-700">{text || "正在读取文本提取…"}</pre> : <div className="py-24 text-center text-sm text-slate-500"><FileText size={30} className="mx-auto text-slate-300" /><p className="mt-3">原始 Office/PDF 文件请通过“打开/下载”查看。</p><p className="mt-1 text-xs text-slate-400">对应的“文本提取”项可在左侧索引中直接预览。</p></div>}</div></> : <div className="flex min-h-[500px] items-center justify-center text-sm text-slate-400">从左侧选择一项查看</div>}</div>
    </div>
  </section>;
}

function Loading() { return <div className="flex min-h-[180px] items-center justify-center text-sm text-slate-400"><Loader2 size={18} className="mr-2 animate-spin" />正在加载…</div>; }
function formatSize(size: number) { return size >= 1024 * 1024 ? `${(size / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(size / 1024))} KB`; }
