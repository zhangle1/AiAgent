"use client";

import Link from "next/link";
import { ArrowUpRight } from "lucide-react";
import { useSettings } from "@/components/settings/SettingsProvider";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";
import { MODEL_SERVICE_CONFIGS } from "@/components/settings/models/model-service-config";
import { serviceConfigured } from "@/lib/settings-types";
import { useI18n } from "@/i18n/I18nProvider";

export function ModelsSettingsPage() {
  const { draft, results } = useSettings();
  const { t } = useI18n();

  if (!draft) {
    return <div className="rounded-xl border border-[var(--border)] bg-white p-5 text-[13px] text-[var(--muted-foreground)]">{t("models.loading")}</div>;
  }

  return (
    <section>
      <SettingsPageHeader
        title={t("models.title")}
        description={t("models.description")}
      />

      <div className="grid gap-4 md:grid-cols-2">
        {MODEL_SERVICE_CONFIGS.map((config) => {
          const Icon = config.icon;
          const configured = serviceConfigured(draft, config.key);
          const result = results[config.key];
          const chip = result
            ? result.state === "success"
              ? t("models.testPassed")
              : t("models.testFailed")
            : configured
              ? t("common.configured")
              : t("common.notSet");
          return (
            <Link
              key={config.key}
              href={config.href}
              className="flex min-h-[122px] items-start justify-between gap-4 rounded-2xl border border-[var(--border)] bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:border-blue-200 hover:shadow-md"
            >
              <div className="flex min-w-0 gap-4">
                <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-lg ${config.tileClass}`}>
                  <Icon size={19} />
                </span>
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="text-[15px] font-semibold">{t(config.titleKey)}</h2>
                    <span className={`rounded-full px-2 py-0.5 text-[11px] ${
                      result?.state === "success"
                        ? "bg-emerald-50 text-emerald-700"
                        : result?.state === "failed"
                          ? "bg-red-50 text-red-700"
                          : configured
                            ? "bg-zinc-100 text-zinc-700"
                            : "text-[var(--muted-foreground)]"
                    }`}>
                      {chip}
                    </span>
                  </div>
                  <p className="mt-6 text-[12.5px] leading-relaxed text-[var(--muted-foreground)]">{result?.summary || t(config.subtitleKey)}</p>
                </div>
              </div>
              <ArrowUpRight size={16} className="shrink-0 text-black" />
            </Link>
          );
        })}
      </div>
    </section>
  );
}
