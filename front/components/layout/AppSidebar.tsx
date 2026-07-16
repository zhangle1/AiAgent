"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ChevronDown, ChevronLeft, Code2, Ellipsis, Feather, Flag, FolderGit2, GitBranch, LayoutDashboard, Library, LogOut, MessageSquare, Pin, PinOff, Plus, Settings, SquarePen, Trash2, Wrench, X, type LucideIcon } from "lucide-react";
import { useI18n } from "@/i18n/I18nProvider";
import { logout } from "@/lib/auth-api";
import { getCodeProjects, updateCodeProject } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";
import { deleteSession, listProjectSessionPreferences, listSessions, renameSession, reorderSessions, updateProjectSessionPreference, updateSessionMetadata, type ProjectSessionPreference, type ProjectSessionSortMode, type SessionPriority, type SessionSummary } from "@/lib/session-api";

type SessionGroupData = { key: string; project: CodeProject | null; sessions: SessionSummary[]; preference?: ProjectSessionPreference; label?: string; pinned?: boolean };
type MenuPosition = { top: number; left: number };

const tools = [
  { label: "代码库", href: "/settings/code-repositories", icon: Code2 },
  { label: "Git 管理", href: "/settings/git", icon: GitBranch },
  { label: "知识中心", href: "/knowledge", icon: Library },
  { label: "设置", href: "/settings", icon: Settings },
];

export function AppSidebar() {
  const pathname = usePathname() ?? "/";
  const router = useRouter();
  const searchParams = useSearchParams();
  const { t } = useI18n();
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [projectPreferences, setProjectPreferences] = useState<ProjectSessionPreference[]>([]);
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});
  const [toolsOpen, setToolsOpen] = useState(false);
  const [draggingId, setDraggingId] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [menuId, setMenuId] = useState<string | null>(null);
  const [menuPosition, setMenuPosition] = useState<MenuPosition | null>(null);
  const menuAnchorRef = useRef<HTMLElement | null>(null);
  const menuLayerRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const load = () => {
      void Promise.all([listSessions(), getCodeProjects()])
        .then(([nextSessions, nextProjects]) => { setSessions(nextSessions); setProjects(nextProjects); })
        .catch(() => { setSessions([]); setProjects([]); });
      void listProjectSessionPreferences().then(setProjectPreferences).catch(() => setProjectPreferences([]));
    };
    load();
    window.addEventListener("aiagent:sessions-updated", load);
    return () => window.removeEventListener("aiagent:sessions-updated", load);
  }, [pathname]);

  useEffect(() => {
    if (!menuId) return;
    const closeOnOutsideClick = (event: MouseEvent) => {
      const target = event.target as Node;
      if (menuAnchorRef.current?.contains(target) || menuLayerRef.current?.contains(target)) return;
      menuAnchorRef.current = null;
      setMenuId(null);
      setMenuPosition(null);
    };
    document.addEventListener("click", closeOnOutsideClick);
    return () => document.removeEventListener("click", closeOnOutsideClick);
  }, [menuId]);

  const groups = useMemo<SessionGroupData[]>(() => {
    const preferences = new Map(projectPreferences.map((item) => [item.project_id, item]));
    const projectGroups = projects.map((project) => {
      const preference = preferences.get(project.id);
      const projectSessions = sessions.filter((session) => session.project_id === project.id);
      return { key: `project-${project.id}`, project, preference, sessions: sortSessions(projectSessions, preference?.sort_mode ?? "updated") };
    }).filter((group) => group.sessions.length > 0);
    projectGroups.sort((left, right) => Number(Boolean(right.preference?.is_pinned)) - Number(Boolean(left.preference?.is_pinned)));
    const unassigned = sortSessions(sessions.filter((session) => !session.project_id), "updated");
    return unassigned.length ? [...projectGroups, { key: "project-none", project: null, sessions: unassigned }] : projectGroups;
  }, [projects, projectPreferences, sessions]);
  const pinnedSessions = useMemo(() => sortSessions(sessions.filter((session) => session.is_pinned), "updated"), [sessions]);

  function closeMenu() {
    menuAnchorRef.current = null;
    setMenuId(null);
    setMenuPosition(null);
  }

  function toggleMenu(id: string | null, anchor?: HTMLElement | null) {
    if (!id || !anchor || menuId === id) {
      closeMenu();
      return;
    }
    const rect = anchor.getBoundingClientRect();
    menuAnchorRef.current = anchor;
    setMenuId(id);
    setMenuPosition({ top: rect.bottom + 4, left: Math.max(8, rect.right - 176) });
  }

  async function removeSession(session: SessionSummary) {
    if (!window.confirm(`确定删除会话“${session.title}”吗？`)) return;
    setDeletingId(session.id);
    try {
      await deleteSession(session.id);
      setSessions((items) => items.filter((item) => item.id !== session.id));
      if (searchParams.get("session") === session.id) router.replace("/chat");
    } finally { setDeletingId(null); }
  }

  async function moveSession(target: SessionSummary) {
    if (!draggingId || draggingId === target.id) return;
    const source = sessions.find((item) => item.id === draggingId);
    if (!source || source.project_id !== target.project_id) return;
    const inGroup = sessions.filter((item) => item.project_id === target.project_id);
    const from = inGroup.findIndex((item) => item.id === draggingId);
    const to = inGroup.findIndex((item) => item.id === target.id);
    if (from < 0 || to < 0) return;
    const reordered = [...inGroup];
    reordered.splice(from, 1);
    reordered.splice(to, 0, source);
    setSessions((items) => {
      let index = 0;
      return items.map((item) => item.project_id === target.project_id ? reordered[index++] : item);
    });
    try { await reorderSessions(reordered.map((item) => item.id)); } catch { window.dispatchEvent(new Event("aiagent:sessions-updated")); }
  }

  async function updateMetadata(session: SessionSummary, metadata: { priority?: SessionPriority; is_pinned?: boolean }) {
    setSessions((items) => items.map((item) => item.id === session.id ? { ...item, ...metadata } : item));
    closeMenu();
    try {
      await updateSessionMetadata(session.id, metadata);
      window.dispatchEvent(new Event("aiagent:sessions-updated"));
    } catch {
      window.dispatchEvent(new Event("aiagent:sessions-updated"));
    }
  }

  async function updateProjectPreference(project: CodeProject, preference: { is_pinned?: boolean; sort_mode?: ProjectSessionSortMode }) {
    setProjectPreferences((items) => {
      const current = items.find((item) => item.project_id === project.id) ?? { project_id: project.id, is_pinned: false, sort_mode: "updated" as ProjectSessionSortMode };
      const next = { ...current, ...preference };
      return [...items.filter((item) => item.project_id !== project.id), next];
    });
    closeMenu();
    try { await updateProjectSessionPreference(project.id, preference); } catch { window.dispatchEvent(new Event("aiagent:sessions-updated")); }
  }

  async function renameProject(project: CodeProject) {
    const displayName = window.prompt("项目名称", project.display_name)?.trim();
    if (!displayName || displayName === project.display_name) return;
    try {
      const updated = await updateCodeProject(project.id, { name: project.name, display_name: displayName, root_path: project.root_path, description: project.description ?? undefined });
      setProjects((items) => items.map((item) => item.id === updated.id ? updated : item));
    } finally { closeMenu(); }
  }

  async function renameChatSession(session: SessionSummary) {
    const title = window.prompt("会话名称", session.title)?.trim();
    if (!title || title === session.title) return;
    try {
      await renameSession(session.id, title);
      setSessions((items) => items.map((item) => item.id === session.id ? { ...item, title } : item));
    } finally { closeMenu(); }
  }

  return <aside className="fixed inset-y-0 left-0 z-20 hidden w-[240px] flex-col border-r border-slate-200 bg-[#fbfcff] lg:flex">
    <div className="flex h-16 items-center justify-between px-4">
      <Link href="/chat" className="flex items-center gap-2 rounded-xl px-1 py-1 transition hover:bg-sky-50"><span className="grid h-8 w-8 place-items-center rounded-[10px] border border-sky-200 bg-white text-sky-500 shadow-sm"><Feather size={17}/></span><span className="font-serif text-xl font-semibold italic text-sky-500">{t("app.name")}</span></Link>
      <button className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100" aria-label="收起侧边栏"><ChevronLeft size={16}/></button>
    </div>

    <nav className="px-3 pb-3"><p className="px-2 pb-1 text-[10px] font-semibold tracking-[.15em] text-slate-400">工作台</p><SidebarLink href="/dashboard-applications" icon={LayoutDashboard} label="看板应用生成" active={pathname.startsWith("/dashboard-applications")}/><SidebarLink href="/chat" icon={MessageSquare} label="聊天" active={pathname === "/chat"}/></nav>

    <section className="flex min-h-0 flex-1 flex-col border-t border-slate-200/80 px-3 py-3">
      <div className="flex items-center justify-between px-2"><p className="text-[10px] font-semibold tracking-[.15em] text-slate-400">项目会话</p><span className="text-[10px] text-slate-400">仅显示我的会话</span></div>
      <div className="workspace-scroll mt-2 min-h-0 space-y-2 overflow-y-auto pr-1">
        {pinnedSessions.length > 0 && <SessionGroup group={{ key: "pinned", project: null, label: "置顶会话", sessions: pinnedSessions, pinned: true }} collapsed={false} activeSessionId={searchParams.get("session")} draggingId={draggingId} deletingId={deletingId} menuId={menuId} menuPosition={menuPosition} menuRef={menuLayerRef} onToggle={() => undefined} onMenuChange={toggleMenu} onDelete={(session) => void removeSession(session)} onRename={(session) => void renameChatSession(session)} onUpdate={(session, metadata) => void updateMetadata(session, metadata)} onDragStart={setDraggingId} onDrop={(session) => void moveSession(session)} />}
        {groups.map((group) => <SessionGroup key={group.key} group={group} collapsed={Boolean(collapsed[group.key])} activeSessionId={searchParams.get("session")} draggingId={draggingId} deletingId={deletingId} menuId={menuId} menuPosition={menuPosition} menuRef={menuLayerRef} onToggle={() => setCollapsed((value) => ({ ...value, [group.key]: !value[group.key] }))} onMenuChange={toggleMenu} onDelete={(session) => void removeSession(session)} onRename={(session) => void renameChatSession(session)} onUpdate={(session, metadata) => void updateMetadata(session, metadata)} onUpdateProjectPreference={(project, preference) => void updateProjectPreference(project, preference)} onRenameProject={(project) => void renameProject(project)} onDragStart={setDraggingId} onDrop={(session) => void moveSession(session)} />)}
        {groups.length === 0 && <p className="px-2 py-4 text-xs leading-5 text-slate-400">项目会在你首次发起会话后显示在这里。</p>}
      </div>
    </section>

    <div className="relative border-t border-slate-200 bg-white/80 px-3 py-3">
      <button type="button" onClick={() => setToolsOpen(true)} className="flex h-10 w-full items-center gap-3 rounded-xl px-3 text-sm font-medium text-slate-600 transition hover:bg-slate-100 hover:text-slate-950"><Wrench size={16}/>工具与设置<span className="ml-auto text-xs text-slate-400">选择</span></button>
      <button type="button" onClick={() => void logout().finally(() => window.location.assign("/login"))} className="mt-1 flex h-9 w-full items-center gap-3 rounded-lg px-3 text-[13px] text-slate-500 hover:bg-red-50 hover:text-red-600"><LogOut size={16}/>退出登录</button>
      <div className="mt-2 px-3 text-[10px] text-slate-400">AiAgent · v0.1.0</div>
    </div>
    {toolsOpen && <ToolDialog pathname={pathname} onClose={() => setToolsOpen(false)}/>}
  </aside>;
}

function SessionGroup({ group, collapsed, activeSessionId, draggingId, deletingId, menuId, menuPosition, menuRef, onToggle, onMenuChange, onDelete, onRename, onUpdate, onUpdateProjectPreference, onRenameProject, onDragStart, onDrop }: { group: SessionGroupData; collapsed: boolean; activeSessionId: string | null; draggingId: string | null; deletingId: string | null; menuId: string | null; menuPosition: MenuPosition | null; menuRef: { current: HTMLDivElement | null }; onToggle: () => void; onMenuChange: (id: string | null, anchor?: HTMLElement | null) => void; onDelete: (session: SessionSummary) => void; onRename: (session: SessionSummary) => void; onUpdate: (session: SessionSummary, metadata: { priority?: SessionPriority; is_pinned?: boolean }) => void; onUpdateProjectPreference?: (project: CodeProject, preference: { is_pinned?: boolean; sort_mode?: ProjectSessionSortMode }) => void; onRenameProject?: (project: CodeProject) => void; onDragStart: (id: string | null) => void; onDrop: (session: SessionSummary) => void }) {
  const label = group.label ?? group.project?.display_name ?? "未归属项目";
  const projectMenuKey = `${group.key}:project`;
  const manual = group.preference?.sort_mode === "manual";
  return <div className={`rounded-xl border p-1 ${group.pinned ? "border-amber-200/80 bg-amber-50/55" : "border-slate-200/70 bg-white/70"}`}><div className="relative flex items-center"><button onClick={onToggle} className="flex min-w-0 flex-1 items-center gap-1.5 rounded-lg px-2 py-1.5 text-left text-xs font-semibold text-slate-700 hover:bg-slate-100/80"><ChevronDown size={14} className={collapsed ? "-rotate-90 transition" : "transition"}/>{group.pinned ? <Pin size={13} className="text-amber-600"/> : <FolderGit2 size={14} className="text-blue-600"/>}<span className="truncate">{label}</span>{group.preference?.is_pinned && <Pin size={11} className="ml-1 text-amber-500"/>}<span className="ml-auto text-[10px] font-normal text-slate-400">{group.sessions.length}</span></button>{group.project && <><Link href={`/chat?project=${group.project.id}`} className="grid h-6 w-6 place-items-center rounded-md text-slate-400 hover:bg-blue-50 hover:text-blue-600" aria-label={`在${label}新建会话`}><Plus size={14}/></Link><button type="button" onClick={(event) => onMenuChange(menuId === projectMenuKey ? null : projectMenuKey, event.currentTarget)} className={`mr-1 grid h-6 w-6 place-items-center rounded-md text-slate-400 hover:bg-slate-100 hover:text-slate-700 ${menuId === projectMenuKey ? "bg-white text-slate-700 shadow-sm" : ""}`} aria-label={`${label}项目操作`} aria-expanded={menuId === projectMenuKey}><Ellipsis size={15}/></button>{menuId === projectMenuKey && <ProjectMenu position={menuPosition} menuRef={menuRef} preference={group.preference} onUpdate={(preference) => onUpdateProjectPreference?.(group.project!, preference)} onRename={() => onRenameProject?.(group.project!)}/>}</>}</div>{!collapsed && <div className="mt-1 space-y-0.5">{group.sessions.map((session) => { const rowKey = `${group.key}:${session.id}`; return <SessionRow key={rowKey} menuKey={rowKey} session={session} active={activeSessionId === session.id} dragging={draggingId === session.id} deleting={deletingId === session.id} manual={manual} menuOpen={menuId === rowKey} menuPosition={menuPosition} menuRef={menuRef} onMenuChange={onMenuChange} onDelete={onDelete} onRename={onRename} onUpdate={onUpdate} onDragStart={onDragStart} onDrop={onDrop}/>; })}</div>}</div>;
}

function ProjectMenu({ position, menuRef, preference, onUpdate, onRename }: { position: MenuPosition | null; menuRef: { current: HTMLDivElement | null }; preference?: ProjectSessionPreference; onUpdate: (preference: { is_pinned?: boolean; sort_mode?: ProjectSessionSortMode }) => void; onRename: () => void }) {
  const mode = preference?.sort_mode ?? "updated";
  if (!position) return null;
  return createPortal(<div ref={menuRef} style={position} className="fixed z-[60] w-44 rounded-xl border border-slate-200 bg-white p-1.5 text-xs text-slate-600 shadow-xl"><button type="button" onClick={() => onUpdate({ is_pinned: !preference?.is_pinned })} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left hover:bg-slate-50">{preference?.is_pinned ? <PinOff size={14}/> : <Pin size={14}/>} {preference?.is_pinned ? "取消置顶项目" : "置顶项目"}</button><button type="button" onClick={onRename} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left hover:bg-slate-50"><SquarePen size={14}/>重命名项目</button><div className="my-1 border-t border-slate-100"/><p className="px-2.5 py-1 text-[10px] font-medium tracking-wide text-slate-400">排序方式</p>{([['updated', '按最近更新'], ['priority', '按优先级'], ['manual', '手动排序']] as const).map(([sortMode, label]) => <button key={sortMode} type="button" onClick={() => onUpdate({ sort_mode: sortMode })} className={`flex w-full items-center rounded-lg px-2.5 py-2 text-left ${mode === sortMode ? "bg-blue-50 text-blue-700" : "hover:bg-slate-50"}`}>{mode === sortMode ? <span className="mr-2 text-blue-600">✓</span> : <span className="mr-2 w-3"/>}{label}</button>)}</div>, document.body);
}

function SessionRow({ menuKey, session, active, dragging, deleting, manual, menuOpen, menuPosition, menuRef, onMenuChange, onDelete, onRename, onUpdate, onDragStart, onDrop }: { menuKey: string; session: SessionSummary; active: boolean; dragging: boolean; deleting: boolean; manual: boolean; menuOpen: boolean; menuPosition: MenuPosition | null; menuRef: { current: HTMLDivElement | null }; onMenuChange: (id: string | null, anchor?: HTMLElement | null) => void; onDelete: (session: SessionSummary) => void; onRename: (session: SessionSummary) => void; onUpdate: (session: SessionSummary, metadata: { priority?: SessionPriority; is_pinned?: boolean }) => void; onDragStart: (id: string | null) => void; onDrop: (session: SessionSummary) => void }) {
  return <div draggable={manual} onDragStart={() => manual && onDragStart(session.id)} onDragEnd={() => onDragStart(null)} onDragOver={(event) => { if (manual) event.preventDefault(); }} onDrop={() => { if (manual) onDrop(session); }} onContextMenu={(event) => { event.preventDefault(); onMenuChange(menuOpen ? null : menuKey, event.currentTarget); }} className={`group relative flex items-center rounded-lg ${dragging ? "opacity-40" : ""} ${active ? "bg-blue-50 text-blue-700" : "hover:bg-slate-100"}`}><Link href={`/chat?session=${encodeURIComponent(session.id)}`} className="flex min-w-0 flex-1 items-center gap-2 px-2 py-1.5 text-xs"><MessageSquare size={13} className={active ? "text-blue-600" : "text-slate-400"}/><span className="truncate">{session.title}</span>{session.priority === "high" && <Flag size={11} className="ml-auto shrink-0 text-rose-500"/>}{session.is_pinned && <Pin size={11} className="shrink-0 text-amber-500"/>}</Link><button type="button" onClick={(event) => { event.preventDefault(); event.stopPropagation(); onMenuChange(menuOpen ? null : menuKey, event.currentTarget); }} className={`mr-1 grid h-6 w-6 shrink-0 place-items-center rounded text-slate-400 transition hover:bg-white hover:text-slate-700 ${menuOpen ? "bg-white text-slate-700 shadow-sm" : "opacity-0 group-hover:opacity-100"}`} aria-label={`会话操作：${session.title}`} aria-expanded={menuOpen}><Ellipsis size={15}/></button>{menuOpen && <SessionMenu position={menuPosition} menuRef={menuRef} session={session} deleting={deleting} onClose={() => onMenuChange(null)} onDelete={() => onDelete(session)} onRename={() => onRename(session)} onUpdate={(metadata) => onUpdate(session, metadata)}/>}</div>;
}

function SessionMenu({ position, menuRef, session, deleting, onClose, onDelete, onRename, onUpdate }: { position: MenuPosition | null; menuRef: { current: HTMLDivElement | null }; session: SessionSummary; deleting: boolean; onClose: () => void; onDelete: () => void; onRename: () => void; onUpdate: (metadata: { priority?: SessionPriority; is_pinned?: boolean }) => void }) {
  if (!position) return null;
  return createPortal(<div ref={menuRef} style={position} className="fixed z-[60] w-44 rounded-xl border border-slate-200 bg-white p-1.5 text-xs text-slate-600 shadow-xl"><button type="button" onClick={() => onUpdate({ is_pinned: !session.is_pinned })} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left hover:bg-slate-50">{session.is_pinned ? <PinOff size={14}/> : <Pin size={14}/>} {session.is_pinned ? "取消置顶会话" : "置顶会话"}</button><button type="button" onClick={onRename} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left hover:bg-slate-50"><SquarePen size={14}/>重命名会话</button><div className="my-1 border-t border-slate-100"/><p className="px-2.5 py-1 text-[10px] font-medium tracking-wide text-slate-400">优先级</p><div className="grid grid-cols-3 gap-1 px-1 pb-1">{(["low", "normal", "high"] as const).map((priority) => <button key={priority} type="button" onClick={() => onUpdate({ priority })} className={`rounded-md px-1 py-1.5 text-[10px] transition ${session.priority === priority ? priorityClass(priority) : "text-slate-500 hover:bg-slate-100"}`}>{priorityLabel(priority)}</button>)}</div><div className="my-1 border-t border-slate-100"/><button type="button" disabled={deleting} onClick={() => { onClose(); onDelete(); }} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-red-600 hover:bg-red-50 disabled:opacity-50"><Trash2 size={14}/>删除会话</button></div>, document.body);
}

function sortSessions(sessions: SessionSummary[], sortMode: ProjectSessionSortMode) { const priority = { high: 3, normal: 2, low: 1 } as const; return [...sessions].sort((left, right) => { if (sortMode === "manual") return right.sort_order - left.sort_order; if (sortMode === "priority" && priority[(right.priority ?? "normal") as SessionPriority] !== priority[(left.priority ?? "normal") as SessionPriority]) return priority[(right.priority ?? "normal") as SessionPriority] - priority[(left.priority ?? "normal") as SessionPriority]; return new Date(right.updated_at).getTime() - new Date(left.updated_at).getTime(); }); }
function priorityLabel(priority: SessionPriority) { return priority === "high" ? "高" : priority === "low" ? "低" : "普通"; }
function priorityClass(priority: SessionPriority) { return priority === "high" ? "bg-rose-50 text-rose-600" : priority === "low" ? "bg-slate-100 text-slate-600" : "bg-blue-50 text-blue-700"; }

function SidebarLink({ href, icon: Icon, label, active }: { href: string; icon: LucideIcon; label: string; active: boolean }) { return <Link href={href} className={`mt-0.5 flex h-10 items-center gap-3 rounded-xl px-3 text-[13px] font-medium transition ${active ? "bg-blue-600 text-white shadow-[0_5px_14px_rgba(37,99,235,.22)]" : "text-slate-600 hover:bg-slate-100 hover:text-slate-950"}`}><Icon size={16}/><span>{label}</span></Link>; }
function ToolDialog({ pathname, onClose }: { pathname: string; onClose: () => void }) { return <div className="fixed inset-0 z-50 bg-slate-950/30" onMouseDown={onClose}><div className="absolute bottom-4 left-[252px] w-72 rounded-2xl border border-slate-200 bg-white p-3 shadow-2xl" onMouseDown={(event) => event.stopPropagation()}><div className="flex items-center justify-between px-2 pb-2"><span className="text-sm font-semibold text-slate-900">工具与设置</span><button onClick={onClose} className="grid h-7 w-7 place-items-center rounded-lg text-slate-400 hover:bg-slate-100"><X size={15}/></button></div><div className="space-y-1">{tools.map(({ label, href, icon: Icon }) => { const active = pathname === href || (href !== "/settings" && pathname.startsWith(`${href}/`)); return <Link key={href} href={href} onClick={onClose} className={`flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm ${active ? "bg-blue-50 text-blue-700" : "text-slate-600 hover:bg-slate-50"}`}><Icon size={16}/>{label}</Link>; })}</div></div></div>; }
