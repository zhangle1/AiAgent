"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Boxes, Code2, GitBranch, Home, Settings2 } from "lucide-react";
import { MODEL_SERVICE_CONFIGS } from "@/components/settings/models/model-service-config";
import { useI18n } from "@/i18n/I18nProvider";

export function SettingsShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { t } = useI18n();
  const onModelsPage = pathname?.startsWith("/settings/models");
  const onCodeRepositoriesPage = pathname?.startsWith("/settings/code-repositories");
  const onGitAccountsPage = pathname?.startsWith("/settings/git-accounts");
  const currentService = MODEL_SERVICE_CONFIGS.find((item) => item.href === pathname);

  return (
    <main className="mx-auto max-w-7xl px-6 py-8">
      <nav className="mb-6 flex flex-wrap items-center gap-2 text-[12px] text-[var(--muted-foreground)]">
        <Link href="/settings" className="inline-flex items-center gap-1 rounded-md px-2 py-1 hover:bg-[var(--muted)] hover:text-[var(--foreground)]">
          <Home size={14} />
          {t("settings.title")}
        </Link>
        {onModelsPage && (
          <>
            <span>/</span>
            <Link href="/settings/models" className="inline-flex items-center gap-1 rounded-md px-2 py-1 hover:bg-[var(--muted)] hover:text-[var(--foreground)]">
              <Boxes size={14} />
              {t("models.title")}
            </Link>
          </>
        )}
        {onCodeRepositoriesPage && (
          <>
            <span>/</span>
            <span className="inline-flex items-center gap-1 rounded-md px-2 py-1 font-semibold text-black"><Code2 size={14} />{t("settings.codeRepository")}</span>
          </>
        )}
        {onGitAccountsPage && (
          <>
            <span>/</span>
            <span className="inline-flex items-center gap-1 rounded-md px-2 py-1 font-semibold text-black"><GitBranch size={14} />{t("settings.gitAccounts")}</span>
          </>
        )}
        {currentService && (
          <>
            <span>/</span>
            <span className="inline-flex items-center gap-1 rounded-md px-2 py-1 font-semibold text-black">{t(currentService.titleKey)}</span>
          </>
        )}
      </nav>
      {children}
    </main>
  );
}

export function SettingsPageHeader({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: React.ReactNode;
}) {
  const { t } = useI18n();

  return (
    <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 className="font-serif text-[26px] font-semibold leading-tight">{title}</h1>
        <p className="mt-2 max-w-3xl text-[13px] leading-relaxed text-[var(--muted-foreground)]">{description}</p>
      </div>
      {action === null ? null : action ?? (
        <button className="inline-flex h-9 items-center gap-2 rounded-md border border-[var(--border)] bg-white px-3 text-[12px] text-[var(--muted-foreground)]">
          <Settings2 size={14} />
          {t("common.tour")}
        </button>
      )}
    </div>
  );
}
