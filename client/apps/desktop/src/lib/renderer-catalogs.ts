import {
  flattenCatalog,
  type AppLocale,
  type EnglishCatalog,
} from "@kunbuddy/i18n/locale-info";
import { en } from "@kunbuddy/i18n/locales/en";
import { remoteEn, remoteZh } from "../locales/remote";

type LocaleModule = { default: EnglishCatalog };
type NonEnglishLocale = Exclude<AppLocale, "en">;

const localeLoaders: Record<NonEnglishLocale, () => Promise<LocaleModule>> = {
  "zh-CN": () => import("@kunbuddy/i18n/locales/zh-CN"),
  "zh-TW": () => import("@kunbuddy/i18n/locales/zh-TW"),
  tr: () => import("@kunbuddy/i18n/locales/tr"),
  de: () => import("@kunbuddy/i18n/locales/de"),
  es: () => import("@kunbuddy/i18n/locales/es"),
  fr: () => import("@kunbuddy/i18n/locales/fr"),
  ko: () => import("@kunbuddy/i18n/locales/ko"),
  "pt-BR": () => import("@kunbuddy/i18n/locales/pt-BR"),
};

const catalogCache = new Map<AppLocale, Promise<EnglishCatalog>>();

export function loadRendererCatalog(locale: AppLocale): Promise<EnglishCatalog> {
  const cached = catalogCache.get(locale);
  if (cached) return cached;

  const pending =
    locale === "en"
      ? Promise.resolve(en)
      : localeLoaders[locale]().then((module) => module.default);
  catalogCache.set(locale, pending);
  void pending.catch(() => catalogCache.delete(locale));
  return pending;
}

export async function loadRendererResources(
  locale: AppLocale,
): Promise<Record<string, { translation: Record<string, string> }>> {
  const localeIds = [...new Set<AppLocale>(["en", locale])];
  const entries = await Promise.all(
    localeIds.map(async (id) => {
      const catalog = await loadRendererCatalog(id);
      return [
        id,
        {
          remote: id === "zh-CN" || id === "zh-TW" ? remoteZh : remoteEn,
          translation: flattenCatalog(
            catalog as unknown as Record<string, unknown>,
          ),
        },
      ] as const;
    }),
  );
  return Object.fromEntries(entries);
}
