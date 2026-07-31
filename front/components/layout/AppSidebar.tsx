"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Archive, Check, ChevronDown, Code2, Edit3, Ellipsis, Feather, FolderGit2, GitBranch, LayoutTemplate, Library, LogOut, MessageSquare, Pin, PinOff, Plus, Settings, Wrench, X, type LucideIcon } from "lucide-react";
import { useI18n } from "@/i18n/I18nProvider";
import { logout } from "@/lib/auth-api";
import { getCodeProjects } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";
import { archiveSession, listProjectSessionPreferences, listSessions, renameSession, updateProjectSessionPreference, updateSessionMetadata, type ProjectSessionPreference, type ProjectSessionSortMode, type SessionSummary } from "@/lib/session-api";
import { useChatStreams, type ChatStreamStatus } from "@/components/chat/ChatStreamProvider";

type NavItem = { href: string; label: string; icon: LucideIcon };
type SessionGroup = { key: string; label: string; project?: CodeProject; preference?: ProjectSessionPreference; sessions: SessionSummary[] };
type ProjectMenuState = { groupKey: string; top: number; left: number };
type SessionMenuState = { session: SessionSummary; top: number; left: number };

const mainItems: NavItem[] = [
  { href: "/chat", label: "聊天", icon: MessageSquare },
  { href: "/prompt-templates", label: "模板市场", icon: LayoutTemplate },
];

const toolItems: NavItem[] = [
  { href: "/settings/code-repositories", label: "代码库", icon: Code2 },
  { href: "/settings/git", label: "Git 管理", icon: GitBranch },
  { href: "/knowledge", label: "知识中心", icon: Library },
  { href: "/settings", label: "设置", icon: Settings },
];

export function AppSidebar({ compact = false }: { compact?: boolean }) {
  const pathname = usePathname() ?? "/";
  const router = useRouter();
  const searchParams = useSearchParams();
  const { t } = useI18n();
  const { streams } = useChatStreams();
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [projectPreferences, setProjectPreferences] = useState<ProjectSessionPreference[]>([]);
  const [collapsedGroups, setCollapsedGroups] = useState<Record<string, boolean>>({});
  const [projectMenu, setProjectMenu] = useState<ProjectMenuState | null>(null);
  const [sessionMenu, setSessionMenu] = useState<SessionMenuState | null>(null);
  const [renameTarget, setRenameTarget] = useState<SessionSummary | null>(null);
  const [toolsOpen, setToolsOpen] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const projectMenuAnchorRef = useRef<HTMLElement | null>(null);
  const projectMenuRef = useRef<HTMLDivElement | null>(null);
  const sessionMenuAnchorRef = useRef<HTMLElement | null>(null);
  const sessionMenuRef = useRef<HTMLDivElement | null>(null);

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
    if (!projectMenu && !sessionMenu) return;
    const closeOnOutsideClick = (event: PointerEvent) => {
      const target = event.target as Node;
      if (projectMenuAnchorRef.current?.contains(target) || projectMenuRef.current?.contains(target) || sessionMenuAnchorRef.current?.contains(target) || sessionMenuRef.current?.contains(target)) return;
      projectMenuAnchorRef.current = null;
      sessionMenuAnchorRef.current = null;
      setProjectMenu(null);
      setSessionMenu(null);
    };
    document.addEventListener("pointerdown", closeOnOutsideClick);
    return () => document.removeEventListener("pointerdown", closeOnOutsideClick);
  }, [projectMenu, sessionMenu]);

  useEffect(() => {
    const toggleMobileDrawer = () => setMobileOpen((current) => !current);
    const closeMobileDrawer = () => setMobileOpen(false);
    window.addEventListener("aiagent:mobile-drawer-toggle", toggleMobileDrawer);
    window.addEventListener("aiagent:mobile-drawer-close", closeMobileDrawer);
    return () => {
      window.removeEventListener("aiagent:mobile-drawer-toggle", toggleMobileDrawer);
      window.removeEventListener("aiagent:mobile-drawer-close", closeMobileDrawer);
    };
  }, []);

  useEffect(() => {
    if (!mobileOpen) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => { document.body.style.overflow = previousOverflow; };
  }, [mobileOpen]);

  const groups = useMemo<SessionGroup[]>(() => {
    const preferences = new Map(projectPreferences.map((item) => [item.project_id, item]));
    const projectGroups = projects.map((project) => {
      const preference = preferences.get(project.id);
      return { key: `project-${project.id}`, label: project.display_name, project, preference, sessions: sortSessions(sessions.filter((session) => session.project_id === project.id), preference?.sort_mode ?? "updated") };
    }).filter((group) => group.sessions.length > 0);
    projectGroups.sort((left, right) => Number(Boolean(right.preference?.is_pinned)) - Number(Boolean(left.preference?.is_pinned)) || left.label.localeCompare(right.label));
    const unassigned = sessions.filter((session) => !session.project_id);
    return unassigned.length ? [...projectGroups, { key: "unassigned", label: "未归属项目", sessions: sortSessions(unassigned, "updated") }] : projectGroups;
  }, [projectPreferences, projects, sessions]);
  const pinnedGroups = useMemo(() => groups.filter((group) => group.project && group.preference?.is_pinned), [groups]);
  const sessionActivity = useMemo(() => {
    const next: Record<string, ChatStreamStatus | "unread"> = {};
    Object.values(streams).forEach((stream) => {
      if (stream.status === "streaming") next[stream.sessionId] = "streaming";
      else if (stream.unread && next[stream.sessionId] !== "streaming") next[stream.sessionId] = stream.status === "error" ? "error" : "unread";
    });
    return next;
  }, [streams]);

  async function archiveSessionItem(session: SessionSummary) {
    try {
      await archiveSession(session.id);
      setSessions((items) => items.filter((item) => item.id !== session.id));
      if (searchParams.get("session") === session.id) router.replace(session.project_id ? `/chat?project=${session.project_id}` : "/chat");
    } catch (value) {
      window.alert(value instanceof Error ? value.message : "归档会话失败，请稍后重试。");
      window.dispatchEvent(new Event("aiagent:sessions-updated"));
    }
  }

  async function toggleSessionPin(session: SessionSummary) {
    const next = !session.is_pinned;
    setSessions((items) => items.map((item) => item.id === session.id ? { ...item, is_pinned: next } : item));
    try { await updateSessionMetadata(session.id, { is_pinned: next }); }
    catch { window.dispatchEvent(new Event("aiagent:sessions-updated")); }
  }

  async function toggleProjectPin(project: CodeProject, preference?: ProjectSessionPreference) {
    await updateProjectPreference(project, { is_pinned: !preference?.is_pinned });
  }

  async function updateProjectPreference(project: CodeProject, change: { is_pinned?: boolean; sort_mode?: ProjectSessionSortMode }) {
    setProjectPreferences((items) => {
      const current = items.find((item) => item.project_id === project.id) ?? { project_id: project.id, is_pinned: false, sort_mode: "updated" as ProjectSessionSortMode };
      return [...items.filter((item) => item.project_id !== project.id), { ...current, ...change }];
    });
    try { await updateProjectSessionPreference(project.id, change); }
    catch { window.dispatchEvent(new Event("aiagent:sessions-updated")); }
  }

  function openProjectMenu(group: SessionGroup, anchor: HTMLElement) {
    if (!group.project) return;
    if (projectMenu?.groupKey === group.key) {
      projectMenuAnchorRef.current = null;
      setProjectMenu(null);
      return;
    }
    const rect = anchor.getBoundingClientRect();
    projectMenuAnchorRef.current = anchor;
    setSessionMenu(null);
    setProjectMenu({ groupKey: group.key, top: rect.bottom + 4, left: Math.max(8, rect.right - 184) });
  }

  function openSessionMenu(session: SessionSummary, anchor: HTMLElement) {
    if (sessionMenu?.session.id === session.id) {
      sessionMenuAnchorRef.current = null;
      setSessionMenu(null);
      return;
    }
    const rect = anchor.getBoundingClientRect();
    sessionMenuAnchorRef.current = anchor;
    setProjectMenu(null);
    setSessionMenu({ session, top: rect.bottom + 4, left: Math.max(8, rect.right - 176) });
  }

  return <>
    {mobileOpen && <button type="button" aria-label="关闭工作台抽屉" onClick={() => setMobileOpen(false)} className="fixed inset-0 z-40 bg-slate-950/35 lg:hidden" />}
    <aside onClickCapture={(event) => { if ((event.target as HTMLElement).closest("a")) setMobileOpen(false); }} className={`fixed inset-y-0 left-0 z-50 flex w-[min(86vw,344px)] -translate-x-full flex-col border-r border-slate-200 bg-[#fbfcff] transition-[transform,width] duration-200 ${mobileOpen ? "translate-x-0" : ""} lg:z-30 lg:translate-x-0 ${compact ? "lg:w-[72px]" : "lg:w-[240px]"}`}>
    <div className={`flex h-16 shrink-0 items-center ${compact ? "justify-center px-2" : "px-4"}`}>
      <button type="button" onClick={() => setMobileOpen(false)} className="order-2 ml-auto grid h-10 w-10 place-items-center rounded-xl text-slate-500 hover:bg-slate-100 lg:hidden" aria-label="关闭工作台抽屉"><X size={18}/></button>
      {compact ? <button type="button" onClick={() => window.dispatchEvent(new Event("aiagent:sidebar-toggle"))} className="grid h-9 w-9 place-items-center rounded-[10px] border border-sky-200 bg-white text-sky-500 shadow-sm transition hover:bg-sky-50" aria-label="展开侧边栏" title="展开侧边栏"><Feather size={18}/></button> : <button type="button" onClick={() => window.dispatchEvent(new Event("aiagent:sidebar-toggle"))} className="flex min-w-0 items-center gap-2 rounded-xl px-1 py-1 text-left transition hover:bg-sky-50" aria-label="收起侧边栏" title="收起侧边栏"><span className="grid h-8 w-8 shrink-0 place-items-center rounded-[10px] border border-sky-200 bg-white text-sky-500 shadow-sm"><Feather size={17}/></span><span className="truncate font-serif text-xl font-semibold italic text-sky-500">{t("app.name")}</span></button>}
    </div>

    <nav className={`${compact ? "px-2" : "px-3"} pb-3`}>
      {!compact && <p className="px-2 pb-1 text-[10px] font-semibold tracking-[.15em] text-slate-400">工作台</p>}
      <div className="space-y-1">{mainItems.map((item) => <SidebarLink key={item.href} item={item} active={isActive(pathname, item.href)} compact={compact}/>)}</div>
    </nav>

    {!compact ? <section className="flex min-h-0 flex-1 flex-col border-t border-slate-200/80 px-3 py-3">
      <div className="flex items-center justify-between px-2"><p className="text-[10px] font-semibold tracking-[.15em] text-slate-400">会话记录</p><Link href="/chat" className="grid h-6 w-6 place-items-center rounded-md text-slate-400 transition hover:bg-blue-50 hover:text-blue-600" aria-label="新建会话"><Plus size={15}/></Link></div>
      <div className="workspace-scroll mt-2 min-h-0 space-y-3 overflow-y-auto pr-1">
        {pinnedGroups.length > 0 && <PinnedProjectList groups={pinnedGroups} activeSessionId={searchParams.get("session")} sessionActivity={sessionActivity} onUnpin={(group) => group.project && void toggleProjectPin(group.project, group.preference)}/>}
        <div><p className="px-2 pb-1 text-[10px] font-semibold tracking-[.12em] text-slate-400">项目</p>{groups.map((group) => <SessionGroupList key={group.key} group={group} isCollapsed={Boolean(collapsedGroups[group.key])} activeSessionId={searchParams.get("session")} sessionActivity={sessionActivity} projectMenuOpen={projectMenu?.groupKey === group.key} sessionMenuId={sessionMenu?.session.id ?? null} onToggleCollapsed={() => setCollapsedGroups((items) => ({ ...items, [group.key]: !items[group.key] }))} onNewSession={(project) => router.push(`/chat?project=${project.id}`)} onOpenProjectMenu={(anchor) => openProjectMenu(group, anchor)} onOpenSessionMenu={openSessionMenu}/>)}</div>
        {groups.length === 0 && <p className="px-2 py-4 text-xs leading-5 text-slate-400">暂无历史会话</p>}
      </div>
    </section> : <div className="flex-1 border-t border-slate-200/80"/>}

    <div className={`border-t border-slate-200 bg-white/80 ${compact ? "flex flex-col items-center gap-1 px-2 py-3" : "px-3 py-3"}`}>
      <button type="button" onClick={() => setToolsOpen(true)} className={`flex h-10 items-center rounded-xl text-sm font-medium text-slate-600 transition hover:bg-slate-100 hover:text-slate-950 ${compact ? "w-10 justify-center" : "w-full gap-3 px-3"}`} title="工具与设置"><Wrench size={16}/>{!compact && <><span>工具与设置</span><span className="ml-auto text-xs text-slate-400">选择</span></>}</button>
      <button type="button" onClick={() => void logout().finally(() => window.location.assign("/login"))} className={`text-slate-500 transition hover:bg-red-50 hover:text-red-600 ${compact ? "grid h-9 w-10 place-items-center rounded-lg" : "mt-1 flex h-9 w-full items-center gap-3 rounded-lg px-3 text-[13px]"}`} title="退出登录"><LogOut size={16}/>{!compact && "退出登录"}</button>
    </div>
    {toolsOpen && <ToolDialog compact={compact} pathname={pathname} onClose={() => setToolsOpen(false)}/>}
    {projectMenu && (() => {
      const group = groups.find((item) => item.key === projectMenu.groupKey);
      return group?.project ? <ProjectMenu position={projectMenu} menuRef={projectMenuRef} project={group.project} preference={group.preference} onClose={() => { projectMenuAnchorRef.current = null; setProjectMenu(null); }} onTogglePin={() => void toggleProjectPin(group.project!, group.preference)} onSortMode={(sortMode) => void updateProjectPreference(group.project!, { sort_mode: sortMode })}/> : null;
    })()}
    {sessionMenu && <SessionMenu position={sessionMenu} menuRef={sessionMenuRef} onClose={() => { sessionMenuAnchorRef.current = null; setSessionMenu(null); }} onTogglePin={() => void toggleSessionPin(sessionMenu.session)} onRename={() => { setRenameTarget(sessionMenu.session); setSessionMenu(null); }} onArchive={() => { void archiveSessionItem(sessionMenu.session); setSessionMenu(null); }} />}
    {renameTarget && <RenameSessionDialog session={renameTarget} onClose={() => setRenameTarget(null)} onSave={async (title) => { await renameSession(renameTarget.id, title); setSessions((items) => items.map((item) => item.id === renameTarget.id ? { ...item, title } : item)); setRenameTarget(null); }} />}
    </aside>
  </>;
}

function SidebarLink({ item, active, compact }: { item: NavItem; active: boolean; compact: boolean }) {
  const Icon = item.icon;
  return <Link href={item.href} title={compact ? item.label : undefined} className={`flex h-10 items-center rounded-xl transition ${compact ? "justify-center" : "gap-3 px-3 text-sm"} ${active ? "bg-blue-600 text-white shadow-sm shadow-blue-200" : "text-slate-600 hover:bg-slate-100 hover:text-slate-950"}`}><Icon size={17}/>{!compact && <span className="truncate">{item.label}</span>}</Link>;
}

function PinnedProjectList({ groups, activeSessionId, sessionActivity, onUnpin }: { groups: SessionGroup[]; activeSessionId: string | null; sessionActivity: Record<string, ChatStreamStatus | "unread">; onUnpin: (group: SessionGroup) => void }) {
  return <section><p className="px-2 pb-1 text-[10px] font-semibold tracking-[.12em] text-amber-600">置顶项目</p><div className="space-y-1 rounded-xl border border-amber-100 bg-amber-50/50 p-1.5">{groups.map((group) => <div key={group.key}><div className="flex items-center gap-1.5 px-1.5 py-1 text-xs font-semibold text-slate-700"><Pin size={12} className="text-amber-600"/><span className="min-w-0 flex-1 truncate">{group.label}</span><button type="button" onClick={() => onUnpin(group)} className="grid h-6 w-6 shrink-0 place-items-center rounded-md text-amber-600 transition hover:bg-amber-100" aria-label={`取消置顶项目：${group.label}`} title="取消置顶项目"><PinOff size={13}/></button></div>{group.sessions.map((session) => <Link key={session.id} href={`/chat?session=${encodeURIComponent(session.id)}`} className={`ml-3 flex min-w-0 items-center gap-1.5 rounded-md px-2 py-1 text-xs ${activeSessionId === session.id ? "bg-white text-blue-700 shadow-sm" : "text-slate-600 hover:bg-white/80"}`}><MessageSquare size={11}/><span className="truncate">{session.title}</span><SessionActivityIndicator status={sessionActivity[session.id]} hidden={activeSessionId === session.id && sessionActivity[session.id] !== "streaming"}/></Link>)}</div>)}</div></section>;
}

function SessionGroupList({ group, isCollapsed, activeSessionId, sessionActivity, projectMenuOpen, sessionMenuId, onToggleCollapsed, onNewSession, onOpenProjectMenu, onOpenSessionMenu }: { group: SessionGroup; isCollapsed: boolean; activeSessionId: string | null; sessionActivity: Record<string, ChatStreamStatus | "unread">; projectMenuOpen: boolean; sessionMenuId: string | null; onToggleCollapsed: () => void; onNewSession: (project: CodeProject) => void; onOpenProjectMenu: (anchor: HTMLElement) => void; onOpenSessionMenu: (session: SessionSummary, anchor: HTMLElement) => void }) {
  const projectPinned = Boolean(group.preference?.is_pinned);
  return <div className="rounded-xl border border-slate-100 bg-white/70 px-1.5 py-1 shadow-sm">
    <div className="flex h-8 items-center gap-1">
      <button type="button" onClick={onToggleCollapsed} className="flex min-w-0 flex-1 items-center gap-1.5 rounded-lg px-1.5 text-left text-xs font-semibold text-slate-700 hover:bg-slate-100"><ChevronDown size={14} className={`shrink-0 text-slate-400 transition ${isCollapsed ? "-rotate-90" : ""}`}/><FolderGit2 size={14} className={projectPinned ? "shrink-0 text-amber-500" : "shrink-0 text-blue-500"}/><span className="truncate">{group.label}</span><span className="ml-auto text-[10px] font-normal text-slate-400">{group.sessions.length}</span></button>
      {group.project && <button type="button" onClick={() => onNewSession(group.project!)} className="grid h-6 w-6 shrink-0 place-items-center rounded-md text-slate-400 transition hover:bg-blue-50 hover:text-blue-600" aria-label={`以项目“${group.label}”新建会话`} title="以当前项目新建会话"><Plus size={14}/></button>}
      {group.project && <button type="button" onClick={(event) => onOpenProjectMenu(event.currentTarget)} className={`grid h-6 w-6 shrink-0 place-items-center rounded-md transition ${projectMenuOpen ? "bg-slate-100 text-slate-700" : "text-slate-400 hover:bg-slate-100 hover:text-slate-700"}`} aria-label="项目菜单" aria-expanded={projectMenuOpen}><Ellipsis size={14}/></button>}
    </div>
    {!isCollapsed && <div className="ml-3 border-l border-slate-100 pl-1">{group.sessions.map((session) => <div key={session.id} className={`group flex items-center gap-0.5 rounded-lg py-0.5 ${activeSessionId === session.id ? "bg-blue-50" : "hover:bg-slate-100"}`}><Link href={`/chat?session=${encodeURIComponent(session.id)}`} className={`flex min-w-0 flex-1 items-center gap-1.5 rounded-md px-2 py-1.5 text-xs ${activeSessionId === session.id ? "text-blue-700" : "text-slate-600"}`}><MessageSquare size={12} className="shrink-0"/><span className="truncate">{session.title}</span><SessionActivityIndicator status={sessionActivity[session.id]} hidden={activeSessionId === session.id && sessionActivity[session.id] !== "streaming"}/></Link><button type="button" onClick={(event) => onOpenSessionMenu(session, event.currentTarget)} className={`grid h-6 w-6 shrink-0 place-items-center rounded-md transition ${sessionMenuId === session.id ? "bg-white text-slate-700 shadow-sm" : "text-slate-300 hover:bg-white hover:text-slate-600"}`} aria-label={`会话菜单：${session.title}`} aria-expanded={sessionMenuId === session.id}><Ellipsis size={14}/></button></div>)}</div>}
  </div>;
}

function SessionActivityIndicator({ status, hidden }: { status?: ChatStreamStatus | "unread"; hidden: boolean }) {
  if (!status || hidden) return null;
  if (status === "streaming") return <span className="h-2 w-2 shrink-0 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" title="正在生成" aria-label="正在生成"/>;
  if (status === "error") return <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-rose-500" title="本会话生成失败" aria-label="本会话生成失败"/>;
  return <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-amber-400" title="本会话有新回复" aria-label="本会话有新回复"/>;
}

function SessionMenu({ position, menuRef, onClose, onTogglePin, onRename, onArchive }: { position: SessionMenuState; menuRef: { current: HTMLDivElement | null }; onClose: () => void; onTogglePin: () => void; onRename: () => void; onArchive: () => void }) {
  const pinned = position.session.is_pinned;
  return createPortal(<div ref={menuRef} style={{ top: position.top, left: position.left }} className="fixed z-[70] w-44 rounded-xl border border-slate-200 bg-white p-1.5 text-xs text-slate-700 shadow-xl"><p className="truncate px-2.5 py-1.5 text-[10px] font-semibold tracking-[.12em] text-slate-400" title={position.session.title}>会话操作</p><button type="button" onClick={() => { onTogglePin(); onClose(); }} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left hover:bg-slate-50">{pinned ? <PinOff size={14}/> : <Pin size={14}/>} {pinned ? "取消置顶" : "置顶"}</button><button type="button" onClick={onRename} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left hover:bg-slate-50"><Edit3 size={14}/>重命名</button><div className="my-1 border-t border-slate-100"/><button type="button" onClick={onArchive} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-slate-600 hover:bg-amber-50 hover:text-amber-700"><Archive size={14}/>归档</button></div>, document.body);
}

function RenameSessionDialog({ session, onClose, onSave }: { session: SessionSummary; onClose: () => void; onSave: (title: string) => Promise<void> }) {
  const [title, setTitle] = useState(session.title);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  return <div className="fixed inset-0 z-[80] flex items-center justify-center bg-slate-950/35 p-4" role="dialog" aria-modal="true" aria-label="重命名会话"><form onSubmit={(event) => { event.preventDefault(); const next = title.trim(); if (!next) return setError("请输入会话名称。"); setSaving(true); setError(""); void onSave(next).catch((value) => setError(value instanceof Error ? value.message : "重命名失败，请稍后重试。")).finally(() => setSaving(false)); }} className="w-full max-w-sm rounded-2xl bg-white p-5 shadow-2xl"><div className="flex items-start justify-between gap-4"><div><h2 className="text-base font-semibold text-slate-900">重命名会话</h2><p className="mt-1 text-xs leading-5 text-slate-500">名称会立即显示在左侧会话记录中。</p></div><button type="button" onClick={onClose} disabled={saving} className="grid h-7 w-7 place-items-center rounded-md text-slate-400 hover:bg-slate-100" aria-label="关闭"><X size={16}/></button></div><input autoFocus maxLength={160} value={title} onChange={(event) => setTitle(event.target.value)} className="mt-4 h-10 w-full rounded-lg border border-slate-200 px-3 text-sm outline-none focus:border-blue-400 focus:ring-2 focus:ring-blue-50"/><p className="mt-1.5 text-right text-[11px] text-slate-400">{title.length}/160</p>{error && <p className="mt-3 rounded-lg border border-red-100 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>}<div className="mt-5 flex justify-end gap-2 border-t border-slate-100 pt-4"><button type="button" onClick={onClose} disabled={saving} className="secondary-button">取消</button><button disabled={saving} className="primary-button">{saving ? "保存中…" : "保存"}</button></div></form></div>;
}

function ProjectMenu({ position, menuRef, project, preference, onClose, onTogglePin, onSortMode }: { position: ProjectMenuState; menuRef: { current: HTMLDivElement | null }; project: CodeProject; preference?: ProjectSessionPreference; onClose: () => void; onTogglePin: () => void; onSortMode: (mode: ProjectSessionSortMode) => void }) {
  const mode = preference?.sort_mode ?? "updated";
  return createPortal(<div ref={menuRef} style={{ top: position.top, left: position.left }} className="fixed z-[70] w-48 rounded-xl border border-slate-200 bg-white p-1.5 text-xs text-slate-700 shadow-xl"><p className="px-2.5 py-1.5 text-[10px] font-semibold tracking-[.12em] text-slate-400">整理 · {project.display_name}</p><button type="button" onClick={() => { onTogglePin(); onClose(); }} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left hover:bg-slate-50">{preference?.is_pinned ? <PinOff size={14}/> : <Pin size={14}/>} {preference?.is_pinned ? "取消置顶项目" : "置顶项目"}</button><div className="my-1 border-t border-slate-100"/><p className="px-2.5 py-1.5 text-[10px] font-semibold tracking-[.12em] text-slate-400">排序方式</p>{([['priority', '优先级'], ['updated', '最近更新'], ['manual', '手动排序']] as const).map(([sortMode, label]) => <button key={sortMode} type="button" onClick={() => { onSortMode(sortMode); onClose(); }} className={`flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left ${mode === sortMode ? "bg-blue-50 text-blue-700" : "hover:bg-slate-50"}`}>{mode === sortMode ? <Check size={14}/> : <span className="w-3.5"/>}{label}</button>)}</div>, document.body);
}

function ToolDialog({ compact, pathname, onClose }: { compact: boolean; pathname: string; onClose: () => void }) {
  return <div className="fixed inset-0 z-50 bg-slate-950/30" onMouseDown={onClose}><div className={`absolute bottom-4 w-72 rounded-2xl border border-slate-200 bg-white p-3 shadow-2xl ${compact ? "left-[84px]" : "left-[252px]"}`} onMouseDown={(event) => event.stopPropagation()}><div className="flex items-center justify-between px-2 pb-2"><span className="text-sm font-semibold text-slate-900">工具与设置</span><button type="button" onClick={onClose} className="grid h-7 w-7 place-items-center rounded-lg text-slate-400 hover:bg-slate-100" aria-label="关闭"><X size={15}/></button></div><div className="space-y-1">{toolItems.map((item) => <SidebarLink key={item.href} item={item} active={isToolActive(pathname, item.href)} compact={false}/>)}</div></div></div>;
}

function isActive(pathname: string, href: string) {
  return href === "/chat" ? pathname === href : pathname === href || pathname.startsWith(`${href}/`);
}

function isToolActive(pathname: string, href: string) {
  if (href !== "/settings") return pathname === href || pathname.startsWith(`${href}/`);
  const dedicatedToolActive = toolItems.some((item) => item.href !== "/settings" && (pathname === item.href || pathname.startsWith(`${item.href}/`)));
  return !dedicatedToolActive && (pathname === "/settings" || pathname.startsWith("/settings/"));
}

function sortSessions(sessions: SessionSummary[], mode: ProjectSessionSortMode) {
  return sessions.slice().sort((left, right) => {
    const pinned = Number(right.is_pinned) - Number(left.is_pinned);
    if (pinned) return pinned;
    if (mode === "priority") return priorityWeight(right.priority) - priorityWeight(left.priority) || compareUpdated(right, left);
    if (mode === "manual") return right.sort_order - left.sort_order || compareUpdated(right, left);
    return compareUpdated(right, left);
  });
}

function compareUpdated(left: SessionSummary, right: SessionSummary) {
  return new Date(left.updated_at ?? 0).getTime() - new Date(right.updated_at ?? 0).getTime();
}

function priorityWeight(priority: SessionSummary["priority"]) {
  return priority === "high" ? 3 : priority === "normal" ? 2 : 1;
}
