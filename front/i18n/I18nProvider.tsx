"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { getUiSettings, updateUiSettings } from "@/lib/api";
import { dictionaries, normalizeLanguage, type AppLanguage, type TranslationKey } from "@/i18n/dictionaries";

type TranslateParams = Record<string, string | number>;

type I18nContextValue = {
  language: AppLanguage;
  setLanguage: (language: AppLanguage) => Promise<void>;
  t: (key: TranslationKey, params?: TranslateParams) => string;
};

const I18nContext = createContext<I18nContextValue | null>(null);

export function I18nProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<AppLanguage>("zh-CN");

  useEffect(() => {
    let cancelled = false;
    const cached = typeof window === "undefined" ? null : window.localStorage.getItem("aiagent.language");
    if (cached) {
      const next = normalizeLanguage(cached);
      setLanguageState(next);
      document.documentElement.lang = next;
    }

    void getUiSettings()
      .then((ui) => {
        if (cancelled) return;
        const next = normalizeLanguage(ui.language);
        setLanguageState(next);
        document.documentElement.lang = next;
        window.localStorage.setItem("aiagent.language", next);
      })
      .catch(() => {
        if (!cached) document.documentElement.lang = "zh-CN";
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const setLanguage = useCallback(async (nextLanguage: AppLanguage) => {
    setLanguageState(nextLanguage);
    document.documentElement.lang = nextLanguage;
    window.localStorage.setItem("aiagent.language", nextLanguage);
    try {
      await updateUiSettings({ language: nextLanguage });
    } catch {
      // Keep the local UI responsive even when the backend is offline.
    }
  }, []);

  const t = useCallback(
    (key: TranslationKey, params?: TranslateParams) => {
      const template = dictionaries[language][key] ?? dictionaries["zh-CN"][key] ?? key;
      if (!params) return template;
      return Object.entries(params).reduce(
        (text, [name, value]) => text.replaceAll(`{{${name}}}`, String(value)),
        template,
      );
    },
    [language],
  );

  const value = useMemo(() => ({ language, setLanguage, t }), [language, setLanguage, t]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n() {
  const value = useContext(I18nContext);
  if (!value) throw new Error("useI18n must be used inside I18nProvider");
  return value;
}
