"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { applyCatalog as applyCatalogApi, getSettings, saveCatalog as saveCatalogApi, testService } from "@/lib/api";
import {
  activeModel,
  activeProfile,
  cloneCatalog,
  type Catalog,
  type CatalogModel,
  type CatalogProfile,
  type ProviderOption,
  type ServiceName,
  type TestResult,
} from "@/lib/settings-types";

type SettingsContextValue = {
  catalog: Catalog | null;
  draft: Catalog | null;
  providers: Record<ServiceName, ProviderOption[]>;
  loading: boolean;
  error: string;
  toast: string;
  dirty: boolean;
  saving: boolean;
  applying: boolean;
  testing: ServiceName | null;
  results: Partial<Record<ServiceName, TestResult>>;
  setToast: (value: string) => void;
  reload: () => Promise<void>;
  saveDraft: () => Promise<void>;
  applyDraft: () => Promise<void>;
  runTest: (service: ServiceName) => Promise<void>;
  setActiveProfile: (service: ServiceName, profileId: string) => void;
  setActiveModel: (service: ServiceName, modelId: string) => void;
  updateProfile: (service: ServiceName, field: keyof CatalogProfile, value: string) => void;
  updateModel: (service: ServiceName, field: keyof CatalogModel, value: string | boolean) => void;
  addProfile: (service: ServiceName) => void;
  addModel: (service: ServiceName) => void;
  deleteActiveProfile: (service: ServiceName) => void;
  deleteActiveModel: (service: ServiceName) => void;
};

const SettingsContext = createContext<SettingsContextValue | null>(null);

export function useSettings() {
  const value = useContext(SettingsContext);
  if (!value) throw new Error("useSettings must be used inside SettingsProvider");
  return value;
}

export function SettingsProvider({ children }: { children: ReactNode }) {
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [draft, setDraft] = useState<Catalog | null>(null);
  const [providers, setProviders] = useState<Record<ServiceName, ProviderOption[]>>({
    llm: [],
    embedding: [],
    search: [],
    tts: [],
    stt: [],
    imagegen: [],
    videogen: [],
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [toast, setToast] = useState("");
  const [saving, setSaving] = useState(false);
  const [applying, setApplying] = useState(false);
  const [testing, setTesting] = useState<ServiceName | null>(null);
  const [results, setResults] = useState<Partial<Record<ServiceName, TestResult>>>({});

  const reload = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const payload = await getSettings();
      setCatalog(payload.catalog);
      setDraft(cloneCatalog(payload.catalog));
      setProviders(payload.providers);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Settings failed to load.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const mutateDraft = useCallback((mutator: (next: Catalog) => void) => {
    setDraft((current) => {
      if (!current) return current;
      const next = cloneCatalog(current);
      mutator(next);
      return next;
    });
  }, []);

  const updateProfile = useCallback(
    (service: ServiceName, field: keyof CatalogProfile, value: string) => {
      mutateDraft((next) => {
        const profile = activeProfile(next, service);
        if (!profile) return;
        (profile[field] as string | null | undefined) = value;
        if (field === "binding" || field === "provider") {
          const option = providers[service]?.find((item) => item.value === value);
          if (option?.base_url) profile.base_url = option.base_url;
        }
      });
    },
    [mutateDraft, providers],
  );

  const setActiveProfile = useCallback(
    (service: ServiceName, profileId: string) => {
      mutateDraft((next) => {
        const target = next.services[service];
        const profile = target.profiles.find((item) => item.id === profileId);
        if (!profile) return;
        target.active_profile_id = profileId;
        target.active_model_id = service === "search" ? null : profile.models[0]?.id ?? null;
      });
    },
    [mutateDraft],
  );

  const setActiveModel = useCallback(
    (service: ServiceName, modelId: string) => {
      if (service === "search") return;
      mutateDraft((next) => {
        const profile = activeProfile(next, service);
        if (!profile?.models.some((item) => item.id === modelId)) return;
        next.services[service].active_model_id = modelId;
      });
    },
    [mutateDraft],
  );

  const updateModel = useCallback(
    (service: ServiceName, field: keyof CatalogModel, value: string | boolean) => {
      mutateDraft((next) => {
        const model = activeModel(next, service);
        if (!model) return;
        (model[field] as string | boolean | undefined) = value;
      });
    },
    [mutateDraft],
  );

  const addProfile = useCallback(
    (service: ServiceName) => {
      mutateDraft((next) => {
        const target = next.services[service];
        const option = providers[service]?.[0];
        const id = `${service}-profile-${Date.now()}`;
        const profile: CatalogProfile = {
          id,
          name: option?.label ?? (service === "search" ? "Search Provider" : "Model Provider"),
          binding: service === "search" ? null : option?.value ?? "openai",
          provider: service === "search" ? option?.value ?? "none" : null,
          base_url: option?.base_url ?? "",
          api_key: "",
          api_version: "",
          extra_headers: {},
          models: [],
        };
        if (service !== "search") {
          const modelId = `${service}-model-${Date.now()}`;
          profile.models.push({
            id: modelId,
            name: option?.default_model || "Model",
            model: option?.default_model || "",
            dimension: service === "embedding" ? option?.default_dim ?? "" : undefined,
            send_dimensions: service === "embedding" ? true : undefined,
          });
          target.active_model_id = modelId;
        }
        target.profiles.push(profile);
        target.active_profile_id = id;
      });
    },
    [mutateDraft, providers],
  );

  const addModel = useCallback(
    (service: ServiceName) => {
      if (service === "search") return;
      mutateDraft((next) => {
        const profile = activeProfile(next, service);
        if (!profile) return;
        const id = `${service}-model-${Date.now()}`;
        profile.models.push({ id, name: `Model ${profile.models.length + 1}`, model: "" });
        next.services[service].active_model_id = id;
      });
    },
    [mutateDraft],
  );

  const deleteActiveProfile = useCallback(
    (service: ServiceName) => {
      mutateDraft((next) => {
        const target = next.services[service];
        if (target.profiles.length <= 1) return;
        const activeId = target.active_profile_id;
        target.profiles = target.profiles.filter((profile) => profile.id !== activeId);
        const nextProfile = target.profiles[0] ?? null;
        target.active_profile_id = nextProfile?.id ?? null;
        target.active_model_id = service === "search" ? null : nextProfile?.models[0]?.id ?? null;
      });
    },
    [mutateDraft],
  );

  const deleteActiveModel = useCallback(
    (service: ServiceName) => {
      if (service === "search") return;
      mutateDraft((next) => {
        const profile = activeProfile(next, service);
        if (!profile || profile.models.length <= 1) return;
        profile.models = profile.models.filter((model) => model.id !== next.services[service].active_model_id);
        next.services[service].active_model_id = profile.models[0]?.id ?? null;
      });
    },
    [mutateDraft],
  );

  const saveDraft = useCallback(async () => {
    if (!draft) return;
    setSaving(true);
    try {
      const payload = await saveCatalogApi(draft);
      setCatalog(payload.catalog);
      setDraft(cloneCatalog(payload.catalog));
      setToast("Draft saved");
    } finally {
      setSaving(false);
    }
  }, [draft]);

  const applyDraft = useCallback(async () => {
    if (!draft) return;
    setApplying(true);
    try {
      const payload = await applyCatalogApi(draft);
      setCatalog(payload.catalog);
      setDraft(cloneCatalog(payload.catalog));
      setToast("All changes saved");
    } finally {
      setApplying(false);
    }
  }, [draft]);

  const runTest = useCallback(
    async (service: ServiceName) => {
      if (!draft) return;
      setTesting(service);
      try {
        const result = await testService(service, draft);
        setResults((current) => ({ ...current, [service]: result }));
        if (result.catalog) {
          setCatalog(result.catalog);
          setDraft(cloneCatalog(result.catalog));
        }
        setToast(result.message);
      } finally {
        setTesting(null);
      }
    },
    [draft],
  );

  const dirty = useMemo(() => {
    if (!catalog || !draft) return false;
    return JSON.stringify(catalog) !== JSON.stringify(draft);
  }, [catalog, draft]);

  const value = useMemo<SettingsContextValue>(
    () => ({
      catalog,
      draft,
      providers,
      loading,
      error,
      toast,
      dirty,
      saving,
      applying,
      testing,
      results,
      setToast,
      reload,
      saveDraft,
      applyDraft,
      runTest,
      setActiveProfile,
      setActiveModel,
      updateProfile,
      updateModel,
      addProfile,
      addModel,
      deleteActiveProfile,
      deleteActiveModel,
    }),
    [addModel, addProfile, applying, catalog, deleteActiveModel, deleteActiveProfile, dirty, draft, error, loading, providers, reload, results, runTest, saveDraft, saving, setActiveModel, setActiveProfile, testing, toast, updateModel, updateProfile, applyDraft],
  );

  return <SettingsContext.Provider value={value}>{children}</SettingsContext.Provider>;
}
