"use client";

import { useEffect, useMemo, useState } from "react";
import { Edit3, FolderKey, KeyRound, Loader2, Plus, Search, ShieldCheck, UserPlus, Users, X } from "lucide-react";
import { createAdminUser, getAdminUsers, resetAdminUserPassword, updateAdminUserAlias, updateAdminUserProjects, type AdminUser } from "@/lib/admin-api";
import { getAuthStatus } from "@/lib/auth-api";
import { getCodeProjects } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";

type DialogState = { kind: "create" } | { kind: "edit"; user: AdminUser } | { kind: "reset"; user: AdminUser } | null;

export function AdminUserManagementTable() {
  const [allowed, setAllowed] = useState<boolean | null>(null);
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [query, setQuery] = useState("");
  const [dialog, setDialog] = useState<DialogState>(null);
  const [loading, setLoading] = useState(true);
  const [notice, setNotice] = useState("");

  const load = async () => {
    setLoading(true);
    try {
      const [nextUsers, nextProjects] = await Promise.all([getAdminUsers(), getCodeProjects()]);
      setUsers(nextUsers);
      setProjects(nextProjects);
    } catch (error) {
      setNotice(error instanceof Error ? error.message : "读取用户信息失败。");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void getAuthStatus().then((status) => setAllowed(status.is_admin === true)).catch(() => setAllowed(false)); }, []);
  useEffect(() => { if (allowed) void load(); }, [allowed]);

  const filteredUsers = useMemo(() => {
    const keyword = query.trim().toLocaleLowerCase();
    if (!keyword) return users;
    return users.filter((user) => user.username.toLocaleLowerCase().includes(keyword) || (user.alias || "").toLocaleLowerCase().includes(keyword));
  }, [query, users]);

  if (allowed === null || (allowed && loading)) return <LoadingState />;
  if (!allowed) return <AccessDenied />;

  return <section>
    <SettingsPageHeader title="用户管理" description="公开注册已关闭。使用别名便于识别成员；普通用户只能在聊天中选择被授予的项目。" action={null} />
    {notice && <div className="mb-4 flex items-center justify-between gap-3 rounded-xl border border-blue-100 bg-blue-50 px-4 py-3 text-sm text-blue-700"><span>{notice}</span><button type="button" onClick={() => setNotice("")} aria-label="关闭提示"><X size={16} /></button></div>}
    <section className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-5 py-4">
        <div><h2 className="flex items-center gap-2 text-[15px] font-semibold text-slate-900"><Users size={17} className="text-blue-600" />注册用户</h2><p className="mt-1 text-xs text-slate-500">可按账号或用户别名搜索，项目授权与重置密码均通过弹窗完成。</p></div>
        <button type="button" onClick={() => setDialog({ kind: "create" })} className="inline-flex h-10 items-center gap-2 rounded-lg bg-blue-600 px-4 text-sm font-medium text-white hover:bg-blue-700"><Plus size={16} />新增用户</button>
      </div>
      <div className="flex flex-wrap items-center justify-between gap-3 px-5 py-4"><label className="relative block w-full max-w-md"><Search size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" /><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="搜索账号或用户别名" className="h-10 w-full rounded-lg border border-slate-200 bg-slate-50 pl-9 pr-3 text-sm outline-none focus:border-blue-400 focus:bg-white focus:ring-2 focus:ring-blue-50" /></label><span className="rounded-full bg-slate-100 px-2.5 py-1 text-xs text-slate-500">{filteredUsers.length} / {users.length} 位</span></div>
      <div className="overflow-x-auto"><table className="min-w-[860px] w-full text-left text-sm"><thead className="border-y border-slate-100 bg-slate-50/70 text-xs font-medium text-slate-500"><tr><th className="px-5 py-3">用户</th><th className="px-4 py-3">账号</th><th className="px-4 py-3">角色</th><th className="px-4 py-3">可选项目</th><th className="px-4 py-3">创建时间</th><th className="px-5 py-3 text-right">操作</th></tr></thead><tbody className="divide-y divide-slate-100">{filteredUsers.length === 0 ? <tr><td colSpan={6} className="px-5 py-16 text-center text-sm text-slate-400">没有匹配的用户</td></tr> : filteredUsers.map((user) => <tr key={user.id} className="transition hover:bg-slate-50"><td className="px-5 py-3.5"><div className="flex items-center gap-3"><span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-sm font-semibold ${user.role === "admin" ? "bg-violet-100 text-violet-700" : "bg-blue-50 text-blue-700"}`}>{(user.alias || user.username).slice(0, 1).toLocaleUpperCase()}</span><span className="min-w-0"><span className="block max-w-48 truncate font-medium text-slate-800">{user.alias || "未设置别名"}</span><span className="mt-0.5 block text-xs text-slate-400">{user.alias ? "用户别名" : "可在编辑中设置"}</span></span></div></td><td className="px-4 py-3.5 font-mono text-[13px] text-slate-700">{user.username}</td><td className="px-4 py-3.5"><span className={`rounded-full px-2.5 py-1 text-xs ${user.role === "admin" ? "bg-violet-50 text-violet-700" : "bg-slate-100 text-slate-600"}`}>{user.role === "admin" ? "管理员" : "普通用户"}</span></td><td className="px-4 py-3.5 text-slate-600">{user.role === "admin" ? "全部项目" : `${user.project_ids.length} 个项目`}</td><td className="px-4 py-3.5 text-xs text-slate-500">{new Date(user.created_at).toLocaleDateString()}</td><td className="px-5 py-3.5"><div className="flex justify-end gap-2"><button type="button" onClick={() => setDialog({ kind: "edit", user })} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-slate-200 px-2.5 text-xs font-medium text-slate-600 hover:border-blue-200 hover:bg-blue-50 hover:text-blue-700"><Edit3 size={14} />编辑</button><button type="button" onClick={() => setDialog({ kind: "reset", user })} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-slate-200 px-2.5 text-xs font-medium text-slate-600 hover:border-amber-200 hover:bg-amber-50 hover:text-amber-700"><KeyRound size={14} />重置密码</button></div></td></tr>)}</tbody></table></div>
    </section>
    {dialog?.kind === "create" && <CreateUserDialog projects={projects} onClose={() => setDialog(null)} onSaved={async (message) => { setDialog(null); setNotice(message); await load(); }} />}
    {dialog?.kind === "edit" && <EditUserDialog user={dialog.user} projects={projects} onClose={() => setDialog(null)} onSaved={async (user, message) => { setDialog(null); setUsers((current) => current.map((item) => item.id === user.id ? user : item)); setNotice(message); }} />}
    {dialog?.kind === "reset" && <ResetPasswordDialog user={dialog.user} onClose={() => setDialog(null)} onSaved={(message) => { setDialog(null); setNotice(message); }} />}
  </section>;
}

function CreateUserDialog({ projects, onClose, onSaved }: { projects: CodeProject[]; onClose: () => void; onSaved: (message: string) => Promise<void> }) {
  const [username, setUsername] = useState(""); const [alias, setAlias] = useState(""); const [password, setPassword] = useState(""); const [projectIds, setProjectIds] = useState<number[]>([]); const [error, setError] = useState(""); const [saving, setSaving] = useState(false);
  const submit = async (event: React.FormEvent) => { event.preventDefault(); setSaving(true); setError(""); try { await createAdminUser({ username, alias, password, project_ids: projectIds }); await onSaved("用户已创建，项目授权已保存。"); } catch (reason) { setError(reason instanceof Error ? reason.message : "创建用户失败。"); } finally { setSaving(false); } };
  return <Dialog title="新增用户" description="账号用于登录，别名用于在管理页面快速识别成员。" onClose={onClose}><form onSubmit={submit} className="space-y-4"><div className="grid gap-4 sm:grid-cols-2"><Field label="账号" value={username} onChange={setUsername} placeholder="例如：zhangsan" required /><Field label="用户别名" value={alias} onChange={setAlias} placeholder="例如：张三" /></div><Field label="初始密码" type="password" value={password} onChange={setPassword} placeholder="至少 6 位" required /><ProjectPicker projects={projects} value={projectIds} onChange={setProjectIds} />{error && <ErrorText message={error} />}<DialogActions onClose={onClose} saving={saving} submitText="创建用户" icon={<UserPlus size={16} />} /></form></Dialog>;
}

function EditUserDialog({ user, projects, onClose, onSaved }: { user: AdminUser; projects: CodeProject[]; onClose: () => void; onSaved: (user: AdminUser, message: string) => Promise<void> }) {
  const [alias, setAlias] = useState(user.alias || ""); const [projectIds, setProjectIds] = useState(user.project_ids); const [error, setError] = useState(""); const [saving, setSaving] = useState(false);
  const submit = async (event: React.FormEvent) => { event.preventDefault(); setSaving(true); setError(""); try { await updateAdminUserAlias(user.id, alias); if (user.role !== "admin") await updateAdminUserProjects(user.id, projectIds); await onSaved({ ...user, alias: alias.trim() || null, project_ids: user.role === "admin" ? user.project_ids : projectIds }, "用户信息已保存。"); } catch (reason) { setError(reason instanceof Error ? reason.message : "保存用户失败。"); } finally { setSaving(false); } };
  return <Dialog title={`编辑 ${user.username}`} description="可以更新用户别名及其聊天项目范围。管理员始终拥有全部项目。" onClose={onClose}><form onSubmit={submit} className="space-y-4"><Field label="账号" value={user.username} onChange={() => undefined} disabled /><Field label="用户别名" value={alias} onChange={setAlias} placeholder="未设置别名" />{user.role === "admin" ? <div className="rounded-xl border border-violet-100 bg-violet-50 px-3 py-3 text-sm text-violet-700"><ShieldCheck size={16} className="mr-1.5 inline" />管理员默认拥有全部项目，无需单独授权。</div> : <ProjectPicker projects={projects} value={projectIds} onChange={setProjectIds} />}{error && <ErrorText message={error} />}<DialogActions onClose={onClose} saving={saving} submitText="保存修改" icon={<Edit3 size={16} />} /></form></Dialog>;
}

function ResetPasswordDialog({ user, onClose, onSaved }: { user: AdminUser; onClose: () => void; onSaved: (message: string) => void }) {
  const [password, setPassword] = useState(""); const [confirm, setConfirm] = useState(""); const [error, setError] = useState(""); const [saving, setSaving] = useState(false);
  const submit = async (event: React.FormEvent) => { event.preventDefault(); if (password !== confirm) { setError("两次输入的密码不一致。"); return; } setSaving(true); setError(""); try { await resetAdminUserPassword(user.id, password); onSaved(`已重置 ${user.alias || user.username} 的密码，并使其现有登录会话失效。`); } catch (reason) { setError(reason instanceof Error ? reason.message : "重置密码失败。"); } finally { setSaving(false); } };
  return <Dialog title="重置用户密码" description={`将为 ${user.alias || user.username}（${user.username}）设置新密码。保存后该用户需要重新登录。`} onClose={onClose}><form onSubmit={submit} className="space-y-4"><Field label="新密码" type="password" value={password} onChange={setPassword} placeholder="至少 6 位" required /><Field label="确认新密码" type="password" value={confirm} onChange={setConfirm} placeholder="再次输入新密码" required />{error && <ErrorText message={error} />}<DialogActions onClose={onClose} saving={saving} submitText="确认重置" icon={<KeyRound size={16} />} tone="amber" /></form></Dialog>;
}

function ProjectPicker({ projects, value, onChange }: { projects: CodeProject[]; value: number[]; onChange: (value: number[]) => void }) { return <fieldset><legend className="flex items-center gap-1.5 text-sm font-medium text-slate-700"><FolderKey size={15} className="text-blue-600" />可选项目</legend><p className="mt-1 text-xs leading-5 text-slate-500">勾选后会出现在该用户的聊天项目菜单中。</p><div className="mt-2 max-h-52 space-y-1.5 overflow-y-auto rounded-xl border border-slate-200 bg-slate-50 p-2">{projects.length === 0 ? <p className="px-2 py-3 text-sm text-slate-400">暂无已配置项目</p> : projects.map((project) => <label key={project.id} className="flex cursor-pointer items-center gap-2 rounded-lg px-2.5 py-2 text-sm text-slate-700 hover:bg-white"><input type="checkbox" checked={value.includes(project.id)} onChange={(event) => onChange(event.target.checked ? [...value, project.id] : value.filter((id) => id !== project.id))} className="h-4 w-4 rounded border-slate-300 text-blue-600" /><span className="truncate">{project.display_name}</span></label>)}</div></fieldset>; }
function Dialog({ title, description, onClose, children }: { title: string; description: string; onClose: () => void; children: React.ReactNode }) { return <div role="dialog" aria-modal="true" aria-label={title} className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/35 p-4"><section className="max-h-[calc(100vh-2rem)] w-full max-w-xl overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl"><div className="flex items-start justify-between gap-4"><div><h2 className="text-lg font-semibold text-slate-900">{title}</h2><p className="mt-1 text-sm leading-6 text-slate-500">{description}</p></div><button type="button" onClick={onClose} className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="关闭"><X size={18} /></button></div><div className="mt-5">{children}</div></section></div>; }
function DialogActions({ onClose, saving, submitText, icon, tone = "blue" }: { onClose: () => void; saving: boolean; submitText: string; icon: React.ReactNode; tone?: "blue" | "amber" }) { const color = tone === "amber" ? "bg-amber-600 hover:bg-amber-700" : "bg-blue-600 hover:bg-blue-700"; return <div className="flex justify-end gap-3 border-t border-slate-100 pt-5"><button type="button" onClick={onClose} disabled={saving} className="h-10 rounded-lg border border-slate-200 px-4 text-sm text-slate-600 hover:bg-slate-50">取消</button><button disabled={saving} className={`inline-flex h-10 items-center gap-2 rounded-lg px-4 text-sm font-medium text-white disabled:bg-slate-300 ${color}`}>{saving ? <Loader2 size={16} className="animate-spin" /> : icon}{saving ? "保存中…" : submitText}</button></div>; }
function Field({ label, value, onChange, placeholder, type = "text", required = false, disabled = false }: { label: string; value: string; onChange: (value: string) => void; placeholder?: string; type?: string; required?: boolean; disabled?: boolean }) { return <label className="block text-sm font-medium text-slate-700">{label}<input type={type} value={value} required={required} disabled={disabled} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} className="mt-1.5 h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-sm outline-none transition placeholder:text-slate-400 focus:border-blue-400 focus:ring-2 focus:ring-blue-50 disabled:cursor-not-allowed disabled:bg-slate-50 disabled:text-slate-500" /></label>; }
function ErrorText({ message }: { message: string }) { return <p className="rounded-lg border border-red-100 bg-red-50 px-3 py-2 text-sm text-red-700">{message}</p>; }
function LoadingState() { return <div className="flex min-h-[300px] items-center justify-center text-sm text-slate-400"><Loader2 size={18} className="mr-2 animate-spin" />正在加载用户管理…</div>; }
function AccessDenied() { return <section className="rounded-2xl border border-amber-200 bg-amber-50 p-8 text-center"><ShieldCheck size={28} className="mx-auto text-amber-600" /><h1 className="mt-3 text-lg font-semibold text-amber-900">没有管理权限</h1><p className="mt-2 text-sm text-amber-700">该区域仅对管理员开放。</p></section>; }
