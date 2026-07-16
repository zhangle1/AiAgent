"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import {
  BookOpen,
  Bot,
  Brain,
  Code2,
  LayoutDashboard,
  ChevronLeft,
  Feather,
  HeartHandshake,
  Home,
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
    <aside className="fixed inset-y-0 left-0 z-20 hidden w-[220px] flex-col border-r border-[var(--border)] bg-[#f3f3f2] lg:flex">
      <div className="flex h-12 items-center justify-between px-4">
        <Link href="/settings" className="flex min-w-0 items-center gap-2">
          <span className="flex h-6 w-6 items-center justify-center rounded-md border border-sky-200 bg-white text-sky-500">
            <Feather size={15} />
          </span>
          <span className="font-serif text-[18px] font-semibold italic text-sky-500">{t("app.name")}</span>
        </Link>
        <button className="flex h-6 w-6 items-center justify-center rounded-md text-[var(--muted-foreground)] hover:bg-white" aria-label="Collapse sidebar">
          <ChevronLeft size={14} />
        </button>
      </div>

      <nav className="mt-3 space-y-1 px-2">
        {mainItems.map((item) => (
          <SidebarLink key={item.href} item={item} active={isActive(pathname, item.href)} />
        ))}
      </nav>

      <div className="mt-7 min-h-0 flex-1 px-4">
        <div className="flex items-center justify-between"><p className="text-[12px] font-medium text-black">会话记录</p><Link href="/chat" className="rounded p-1 text-zinc-500 hover:bg-white hover:text-black" aria-label="新建会话"><Plus size={14} /></Link></div>
        <div className="mt-3 space-y-2">
          {sessions.map((session) => (
            <div key={session.id} className="group flex items-center gap-1 rounded px-1 py-1 hover:bg-white">
              <Link href={`/chat?session=${encodeURIComponent(session.id)}`} className="flex min-w-0 flex-1 items-center gap-2 text-[12px] text-[var(--muted-foreground)] hover:text-black">
                <MessageSquare size={14} strokeWidth={1.5} />
                <span className="truncate">{session.title}</span>
              </Link>
              <button type="button" onClick={() => void removeSession(session)} disabled={deletingSessionId === session.id} className="hidden h-6 w-6 shrink-0 items-center justify-center rounded text-zinc-400 hover:bg-red-50 hover:text-red-600 disabled:opacity-40 group-hover:inline-flex" aria-label={`删除会话：${session.title}`}>
                <Trash2 size={13} />
              </button>
            </div>
          ))}
          {sessions.length === 0 && <p className="px-1 text-[11px] text-zinc-400">暂无历史会话</p>}
        </div>
      </div>

      <nav className="mt-auto border-t border-[var(--border)] p-2">
        {bottomItems.map((item) => (
          <SidebarLink key={item.href} item={item} active={isActive(pathname, item.href)} />
        ))}
        <button type="button" onClick={() => void logout().finally(() => window.location.assign("/login"))} className="mt-1 flex h-9 w-full items-center gap-3 rounded-lg px-3 text-[13px] text-black hover:bg-white/70"><LogOut size={16} strokeWidth={1.6} />退出登录</button>
        <div className="mt-2 px-2 text-[11px] text-black">v0.1.0</div>
      </nav>
    </aside>
  );
}

function SidebarLink({
  item,
  active,
}: {
  item: SidebarItem;
  active: boolean;
}) {
  const Icon = item.icon;
  const { t } = useI18n();
  return (
    <Link
      href={item.href}
      className={`flex h-9 items-center gap-3 rounded-lg px-3 text-[13px] transition ${
        active ? "bg-white text-black shadow-sm" : "text-black hover:bg-white/70"
      }`}
    >
      <Icon size={16} strokeWidth={1.6} />
      <span className="truncate">{item.label ?? t(item.labelKey!)}</span>
    </Link>
  );
}

function isActive(pathname: string, href: string) {
  if (href === "/") return pathname === "/";
  if (href === "/settings") {
    return pathname === href || (pathname.startsWith(`${href}/`) && !pathname.startsWith("/settings/code-repositories"));
  }
  return pathname === href || pathname.startsWith(`${href}/`);
}
