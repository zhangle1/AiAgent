"use client";

import { SettingsProvider, useSettings } from "@/components/settings/SettingsProvider";
import { SettingsShell } from "@/components/settings/layout/SettingsShell";
import { useI18n } from "@/i18n/I18nProvider";

export default function SettingsLayout({ children }: { children: React.ReactNode }) {
  return (
    <SettingsProvider>
      <SettingsFrame>{children}</SettingsFrame>
    </SettingsProvider>
  );
}

function SettingsFrame({ children }: { children: React.ReactNode }) {
  const { loading, error, reload } = useSettings();
  const { t } = useI18n();

  if (loading) {
    return <main className="mx-auto max-w-7xl px-6 py-8 text-[13px] text-[var(--muted-foreground)]">{t("common.loadingSettings")}</main>;
  }

  if (error) {
    return (
      <main className="mx-auto max-w-7xl px-6 py-8">
        <div className="rounded-2xl border border-red-200 bg-red-50 p-5 text-red-800">
          <h1 className="text-[18px] font-semibold">{t("settings.unavailable")}</h1>
          <p className="mt-2 text-[13px]">{error}</p>
          <button onClick={() => void reload()} className="mt-4 rounded-md bg-red-900 px-3 py-2 text-[12px] font-medium text-white">
            {t("common.retry")}
          </button>
        </div>
      </main>
    );
  }

  return <SettingsShell>{children}</SettingsShell>;
}
