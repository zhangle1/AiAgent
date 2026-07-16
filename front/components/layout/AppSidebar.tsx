"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState, type ReactNode } from "react";
import {
  BookOpen,
  Bot,
  Brain,
  ChevronLeft,
  Code2,
  Feather,
  GitBranch,
  HeartHandshake,
  Home,
  LayoutDashboard,
  Library,
  LogOut,
  MessageSquare,
  PenLine,
  Plus,
  Settings,
  SquareLibrary,
  Trash2,
  type LucideIcon,
} from "lucide-react";
import { useI18n } from "@/i18n/I18nProvider";
import type { TranslationKey } from "@/i18n/dictionaries";
import { logout } from "@/lib/auth-api";
import { deleteSession, listSessions, type SessionSummary } from "@/lib/session-api";

type SidebarItem = { labelKey?: TranslationKey; label?: string; href: string; icon: LucideIcon };

const mainItems: SidebarItem[] = [
  { labelKey: "nav.home", href: "/chat", icon: Home },
  { labelKey: "nav.partners", href: "/partners", icon: HeartHandshake },
  { labelKey: "nav.myAgents", href: "/agents", icon: Bot },
  { labelKey: "nav.coWriter", href: "/co-writer", icon: PenLine },
  { labelKey: "nav.book", href: "/book", icon: BookOpen },
  { labelKey: "nav.learningSpace", href: "/learning-space", icon: SquareLibrary },
];

const bottomItems: SidebarItem[] = [
  { labelKey: "nav.memory", href: "/memory", icon: Brain },
  { labelKey: "nav.knowledgeCenter", href: "/knowledge", icon: Library },
  { labelKey: "nav.codeRepositories", href: "/settings/code-repositories", icon: Code2 },
  { label: "Git 管理", href: "/settings/git", icon: GitBranch },
  { label: "看板应用生成", href: "/dashboard-applications", icon: LayoutDashboard },
  { labelKey: "nav.settings", href: "/settings", icon: Settings },
];

export function AppSidebar() {
  const pathname = usePathname() ?? "/";
  const router = useRouter();
  const searchParams = useSearchParams();
  const { t } = useI18n();
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [deletingSessionId, setDeletingSessionId] = useState<string | null>(null);

  useEffect(() => {
    const load = () => { void listSessions().then(setSessions).catch(() => setSessions([])); };
    load();
    window.addEventListener("aiagent:sessions-updated", load);
    return () => window.removeEventListener("aiagent:sessions-updated", load);
  }, [pathname]);

  const removeSession = async (session: SessionSummary) => {
    if (!window.confirm(`确定删除会话“${session.title}”吗？`)) return;
    setDeletingSessionId(session.id);
    try {
      await deleteSession(session.id);
      setSessions((items) => items.filter((item) => item.id !== session.id));
      if (searchParams.get("session") === session.id) router.replace("/chat");
    } catch (ex) {
      window.alert(ex instanceof Error ? ex.message : "删除会话失败，请重试。");
    } finally {
      setDeletingSessionId(null);
    }
  };

  return (
    <aside className="fixed inset-y-0 left-0 z-20 hidden w-[220px] flex-col border-r border-slate-200/90 bg-[#fbfcff] lg:flex">
      <div className="flex h-[62px] items-center justify-between px-3.5">
        <Link href="/chat" className="group flex min-w-0 items-center gap-2 rounded-xl px-1.5 py-1 transition hover:bg-sky-50">
          <span className="flex h-8 w-8 items-center justify-center rounded-[10px] border border-sky-200 bg-gradient-to-br from-white to-sky-50 text-sky-500 shadow-[0_3px_10px_rgba(14,165,233,0.14)]">
            <Feather size={17} strokeWidth={1.8} />
          </span>
          <span className="font-serif text-[20px] font-semibold italic tracking-tight text-sky-500">{t("app.name")}</span>
        </Link>
        <button className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 transition hover:bg-slate-100 hover:text-slate-700" aria-label="收起侧边栏">
          <ChevronLeft size={16} />
        </button>
      </div>

      <nav className="px-3 pb-2">
        <SidebarSectionLabel>工作台</SidebarSectionLabel>
        <div className="mt-1 space-y-0.5">
          {mainItems.map((item) => <SidebarLink key={item.href} item={item} active={isActive(pathname, item.href)} />)}
        </div>
      </nav>

      <section className="mt-4 flex min-h-0 flex-1 flex-col px-3 pb-3">
        <div className="flex items-center justify-between px-2">
          <SidebarSectionLabel className="mb-0">会话记录</SidebarSectionLabel>
          <Link href="/chat" className="grid h-7 w-7 place-items-center rounded-lg border border-slate-200 bg-white text-slate-500 shadow-sm transition hover:border-blue-200 hover:bg-blue-50 hover:text-blue-600" aria-label="新建会话">
            <Plus size={15} />
          </Link>
        </div>
        <div className="workspace-scroll mt-2 min-h-0 space-y-0.5 overflow-y-auto pr-1">
          {sessions.map((session) => (
            <div key={session.id} className="group flex items-center gap-0.5 rounded-lg px-1 py-0.5 transition hover:bg-slate-100/90">
              <Link href={`/chat?session=${encodeURIComponent(session.id)}`} className="flex min-w-0 flex-1 items-center gap-2 rounded-md px-2 py-1.5 text-[12px] text-slate-500 transition group-hover:text-slate-800">
                <MessageSquare size={13} strokeWidth={1.7} className="shrink-0 text-slate-400" />
                <span className="truncate">{session.title}</span>
              </Link>
              <button type="button" onClick={() => void removeSession(session)} disabled={deletingSessionId === session.id} className="hidden h-6 w-6 shrink-0 place-items-center rounded-md text-slate-400 transition hover:bg-red-50 hover:text-red-600 disabled:opacity-40 group-hover:grid" aria-label={`删除会话：${session.title}`}>
                <Trash2 size={13} />
              </button>
            </div>
          ))}
          {sessions.length === 0 && <p className="px-2 py-3 text-[11px] leading-5 text-slate-400">暂时没有历史会话</p>}
        </div>
      </section>

      <nav className="border-t border-slate-200/80 bg-white/70 px-3 py-3">
        <SidebarSectionLabel>工具与设置</SidebarSectionLabel>
        <div className="mt-1 space-y-0.5">
          {bottomItems.map((item) => <SidebarLink key={item.href} item={item} active={isActive(pathname, item.href)} />)}
        </div>
        <button type="button" onClick={() => void logout().finally(() => window.location.assign("/login"))} className="mt-2 flex h-9 w-full items-center gap-3 rounded-lg px-3 text-[13px] text-slate-500 transition hover:bg-red-50 hover:text-red-600">
          <LogOut size={16} strokeWidth={1.7} />退出登录
        </button>
        <div className="mt-2 px-2 text-[10px] font-medium tracking-wide text-slate-400">AiAgent · v0.1.0</div>
      </nav>
    </aside>
  );
}

function SidebarSectionLabel({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <p className={`mb-1 px-2 text-[10px] font-semibold uppercase tracking-[0.16em] text-slate-400 ${className}`}>{children}</p>;
}

function SidebarLink({ item, active }: { item: SidebarItem; active: boolean }) {
  const Icon = item.icon;
  const { t } = useI18n();
  return (
    <Link href={item.href} className={`flex h-9 items-center gap-3 rounded-lg px-3 text-[13px] font-medium transition-all duration-150 ${active ? "bg-blue-600 text-white shadow-[0_6px_16px_rgba(37,99,235,0.22)]" : "text-slate-600 hover:bg-slate-100 hover:text-slate-950"}`}>
      <Icon size={16} strokeWidth={1.75} className={active ? "text-white" : "text-slate-500"} />
      <span className="truncate">{item.label ?? t(item.labelKey!)}</span>
    </Link>
  );
}

function isActive(pathname: string, href: string) {
  if (href === "/") return pathname === "/";
  if (href === "/settings") return pathname === href || (pathname.startsWith(`${href}/`) && !pathname.startsWith("/settings/code-repositories") && !pathname.startsWith("/settings/git"));
  return pathname === href || pathname.startsWith(`${href}/`);
}
