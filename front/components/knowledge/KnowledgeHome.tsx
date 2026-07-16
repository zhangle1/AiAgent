"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import {
  ArrowLeft,
  Boxes,
  Check,
  Cloud,
  Cpu,
  Database,
  Download,
  Eye,
  FileText,
  FileUp,
  FolderOpen,
  Layers,
  Network,
  Plus,
  RefreshCw,
  Server,
  Settings,
  SlidersHorizontal,
  Star,
  Trash2,
  Upload,
  Workflow,
  X,
  XCircle,
  ZoomIn,
  ZoomOut,
  type LucideIcon,
} from "lucide-react";
import { Document, Page, pdfjs } from "react-pdf";
import { getSettings } from "@/lib/api";
import {
  checkKnowledgeEnvironment,
  createKnowledgeBase,
  deleteKnowledgeBase,
  deleteKnowledgeDocument,
  getKnowledgeBase,
  getKnowledgeDiagnostics,
  getKnowledgeBases,
  getKnowledgeIndexVersions,
  getKnowledgeProgress,
  getKnowledgeProviderConfig,
  getKnowledgeProviders,
  knowledgeDocumentFileUrl,
  knowledgeProgressWebSocketUrl,
  reindexKnowledgeBase,
  saveKnowledgeProviderConfig,
  setDefaultKnowledgeBase,
  uploadKnowledgeDocuments,
} from "@/lib/knowledge-api";
import type { KnowledgeBase, KnowledgeDetail, KnowledgeDocument, KnowledgeEnvironmentCheck, KnowledgeIndexVersion, KnowledgeJob, KnowledgeProvider } from "@/lib/knowledge-types";
import { activeModel, activeProfile } from "@/lib/settings-types";
import { useI18n } from "@/i18n/I18nProvider";

type RetrievalProfile = "hybrid" | "vector";
type DetailTab = "files" | "add" | "versions" | "settings";

pdfjs.GlobalWorkerOptions.workerSrc = new URL("pdfjs-dist/build/pdf.worker.min.mjs", import.meta.url).toString();

const engineIcons: Record<string, LucideIcon> = {
  llamaindex: Boxes,
  pageindex: Cloud,
  graphrag: Network,
  lightrag: Workflow,
  "lightrag-server": Server,
  obsidian: FolderOpen,
};

const supportedDocumentAccept = [
  ".pdf",
  "application/pdf",
  ".txt",
  "text/plain",
  ".md",
  ".markdown",
  "text/markdown",
  ".csv",
  "text/csv",
  ".json",
  "application/json",
  ".doc",
  ".docx",
  ".xls",
  ".xlsx",
  ".ppt",
  ".pptx",
].join(",");

export function KnowledgeHome() {
  const { t } = useI18n();
  const [providers, setProviders] = useState<KnowledgeProvider[]>([]);
  const [knowledgeBases, setKnowledgeBases] = useState<KnowledgeBase[]>([]);
  const [selectedProviderId, setSelectedProviderId] = useState<string | null>(null);
  const [selectedKbName, setSelectedKbName] = useState<string | null>(null);
  const [detail, setDetail] = useState<KnowledgeDetail | null>(null);
  const [versions, setVersions] = useState<KnowledgeIndexVersion[]>([]);
  const [detailTab, setDetailTab] = useState<DetailTab>("files");
  const [embeddingLabel, setEmbeddingLabel] = useState("");
  const [createOpen, setCreateOpen] = useState(false);
  const [retrievalProfile, setRetrievalProfile] = useState<RetrievalProfile>("hybrid");
  const [topK, setTopK] = useState(5);
  const [vectorCandidate, setVectorCandidate] = useState(2);
  const [keywordCandidate, setKeywordCandidate] = useState(2);
  const [chunkSize, setChunkSize] = useState(512);
  const [chunkOverlap, setChunkOverlap] = useState(50);
  const [environmentResult, setEnvironmentResult] = useState<KnowledgeEnvironmentCheck | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [checking, setChecking] = useState(false);
  const [savingConfig, setSavingConfig] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const selectedProvider = useMemo(() => {
    const rows = providers.length > 0 ? providers : fallbackProviders();
    return rows.find((item) => item.id === selectedProviderId) ?? null;
  }, [providers, selectedProviderId]);

  async function reload() {
    setLoading(true);
    setError(null);
    try {
      const [providerRows, kbRows] = await Promise.all([getKnowledgeProviders(), getKnowledgeBases()]);
      setProviders(providerRows);
      setKnowledgeBases(kbRows);
      await reloadEmbeddingLabel();
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("knowledge.errorLoad"));
    } finally {
      setLoading(false);
    }
  }

  async function reloadEmbeddingLabel() {
    try {
      const settings = await getSettings();
      const profile = activeProfile(settings.catalog, "embedding");
      const model = activeModel(settings.catalog, "embedding");
      const dimension = model?.dimension ? ` · ${model.dimension}d` : "";
      setEmbeddingLabel(model ? `${model.name || model.model}${dimension}` : profile?.name ?? "");
    } catch {
      setEmbeddingLabel("");
    }
  }

  async function reloadDetail(name = selectedKbName) {
    if (!name) return;
    setDetailLoading(true);
    setError(null);
    try {
      const [row, versionRows] = await Promise.all([getKnowledgeBase(name), getKnowledgeIndexVersions(name)]);
      setDetail(row);
      setVersions(versionRows);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("knowledge.errorLoad"));
    } finally {
      setDetailLoading(false);
    }
  }

  useEffect(() => {
    void reload();
  }, []);

  useEffect(() => {
    if (!selectedProviderId) return;
    let disposed = false;
    async function loadProviderConfig() {
      setError(null);
      try {
        const config = await getKnowledgeProviderConfig(selectedProviderId!);
        if (disposed) return;
        setRetrievalProfile(config.retrieval_profile === "vector" ? "vector" : "hybrid");
        setTopK(config.top_k);
        setVectorCandidate(config.vector_candidate_multiplier);
        setKeywordCandidate(config.keyword_candidate_multiplier);
        setChunkSize(config.chunk_size);
        setChunkOverlap(config.chunk_overlap);
      } catch (ex) {
        if (!disposed) setError(ex instanceof Error ? ex.message : t("knowledge.errorLoad"));
      }
    }
    void loadProviderConfig();
    return () => {
      disposed = true;
    };
  }, [selectedProviderId]);

  async function handleCreate(name: string, files: File[], provider: string) {
    setBusy(true);
    setError(null);
    try {
      await createKnowledgeBase(name, files, provider);
      setCreateOpen(false);
      await reload();
      setSelectedKbName(name.trim().toLowerCase());
      setDetailTab("files");
      await reloadDetail(name.trim().toLowerCase());
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("knowledge.errorCreate"));
    } finally {
      setBusy(false);
    }
  }

  async function handleUpload(files: File[]) {
    if (!detail || files.length === 0) return;
    const kbName = detail.name || selectedKbName;
    if (!kbName) {
      setError(t("knowledge.errorUpload"));
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await uploadKnowledgeDocuments(kbName, files);
      await Promise.all([reload(), reloadDetail(kbName)]);
      setNotice(t("knowledge.uploadStarted"));
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("knowledge.errorUpload"));
    } finally {
      setBusy(false);
    }
  }

  async function handleDeleteDocument(kbName: string, documentId: number) {
    setBusy(true);
    setError(null);
    try {
      await deleteKnowledgeDocument(kbName, documentId);
      await Promise.all([reload(), reloadDetail(kbName)]);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("knowledge.errorDelete"));
    } finally {
      setBusy(false);
    }
  }

  async function handleReindex(kb: KnowledgeBase | KnowledgeDetail) {
    setBusy(true);
    setError(null);
    try {
      await reindexKnowledgeBase(kb.name);
      await Promise.all([reload(), reloadDetail(kb.name)]);
      setNotice(t("knowledge.reindexStarted"));
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("knowledge.errorReindex"));
    } finally {
      setBusy(false);
    }
  }

  async function handleDelete(kb: KnowledgeBase | KnowledgeDetail) {
    setBusy(true);
    setError(null);
    try {
      await deleteKnowledgeBase(kb.name);
      setSelectedKbName(null);
      setDetail(null);
      await reload();
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("knowledge.errorDelete"));
    } finally {
      setBusy(false);
    }
  }

  async function handleSetDefault(kb: KnowledgeDetail) {
    setBusy(true);
    setError(null);
    try {
      await setDefaultKnowledgeBase(kb.name);
      await Promise.all([reload(), reloadDetail(kb.name)]);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("knowledge.errorDefault"));
    } finally {
      setBusy(false);
    }
  }

  async function handleEnvironmentCheck(providerId: string) {
    setChecking(true);
    setNotice(null);
    setError(null);
    try {
      const result = await checkKnowledgeEnvironment(providerId);
      setEnvironmentResult(result);
      setNotice(isEnvironmentOk(result) ? t("knowledge.environmentReady") : t("knowledge.environmentFailed"));
      await reload();
    } catch (ex) {
      setEnvironmentResult({ ok: false, error_message: ex instanceof Error ? ex.message : t("knowledge.errorEnvironment") });
      setNotice(t("knowledge.environmentFailed"));
    } finally {
      setChecking(false);
    }
  }

  async function saveLocalConfig() {
    if (!selectedProviderId || savingConfig) return;
    setSavingConfig(true);
    setNotice(null);
    setError(null);
    try {
      const config = await saveKnowledgeProviderConfig(selectedProviderId, {
        provider: selectedProviderId,
        retrieval_profile: retrievalProfile,
        top_k: topK,
        vector_candidate_multiplier: vectorCandidate,
        keyword_candidate_multiplier: keywordCandidate,
        chunk_size: chunkSize,
        chunk_overlap: chunkOverlap,
      });
      setRetrievalProfile(config.retrieval_profile === "vector" ? "vector" : "hybrid");
      setTopK(config.top_k);
      setVectorCandidate(config.vector_candidate_multiplier);
      setKeywordCandidate(config.keyword_candidate_multiplier);
      setChunkSize(config.chunk_size);
      setChunkOverlap(config.chunk_overlap);
      setNotice(t("knowledge.saved"));
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("knowledge.errorSaveConfig"));
    } finally {
      setSavingConfig(false);
    }
  }

  if (selectedKbName) {
    return (
      <main className="min-h-screen bg-white">
        <KnowledgeDetailView
          busy={busy}
          detail={detail}
          detailLoading={detailLoading}
          embeddingLabel={embeddingLabel}
          error={error}
          notice={notice}
          tab={detailTab}
          versions={versions}
          onBack={() => {
            setSelectedKbName(null);
            setDetail(null);
            setVersions([]);
            setNotice(null);
          }}
          onDelete={handleDelete}
          onDeleteDocument={handleDeleteDocument}
          onRefresh={() => void reloadDetail(selectedKbName)}
          onReindex={handleReindex}
          onSetDefault={handleSetDefault}
          onTabChange={setDetailTab}
          onUpload={handleUpload}
        />
      </main>
    );
  }

  if (selectedProvider) {
    return (
      <main className="mx-auto max-w-5xl px-6 py-8">
        <button
          type="button"
          onClick={() => {
            setSelectedProviderId(null);
            setEnvironmentResult(null);
            setNotice(null);
          }}
          className="mb-4 inline-flex items-center gap-1.5 text-[12.5px] font-medium text-[var(--muted-foreground)] transition hover:text-[var(--foreground)]"
        >
          <ArrowLeft size={15} />
          {t("knowledge.back")}
        </button>

        <EngineHeader engine={selectedProvider} />
        <PageMessage error={error} notice={notice} />

        <section className="mt-8">
          <SectionTitle icon={Database} title={t("knowledge.requirements")} />
          <div className="rounded-lg border border-[var(--border)] bg-white p-4">
            <p className="max-w-3xl text-[12.5px] leading-6 text-[var(--muted-foreground)]">{t("knowledge.requirementsDesc")}</p>
            <div className="mt-4 flex flex-wrap items-center gap-3">
              <button
                type="button"
                disabled={checking}
                onClick={() => void handleEnvironmentCheck(selectedProvider.id)}
                className="inline-flex h-9 items-center gap-2 rounded-md border border-[var(--border)] bg-white px-3 text-[12.5px] font-semibold transition hover:border-blue-300 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <RefreshCw size={15} className={checking ? "animate-spin" : ""} />
                {checking ? t("common.checking") : t("knowledge.checkEnvironment")}
              </button>
              {environmentResult && <EnvironmentResult result={environmentResult} />}
            </div>
          </div>
        </section>

        <section className="mt-8">
          <SectionTitle icon={SlidersHorizontal} title={t("knowledge.retrievalChunking")} />
          <div className="rounded-lg border border-[var(--border)] bg-white p-4">
            <label className="text-[12px] font-semibold">{t("knowledge.retrievalProfile")}</label>
            <div className="mt-2 grid gap-2 md:grid-cols-2">
              <RetrievalOption active={retrievalProfile === "hybrid"} title={t("knowledge.hybrid")} description={t("knowledge.hybridDesc")} onClick={() => setRetrievalProfile("hybrid")} />
              <RetrievalOption active={retrievalProfile === "vector"} title={t("knowledge.vector")} description={t("knowledge.vectorDesc")} onClick={() => setRetrievalProfile("vector")} />
            </div>
            <div className="mt-6 grid gap-3 md:grid-cols-3">
              <NumberField label={t("knowledge.resultsPerQuery")} value={topK} onChange={setTopK} />
              <NumberField label={t("knowledge.vectorCandidate")} value={vectorCandidate} onChange={setVectorCandidate} />
              <NumberField label={t("knowledge.keywordCandidate")} value={keywordCandidate} onChange={setKeywordCandidate} />
            </div>
            <div className="mt-6 flex items-center justify-between gap-3">
              <label className="text-[12px] font-semibold">{t("knowledge.chunking")}</label>
              <span className="text-[11px] text-[var(--muted-foreground)]">{t("knowledge.appliesNextReindex")}</span>
            </div>
            <div className="mt-2 grid gap-3 md:grid-cols-2">
              <NumberField label={t("knowledge.chunkSize")} value={chunkSize} onChange={setChunkSize} />
              <NumberField label={t("knowledge.chunkOverlap")} value={chunkOverlap} onChange={setChunkOverlap} />
            </div>
            <div className="mt-5 flex justify-end">
              <button type="button" disabled={savingConfig} onClick={() => void saveLocalConfig()} className="inline-flex h-9 items-center rounded-md bg-blue-600 px-4 text-[12.5px] font-semibold text-white transition hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-zinc-300">
                {savingConfig ? t("common.saving") : t("knowledge.saveChanges")}
              </button>
            </div>
          </div>
        </section>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-5xl px-6 py-9">
      <div className="mb-8 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-[26px] font-semibold leading-tight">{t("knowledge.title")}</h1>
          <p className="mt-2 text-[13px] text-[var(--muted-foreground)]">{t("knowledge.description")}</p>
        </div>
        <div className="flex items-center gap-2">
          <button type="button" onClick={() => void reload()} className="inline-flex h-9 items-center gap-2 rounded-md border border-[var(--border)] bg-white px-3 text-[12.5px] font-semibold transition hover:border-blue-300">
            <RefreshCw size={15} />
            {t("knowledge.refresh")}
          </button>
          <button type="button" onClick={() => setCreateOpen(true)} className="inline-flex h-9 items-center gap-2 rounded-md bg-blue-600 px-4 text-[12.5px] font-semibold text-white transition hover:bg-blue-700">
            <Plus size={15} />
            {t("knowledge.new")}
          </button>
        </div>
      </div>

      <PageMessage error={error} notice={notice} />
      <EngineGrid providers={providers.length > 0 ? providers : fallbackProviders()} onSelect={setSelectedProviderId} />
      <KnowledgeBaseList
        busy={busy}
        loading={loading}
        knowledgeBases={knowledgeBases}
        onDelete={handleDelete}
        onOpen={(kb) => {
          setSelectedKbName(kb.name);
          setDetailTab("files");
          void reloadDetail(kb.name);
        }}
        onReindex={handleReindex}
      />
      {createOpen && <CreateKnowledgeModal busy={busy} providers={providers.length > 0 ? providers : fallbackProviders()} onClose={() => setCreateOpen(false)} onCreate={handleCreate} />}
    </main>
  );
}

function EngineGrid({ providers, onSelect }: { providers: KnowledgeProvider[]; onSelect: (id: string) => void }) {
  const { t } = useI18n();
  return (
    <section>
      <SectionTitle icon={Cpu} title={t("knowledge.retrievalEngines")} />
      <div className="grid gap-3 md:grid-cols-2">
        {providers.map((engine) => {
          const Icon = engineIcons[engine.id] ?? Boxes;
          const tone = engine.configured ? "emerald" : engine.status === "needs_setup" ? "amber" : "zinc";
          return (
            <button key={engine.id} type="button" onClick={() => onSelect(engine.id)} className="group flex min-h-[116px] flex-col justify-between rounded-lg border border-[var(--border)] bg-white p-4 text-left transition hover:border-blue-300">
              <div className="flex items-start justify-between gap-3">
                <div className="flex min-w-0 items-center gap-2">
                  <Icon size={17} strokeWidth={1.7} className="shrink-0 text-[var(--muted-foreground)]" />
                  <span className="truncate text-[14px] font-semibold">{engine.name}</span>
                </div>
                <EngineBadge tone={tone} label={statusLabel(engine.status, t)} />
              </div>
              <p className="mt-3 line-clamp-2 text-[12px] leading-relaxed text-[var(--muted-foreground)]">{providerDescription(engine.id, engine.description, t)}</p>
              <div className="mt-3 flex items-center gap-2 text-[11px] text-[var(--muted-foreground)]">
                <span className="rounded-full border border-[var(--border)] px-2 py-0.5">{modeLabel(engine.default_mode, t)}</span>
              </div>
            </button>
          );
        })}
      </div>
    </section>
  );
}

function KnowledgeBaseList({ busy, loading, knowledgeBases, onDelete, onOpen, onReindex }: { busy: boolean; loading: boolean; knowledgeBases: KnowledgeBase[]; onDelete: (kb: KnowledgeBase) => Promise<void>; onOpen: (kb: KnowledgeBase) => void; onReindex: (kb: KnowledgeBase) => Promise<void> }) {
  const { t } = useI18n();
  return (
    <section className="mt-9">
      <SectionTitle icon={Database} title={`${t("knowledge.knowledgeBases")} · ${knowledgeBases.length}`} />
      {loading ? (
        <div className="rounded-lg border border-[var(--border)] bg-white p-4 text-[12px] text-[var(--muted-foreground)]">{t("knowledge.loading")}</div>
      ) : knowledgeBases.length === 0 ? (
        <div className="rounded-lg border border-[var(--border)] bg-white p-4">
          <h3 className="text-[14px] font-semibold">{t("knowledge.noKnowledgeBases")}</h3>
          <p className="mt-2 text-[12px] leading-relaxed text-[var(--muted-foreground)]">{t("knowledge.noKnowledgeBasesDesc")}</p>
        </div>
      ) : (
        <div className="grid gap-3 md:grid-cols-2">
          {knowledgeBases.map((kb) => (
            <div key={kb.id} className="rounded-lg border border-[var(--border)] bg-white p-4">
              <button type="button" onClick={() => onOpen(kb)} className="block w-full text-left">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className={`h-2 w-2 rounded-full ${kb.status === "ready" ? "bg-emerald-500" : kb.status === "error" ? "bg-red-500" : "bg-amber-500"}`} />
                      <h3 className="truncate text-[14px] font-semibold">{kb.display_name || kb.name}</h3>
                      {kb.is_default && <Star size={14} className="fill-amber-400 text-amber-400" />}
                    </div>
                    <p className="mt-2 text-[12px] leading-relaxed text-[var(--muted-foreground)]">
                      <span className="rounded-full border border-[var(--border)] px-2 py-0.5">{kb.engine_type}</span>
                      <span className="ml-2">{t("knowledge.docsShort", { count: kb.document_count })}</span>
                    </p>
                  </div>
                  <EngineBadge tone={kb.status === "ready" ? "emerald" : kb.status === "error" ? "amber" : "zinc"} label={kb.status === "ready" ? t("knowledge.ready") : kb.status} />
                </div>
              </button>
              <div className="mt-4 flex items-center gap-2">
                <button type="button" disabled={busy} onClick={() => void onReindex(kb)} className="inline-flex h-8 items-center gap-2 rounded-md border border-[var(--border)] px-2.5 text-[12px] transition hover:border-blue-300 disabled:opacity-50">
                  <RefreshCw size={14} />
                  {t("knowledge.reindex")}
                </button>
                <button type="button" disabled={busy} onClick={() => void onDelete(kb)} className="inline-flex h-8 items-center gap-2 rounded-md border border-red-200 px-2.5 text-[12px] text-red-600 transition hover:bg-red-50 disabled:opacity-50">
                  <Trash2 size={14} />
                  {t("knowledge.delete")}
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}

function CreateKnowledgeModal({ busy, providers, onClose, onCreate }: { busy: boolean; providers: KnowledgeProvider[]; onClose: () => void; onCreate: (name: string, files: File[], provider: string) => Promise<void> }) {
  const { t } = useI18n();
  const [name, setName] = useState("");
  const [provider, setProvider] = useState(providers[0]?.id ?? "llamaindex");
  const [files, setFiles] = useState<File[]>([]);
  const canCreate = name.trim().length > 0 && !busy;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/35 px-4">
      <div className="flex max-h-[86vh] w-full max-w-3xl flex-col overflow-hidden rounded-lg bg-white shadow-2xl">
        <div className="flex items-center justify-between border-b border-[var(--border)] px-5 py-4">
          <div className="flex items-center gap-2 text-[17px] font-semibold">
            <Plus size={18} />
            {t("knowledge.createTitle")}
          </div>
          <button type="button" onClick={onClose} className="flex h-8 w-8 items-center justify-center rounded-md text-[var(--muted-foreground)] hover:bg-zinc-100">
            <X size={18} />
          </button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">
          <label className="block">
            <span className="text-[11px] font-semibold uppercase tracking-[0.08em] text-[var(--muted-foreground)]">{t("knowledge.nameLabel")}</span>
            <input value={name} onChange={(event) => setName(event.target.value)} placeholder={t("knowledge.namePlaceholder")} className="mt-2 h-10 w-full rounded-md border border-[var(--border)] px-3 text-[13px] outline-none focus:border-blue-400" />
          </label>

          <div className="mt-5">
            <div className="text-[11px] font-semibold uppercase tracking-[0.08em] text-[var(--muted-foreground)]">{t("knowledge.indexEngine")}</div>
            <div className="mt-2 grid gap-2 md:grid-cols-2">
              {providers.filter((item) => item.id !== "obsidian").map((item) => {
                const active = provider === item.id;
                const unavailable = item.id !== "llamaindex" && !item.configured;
                return (
                  <button key={item.id} type="button" disabled={unavailable} onClick={() => setProvider(item.id)} className={`min-h-[92px] rounded-lg border p-3 text-left transition disabled:cursor-not-allowed disabled:opacity-60 ${active ? "border-blue-500 bg-blue-50/30" : "border-[var(--border)] hover:border-blue-300"}`}>
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-[13px] font-semibold">{item.name}</span>
                      {unavailable ? <EngineBadge tone="zinc" label={statusLabel(item.status, t)} /> : active && <Check size={15} className="text-blue-600" />}
                    </div>
                    <p className="mt-2 text-[12px] leading-5 text-[var(--muted-foreground)]">{providerDescription(item.id, item.description, t)}</p>
                  </button>
                );
              })}
            </div>
          </div>

          <div className="mt-5">
            <div className="text-[11px] font-semibold uppercase tracking-[0.08em] text-[var(--muted-foreground)]">{t("knowledge.initialDocuments")}</div>
            <FilePicker files={files} onFiles={setFiles} />
          </div>
        </div>
        <div className="flex justify-end gap-2 border-t border-[var(--border)] px-5 py-4">
          <button type="button" onClick={onClose} className="h-9 rounded-md px-3 text-[13px] font-medium text-[var(--muted-foreground)] hover:bg-zinc-100">{t("knowledge.cancel")}</button>
          <button type="button" disabled={!canCreate} onClick={() => void onCreate(name.trim(), files, provider)} className="inline-flex h-9 items-center gap-2 rounded-md bg-blue-600 px-4 text-[13px] font-semibold text-white hover:bg-blue-700 disabled:bg-zinc-300">
            <Plus size={15} />
            {busy ? t("common.saving") : t("knowledge.create")}
          </button>
        </div>
      </div>
    </div>
  );
}

function KnowledgeDetailView({ busy, detail, detailLoading, embeddingLabel, error, notice, tab, versions, onBack, onDelete, onDeleteDocument, onRefresh, onReindex, onSetDefault, onTabChange, onUpload }: { busy: boolean; detail: KnowledgeDetail | null; detailLoading: boolean; embeddingLabel: string; error: string | null; notice: string | null; tab: DetailTab; versions: KnowledgeIndexVersion[]; onBack: () => void; onDelete: (kb: KnowledgeDetail) => Promise<void>; onDeleteDocument: (kbName: string, documentId: number) => Promise<void>; onRefresh: () => void; onReindex: (kb: KnowledgeDetail) => Promise<void>; onSetDefault: (kb: KnowledgeDetail) => Promise<void>; onTabChange: (tab: DetailTab) => void; onUpload: (files: File[]) => Promise<void> }) {
  const { t } = useI18n();
  const [selectedDocId, setSelectedDocId] = useState<number | null>(null);
  const selectedDocument = detail?.documents.find((item) => item.id === selectedDocId) ?? detail?.documents[0] ?? null;

  useEffect(() => {
    if (detail?.documents.length && !detail.documents.some((item) => item.id === selectedDocId)) {
      setSelectedDocId(detail.documents[0].id);
    }
  }, [detail, selectedDocId]);

  if (!detail) {
    return (
      <div className="p-6">
        <button type="button" onClick={onBack} className="inline-flex items-center gap-1.5 text-[12.5px] text-[var(--muted-foreground)] hover:text-[var(--foreground)]">
          <ArrowLeft size={15} />
          {t("knowledge.back")}
        </button>
        <div className="mt-8 rounded-lg border border-[var(--border)] bg-white p-6 text-[13px] text-[var(--muted-foreground)]">{detailLoading ? t("knowledge.loading") : t("knowledge.noKnowledgeBases")}</div>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col">
      <div className="border-b border-[var(--border)] px-6 py-4">
        <button type="button" onClick={onBack} className="mb-2 inline-flex items-center gap-1 text-[12px] text-[var(--muted-foreground)] hover:text-[var(--foreground)]">
          <ArrowLeft size={14} />
          {t("knowledge.knowledgeBases")}
        </button>
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="text-[20px] font-semibold">{detail.display_name || detail.name}</h1>
          {detail.is_default && <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-[11px] text-amber-700"><Star size={12} className="fill-amber-500" />{t("knowledge.default")}</span>}
          <EngineBadge tone={detail.status === "ready" ? "emerald" : detail.status === "error" ? "amber" : "zinc"} label={detail.status === "ready" ? t("knowledge.ready") : detail.status} />
        </div>
        <p className="mt-1 text-[12.5px] text-[var(--muted-foreground)]">
          {detail.engine_type} · {embeddingLabel || t("knowledge.noEmbeddingModel")} · {t("knowledge.updated")} {formatDate(detail.updated_at ?? detail.created_at)}
        </p>
        <nav className="mt-5 flex gap-1 overflow-x-auto">
          <DetailTabButton active={tab === "files"} icon={FileText} label={t("knowledge.files")} onClick={() => onTabChange("files")} />
          <DetailTabButton active={tab === "add"} icon={Upload} label={t("knowledge.addDocuments")} onClick={() => onTabChange("add")} />
          <DetailTabButton active={tab === "versions"} icon={Layers} label={t("knowledge.indexVersions")} onClick={() => onTabChange("versions")} />
          <DetailTabButton active={tab === "settings"} icon={Settings} label={t("knowledge.settings")} onClick={() => onTabChange("settings")} />
        </nav>
      </div>
      <PageMessage error={error} notice={notice} />
      {tab === "files" && <FilesTab busy={busy} detail={detail} selectedDocument={selectedDocument} onDeleteDocument={onDeleteDocument} onSelect={setSelectedDocId} />}
      {tab === "add" && <AddDocumentsTab busy={busy} documents={detail.documents} onUpload={onUpload} />}
      {tab === "versions" && <IndexVersionsTab busy={busy} detail={detail} versions={versions} onRefresh={onRefresh} onReindex={onReindex} />}
      {tab === "settings" && <SettingsTab busy={busy} detail={detail} embeddingLabel={embeddingLabel} onDelete={onDelete} onSetDefault={onSetDefault} />}
    </div>
  );
}

function FilesTab({ busy, detail, selectedDocument, onDeleteDocument, onSelect }: { busy: boolean; detail: KnowledgeDetail; selectedDocument: KnowledgeDocument | null; onDeleteDocument: (kbName: string, documentId: number) => Promise<void>; onSelect: (id: number) => void }) {
  const { t } = useI18n();
  return (
    <div className="grid min-h-[calc(100vh-178px)] grid-cols-1 md:grid-cols-[260px_1fr]">
      <aside className="border-r border-[var(--border)] p-3">
        <div className="mb-3 flex items-center justify-between">
          <span className="text-[12.5px] font-semibold">{t("knowledge.files")} · {detail.documents.length}</span>
        </div>
        {detail.documents.length === 0 ? (
          <p className="text-[12px] text-[var(--muted-foreground)]">{t("knowledge.noDocuments")}</p>
        ) : (
          <div className="space-y-1">
            {detail.documents.map((doc) => (
              <div key={doc.id} className={`group flex items-start gap-1 rounded-md px-2 py-2 text-[12px] transition ${selectedDocument?.id === doc.id ? "bg-blue-50 text-blue-700" : "hover:bg-zinc-50"}`}>
                <button type="button" onClick={() => onSelect(doc.id)} className="min-w-0 flex-1 text-left">
                  <div className="flex items-center gap-2 font-medium">
                    <FileText size={14} className="shrink-0" />
                    <span className="truncate">{doc.original_file_name || doc.file_name}</span>
                  </div>
                  <div className="mt-1 text-[11px] text-[var(--muted-foreground)]">{formatBytes(doc.file_size)}</div>
                </button>
                <button type="button" disabled={busy} onClick={() => void onDeleteDocument(detail.name, doc.id)} className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-[var(--muted-foreground)] opacity-0 transition hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:opacity-40 group-hover:opacity-100" title={t("common.delete")}>
                  <Trash2 size={14} />
                </button>
              </div>
            ))}
          </div>
        )}
      </aside>
      <section className="min-h-0 p-4">
        <FilePreview kbName={detail.name} document={selectedDocument} />
      </section>
    </div>
  );
}

function FilePreview({ kbName, document }: { kbName: string; document: KnowledgeDocument | null }) {
  const { t } = useI18n();
  const [textPreview, setTextPreview] = useState("");
  const [textLoading, setTextLoading] = useState(false);
  const [textError, setTextError] = useState<string | null>(null);

  useEffect(() => {
    if (!document || !isTextDocument(document)) {
      setTextPreview("");
      setTextError(null);
      setTextLoading(false);
      return;
    }

    const controller = new AbortController();
    setTextPreview("");
    setTextError(null);
    setTextLoading(true);
    fetch(knowledgeDocumentFileUrl(kbName, document.id), { signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.text();
      })
      .then((text) => setTextPreview(text))
      .catch((error) => {
        if ((error as Error).name !== "AbortError") {
          setTextError(error instanceof Error ? error.message : String(error));
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setTextLoading(false);
      });

    return () => controller.abort();
  }, [document, kbName]);

  if (!document) {
    return <div className="flex h-full items-center justify-center rounded-lg border border-dashed border-[var(--border)] text-[13px] text-[var(--muted-foreground)]">{t("knowledge.selectFile")}</div>;
  }

  const url = knowledgeDocumentFileUrl(kbName, document.id);
  const downloadUrl = knowledgeDocumentFileUrl(kbName, document.id, true);
  const isPdf = isPdfDocument(document);
  const isText = isTextDocument(document);
  return (
    <div className="flex h-full min-h-[640px] flex-col">
      <div className="mb-3 flex items-center justify-between gap-3">
        <div className="min-w-0">
          <div className="truncate text-[13px] font-semibold">{document.original_file_name || document.file_name}</div>
          <div className="text-[11px] text-[var(--muted-foreground)]">{document.content_type || document.extension || t("common.notSet")} · {formatBytes(document.file_size)}</div>
        </div>
        <a href={downloadUrl} download className="inline-flex h-8 items-center gap-2 rounded-md border border-[var(--border)] px-2.5 text-[12px] hover:border-blue-300">
          <Download size={14} />
          {t("knowledge.download")}
        </a>
      </div>
      {isPdf ? (
        <PdfDocumentPreview title={document.original_file_name || document.file_name} url={url} />
      ) : isText ? (
        <div className="min-h-0 flex-1 overflow-auto rounded-lg border border-[var(--border)] bg-white">
          {textLoading ? (
            <div className="flex h-full min-h-[260px] items-center justify-center text-[13px] text-[var(--muted-foreground)]">{t("common.checking")}</div>
          ) : textError ? (
            <div className="flex h-full min-h-[260px] items-center justify-center px-4 text-center text-[13px] text-red-600">{textError}</div>
          ) : (
            <pre className="min-h-full whitespace-pre-wrap break-words p-4 font-mono text-[12px] leading-6 text-[var(--foreground)]">{textPreview}</pre>
          )}
        </div>
      ) : (
        <div className="flex flex-1 flex-col items-center justify-center rounded-lg border border-dashed border-[var(--border)] text-center text-[13px] text-[var(--muted-foreground)]">
          <Eye size={22} className="mb-2" />
          {t("knowledge.previewPdfOnly")}
        </div>
      )}
    </div>
  );
}

function PdfDocumentPreview({ title, url }: { title: string; url: string }) {
  const { t } = useI18n();
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [numPages, setNumPages] = useState(0);
  const [pageWidth, setPageWidth] = useState(820);
  const [scale, setScale] = useState(1);
  const [previewError, setPreviewError] = useState<string | null>(null);

  useEffect(() => {
    setNumPages(0);
    setPreviewError(null);
  }, [url]);

  useEffect(() => {
    const element = containerRef.current;
    if (!element) return;

    const updateWidth = () => {
      setPageWidth(Math.max(320, Math.min(980, element.clientWidth - 48)));
    };
    updateWidth();

    const observer = new ResizeObserver(updateWidth);
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  return (
    <div className="min-h-0 flex-1 overflow-hidden rounded-lg border border-[var(--border)] bg-zinc-100">
      <div className="flex h-10 items-center justify-between gap-3 border-b border-[var(--border)] bg-white px-3">
        <div className="min-w-0 truncate text-[12px] font-medium text-[var(--muted-foreground)]">{title}</div>
        <div className="flex items-center gap-2">
          <button type="button" onClick={() => setScale((value) => Math.max(0.6, Number((value - 0.1).toFixed(1))))} className="inline-flex h-7 w-7 items-center justify-center rounded-md border border-[var(--border)] hover:border-blue-300" title="Zoom out">
            <ZoomOut size={14} />
          </button>
          <span className="w-12 text-center text-[11px] font-semibold">{Math.round(scale * 100)}%</span>
          <button type="button" onClick={() => setScale((value) => Math.min(2, Number((value + 0.1).toFixed(1))))} className="inline-flex h-7 w-7 items-center justify-center rounded-md border border-[var(--border)] hover:border-blue-300" title="Zoom in">
            <ZoomIn size={14} />
          </button>
        </div>
      </div>
      <div ref={containerRef} className="h-[calc(100%-40px)] overflow-auto px-4 py-5">
        <Document
          file={url}
          loading={<div className="flex min-h-[260px] items-center justify-center text-[13px] text-[var(--muted-foreground)]">{t("common.checking")}</div>}
          error={<div className="flex min-h-[260px] items-center justify-center px-4 text-center text-[13px] text-red-600">PDF 预览失败：{previewError ?? "无法读取文件"}</div>}
          onLoadSuccess={({ numPages: loadedPages }: { numPages: number }) => { setNumPages(loadedPages); setPreviewError(null); }}
          onLoadError={(reason) => setPreviewError(reason instanceof Error ? reason.message : String(reason))}
        >
          <div className="mx-auto flex w-fit flex-col gap-5">
            {Array.from({ length: numPages }, (_, index) => (
              <PdfPagePreview key={index + 1} pageNumber={index + 1} width={pageWidth * scale} />
            ))}
          </div>
        </Document>
      </div>
    </div>
  );
}

function PdfPagePreview({ pageNumber, width }: { pageNumber: number; width: number }) {
  const pageRef = useRef<HTMLDivElement | null>(null);
  const [shouldRender, setShouldRender] = useState(pageNumber <= 2);
  const placeholderHeight = Math.round(width * 1.414);

  useEffect(() => {
    const element = pageRef.current;
    if (!element || shouldRender) return;

    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) {
          setShouldRender(true);
          observer.disconnect();
        }
      },
      { root: null, rootMargin: "900px 0px", threshold: 0.01 },
    );
    observer.observe(element);
    return () => observer.disconnect();
  }, [shouldRender]);

  return (
    <div ref={pageRef} className="relative overflow-hidden rounded-md bg-white shadow-sm" data-pdf-page={pageNumber} style={!shouldRender ? { width, minHeight: placeholderHeight } : undefined}>
      {shouldRender ? (
        <>
          <Page
            pageNumber={pageNumber}
            renderAnnotationLayer
            renderTextLayer
            width={width}
          />
          <div className="pointer-events-none absolute inset-0" data-highlight-layer />
        </>
      ) : (
        <div className="flex h-full min-h-[360px] items-center justify-center text-[12px] text-[var(--muted-foreground)]">#{pageNumber}</div>
      )}
    </div>
  );
}

function AddDocumentsTab({ busy, documents, onUpload }: { busy: boolean; documents: KnowledgeDocument[]; onUpload: (files: File[]) => Promise<void> }) {
  const { t } = useI18n();
  const [files, setFiles] = useState<File[]>([]);
  return (
    <div className="mx-auto max-w-5xl px-6 py-6">
      <h2 className="text-[15px] font-semibold">{t("knowledge.addDocuments")}</h2>
      <p className="mt-1 text-[12px] text-[var(--muted-foreground)]">{t("knowledge.addDocumentsDesc")}</p>
      <FilePicker files={files} onFiles={setFiles} />
      <div className="mt-4 flex justify-end">
        <button type="button" disabled={busy || files.length === 0} onClick={() => void onUpload(files)} className="inline-flex h-9 items-center gap-2 rounded-md bg-blue-600 px-4 text-[13px] font-semibold text-white hover:bg-blue-700 disabled:bg-zinc-300">
          <Upload size={15} />
          {busy ? t("common.saving") : t("knowledge.upload")}
        </button>
      </div>
      <div className="mt-8">
        <SectionTitle icon={RefreshCw} title={`${t("knowledge.updateHistory")} · ${documents.length}`} />
        <div className="divide-y divide-[var(--border)] rounded-lg border border-[var(--border)] bg-white">
          {documents.map((doc) => (
            <div key={doc.id} className="flex items-center justify-between px-3 py-3 text-[12px]">
              <span className="truncate font-medium">{doc.original_file_name || doc.file_name}</span>
              <span className="shrink-0 text-[var(--muted-foreground)]">{formatDate(doc.created_at)}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function IndexVersionsTab({ busy, detail, versions, onRefresh, onReindex }: { busy: boolean; detail: KnowledgeDetail; versions: KnowledgeIndexVersion[]; onRefresh: () => void; onReindex: (kb: KnowledgeDetail) => Promise<void> }) {
  const { t } = useI18n();
  const [job, setJob] = useState<KnowledgeJob | null>(detail.latest_job ?? null);
  const [wsState, setWsState] = useState("idle");
  const [diagnostics, setDiagnostics] = useState<unknown | null>(null);
  const refreshRef = useRef(onRefresh);
  const refreshedTerminalJobRef = useRef<number | null>(null);

  useEffect(() => {
    refreshRef.current = onRefresh;
  }, [onRefresh]);

  function applyJobUpdate(row: KnowledgeJob | null) {
    setJob(row);
    if (!row) return;
    if (isActiveJob(row)) {
      refreshedTerminalJobRef.current = null;
      return;
    }
    if (isTerminalJob(row) && refreshedTerminalJobRef.current !== row.id) {
      refreshedTerminalJobRef.current = row.id;
      refreshRef.current();
    }
  }

  useEffect(() => {
    setJob(detail.latest_job ?? null);
  }, [detail.latest_job]);

  useEffect(() => {
    let disposed = false;
    void getKnowledgeProgress(detail.name).then((row) => {
      if (!disposed) setJob(row);
    }).catch(() => undefined);
    return () => {
      disposed = true;
    };
  }, [detail.name]);

  useEffect(() => {
    let disposed = false;
    let socket: WebSocket | null = null;

    try {
      socket = new WebSocket(knowledgeProgressWebSocketUrl(detail.name));
      setWsState("connecting");
      socket.onopen = () => setWsState("connected");
      socket.onclose = () => setWsState("closed");
      socket.onerror = () => setWsState("error");
      socket.onmessage = (event) => {
        try {
          const payload = JSON.parse(event.data as string) as { job?: KnowledgeJob | null };
          if (disposed || !payload.job) return;
          applyJobUpdate(payload.job);
        } catch {
        }
      };
    } catch {
      setWsState("error");
    }

    const timer = window.setInterval(() => {
      void getKnowledgeProgress(detail.name).then((row) => {
        if (!disposed) {
          applyJobUpdate(row);
        }
      }).catch(() => undefined);
    }, 5000);

    return () => {
      disposed = true;
      window.clearInterval(timer);
      socket?.close();
    };
  }, [detail.name]);

  async function loadDiagnostics() {
    setDiagnostics(await getKnowledgeDiagnostics());
  }

  return (
    <div className="mx-auto max-w-3xl px-6 py-6">
      <div className="mb-4 flex items-center justify-between gap-3">
        <div>
          <h2 className="text-[15px] font-semibold">{t("knowledge.indexVersions")} · {versions.length}</h2>
          <p className="mt-1 text-[12px] text-[var(--muted-foreground)]">{t("knowledge.indexVersionsDesc")}</p>
        </div>
        <div className="flex gap-2">
          <button type="button" onClick={onRefresh} className="inline-flex h-8 items-center gap-2 rounded-md border border-[var(--border)] px-2.5 text-[12px] hover:border-blue-300"><RefreshCw size={14} />{t("knowledge.refresh")}</button>
          <button type="button" disabled={busy} onClick={() => void onReindex(detail)} className="inline-flex h-8 items-center gap-2 rounded-md bg-blue-600 px-2.5 text-[12px] font-semibold text-white hover:bg-blue-700 disabled:bg-zinc-300"><RefreshCw size={14} />{t("knowledge.reindex")}</button>
        </div>
      </div>
      <IndexProgressPanel diagnostics={diagnostics} job={job} wsState={wsState} onDiagnostics={() => void loadDiagnostics()} />
      <div className="divide-y divide-[var(--border)] rounded-lg border border-[var(--border)] bg-white">
        {versions.length === 0 ? (
          <div className="p-4 text-[12px] text-[var(--muted-foreground)]">{t("knowledge.noIndexVersions")}</div>
        ) : versions.map((version) => (
          <div key={version.id} className="flex items-center gap-3 px-3 py-3">
            <div className={`flex h-8 w-8 items-center justify-center rounded-md ${version.active ? "bg-emerald-100 text-emerald-700" : "bg-zinc-100 text-zinc-500"}`}>
              {version.active ? <Star size={15} className="fill-current" /> : <Layers size={15} />}
            </div>
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-2 text-[13px] font-semibold">
                {t("knowledge.versionName", { version: version.version_no })}
                {version.active && <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] text-emerald-700">{t("knowledge.active")}</span>}
              </div>
              <div className="mt-1 truncate text-[11px] text-[var(--muted-foreground)]">
                {version.engine_type} · {t("knowledge.documents", { count: version.document_count })} · {t("knowledge.chunks", { count: version.chunk_count })} · {formatDate(version.activated_at ?? version.created_at)}
              </div>
            </div>
            <span className="rounded-full bg-zinc-100 px-2 py-0.5 text-[11px] text-zinc-600">{version.status}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function IndexProgressPanel({ diagnostics, job, wsState, onDiagnostics }: { diagnostics: unknown | null; job: KnowledgeJob | null; wsState: string; onDiagnostics: () => void }) {
  const { t } = useI18n();
  if (!job) return null;

  const active = isActiveJob(job);
  const progress = Math.max(0, Math.min(100, job.progress ?? 0));
  const tone = job.status === "error" ? "red" : job.status === "success" ? "emerald" : "blue";
  const barClass = tone === "red" ? "bg-red-500" : tone === "emerald" ? "bg-emerald-500" : "bg-blue-600";
  const diagnosticSummary = summarizeDiagnostics(diagnostics);
  return (
    <section className={`mb-4 rounded-lg border p-3 ${active ? "border-blue-200 bg-blue-50/50" : "border-[var(--border)] bg-white"}`}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2 text-[12.5px] font-semibold">
            <span>{job.job_type}</span>
            <span className="rounded-full bg-white px-2 py-0.5 text-[11px] text-[var(--muted-foreground)]">{job.status}</span>
            <span className="text-[11px] text-[var(--muted-foreground)]">WS: {wsState}</span>
          </div>
          <div className="mt-1 truncate text-[11px] text-[var(--muted-foreground)]">{job.error_message || job.message || t("common.running")}</div>
        </div>
        <button type="button" onClick={onDiagnostics} className="inline-flex h-8 items-center rounded-md border border-[var(--border)] bg-white px-2.5 text-[12px] hover:border-blue-300">
          {t("models.diagnostics")}
        </button>
      </div>
      <div className="mt-3 h-2 overflow-hidden rounded-full bg-white">
        <div className={`h-full rounded-full transition-all ${barClass}`} style={{ width: `${progress}%` }} />
      </div>
      <div className="mt-1 flex items-center justify-between text-[11px] text-[var(--muted-foreground)]">
        <span>{formatDate(job.started_at ?? job.created_at)}</span>
        <span>{progress}%</span>
      </div>
      {active && (
        <div className="mt-2 text-[11px] text-[var(--muted-foreground)]">后台正在构建索引，完成或失败后会自动刷新。</div>
      )}
      {diagnosticSummary && (
        <div className="mt-3 flex flex-wrap gap-2 text-[11px] text-[var(--muted-foreground)]">
          <span className="rounded-md border border-[var(--border)] bg-white px-2 py-1">连接 {diagnosticSummary.connections}</span>
          <span className="rounded-md border border-[var(--border)] bg-white px-2 py-1">订阅 {diagnosticSummary.clients}</span>
          <span className="rounded-md border border-[var(--border)] bg-white px-2 py-1">事件 {diagnosticSummary.events}</span>
        </div>
      )}
    </section>
  );
}

function summarizeDiagnostics(diagnostics: unknown) {
  if (!diagnostics || typeof diagnostics !== "object") return null;
  const data = diagnostics as { total_connections?: unknown; clients?: unknown; recent_events?: unknown };
  return {
    connections: typeof data.total_connections === "number" ? data.total_connections : 0,
    clients: Array.isArray(data.clients) ? data.clients.length : 0,
    events: Array.isArray(data.recent_events) ? data.recent_events.length : 0
  };
}

function SettingsTab({ busy, detail, embeddingLabel, onDelete, onSetDefault }: { busy: boolean; detail: KnowledgeDetail; embeddingLabel: string; onDelete: (kb: KnowledgeDetail) => Promise<void>; onSetDefault: (kb: KnowledgeDetail) => Promise<void> }) {
  const { t } = useI18n();
  return (
    <div className="mx-auto max-w-3xl space-y-5 px-6 py-6">
      <section className="rounded-lg border border-[var(--border)] bg-white p-4">
        <h2 className="text-[15px] font-semibold">{t("knowledge.overview")}</h2>
        <div className="mt-4 grid gap-4 text-[12px] md:grid-cols-2">
          <Meta label={t("knowledge.ragProvider")} value={detail.engine_type} />
          <Meta label={t("knowledge.embeddingModel")} value={embeddingLabel || t("knowledge.noEmbeddingModel")} />
          <Meta label={t("knowledge.created")} value={formatDate(detail.created_at)} />
          <Meta label={t("knowledge.updated")} value={formatDate(detail.updated_at ?? detail.created_at)} />
          <Meta label={t("knowledge.documentsLabel")} value={String(detail.document_count)} />
          <Meta label={t("knowledge.activeVersion")} value={detail.active_version_id ? String(detail.active_version_id) : t("common.notSet")} />
        </div>
      </section>
      <section className="rounded-lg border border-[var(--border)] bg-white p-4">
        <h2 className="text-[15px] font-semibold">{t("knowledge.defaultKnowledgeBase")}</h2>
        <p className="mt-2 text-[12px] text-[var(--muted-foreground)]">{t("knowledge.defaultKnowledgeBaseDesc")}</p>
        <button type="button" disabled={busy || detail.is_default} onClick={() => void onSetDefault(detail)} className="mt-4 inline-flex h-9 items-center gap-2 rounded-md border border-amber-200 px-3 text-[12px] font-semibold text-amber-700 hover:bg-amber-50 disabled:opacity-60">
          <Star size={14} className={detail.is_default ? "fill-current" : ""} />
          {detail.is_default ? t("knowledge.currentlyDefault") : t("knowledge.setDefault")}
        </button>
      </section>
      <section className="rounded-lg border border-red-200 bg-red-50 p-4 text-red-700">
        <h2 className="text-[15px] font-semibold">{t("knowledge.dangerZone")}</h2>
        <p className="mt-2 text-[12px]">{t("knowledge.dangerZoneDesc")}</p>
        <button type="button" disabled={busy} onClick={() => void onDelete(detail)} className="mt-4 inline-flex h-9 items-center gap-2 rounded-md border border-red-300 px-3 text-[12px] font-semibold hover:bg-red-100 disabled:opacity-60">
          <Trash2 size={14} />
          {t("knowledge.deleteKnowledgeBase")}
        </button>
      </section>
    </div>
  );
}

function FilePicker({ files, onFiles }: { files: File[]; onFiles: (files: File[]) => void }) {
  const { t } = useI18n();
  return (
    <label className="mt-2 flex min-h-[126px] cursor-pointer flex-col items-center justify-center rounded-lg border border-dashed border-[var(--border)] bg-white px-4 py-6 text-center transition hover:border-blue-300">
      <FileUp size={24} className="text-[var(--muted-foreground)]" />
      <span className="mt-2 text-[13px] font-semibold">{files.length > 0 ? t("knowledge.fileCount", { count: files.length }) : t("knowledge.chooseFiles")}</span>
      <span className="mt-1 text-[11px] text-[var(--muted-foreground)]">{t("knowledge.supportedDocuments")}</span>
      {files.length > 0 && <span className="mt-2 max-w-full truncate text-[11px] text-blue-600">{files.map((file) => file.name).join(", ")}</span>}
      <input type="file" multiple accept={supportedDocumentAccept} className="hidden" onChange={(event) => onFiles(Array.from(event.target.files ?? []))} />
    </label>
  );
}

function DetailTabButton({ active, icon: Icon, label, onClick }: { active: boolean; icon: LucideIcon; label: string; onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} className={`inline-flex shrink-0 items-center gap-1.5 border-b-2 px-3 py-2 text-[13px] font-medium transition ${active ? "border-blue-600 text-[var(--foreground)]" : "border-transparent text-[var(--muted-foreground)] hover:text-[var(--foreground)]"}`}>
      <Icon size={14} />
      {label}
    </button>
  );
}

function Meta({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-[10px] font-semibold uppercase tracking-[0.12em] text-[var(--muted-foreground)]">{label}</div>
      <div className="mt-1 break-all text-[13px]">{value}</div>
    </div>
  );
}

function EngineHeader({ engine }: { engine: KnowledgeProvider }) {
  const { t } = useI18n();
  const Icon = engineIcons[engine.id] ?? Boxes;
  const tone = engine.configured ? "emerald" : engine.status === "needs_setup" ? "amber" : "zinc";
  return (
    <header className="flex items-start gap-4">
      <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-lg border border-[var(--border)] bg-white">
        <Icon size={23} strokeWidth={1.7} />
      </div>
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="text-[24px] font-semibold leading-tight">{engine.name}</h1>
          <EngineBadge tone={tone} label={statusLabel(engine.status, t)} />
        </div>
        <p className="mt-2 text-[13px] leading-relaxed text-[var(--muted-foreground)]">{engine.id === "llamaindex" ? t("knowledge.llamaSubtitle") : providerDescription(engine.id, engine.description, t)}</p>
      </div>
    </header>
  );
}

function RetrievalOption({ active, title, description, onClick }: { active: boolean; title: string; description: string; onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} className={`min-h-[72px] rounded-lg border p-3 text-left transition ${active ? "border-blue-500 bg-blue-50/30" : "border-[var(--border)] bg-white hover:border-blue-300"}`}>
      <div className="flex items-center justify-between gap-3">
        <span className="text-[12.5px] font-semibold">{title}</span>
        {active && <Check size={15} className="shrink-0 text-blue-600" />}
      </div>
      <p className="mt-1 text-[11.5px] leading-5 text-[var(--muted-foreground)]">{description}</p>
    </button>
  );
}

function NumberField({ label, value, onChange }: { label: string; value: number; onChange: (value: number) => void }) {
  return (
    <label className="block">
      <span className="text-[12px] font-semibold">{label}</span>
      <input type="number" min={0} value={value} onChange={(event) => onChange(Number(event.target.value))} className="mt-1 h-9 w-full rounded-md border border-[var(--border)] px-3 text-[12.5px] outline-none focus:border-blue-400" />
    </label>
  );
}

function EnvironmentResult({ result }: { result: KnowledgeEnvironmentCheck }) {
  const { t } = useI18n();
  const ok = isEnvironmentOk(result);
  const message = result.error_message ?? result.errorMessage ?? result.ErrorMessage ?? (ok ? t("knowledge.environmentReady") : t("knowledge.environmentFailed"));
  const details = (result.details ?? result.Details ?? {}) as Record<string, unknown>;
  const dependencies = (details.dependencies ?? {}) as Record<string, unknown>;
  return (
    <div className={`rounded-md px-3 py-2 text-[12px] ${ok ? "bg-emerald-50 text-emerald-700" : "bg-red-50 text-red-700"}`}>
      <div className="flex items-center gap-2 font-medium">{ok ? <Check size={15} /> : <XCircle size={15} />}{message}</div>
      {Object.keys(details).length > 0 && (
        <div className="mt-1 space-y-0.5 break-all text-[11px] opacity-90">
          {typeof details.python === "string" && <div>Python: {details.python}</div>}
          {typeof details.python_path === "string" && <div>PythonPath: {details.python_path}</div>}
          {typeof details.worker_path === "string" && <div>Worker: {details.worker_path}</div>}
          {Object.keys(dependencies).length > 0 && <div>Dependencies: {Object.entries(dependencies).map(([name, ready]) => `${name}=${String(ready)}`).join(", ")}</div>}
        </div>
      )}
    </div>
  );
}

function SectionTitle({ icon: Icon, title }: { icon: LucideIcon; title: string }) {
  return (
    <h2 className="mb-3 flex items-center justify-between gap-3 text-[11px] font-semibold uppercase tracking-[0.08em] text-[var(--muted-foreground)]">
      <span className="inline-flex items-center gap-2"><Icon size={15} />{title}</span>
    </h2>
  );
}

function PageMessage({ error, notice }: { error: string | null; notice: string | null }) {
  if (!error && !notice) return null;
  return <div className={`mx-6 mb-3 mt-3 rounded-md border px-3 py-2 text-[12px] ${error ? "border-red-200 bg-red-50 text-red-700" : "border-emerald-200 bg-emerald-50 text-emerald-700"}`}>{error ?? notice}</div>;
}

function EngineBadge({ tone, label }: { tone: "emerald" | "amber" | "zinc"; label: string }) {
  const className = tone === "emerald" ? "bg-emerald-50 text-emerald-700" : tone === "amber" ? "bg-amber-50 text-amber-700" : "bg-zinc-100 text-zinc-600";
  return <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-medium ${className}`}>{tone === "emerald" && <Check size={12} />}{label}</span>;
}

function isEnvironmentOk(result: KnowledgeEnvironmentCheck) {
  return Boolean(result.ok ?? result.Ok);
}

function statusLabel(status: string, t: ReturnType<typeof useI18n>["t"]) {
  if (status === "ready") return t("knowledge.ready");
  if (status === "needs_setup") return t("knowledge.needsSetup");
  if (status === "planned") return t("knowledge.planned");
  if (status === "loading") return t("knowledge.loading");
  return status;
}

function providerDescription(id: string, fallback: string, t: ReturnType<typeof useI18n>["t"]) {
  if (id === "llamaindex") return t("knowledge.llamaSubtitle");
  if (id === "pageindex") return t("knowledge.pageIndexDesc");
  if (id === "graphrag") return t("knowledge.graphRagDesc");
  if (id === "lightrag") return t("knowledge.lightRagDesc");
  if (id === "lightrag-server") return t("knowledge.lightRagServerDesc");
  if (id === "obsidian") return t("knowledge.obsidianDesc");
  return fallback;
}

function modeLabel(mode: string, t: ReturnType<typeof useI18n>["t"]) {
  if (mode === "semantic") return t("knowledge.modeSemantic");
  return mode;
}

function isActiveJob(job: KnowledgeJob) {
  return job.status === "queued" || job.status === "processing";
}

function isTerminalJob(job: KnowledgeJob) {
  return job.status === "success" || job.status === "error";
}

function isPdfDocument(document: KnowledgeDocument) {
  const contentType = (document.content_type ?? "").toLowerCase();
  const extension = (document.extension ?? "").toLowerCase();
  const fileName = (document.original_file_name || document.file_name).toLowerCase();
  return contentType.includes("pdf") || extension === ".pdf" || fileName.endsWith(".pdf");
}

function isTextDocument(document: KnowledgeDocument) {
  const contentType = (document.content_type ?? "").toLowerCase();
  const extension = (document.extension ?? "").toLowerCase();
  const fileName = (document.original_file_name || document.file_name).toLowerCase();
  return contentType.startsWith("text/") || extension === ".txt" || fileName.endsWith(".txt");
}

function formatBytes(value: number) {
  if (!Number.isFinite(value) || value <= 0) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1);
  return `${(value / 1024 ** index).toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}

function formatDate(value?: string | null) {
  if (!value) return "-";
  try {
    return new Intl.DateTimeFormat(undefined, { dateStyle: "short", timeStyle: "short" }).format(new Date(value));
  } catch {
    return value;
  }
}

function fallbackProviders(): KnowledgeProvider[] {
  return [{ id: "llamaindex", name: "LlamaIndex", description: "Local vector retrieval backed by LlamaIndex.", configured: false, status: "loading", modes: ["semantic"], default_mode: "semantic" }];
}
