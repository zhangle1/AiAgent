"use client";

import Link from "next/link";
import { Bot, Brain, ChevronRight, Code2, Database, GitBranch, MessageSquare, Network, Palette, Settings2, Users, type LucideIcon } from "lucide-react";
import { activeModel, activeProfile, serviceConfigured, type Catalog, type ServiceName } from "@/lib/settings-types";
import { useI18n } from "@/i18n/I18nProvider";
import type { TranslationKey } from "@/i18n/dictionaries";

const cards: Array<{ titleKey: TranslationKey; descKey: TranslationKey; icon: LucideIcon; href: string }> = [
  { titleKey: "settings.appearance", descKey: "settings.appearanceDesc", icon: Palette, href: "/settings" },
  { titleKey: "settings.network", descKey: "settings.networkDesc", icon: Network, href: "/settings" },
  { titleKey: "settings.models", descKey: "settings.modelsDesc", icon: Bot, href: "/settings/models" },
  { titleKey: "settings.knowledgeBase", descKey: "settings.knowledgeBaseDesc", icon: Database, href: "/settings" },
  { titleKey: "settings.codeRepository", descKey: "settings.codeRepositoryDesc", icon: Code2, href: "/settings/code-repositories" },
  { titleKey: "settings.gitAccounts", descKey: "settings.gitAccountsDesc", icon: GitBranch, href: "/settings/git-accounts" },
  { titleKey: "settings.chat", descKey: "settings.chatDesc", icon: MessageSquare, href: "/settings" },
  { titleKey: "settings.partnersAgents", descKey: "settings.partnersAgentsDesc", icon: Users, href: "/settings" },
  { titleKey: "nav.memory", descKey: "settings.memoryDesc", icon: Brain, href: "/settings" },
];

const statusItems: Array<{ labelKey: TranslationKey; service?: ServiceName }> = [
  { labelKey: "settings.backend" },
  { labelKey: "models.llm", service: "llm" },
  { labelKey: "models.embedding", service: "embedding" },
  { labelKey: "models.search", service: "search" },
];

export function SettingsHub({ catalog }: { catalog: Catalog | null }) {
  const { language, setLanguage, t } = useI18n();

  return (
    <section className="min-w-0">
      <div className="mb-9 flex items-start justify-between gap-4">
        <div>
          <h1 className="font-serif text-[28px] font-semibold leading-tight tracking-tight">{t("settings.title")}</h1>
          <p className="mt-2 text-[13px] text-[var(--muted-foreground)]">{t("settings.description")}</p>
        </div>
        <button className="inline-flex h-9 items-center gap-2 rounded-md border border-[var(--border)] bg-white px-3 text-[12px] text-[var(--muted-foreground)]">
          <Settings2 size={14} />
          {t("common.tour")}
        </button>
      </div>

      <div className="mb-5 flex flex-wrap gap-x-7 gap-y-3 rounded-2xl border border-[var(--border)] bg-white px-5 py-4 shadow-sm">
        {statusItems.map((item) => {
          const ready = item.service ? Boolean(catalog && serviceConfigured(catalog, item.service)) : Boolean(catalog);
          return (
            <div key={item.labelKey} className="inline-flex items-center gap-2 text-[12px]">
              <span className={`h-2 w-2 rounded-full ${ready ? "bg-emerald-500" : "bg-zinc-300"}`} />
              <span className="font-medium text-black">{t(item.labelKey)}</span>
              <span className="text-[var(--muted-foreground)]">{statusText(catalog, item.service, t)}</span>
            </div>
          );
        })}
      </div>

      <section className="mb-6 rounded-2xl border border-[var(--border)] bg-white p-5 shadow-sm">
        <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_220px] md:items-center">
          <div>
            <h2 className="text-[15px] font-semibold">{t("settings.language")}</h2>
            <p className="mt-1 text-[12.5px] leading-relaxed text-[var(--muted-foreground)]">{t("settings.languageDesc")}</p>
          </div>
          <select
            value={language}
            onChange={(event) => void setLanguage(event.target.value === "en-US" ? "en-US" : "zh-CN")}
            className="h-10 rounded-md border border-[var(--border)] bg-white px-3 text-[13px]"
          >
            <option value="zh-CN">中文</option>
            <option value="en-US">English</option>
          </select>
        </div>
      </section>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {cards.map((card) => {
          const Icon = card.icon;
          return (
            <Link key={card.titleKey} href={card.href} className="group flex min-h-[158px] flex-col justify-between rounded-2xl border border-[var(--border)] bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:border-blue-200 hover:shadow-md">
              <div className="flex items-start justify-between gap-3">
                <div className="flex items-center gap-3">
                  <Icon size={19} strokeWidth={1.6} className="text-[var(--muted-foreground)]" />
                  <h2 className="text-[15px] font-semibold leading-snug">{t(card.titleKey)}</h2>
                </div>
                <ChevronRight size={18} className="text-[var(--muted-foreground)] transition group-hover:translate-x-0.5 group-hover:text-blue-600" />
              </div>
              <p className="text-[12.5px] leading-relaxed text-[var(--muted-foreground)]">{t(card.descKey)}</p>
            </Link>
          );
        })}
      </div>
    </section>
  );
}

function statusText(catalog: Catalog | null, service: ServiceName | undefined, t: (key: TranslationKey) => string) {
  if (!catalog) return t("common.checking");
  if (!service) return t("common.online");
  if (!serviceConfigured(catalog, service)) return t("common.notSet");
  if (service === "search") return activeProfile(catalog, service)?.provider || t("common.configured");
  return activeModel(catalog, service)?.model || t("common.configured");
}
