"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Archive, Check, ChevronDown, Code2, Ellipsis, Feather, FolderGit2, GitBranch, Library, LogOut, MessageSquare, Pencil, Pin, PinOff, Plus, Settings, Wrench, X, type LucideIcon } from "lucide-react";
import { useI18n } from "@/i18n/I18nProvider";
import { logout } from "@/lib/auth-api";
import { getCodeProjects } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";
import { deleteSession, listProjectSessionPreferences, listSessions, renameSession, updateProjectSessionPreference, updateSessionMetadata, type ProjectSessionPreference, type ProjectSessionSortMode, type SessionSummary } from "@/lib/session-api";

type NavItem = { href: string; label: string; icon: LucideIcon };
type SessionGroup = { key: string; label: string; project?: CodeProject; preference?: ProjectSessionPreference; sessions: SessionSummary[] };
type ProjectMenuState = { groupKey: string; top: number; left: number };
type SessionMenuState = { sessionId: string; top: number; left: number };

const mainItems: NavItem[] = [
  { href: "/chat", label: "聊天", icon: MessageSquare },
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
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [projectPreferences, setProjectPreferences] = useState<ProjectSessionPreference[]>([]);
  const [collapsedGroups, setCollapsedGroups] = useState<Record<string, boolean>>({});
  const [projectMenu, setProjectMenu] = useState<ProjectMenuState | null>(null);
  const [sessionMenu, setSessionMenu] = useState<SessionMenuState | null>(null);
  const [toolsOpen, setToolsOpen] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const menuAnchorRef = useRef<HTMLElement | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);

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
      if (menuAnchorRef.current?.contains(target) || menuRef.current?.contains(target)) return;
      menuAnchorRef.current = null;
      setProjectMenu(null);
      setSessionMenu(null);
    };
    document.addEventListener("pointerdown", closeOnOutsideClick);
    return () => document.removeEventListener("pointerdown", closeOnOutsideClick);
  }, [projectMenu, sessionMenu]);

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

  async function toggleSessionPin(session: SessionSummary) {
    const next = !session.is_pinned;
    setSessions((items) => items.map((item) => item.id === session.id ? { ...item, is_pinned: next } : item));
    try { await updateSessionMetadata(session.id, { is_pinned: next }); }
    catch { window.dispatchEvent(new Event("aiagent:sessions-updated")); }
  }

  async function archiveSession(session: SessionSummary) {
    setDeletingId(session.id);
    try {
      await deleteSession(session.id);
      setSessions((items) => items.filter((item) => item.id !== session.id));
      if (searchParams.get("session") === session.id) router.replace("/chat");
    } finally {
      setDeletingId(null);
    }
  }

  async function renameChatSession(session: SessionSummary) {
    const title = window.prompt("重命名会话", session.title)?.trim();
    if (!title || title === session.title) return;
    await renameSession(session.id, title);
    setSessions((items) => items.map((item) => item.id === session.id ? { ...item, title } : item));
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
      menuAnchorRef.current = null;
      setProjectMenu(null);
      return;
    }
    const rect = anchor.getBoundingClientRect();
    menuAnchorRef.current = anchor;
    setProjectMenu({ groupKey: group.key, top: rect.bottom + 4, left: Math.max(8, rect.right - 184) });
  }

  function openSessionMenu(session: SessionSummary, anchor: HTMLElement) {
    if (sessionMenu?.sessionId === session.id) {
      menuAnchorRef.current = null;
      setSessionMenu(null);
      return;
    }
    const rect = anchor.getBoundingClientRect();
    menuAnchorRef.current = anchor;
    setProjectMenu(null);
    setSessionMenu({ sessionId: session.id, top: rect.bottom + 4, left: Math.max(8, rect.right - 176) });
  }

  return <aside className={`fixed inset-y-0 left-0 z-30 flex flex-col border-r border-slate-200 bg-[#fbfcff] transition-[width] duration-200 ${compact ? "w-[72px]" : "w-[240px]"}`}>
    <div className={`flex h-16 shrink-0 items-center ${compact ? "justify-center px-2" : "px-4"}`}>
      {compact ? <button type="button" onClick={() => window.dispatchEvent(new Event("aiagent:sidebar-toggle"))} className="grid h-9 w-9 place-items-center rounded-[10px] border border-sky-200 bg-white text-sky-500 shadow-sm transition hover:bg-sky-50" aria-label="展开侧边栏" title="展开侧边栏"><Feather size={18}/></button> : <button type="button" onClick={() => window.dispatchEvent(new Event("aiagent:sidebar-toggle"))} className="flex min-w-0 items-center gap-2 rounded-xl px-1 py-1 text-left transition hover:bg-sky-50" aria-label="收起侧边栏" title="收起侧边栏"><span className="grid h-8 w-8 shrink-0 place-items-center rounded-[10px] border border-sky-200 bg-white text-sky-500 shadow-sm"><Feather size={17}/></span><span className="truncate font-serif text-xl font-semibold italic text-sky-500">{t("app.name")}</span></button>}
    </div>

    <nav className={`${compact ? "px-2" : "px-3"} pb-3`}>
      {!compact && <p className="px-2 pb-1 text-[10px] font-semibold tracking-[.15em] text-slate-400">工作台</p>}
      <div className="space-y-1">{mainItems.map((item) => <SidebarLink key={item.href} item={item} active={isActive(pathname, item.href)} compact={compact}/>)}</div>
    </nav>

    {!compact ? <section className="flex min-h-0 flex-1 flex-col border-t border-slate-200/80 px-3 py-3">
      <div className="flex items-center justify-between px-2"><p className="text-[10px] font-semibold tracking-[.15em] text-slate-400">会话记录</p><Link href="/chat" className="grid h-6 w-6 place-items-center rounded-md text-slate-400 transition hover:bg-blue-50 hover:text-blue-600" aria-label="新建会话"><Plus size={15}/></Link></div>
      <div className="workspace-scroll mt-2 min-h-0 space-y-3 overflow-y-auto pr-1">
        {pinnedGroups.length > 0 && <PinnedProjectList groups={pinnedGroups} activeSessionId={searchParams.get("session")} onUnpin={(group) => group.project && void toggleProjectPin(group.project, group.preference)}/>}
        <div><p className="px-2 pb-1 text-[10px] font-semibold tracking-[.12em] text-slate-400">项目</p>{groups.map((group) => <SessionGroupList key={group.key} group={group} isCollapsed={Boolean(collapsedGroups[group.key])} activeSessionId={searchParams.get("session")} deletingId={deletingId} menuOpen={projectMenu?.groupKey === group.key} openSessionMenuId={sessionMenu?.sessionId ?? null} onToggleCollapsed={() => setCollapsedGroups((items) => ({ ...items, [group.key]: !items[group.key] }))} onOpenMenu={(anchor) => openProjectMenu(group, anchor)} onOpenSessionMenu={openSessionMenu}/>)}</div>
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
      return group?.project ? <ProjectMenu position={projectMenu} menuRef={menuRef} project={group.project} preference={group.preference} onClose={() => { menuAnchorRef.current = null; setProjectMenu(null); }} onTogglePin={() => void toggleProjectPin(group.project!, group.preference)} onSortMode={(sortMode) => void updateProjectPreference(group.project!, { sort_mode: sortMode })}/> : null;
    })()}
    {sessionMenu && (() => {
      const session = sessions.find((item) => item.id === sessionMenu.sessionId);
      return session ? <SessionMenu position={sessionMenu} menuRef={menuRef} session={session} isArchiving={deletingId === session.id} onClose={() => { menuAnchorRef.current = null; setSessionMenu(null); }} onTogglePin={() => void toggleSessionPin(session)} onRename={() => void renameChatSession(session)} onArchive={() => void archiveSession(session)}/> : null;
    })()}
  </aside>;
}

function SidebarLink({ item, active, compact }: { item: NavItem; active: boolean; compact: boolean }) {
  const Icon = item.icon;
  return <Link href={item.href} title={compact ? item.label : undefined} className={`flex h-10 items-center rounded-xl transition ${compact ? "justify-center" : "gap-3 px-3 text-sm"} ${active ? "bg-blue-600 text-white shadow-sm shadow-blue-200" : "text-slate-600 hover:bg-slate-100 hover:text-slate-950"}`}><Icon size={17}/>{!compact && <span className="truncate">{item.label}</span>}</Link>;
}

function PinnedProjectList({ groups, activeSessionId, onUnpin }: { groups: SessionGroup[]; activeSessionId: string | null; onUnpin: (group: SessionGroup) => void }) {
  return <section><p className="px-2 pb-1 text-[10px] font-semibold tracking-[.12em] text-amber-600">置顶项目</p><div className="space-y-1 rounded-xl border border-amber-100 bg-amber-50/50 p-1.5">{groups.map((group) => <div key={group.key}><div className="flex items-center gap-1.5 px-1.5 py-1 text-xs font-semibold text-slate-700"><Pin size={12} className="text-amber-600"/><span className="min-w-0 flex-1 truncate">{group.label}</span><button type="button" onClick={() => onUnpin(group)} className="grid h-6 w-6 shrink-0 place-items-center rounded-md text-amber-600 transition hover:bg-amber-100" aria-label={`取消置顶项目：${group.label}`} title="取消置顶项目"><PinOff size={13}/></button></div>{group.sessions.map((session) => <Link key={session.id} href={`/chat?session=${encodeURIComponent(session.id)}`} className={`ml-3 flex min-w-0 items-center gap-1.5 rounded-md px-2 py-1 text-xs ${activeSessionId === session.id ? "bg-white text-blue-700 shadow-sm" : "text-slate-600 hover:bg-white/80"}`}><MessageSquare size={11}/><span className="truncate">{session.title}</span></Link>)}</div>)}</div></section>;
}

function SessionGroupList({ group, isCollapsed, activeSessionId, deletingId, menuOpen, openSessionMenuId, onToggleCollapsed, onOpenMenu, onOpenSessionMenu }: { group: SessionGroup; isCollapsed: boolean; activeSessionId: string | null; deletingId: string | null; menuOpen: boolean; openSessionMenuId: string | null; onToggleCollapsed: () => void; onOpenMenu: (anchor: HTMLElement) => void; onOpenSessionMenu: (session: SessionSummary, anchor: HTMLElement) => void }) {
  const projectPinned = Boolean(group.preference?.is_pinned);
  return <div className="rounded-xl border border-slate-100 bg-white/70 px-1.5 py-1 shadow-sm">
    <div className="flex h-8 items-center gap-1">
      <button type="button" onClick={onToggleCollapsed} className="flex min-w-0 flex-1 items-center gap-1.5 rounded-lg px-1.5 text-left text-xs font-semibold text-slate-700 hover:bg-slate-100"><ChevronDown size={14} className={`shrink-0 text-slate-400 transition ${isCollapsed ? "-rotate-90" : ""}`}/><FolderGit2 size={14} className={projectPinned ? "shrink-0 text-amber-500" : "shrink-0 text-blue-500"}/><span className="truncate">{group.label}</span><span className="ml-auto text-[10px] font-normal text-slate-400">{group.sessions.length}</span></button>
      {group.project && <button type="button" onClick={(event) => onOpenMenu(event.currentTarget)} className={`grid h-6 w-6 shrink-0 place-items-center rounded-md transition ${menuOpen ? "bg-slate-100 text-slate-700" : "text-slate-400 hover:bg-slate-100 hover:text-slate-700"}`} aria-label="项目菜单" aria-expanded={menuOpen}><Ellipsis size={14}/></button>}
    </div>
    {!isCollapsed && <div className="ml-3 border-l border-slate-100 pl-1">{group.sessions.map((session) => <div key={session.id} className={`group flex items-center gap-0.5 rounded-lg py-0.5 ${activeSessionId === session.id ? "bg-blue-50" : "hover:bg-slate-100"}`}><Link href={`/chat?session=${encodeURIComponent(session.id)}`} className={`flex min-w-0 flex-1 items-center gap-1.5 rounded-md px-2 py-1.5 text-xs ${activeSessionId === session.id ? "text-blue-700" : "text-slate-600"}`}><MessageSquare size={12} className="shrink-0"/><span className="truncate">{session.title}</span></Link><button type="button" disabled={deletingId === session.id} onClick={(event) => onOpenSessionMenu(session, event.currentTarget)} className={`grid h-6 w-6 shrink-0 place-items-center rounded-md transition ${openSessionMenuId === session.id ? "bg-white text-slate-700" : "text-slate-300 hover:bg-white hover:text-slate-600"}`} aria-label={`会话菜单：${session.title}`} aria-expanded={openSessionMenuId === session.id}><Ellipsis size={14}/></button></div>)}</div>}
  </div>;
}

function ProjectMenu({ position, menuRef, project, preference, onClose, onTogglePin, onSortMode }: { position: ProjectMenuState; menuRef: { current: HTMLDivElement | null }; project: CodeProject; preference?: ProjectSessionPreference; onClose: () => void; onTogglePin: () => void; onSortMode: (mode: ProjectSessionSortMode) => void }) {
  const mode = preference?.sort_mode ?? "updated";
  return createPortal(<div ref={menuRef} style={{ top: position.top, left: position.left }} className="fixed z-[70] w-48 rounded-xl border border-slate-200 bg-white p-1.5 text-xs text-slate-700 shadow-xl"><p className="px-2.5 py-1.5 text-[10px] font-semibold tracking-[.12em] text-slate-400">整理 · {project.display_name}</p><button type="button" onClick={() => { onTogglePin(); onClose(); }} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left hover:bg-slate-50">{preference?.is_pinned ? <PinOff size={14}/> : <Pin size={14}/>} {preference?.is_pinned ? "取消置顶项目" : "置顶项目"}</button><div className="my-1 border-t border-slate-100"/><p className="px-2.5 py-1.5 text-[10px] font-semibold tracking-[.12em] text-slate-400">排序方式</p>{([['priority', '优先级'], ['updated', '最近更新'], ['manual', '手动排序']] as const).map(([sortMode, label]) => <button key={sortMode} type="button" onClick={() => { onSortMode(sortMode); onClose(); }} className={`flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left ${mode === sortMode ? "bg-blue-50 text-blue-700" : "hover:bg-slate-50"}`}>{mode === sortMode ? <Check size={14}/> : <span className="w-3.5"/>}{label}</button>)}</div>, document.body);
}

function SessionMenu({ position, menuRef, session, isArchiving, onClose, onTogglePin, onRename, onArchive }: { position: SessionMenuState; menuRef: { current: HTMLDivElement | null }; session: SessionSummary; isArchiving: boolean; onClose: () => void; onTogglePin: () => void; onRename: () => void; onArchive: () => void }) {
  return createPortal(
    <div ref={menuRef} style={{ top: position.top, left: position.left }} className="fixed z-[70] w-44 rounded-xl border border-slate-200 bg-white p-1.5 text-xs text-slate-700 shadow-xl">
      <p className="truncate px-2.5 py-1.5 text-[10px] font-semibold tracking-[.12em] text-slate-400">会话操作</p>
      <button type="button" onClick={() => { onTogglePin(); onClose(); }} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left hover:bg-slate-50">
        {session.is_pinned ? <PinOff size={14}/> : <Pin size={14}/>} {session.is_pinned ? "取消置顶" : "置顶"}
      </button>
      <button type="button" onClick={() => { onRename(); onClose(); }} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left hover:bg-slate-50">
        <Pencil size={14}/> 重命名
      </button>
      <div className="my-1 border-t border-slate-100"/>
      <button type="button" disabled={isArchiving} onClick={() => { onArchive(); onClose(); }} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-amber-700 hover:bg-amber-50 disabled:opacity-50">
        <Archive size={14}/> 归档
      </button>
    </div>,
    document.body,
  );
}

function ToolDialog({ compact, pathname, onClose }: { compact: boolean; pathname: string; onClose: () => void }) {
  return <div className="fixed inset-0 z-50 bg-slate-950/30" onMouseDown={onClose}><div className={`absolute bottom-4 w-72 rounded-2xl border border-slate-200 bg-white p-3 shadow-2xl ${compact ? "left-[84px]" : "left-[252px]"}`} onMouseDown={(event) => event.stopPropagation()}><div className="flex items-center justify-between px-2 pb-2"><span className="text-sm font-semibold text-slate-900">工具与设置</span><button type="button" onClick={onClose} className="grid h-7 w-7 place-items-center rounded-lg text-slate-400 hover:bg-slate-100" aria-label="关闭"><X size={15}/></button></div><div className="space-y-1">{toolItems.map((item) => <SidebarLink key={item.href} item={item} active={isActive(pathname, item.href)} compact={false}/>)}</div></div></div>;
}

function isActive(pathname: string, href: string) {
  return href === "/chat" || href === "/settings"
    ? pathname === href
    : pathname === href || pathname.startsWith(`${href}/`);
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
