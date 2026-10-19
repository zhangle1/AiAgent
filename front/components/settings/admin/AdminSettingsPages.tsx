"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { BarChart3, BellRing, Bot, ChevronRight, ClipboardList, FileUp, FolderKey, History, Loader2, ShieldCheck, UserPlus, Users } from "lucide-react";
import { createAdminUser, getAdminSession, getAdminSessions, getAdminUsers, updateAdminUserProjects, type AdminSession, type AdminUser } from "@/lib/admin-api";
import { getCodeProjects } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";
import { getAuthStatus } from "@/lib/auth-api";
import type { SessionDetail } from "@/lib/session-api";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";

const cardItems = [
  { href: "/settings/admin/users", title: "用户管理", description: "创建账号，为每位用户分配可在聊天中选择的项目。", icon: Users },
  { href: "/settings/admin/sessions", title: "历史会话", description: "按用户查看已保存的聊天会话与消息记录。", icon: History },
  { href: "/settings/admin/usage", title: "用户流量", description: "按日、周、月或年汇总注册用户的 Token 消耗。", icon: BarChart3 },
  { href: "/settings/admin/agents", title: "第三方代理", description: "检测本地 CLI，并维护 Codex 模型与 Profile 配置。", icon: Bot },
  { href: "/settings/admin/uploads", title: "上传管理", description: "按用户筛选查看聊天附件、图片与文本提取。", icon: FileUp },
  { href: "/settings/admin/push", title: "推送模块", description: "配置钉钉自定义机器人 Webhook，并绑定项目 Git 推送成功通知。", icon: BellRing },
];

export function AdminSettingsHome() {
  const allowed = useAdminAccess();
  if (allowed === null) return <LoadingState />;
  if (!allowed) return <AccessDenied />;
  return <section><SettingsPageHeader title="管理配置" description="仅管理员可用。集中管理用户、会话审计、模型代理与图片上传能力。" action={null} /><div className="grid gap-4 md:grid-cols-3">{cardItems.map((item) => { const Icon = item.icon; return <Link key={item.href} href={item.href} className="group flex min-h-[190px] flex-col justify-between rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:border-blue-200 hover:shadow-md"><div className="flex items-start justify-between"><span className="flex h-10 w-10 items-center justify-center rounded-xl bg-blue-50 text-blue-600"><Icon size={19} /></span><ChevronRight size={18} className="text-slate-400 transition group-hover:translate-x-0.5 group-hover:text-blue-600" /></div><div><h2 className="text-[16px] font-semibold text-slate-900">{item.title}</h2><p className="mt-2 text-[13px] leading-6 text-slate-500">{item.description}</p></div></Link>; })}</div></section>;
}

export function AdminUsersPage() {
  const allowed = useAdminAccess();
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [newProjectIds, setNewProjectIds] = useState<number[]>([]);
  const [message, setMessage] = useState("");
  const [loading, setLoading] = useState(true);
  const selected = users.find((item) => item.id === selectedId);

  const load = async () => { setLoading(true); try { const [nextUsers, nextProjects] = await Promise.all([getAdminUsers(), getCodeProjects()]); setUsers(nextUsers); setProjects(nextProjects); setSelectedId((current) => current || nextUsers.find((item) => item.role !== "admin")?.id || nextUsers[0]?.id || ""); } catch (error) { setMessage(error instanceof Error ? error.message : "读取用户失败。"); } finally { setLoading(false); } };
  useEffect(() => { if (allowed) void load(); }, [allowed]);
  const createUser = async (event: React.FormEvent) => { event.preventDefault(); setMessage(""); try { await createAdminUser({ username, password, project_ids: newProjectIds, can_commit_code: false }); setUsername(""); setPassword(""); setNewProjectIds([]); setMessage("用户已创建并已保存项目权限。"); await load(); } catch (error) { setMessage(error instanceof Error ? error.message : "创建用户失败。"); } };
  const saveProjects = async (projectIds: number[]) => { if (!selected) return; setMessage(""); try { await updateAdminUserProjects(selected.id, projectIds); setUsers((current) => current.map((item) => item.id === selected.id ? { ...item, project_ids: projectIds } : item)); setMessage("项目权限已保存。"); } catch (error) { setMessage(error instanceof Error ? error.message : "保存失败。"); } };

  if (allowed === null) return <LoadingState />;
  if (!allowed) return <AccessDenied />;
  if (loading) return <LoadingState />;
  return <section><SettingsPageHeader title="用户管理" description="公开注册已关闭。新用户只能由管理员创建，并且仅能在聊天中选择被授予的项目。" action={null} />{message && <p className="mb-4 rounded-xl border border-blue-100 bg-blue-50 px-4 py-3 text-sm text-blue-700">{message}</p>}<div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_420px]"><div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm"><div className="flex items-center justify-between border-b border-slate-100 px-5 py-4"><div><h2 className="text-[15px] font-semibold">已注册用户</h2><p className="mt-1 text-xs text-slate-500">选择用户后可调整其聊天项目范围。</p></div><span className="rounded-full bg-slate-100 px-2.5 py-1 text-xs text-slate-500">{users.length} 位</span></div><div className="divide-y divide-slate-100">{users.map((user) => <button type="button" key={user.id} onClick={() => setSelectedId(user.id)} className={`flex w-full items-center gap-3 px-5 py-4 text-left transition ${user.id === selectedId ? "bg-blue-50/70" : "hover:bg-slate-50"}`}><span className={`flex h-9 w-9 items-center justify-center rounded-full text-sm font-semibold ${user.role === "admin" ? "bg-violet-100 text-violet-700" : "bg-slate-100 text-slate-600"}`}>{user.username.slice(0, 1).toUpperCase()}</span><span className="min-w-0 flex-1"><span className="block truncate text-sm font-medium text-slate-800">{user.username}</span><span className="mt-0.5 block text-xs text-slate-500">{user.role === "admin" ? "管理员 · 拥有全部项目" : `${user.project_ids.length} 个可选项目`}</span></span><ChevronRight size={16} className="text-slate-400" /></button>)}</div></div><div className="space-y-5"><form onSubmit={createUser} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm"><div className="flex items-center gap-2"><UserPlus size={17} className="text-blue-600" /><h2 className="text-[15px] font-semibold">新增用户</h2></div><div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-1"><Input label="账号" value={username} onChange={setUsername} placeholder="例如：zhangsan" /><Input label="初始密码" type="password" value={password} onChange={setPassword} placeholder="至少 6 位" /></div><ProjectChecks projects={projects} value={newProjectIds} onChange={setNewProjectIds} className="mt-4" /><button className="mt-4 inline-flex h-10 w-full items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 text-sm font-medium text-white hover:bg-blue-700"><UserPlus size={15} />创建并授权</button></form>{selected && <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm"><div className="flex items-center gap-2"><FolderKey size={17} className="text-blue-600" /><div><h2 className="text-[15px] font-semibold">{selected.username} 的项目</h2><p className="mt-0.5 text-xs text-slate-500">这些项目会出现在该用户的聊天项目菜单。</p></div></div>{selected.role === "admin" ? <p className="mt-4 rounded-lg bg-violet-50 px-3 py-3 text-sm text-violet-700">管理员默认拥有全部项目，无需单独设置。</p> : <ProjectChecks projects={projects} value={selected.project_ids} onChange={saveProjects} className="mt-4" />}</div>}</div></div></section>;
}

export function AdminSessionsPage() {
  const allowed = useAdminAccess();
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [userId, setUserId] = useState("");
  const [sessions, setSessions] = useState<AdminSession[]>([]);
  const [detail, setDetail] = useState<SessionDetail | null>(null);
  const [message, setMessage] = useState("");

  useEffect(() => { if (!allowed) return; void getAdminUsers().then(setUsers).catch((error) => setMessage(error instanceof Error ? error.message : "读取用户失败。")); }, [allowed]);
  useEffect(() => { if (!allowed) return; setDetail(null); void getAdminSessions(userId || undefined).then(setSessions).catch((error) => setMessage(error instanceof Error ? error.message : "读取会话失败。")); }, [allowed, userId]);
  const openDetail = async (session: AdminSession) => { try { setDetail(await getAdminSession(session.user_id, session.id)); } catch (error) { setMessage(error instanceof Error ? error.message : "读取会话详情失败。"); } };

  if (allowed === null) return <LoadingState />;
  if (!allowed) return <AccessDenied />;
  return <section><SettingsPageHeader title="历史会话" description="管理员只读查看用户已保存的会话内容，用于审计、回溯和支持。" action={null} />{message && <p className="mb-4 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-700">{message}</p>}<div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_minmax(340px,0.8fr)]"><div className="rounded-2xl border border-slate-200 bg-white shadow-sm"><div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-5 py-4"><div><h2 className="text-[15px] font-semibold">会话列表</h2><p className="mt-1 text-xs text-slate-500">最近 100 条会话，点击可查看消息。</p></div><select value={userId} onChange={(event) => setUserId(event.target.value)} className="h-9 rounded-lg border border-slate-200 bg-white px-3 text-sm"><option value="">全部用户</option>{users.map((user) => <option key={user.id} value={user.id}>{user.username}</option>)}</select></div><div className="divide-y divide-slate-100">{sessions.length === 0 ? <p className="px-5 py-10 text-center text-sm text-slate-400">暂无会话记录</p> : sessions.map((session) => <button type="button" key={session.id} onClick={() => void openDetail(session)} className="block w-full px-5 py-4 text-left hover:bg-slate-50"><div className="flex items-center justify-between gap-3"><span className="truncate text-sm font-medium text-slate-800">{session.title}</span><span className="shrink-0 text-[11px] text-slate-400">{new Date(session.updated_at).toLocaleString()}</span></div><div className="mt-1 flex gap-2 text-xs text-slate-500"><span>{session.username}</span><span>·</span><span>{session.message_count} 条消息</span>{session.project_name && <><span>·</span><span className="truncate">{session.project_name}</span></>}</div><p className="mt-2 line-clamp-2 text-xs leading-5 text-slate-500">{session.last_message}</p></button>)}</div></div><div className="min-h-[480px] rounded-2xl border border-slate-200 bg-white shadow-sm">{detail ? <><div className="border-b border-slate-100 px-5 py-4"><h2 className="truncate text-[15px] font-semibold">{detail.title}</h2><p className="mt-1 text-xs text-slate-500">{detail.project_name || "未关联项目"} · {detail.messages.length} 条消息</p></div><div className="max-h-[620px] space-y-3 overflow-y-auto p-5">{detail.messages.map((message) => <div key={message.id} className={`rounded-xl px-3.5 py-3 text-sm leading-6 ${message.role === "user" ? "bg-blue-50 text-slate-700" : "bg-slate-50 text-slate-700"}`}><p className="mb-1 text-[11px] font-medium text-slate-400">{message.role === "user" ? "用户" : "助手"}</p><p className="whitespace-pre-wrap break-words">{message.content}</p></div>)}</div></> : <div className="flex min-h-[480px] flex-col items-center justify-center px-6 text-center text-slate-400"><ClipboardList size={27} strokeWidth={1.4} /><p className="mt-3 text-sm">从左侧选择一个会话查看内容</p></div>}</div></div></section>;
}

export { AdminUsagePage } from "./AdminUsagePage";

function ProjectChecks({ projects, value, onChange, className = "" }: { projects: CodeProject[]; value: number[]; onChange: (value: number[]) => void; className?: string }) { return <fieldset className={className}><legend className="text-xs font-medium text-slate-600">可选项目</legend><div className="mt-2 max-h-44 space-y-1.5 overflow-y-auto rounded-xl border border-slate-100 bg-slate-50 p-2">{projects.length === 0 ? <p className="px-2 py-3 text-xs text-slate-400">暂无已配置项目</p> : projects.map((project) => <label key={project.id} className="flex cursor-pointer items-center gap-2 rounded-lg px-2 py-2 text-sm text-slate-700 hover:bg-white"><input type="checkbox" checked={value.includes(project.id)} onChange={(event) => onChange(event.target.checked ? [...value, project.id] : value.filter((id) => id !== project.id))} className="h-4 w-4 rounded border-slate-300 text-blue-600" /><span className="truncate">{project.display_name}</span></label>)}</div></fieldset>; }
function Input({ label, value, onChange, placeholder, type = "text" }: { label: string; value: string; onChange: (value: string) => void; placeholder: string; type?: string }) { return <label className="text-xs font-medium text-slate-600">{label}<input required type={type} value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} className="mt-1.5 h-10 w-full rounded-lg border border-slate-200 px-3 text-sm outline-none focus:border-blue-400 focus:ring-2 focus:ring-blue-50" /></label>; }
function Metric({ label, value }: { label: string; value: string }) { return <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm"><p className="text-xs text-slate-500">{label}</p><p className="mt-2 text-2xl font-semibold tracking-tight text-slate-900">{value}</p></div>; }
function compactNumber(value: number) { return value >= 10000 ? `${Math.round(value / 1000)}k` : value.toLocaleString(); }
function LoadingState() { return <div className="flex min-h-[300px] items-center justify-center text-sm text-slate-400"><Loader2 size={18} className="mr-2 animate-spin" />正在加载管理配置…</div>; }
function AccessDenied() { return <section className="rounded-2xl border border-amber-200 bg-amber-50 p-8 text-center"><ShieldCheck size={28} className="mx-auto text-amber-600" /><h1 className="mt-3 text-lg font-semibold text-amber-900">没有管理权限</h1><p className="mt-2 text-sm text-amber-700">该区域只对 superadmin 管理员开放。</p></section>; }
function useAdminAccess() { const [allowed, setAllowed] = useState<boolean | null>(null); useEffect(() => { void getAuthStatus().then((status) => setAllowed(status.is_admin === true)).catch(() => setAllowed(false)); }, []); return allowed; }
