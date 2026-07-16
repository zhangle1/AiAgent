"use client";

import { ChevronDown, FlaskConical, Plus, Save, Trash2, UploadCloud } from "lucide-react";
import { activeModel, activeProfile, type CatalogModel, type CatalogProfile } from "@/lib/settings-types";
import { useSettings } from "@/components/settings/SettingsProvider";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";
import type { ModelServiceConfig } from "@/components/settings/models/model-service-config";
import { ProviderIcon } from "@/components/settings/models/ProviderIcon";
import { useI18n } from "@/i18n/I18nProvider";
import type { TranslationKey } from "@/i18n/dictionaries";

const fieldLabelKeys: Record<string, TranslationKey> = {
  model: "models.modelId",
  dimension: "models.dimension",
  context_window: "models.contextWindow",
  voice: "models.voice",
  response_format: "models.responseFormat",
  language: "models.language",
  size: "models.imageSize",
  quality: "models.quality",
  aspect_ratio: "models.aspectRatio",
  duration: "models.duration",
  resolution: "models.resolution",
};

export function ModelServiceEditor({ config }: { config: ModelServiceConfig }) {
  const { t } = useI18n();
  const settings = useSettings();
  const {
    draft,
    providers,
    results,
    dirty,
    saving,
    applying,
    testing,
    addProfile,
    addModel,
    setActiveProfile,
    setActiveModel,
    deleteActiveProfile,
    deleteActiveModel,
    updateProfile,
    updateModel,
    runTest,
    saveDraft,
    applyDraft,
  } = settings;

  if (!draft) {
    return <div className="rounded-xl border border-[var(--border)] bg-white p-5 text-[13px] text-[var(--muted-foreground)]">{t("models.loading")}</div>;
  }

  const service = config.key;
  const profile = activeProfile(draft, service);
  const model = activeModel(draft, service);
  const result = results[service];
  const providerOptions = providers[service] ?? [];
  const providerField: keyof CatalogProfile = service === "search" ? "provider" : "binding";
  const providerValue = String((profile?.[providerField] as string | null | undefined) ?? "");

  return (
    <section>
      <div className="mb-5 flex items-center justify-between gap-4 text-[12px] text-[var(--muted-foreground)]">
        <span>{t("models.savedToDb")}</span>
        <div className="flex items-center gap-2">
          <button className="inline-flex h-9 items-center gap-2 rounded-md border border-[var(--border)] bg-white px-3 text-[12px]">
            <FlaskConical size={14} />
            {t("common.tour")}
          </button>
          <button
            onClick={() => void saveDraft()}
            disabled={!dirty || saving}
            className="inline-flex h-9 items-center gap-2 rounded-md border border-[var(--border)] bg-white px-3 text-[12px] disabled:cursor-not-allowed disabled:opacity-45"
          >
            <Save size={14} />
            {saving ? t("common.saving") : t("common.saveDraft")}
          </button>
          <button
            onClick={() => void applyDraft()}
            disabled={!dirty || applying}
            className="inline-flex h-9 items-center gap-2 rounded-md bg-black px-3 text-[12px] font-semibold text-white disabled:cursor-not-allowed disabled:opacity-45"
          >
            <UploadCloud size={14} />
            {applying ? t("common.applying") : t("common.apply")}
          </button>
        </div>
      </div>

      <SettingsPageHeader
        title={t(config.titleKey)}
        description={service === "llm" ? t("models.llmDescription") : t(config.subtitleKey)}
        action={null}
      />

      <div className="grid gap-5 xl:grid-cols-[200px_minmax(0,1fr)]">
        <ProfilesPanel
          config={config}
          profiles={draft.services[service].profiles}
          activeProfileId={draft.services[service].active_profile_id}
          onSelect={(profileId) => setActiveProfile(service, profileId)}
          onRename={(value) => updateProfile(service, "name", value)}
          onDelete={() => deleteActiveProfile(service)}
        />

        <div className="space-y-5">
          <section className="rounded-xl border border-[var(--border)] bg-white p-5">
            <div className="mb-5 flex items-center justify-between gap-4">
              <h2 className="text-[14px] font-semibold">{t("models.providerConnection")}</h2>
              <button onClick={() => addProfile(service)} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-[var(--border)] bg-white px-2.5 text-[12px]">
                <Plus size={13} />
                {t("common.profile")}
              </button>
            </div>

            <label className="block">
              <span className="text-[12px] text-[var(--muted-foreground)]">{t("models.provider")}</span>
              <div className="relative mt-2">
                <span className="pointer-events-none absolute left-3 top-1/2 flex -translate-y-1/2 items-center">
                  <ProviderIcon provider={providerValue} fallback={config.icon} className="h-4 w-4" />
                </span>
                <select
                  value={providerValue}
                  onChange={(event) => updateProfile(service, providerField, event.target.value)}
                  className="h-10 w-full rounded-md border border-[var(--border)] bg-white pl-10 pr-3 text-[14px]"
                >
                  <option value="">{t("models.selectProvider")}</option>
                  {providerOptions.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
              </div>
            </label>

            <div className="mt-4 space-y-4">
              <Field label={t("models.baseUrl")} value={profile?.base_url ?? ""} onChange={(value) => updateProfile(service, "base_url", value)} />
              <Field label={t("models.apiKey")} value={profile?.api_key ?? ""} onChange={(value) => updateProfile(service, "api_key", value)} type="password" />
              <button className="flex h-[62px] w-full items-center justify-between rounded-lg border border-[var(--border)] bg-white px-4 text-left">
                <span>
                  <span className="block text-[12px] font-semibold">{t("models.extra")}</span>
                  <span className="mt-1 block text-[12px] text-[var(--muted-foreground)]">{t("models.extraDesc")}</span>
                </span>
                <ChevronDown size={16} />
              </button>
            </div>
          </section>

          <section className="rounded-xl border border-[var(--border)] bg-white p-5">
            <div className="mb-5 flex items-center justify-between gap-4">
              <h2 className="text-[14px] font-semibold">{t("models.modelList")}</h2>
              {service !== "search" && (
                <div className="flex items-center gap-2">
                  <button onClick={() => addModel(service)} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-[var(--border)] bg-white px-2.5 text-[12px]">
                    <Plus size={13} />
                    {t("common.model")}
                  </button>
                  <button onClick={() => deleteActiveModel(service)} className="inline-flex h-8 items-center gap-1.5 rounded-md bg-white px-2.5 text-[12px]">
                    <Trash2 size={13} />
                    {t("common.delete")}
                  </button>
                </div>
              )}
            </div>

            {service === "search" ? (
              <p className="text-[12.5px] text-[var(--muted-foreground)]">{t("models.searchNoModels")}</p>
            ) : (
              <>
                <div className="mb-4 flex flex-wrap items-center gap-2">
                  {(profile?.models ?? []).map((item, index) => {
                    const active = item.id === draft.services[service].active_model_id;
                    return (
                      <button
                        key={item.id}
                        onClick={() => setActiveModel(service, item.id)}
                        className={`rounded-lg px-3 py-2 text-[12px] ${active ? "bg-zinc-100 font-semibold text-black" : "text-[var(--muted-foreground)] hover:bg-zinc-50"}`}
                      >
                        {item.name || item.model || `${t("common.model")} ${index + 1}`}
                      </button>
                    );
                  })}
                </div>
                <div className="grid gap-4 md:grid-cols-2">
                  <Field label={t("models.modelName")} value={model?.name ?? ""} onChange={(value) => updateModel(service, "name", value)} />
                  {config.modelFields.map((field) => {
                    if (field === "model") {
                      return <Field key={field} label={t(fieldLabelKeys[field])} value={model?.model ?? ""} onChange={(value) => updateModel(service, field, value)} />;
                    }
                    return (
                      <Field
                        key={field}
                        label={t(fieldLabelKeys[field])}
                        value={String((model?.[field as keyof CatalogModel] as string | undefined) ?? "")}
                        onChange={(value) => updateModel(service, field as keyof CatalogModel, value)}
                      />
                    );
                  })}
                </div>
              </>
            )}
          </section>

          <section className="rounded-xl border border-[var(--border)] bg-white p-4">
            <div className="flex items-center justify-between">
              <h2 className="text-[14px] font-semibold">{t("models.diagnostics")}</h2>
              <button onClick={() => void runTest(service)} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-[var(--border)] bg-white px-3 text-[12px]">
                {testing === service ? t("common.running") : t("models.runTest")}
              </button>
            </div>
            {result && (
              <>
                <div className={`mt-3 rounded-md px-3 py-2 text-[12px] ${result.state === "success" ? "bg-emerald-50 text-emerald-800" : "bg-red-50 text-red-800"}`}>
                  {result.summary || result.message}
                </div>
                <div className="mt-3 max-h-[360px] overflow-auto rounded-lg bg-[#111] p-4 font-mono text-[12px] leading-6 text-zinc-300">
                  {(result.logs?.length ? result.logs : [result.message]).map((line, index) => (
                    <div key={`${line}-${index}`}>{line}</div>
                  ))}
                </div>
              </>
            )}
          </section>
        </div>
      </div>
    </section>
  );
}

function ProfilesPanel({
  config,
  profiles,
  activeProfileId,
  onSelect,
  onRename,
  onDelete,
}: {
  config: ModelServiceConfig;
  profiles: CatalogProfile[];
  activeProfileId: string | null;
  onSelect: (profileId: string) => void;
  onRename: (value: string) => void;
  onDelete: () => void;
}) {
  const { t } = useI18n();
  const Icon = config.icon;
  const activeProfile = profiles.find((profile) => profile.id === activeProfileId) ?? profiles[0] ?? null;

  return (
    <aside className="h-fit rounded-xl border border-[var(--border)] bg-white p-3">
      <p className="mb-3 px-1 text-[11px] font-semibold uppercase tracking-[0.08em] text-zinc-600">{t("models.profiles")}</p>
      <div className="space-y-2">
        {profiles.map((profile) => {
          const active = profile.id === activeProfile?.id;
          return (
            <button
              key={profile.id}
              onClick={() => onSelect(profile.id)}
              className={`w-full rounded-lg px-2 py-2 text-left ${active ? "bg-zinc-50" : "hover:bg-zinc-50"}`}
            >
              <div className="flex items-center gap-2">
                <ProviderIcon provider={profile.binding ?? profile.provider} fallback={Icon} className="h-4 w-4" />
                {active ? (
                  <input
                    value={profile.name}
                    onClick={(event) => event.stopPropagation()}
                    onChange={(event) => onRename(event.target.value)}
                    className="min-w-0 flex-1 bg-transparent text-[12px] font-semibold outline-none"
                  />
                ) : (
                  <span className="min-w-0 flex-1 truncate text-[12px] font-semibold">{profile.name || t("models.defaultProfile", { name: config.shortTitle })}</span>
                )}
              </div>
              <p className="mt-1 truncate text-[12px] text-black">{profile.base_url || t("models.noEndpoint")}</p>
            </button>
          );
        })}
      </div>
      <div className="mt-3 border-t border-[var(--border)] pt-3">
        <button onClick={onDelete} disabled={profiles.length <= 1} className="flex w-full items-center gap-2 px-2 text-left text-[12px] text-black disabled:cursor-not-allowed disabled:opacity-40">
          <Trash2 size={13} />
          <span className="truncate">{t("models.deleteProfile", { name: activeProfile?.name || t("common.profile") })}</span>
        </button>
      </div>
    </aside>
  );
}

function Field({
  label,
  value,
  onChange,
  type = "text",
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  type?: "text" | "password";
}) {
  return (
    <label className="block">
      <span className="text-[12px] text-[var(--muted-foreground)]">{label}</span>
      <input
        type={type}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="mt-2 h-10 w-full rounded-md border border-[var(--border)] bg-white px-3 text-[14px]"
      />
    </label>
  );
}
