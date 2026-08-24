"use client";

import { type FormEvent, type ReactNode, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useRouter, useSearchParams } from "next/navigation";
import { Activity, ArrowUp, BookOpen, Bot, Braces, Check, ChevronDown, Copy, Database, Eye, FileCode2, FileText, FolderSearch, Globe2, ImagePlus, ListTodo, Loader2, Menu, Mic, PanelRight, Plus, RefreshCw, Search, ShieldAlert, ShieldCheck, Sparkles, Square, Terminal, UserRound, X, ZoomIn, ZoomOut } from "lucide-react";
import { chatImagePreviewUrl, deleteChatFile, deleteChatImage, persistedChatImageUrl, uploadChatFile, uploadChatImage, type ChatDebugTraceEvent, type ChatFileAttachment, type ChatImageAttachment, type ChatStreamEvent, type CodexSandboxMode } from "@/lib/chat-api";
import { useChatStreams, type ChatStreamRecord } from "@/components/chat/ChatStreamProvider";
import { MarkdownMessage } from "@/components/chat/MarkdownMessage";
import { ChatInspectorPanel, type ChatCodeFileReference } from "@/components/chat/ChatInspectorPanel";
import { ChatRuntimeToolbar } from "@/components/chat/ChatRuntimeToolbar";
import { ClientScanDialog } from "@/components/chat/ClientScanDialog";
import { getSettings } from "@/lib/api";
import { getKnowledgeBases } from "@/lib/knowledge-api";
import { getChatProjectReferences, getCodeProjects, getProjectMarkdownDocuments } from "@/lib/code-repository-api";
import { getProjectTaskChatHandoff } from "@/lib/project-task-api";
import { activeModel, activeProfile, type Catalog, type CatalogModel } from "@/lib/settings-types";
import type { TranslationKey } from "@/i18n/dictionaries";
import type { KnowledgeBase, KnowledgeCitation } from "@/lib/knowledge-types";
import type { CodeProject, CodeProjectMarkdownDocument, CodeProjectReference } from "@/lib/code-repository-types";
import { useI18n } from "@/i18n/I18nProvider";
import { getSession, getSessionDiagnostics, type ChatDebugTraceRecord, type SessionDetail } from "@/lib/session-api";
import { getAgentProviderEnvironments, getCodexModelPolicy, getImageOcrPolicy, type AgentProviderEnvironment, type CodexModelPolicy, type ImageOcrPolicy } from "@/lib/agent-provider-api";

type InspectorTab = "preview" | "file" | "documents" | "uploads" | "tasks" | "terminal";

type ChatImagePreview = ChatImageAttachment & { previewUrl: string };

type SlashProjectCommand = {
  start: number;
  end: number;
  query: string;
  kind: "projects" | "documents";
};

type ChatMessage = {
  id: string;
  role: "user" | "assistant";
  content: string;
  thinking?: string;
  label?: string | null;
  citations?: KnowledgeCitation[];
  model?: string | null;
  status?: "streaming" | "done" | "stopped" | "error";
  startedAt?: number;
  elapsedSeconds?: number;
  iteration?: number;
  llmCalls?: number;
  toolCalls?: number;
  totalTokens?: number;
  trace?: string[];
  agent?: "codex" | "deepseek-harness" | "codebuddy";
  modificationStatus?: string;
  attachments?: ChatImagePreview[];
  documentAttachments?: ChatFileAttachment[];
  markdownDocuments?: CodeProjectMarkdownDocument[];
  projectReferences?: CodeProjectReference[];
  debugTrace?: ChatDebugTraceEvent[];
};

type SessionDraft = { content: string; imageAttachments: ChatImageAttachment[]; imageWarning?: string };

const CHAT_DEBUG_STORAGE_PREFIX = "aiagent:chat-debug:";

function chatDebugStorageKey(sessionId: string | null) {
  return `${CHAT_DEBUG_STORAGE_PREFIX}${sessionId ?? "new"}`;
}

function createClientId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

function toHistoryMessages(session: SessionDetail): ChatMessage[] {
  return session.messages.map((message) => ({
    id: String(message.id),
    role: message.role,
    content: message.content,
    thinking: message.thinking ?? undefined,
    citations: message.citations ?? undefined,
    model: message.metadata?.model ?? null,
    attachments: message.metadata?.attachments?.map((attachment) => ({
      ...attachment,
      previewUrl: persistedChatImageUrl(session.id, attachment.id),
    })),
    documentAttachments: message.metadata?.document_attachments,
    status: "done",
  }));
}

function sessionDraft(preferences: Record<string, unknown>): SessionDraft | null {
  const draft = preferences.draft;
  if (!draft || typeof draft !== "object") return null;
  const value = draft as { content?: unknown; image_attachments?: unknown; image_warning?: unknown };
  if (typeof value.content !== "string" || !value.content.trim()) return null;
  const imageAttachments = Array.isArray(value.image_attachments)
    ? value.image_attachments.filter((item): item is ChatImageAttachment => Boolean(item && typeof item === "object" && typeof (item as ChatImageAttachment).id === "string" && typeof (item as ChatImageAttachment).file_name === "string" && typeof (item as ChatImageAttachment).content_type === "string" && typeof (item as ChatImageAttachment).size_bytes === "number")).slice(0, 4)
    : [];
  return { content: value.content, imageAttachments, imageWarning: typeof value.image_warning === "string" ? value.image_warning : undefined };
}

function applyStreamEvent(message: ChatMessage, event: ChatStreamEvent, t: (key: TranslationKey) => string): ChatMessage {
  if (event.type === "debug_trace" && event.debug_trace)
    return {
      ...message,
      debugTrace: [...(message.debugTrace ?? []), event.debug_trace],
    };
  const stats = event.metadata ? extractRunStats(event.metadata) : {};
  if (event.type === "label")
    return {
      ...message,
      label: event.label ?? null,
      trace: appendTrace(message.trace, formatTraceEvent(event, t)),
      ...stats,
    };
  if (event.type === "thinking")
    return {
      ...message,
      thinking: `${message.thinking ?? ""}${event.content ?? ""}`,
      trace: appendTrace(message.trace, formatTraceEvent(event, t)),
      ...stats,
    };
  if (event.type === "content")
    return {
      ...message,
      content: `${message.content}${event.content ?? ""}`,
      model: event.model ?? message.model,
      ...stats,
    };
  if (event.type === "loop" || event.type === "tool" || event.type === "tool_result" || event.type === "tool_request")
    return {
      ...message,
      trace: appendTrace(message.trace, formatTraceEvent(event, t)),
      ...stats,
    };
  if (event.type === "sources" || event.type === "done")
    return {
      ...message,
      content: message.content || event.content || "",
      citations: event.citations ?? message.citations,
      model: event.model ?? message.model,
      modificationStatus: stringifyMeta(event.metadata?.modification_status) || message.modificationStatus,
      label: event.label ?? message.label,
      status: event.type === "done" ? "done" : message.status,
      elapsedSeconds: stats.elapsedSeconds ?? Math.max(1, Math.round((Date.now() - (message.startedAt ?? Date.now())) / 1000)),
      ...stats,
    };
  if (event.type === "error")
    return {
      ...message,
      content: message.content || event.content || t("chat.searchFailed"),
      status: "error",
    };
  return message;
}

function mergeStreamMessages(items: ChatMessage[], streams: ChatStreamRecord[], t: (key: TranslationKey) => string): ChatMessage[] {
  const next = [...items];
  for (const stream of streams) {
    const id = `stream:${stream.id}`;
    const initial: ChatMessage = {
      id,
      role: "assistant",
      content: "",
      thinking: "",
      trace: [],
      label: null,
      status: "streaming",
      startedAt: stream.startedAt,
      agent: stream.agent,
    };
    const message = stream.events.reduce((current, event) => applyStreamEvent(current, event, t), initial);
    const candidate = {
      ...message,
      status: stream.status === "streaming" ? message.status : stream.status,
      content: stream.status === "done" ? message.content.trim() || t("chat.emptyAnswer") : message.content,
      elapsedSeconds: message.elapsedSeconds ?? (stream.status === "streaming" ? undefined : Math.max(1, Math.round((Date.now() - stream.startedAt) / 1000))),
    };
    const index = next.findIndex((item) => item.id === id);
    if (index >= 0) next[index] = { ...next[index], ...candidate };
    else {
      const alreadyPersisted = stream.status === "done" && candidate.content.trim().length > 0 && next.some((item) => item.role === "assistant" && item.status === "done" && item.content.trim() === candidate.content.trim());
      if (!alreadyPersisted) next.push(candidate);
    }
  }
  return next;
}

export function KnowledgeChatHome({ embeddedSessionId, embedded = false }: { embeddedSessionId?: string | null; embedded?: boolean } = {}) {
  const { t } = useI18n();
  const router = useRouter();
  const searchParams = useSearchParams();
  const requestedSessionId = embedded ? (embeddedSessionId ?? null) : searchParams.get("session");
  const requestedTemplateHandoff = searchParams.get("template_handoff");
  const requestedProjectId = Number(searchParams.get("project")) || null;
  const requestedNewSessionKey = embedded ? null : searchParams.get("new");
  const [knowledgeBases, setKnowledgeBases] = useState<KnowledgeBase[]>([]);
  const [codeProjects, setCodeProjects] = useState<CodeProject[]>([]);
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [selectedKbNames, setSelectedKbNames] = useState<string[]>([]);
  const [selectedProjectId, setSelectedProjectId] = useState<number | null>(requestedProjectId);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(requestedSessionId);
  const [debugTraceEnabled, setDebugTraceEnabled] = useState(false);
  const [diagnosticDialogOpen, setDiagnosticDialogOpen] = useState(false);
  const [storedDiagnostics, setStoredDiagnostics] = useState<ChatDebugTraceRecord[]>([]);
  const [diagnosticsLoading, setDiagnosticsLoading] = useState(false);
  const [openContextPicker, setOpenContextPicker] = useState<"knowledge" | "project" | null>(null);
  const [selectedModelId, setSelectedModelId] = useState("");
  const [selectedCodexModelId, setSelectedCodexModelId] = useState("");
  const [selectedCodexReasoningEffort, setSelectedCodexReasoningEffort] = useState("");
  const [selectedCodexSandboxMode, setSelectedCodexSandboxMode] = useState<CodexSandboxMode>("full-access");
  const [selectedAgentId, setSelectedAgentId] = useState<"codex" | "deepseek-harness" | "codebuddy" | "">("");
  const [agentProviders, setAgentProviders] = useState<AgentProviderEnvironment[]>([]);
  const [codexModelPolicy, setCodexModelPolicy] = useState<CodexModelPolicy | null>(null);
  const [imageOcrPolicy, setImageOcrPolicy] = useState<ImageOcrPolicy | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [composerExpanded, setComposerExpanded] = useState(false);
  const [mobilePicker, setMobilePicker] = useState<"model" | "project" | null>(null);
  const [slashProjectCommand, setSlashProjectCommand] = useState<SlashProjectCommand | null>(null);
  const [projectReferenceOptions, setProjectReferenceOptions] = useState<CodeProjectReference[]>([]);
  const [projectReferenceLoading, setProjectReferenceLoading] = useState(false);
  const [pendingProjectReferences, setPendingProjectReferences] = useState<CodeProjectReference[]>([]);
  const [markdownDocumentOptions, setMarkdownDocumentOptions] = useState<CodeProjectMarkdownDocument[]>([]);
  const [markdownDocumentLoading, setMarkdownDocumentLoading] = useState(false);
  const [pendingMarkdownDocuments, setPendingMarkdownDocuments] = useState<CodeProjectMarkdownDocument[]>([]);
  const [activeProjectReferenceIndex, setActiveProjectReferenceIndex] = useState(0);
  const [imageAttachments, setImageAttachments] = useState<ChatImagePreview[]>([]);
  const [documentAttachments, setDocumentAttachments] = useState<ChatFileAttachment[]>([]);
  const [previewingImage, setPreviewingImage] = useState<ChatImagePreview | null>(null);
  const [requestedDocumentAttachment, setRequestedDocumentAttachment] = useState<ChatFileAttachment | null>(null);
  const [uploadingImages, setUploadingImages] = useState(false);
  const [uploadingFiles, setUploadingFiles] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [rightPanelOpen, setRightPanelOpen] = useState(false);
  const [requestedInspectorTab, setRequestedInspectorTab] = useState<InspectorTab | null>(null);
  const [requestedMarkdownDocument, setRequestedMarkdownDocument] = useState<CodeProjectMarkdownDocument | null>(null);
  const [markdownDocumentsRefreshToken, setMarkdownDocumentsRefreshToken] = useState(0);
  const [fileReference, setFileReference] = useState<ChatCodeFileReference | null>(null);
  const bottomRef = useRef<HTMLDivElement | null>(null);
  const contextPickerRef = useRef<HTMLDivElement | null>(null);
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const activeComposerRef = useRef<HTMLDivElement | null>(null);
  const [composerCursor, setComposerCursor] = useState(0);
  const pendingSessionIdRef = useRef<string | null>(null);
  const activeSessionIdRef = useRef<string | null>(activeSessionId);
  const wasSendingRef = useRef(false);
  const attachmentPreviewUrlsRef = useRef(new Set<string>());
  const { streams, startStream, cancelStream, markSessionViewed, clearFinishedStreams, activateCodexRuntime } = useChatStreams();

  useEffect(() => {
    setDebugTraceEnabled(sessionStorage.getItem(chatDebugStorageKey(activeSessionId)) === "1");
  }, [activeSessionId]);

  const readyKnowledgeBases = useMemo(() => knowledgeBases.filter((kb) => kb.active_version_id && kb.status !== "error"), [knowledgeBases]);
  const llmModels = useMemo(() => resolveLlmModels(catalog), [catalog]);
  const currentKnowledgeBase = readyKnowledgeBases.find((kb) => kb.name === selectedKbNames[0]);
  const selectedProject = codeProjects.find((project) => project.id === selectedProjectId) ?? null;
  const selectedCodeRepositoryNames = selectedProject?.repositories.map((repository) => repository.name) ?? [];
  const currentModel = llmModels.find((model) => model.id === selectedModelId) ?? llmModels[0] ?? null;
  const codexModels = codexModelPolicy?.models.filter((model) => codexModelPolicy.allowed_model_ids.includes(model.id)) ?? [];
  const currentCodexModel = codexModels.find((model) => model.id === selectedCodexModelId) ?? codexModels[0] ?? null;
  const imageInput = selectedAgentId === "codex" ? (currentCodexModel?.image_input ?? "none") : "none";
  const canAttachImages = imageInput === "native" ? imageOcrPolicy?.native_image_input_enabled === true : imageInput === "ocr" && imageOcrPolicy?.enabled === true;
  const imageAttachmentHint = imageInput === "native" ? "原生 Codex 原图识图" : imageInput === "ocr" ? "第三方 Profile 使用本地 PaddleOCR 识别图片文字" : "当前模型未启用图片识别";
  const codexReasoningEfforts = currentCodexModel?.supports_reasoning_effort ? (codexModelPolicy?.allowed_reasoning_efforts ?? []) : [];
  const mobileModelLabel = selectedAgentId === "codex" ? `${currentCodexModel?.name ?? "Auto"}${currentCodexModel?.supports_reasoning_effort && selectedCodexReasoningEffort ? ` · ${codexReasoningEffortLabel(selectedCodexReasoningEffort)}` : ""}` : currentModel?.name || currentModel?.model || "Auto";
  const selectedAgentProvider = agentProviders.find((provider) => provider.id === selectedAgentId) ?? null;
  const sessionStreams = useMemo(() => Object.values(streams).filter((stream) => stream.sessionId === activeSessionId), [activeSessionId, streams]);
  const displayMessages = useMemo(() => mergeStreamMessages(messages, sessionStreams, t), [messages, sessionStreams, t]);
  const sending = sessionStreams.some((stream) => stream.status === "streaming");

  useEffect(() => {
    if (!sending && wasSendingRef.current) setMarkdownDocumentsRefreshToken((current) => current + 1);
    wasSendingRef.current = sending;
  }, [sending]);

  useEffect(() => {
    void loadBootstrap();
  }, []);

  useEffect(() => {
    void getAgentProviderEnvironments()
      .then(setAgentProviders)
      .catch(() => setAgentProviders([]));
    void getCodexModelPolicy()
      .then((policy) => {
        setCodexModelPolicy(policy);
        setSelectedCodexModelId((current) => current || policy.default_model_id);
        setSelectedCodexReasoningEffort((current) => current || policy.default_reasoning_effort);
      })
      .catch(() => setCodexModelPolicy(null));
    void getImageOcrPolicy()
      .then(setImageOcrPolicy)
      .catch(() => setImageOcrPolicy(null));
  }, []);

  useEffect(() => {
    if (agentProviders.length === 0 || !selectedAgentId) return;
    const selected = agentProviders.find((provider) => provider.id === selectedAgentId);
    if (selected?.chat_supported) return;
    setSelectedAgentId(agentProviders.find((provider) => provider.id === "codex" && provider.chat_supported) ? "codex" : "");
  }, [agentProviders, selectedAgentId]);

  useEffect(() => {
    if (selectedAgentId === "codex" && selectedProjectId) activateCodexRuntime(selectedProjectId, selectedCodexModelId || undefined, currentCodexModel?.supports_reasoning_effort ? selectedCodexReasoningEffort || undefined : undefined, selectedCodexSandboxMode);
  }, [activateCodexRuntime, currentCodexModel?.supports_reasoning_effort, selectedAgentId, selectedCodexModelId, selectedCodexReasoningEffort, selectedCodexSandboxMode, selectedProjectId]);

  useEffect(() => {
    if (!requestedSessionId) setSelectedProjectId(requestedProjectId);
  }, [requestedNewSessionKey, requestedProjectId, requestedSessionId]);

  useEffect(() => {
    if (requestedSessionId || !requestedTemplateHandoff) return;
    let cancelled = false;
    const apply = (pending: { content?: string; project_id?: number | null; image_attachments?: ChatImageAttachment[]; image_warning?: string }) => {
      if (cancelled) return;
      if (typeof pending.content === "string" && pending.content.trim()) setInput(pending.content);
      if (typeof pending.project_id === "number") setSelectedProjectId(pending.project_id);
      if (Array.isArray(pending.image_attachments)) setImageAttachments(pending.image_attachments.slice(0, 4).filter((attachment) => typeof attachment?.id === "string").map((attachment) => ({ ...attachment, previewUrl: chatImagePreviewUrl(attachment.id) })));
      if (pending.image_warning) setError(pending.image_warning);
    };
    void getProjectTaskChatHandoff(requestedTemplateHandoff).then(apply).catch(() => {
      const raw = sessionStorage.getItem("aiagent:pending-template-turn");
      if (!raw) { if (!cancelled) setError("工作项聊天交接已过期，请返回任务列表重新点击处理。"); return; }
      try {
        const pending = JSON.parse(raw) as { handoff_id?: string; content?: string; project_id?: number | null; image_attachments?: ChatImageAttachment[]; image_warning?: string };
        if (pending.handoff_id === requestedTemplateHandoff) apply(pending);
      } finally {
        sessionStorage.removeItem("aiagent:pending-template-turn");
      }
    });
    return () => { cancelled = true; };
  }, [requestedSessionId, requestedTemplateHandoff]);

  useEffect(() => {
    activeSessionIdRef.current = activeSessionId;
  }, [activeSessionId]);

  useEffect(() => {
    let cancelled = false;
    if (!requestedSessionId) {
      pendingSessionIdRef.current = null;
      setActiveSessionId(null);
      setMessages([]);
      setInput("");
      setImageAttachments([]);
      setDocumentAttachments([]);
      setPendingMarkdownDocuments([]);
      setPendingProjectReferences([]);
      setError(null);
      return;
    }
    if (requestedSessionId === pendingSessionIdRef.current) {
      pendingSessionIdRef.current = null;
      return;
    }
    void getSession(requestedSessionId)
      .then((session) => {
        if (cancelled) return;
        const draft = session.messages.length === 0 ? sessionDraft(session.preferences) : null;
        setActiveSessionId(session.id);
        setSelectedProjectId(session.project_id ?? null);
        setMessages(toHistoryMessages(session));
        setInput(draft?.content ?? "");
        setImageAttachments(draft?.imageAttachments.map((attachment) => ({ ...attachment, previewUrl: chatImagePreviewUrl(attachment.id) })) ?? []);
        setDocumentAttachments([]);
        if (draft?.imageWarning) setError(draft.imageWarning);
        clearFinishedStreams(session.id);
      })
      .catch((ex) => {
        if (!cancelled) setError(ex instanceof Error ? ex.message : t("chat.errorLoadKnowledge"));
      });
    return () => {
      cancelled = true;
    };
  }, [clearFinishedStreams, requestedNewSessionKey, requestedSessionId, t]);

  useEffect(() => {
    const refreshCompletedSession = (event: Event) => {
      const sessionId = (event as CustomEvent<{ sessionId?: string }>).detail?.sessionId;
      if (!sessionId || sessionId !== activeSessionIdRef.current) return;
      window.setTimeout(() => {
        void getSession(sessionId)
          .then((session) => {
            if (activeSessionIdRef.current !== session.id) return;
            setSelectedProjectId(session.project_id ?? null);
            setMessages(toHistoryMessages(session));
            clearFinishedStreams(session.id);
          })
          .catch(() => {
            // The live stream remains visible; the next session entry retries history loading.
          });
      }, 300);
    };
    window.addEventListener("aiagent:chat-stream-complete", refreshCompletedSession);
    return () => window.removeEventListener("aiagent:chat-stream-complete", refreshCompletedSession);
  }, [clearFinishedStreams]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [displayMessages, sending]);

  useEffect(() => {
    if (!openContextPicker) return;
    const closeWhenOutside = (event: PointerEvent) => {
      if (contextPickerRef.current && !contextPickerRef.current.contains(event.target as Node)) {
        setOpenContextPicker(null);
      }
    };
    document.addEventListener("pointerdown", closeWhenOutside);
    return () => document.removeEventListener("pointerdown", closeWhenOutside);
  }, [openContextPicker]);

  useEffect(() => {
    if (!slashProjectCommand) {
      setProjectReferenceOptions([]);
      setProjectReferenceLoading(false);
      setMarkdownDocumentOptions([]);
      setMarkdownDocumentLoading(false);
      return;
    }
    let cancelled = false;
    const timer = window.setTimeout(() => {
      if (slashProjectCommand.kind === "documents") {
        if (!selectedProjectId) {
          setMarkdownDocumentOptions([]);
          setMarkdownDocumentLoading(false);
          return;
        }
        setMarkdownDocumentLoading(true);
        void getProjectMarkdownDocuments(selectedProjectId, slashProjectCommand.query)
          .then((items) => {
            if (cancelled) return;
            setMarkdownDocumentOptions([...items].sort((left, right) => markdownDocumentUpdatedAt(right) - markdownDocumentUpdatedAt(left)));
            setActiveProjectReferenceIndex(0);
          })
          .catch(() => {
            if (!cancelled) setMarkdownDocumentOptions([]);
          })
          .finally(() => {
            if (!cancelled) setMarkdownDocumentLoading(false);
          });
        return;
      }
      setProjectReferenceLoading(true);
      void getChatProjectReferences(slashProjectCommand.query, selectedProjectId)
        .then((items) => {
          if (cancelled) return;
          setProjectReferenceOptions(items);
          setActiveProjectReferenceIndex(0);
        })
        .catch(() => {
          if (!cancelled) setProjectReferenceOptions([]);
        })
        .finally(() => {
          if (!cancelled) setProjectReferenceLoading(false);
        });
    }, 120);
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [selectedProjectId, slashProjectCommand?.kind, slashProjectCommand?.query]);

  useEffect(() => () => attachmentPreviewUrlsRef.current.forEach((url) => URL.revokeObjectURL(url)), []);
  useEffect(() => {
    if (!activeSessionId) return;
    markSessionViewed(activeSessionId);
  }, [activeSessionId, markSessionViewed, sessionStreams]);

  async function loadBootstrap() {
    setLoading(true);
    setError(null);
    try {
      const [kbRows, projectRows, settings] = await Promise.all([getKnowledgeBases(), getCodeProjects(), getSettings()]);
      setKnowledgeBases(kbRows);
      setCodeProjects(projectRows);
      setSelectedAgentId(settings.ui.preferred_agent === "codebuddy" ? "codebuddy" : settings.ui.preferred_agent === "none" ? "" : "codex");
      setSelectedProjectId((current) => current ?? requestedProjectId ?? null);
      setCatalog(settings.catalog);

      const active = activeModel(settings.catalog, "llm");
      setSelectedModelId((current) => current || active?.id || "");
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("chat.errorLoadKnowledge"));
    } finally {
      setLoading(false);
    }
  }

  function openDiagnostics() {
    if (!activeSessionId) return;
    setDiagnosticDialogOpen(true);
    setStoredDiagnostics([]);
    setDiagnosticsLoading(true);
    void getSessionDiagnostics(activeSessionId)
      .then(setStoredDiagnostics)
      .catch((ex) => setError(ex instanceof Error ? ex.message : "诊断日志读取失败。"))
      .finally(() => setDiagnosticsLoading(false));
  }

  function syncSlashProjectCommand(value: string, cursor: number) {
    const command = findSlashProjectCommand(value, cursor);
    setSlashProjectCommand(command);
    if (command) setActiveProjectReferenceIndex(0);
  }

  function handleComposerChange(value: string, cursor: number) {
    setInput(value);
    setComposerCursor(cursor);
    setPendingMarkdownDocuments((current) => current.filter((document) => extractMarkdownDocumentReferences(value).some((reference) => markdownDocumentKey(document) === `${reference.repository_name}\u0000${reference.path}`)));
    setPendingProjectReferences((current) => current.filter((project) => extractProjectReferenceIds(value).includes(project.id)));
    syncSlashProjectCommand(value, cursor);
  }

  function handleComposerFocus(element: HTMLDivElement, cursor: number) {
    activeComposerRef.current = element;
    setComposerCursor(cursor);
    setComposerExpanded(true);
    syncSlashProjectCommand(input, cursor);
  }

  function handleComposerCursorChange(cursor: number) {
    setComposerCursor(cursor);
    syncSlashProjectCommand(input, cursor);
  }

  function handleComposerKeyDown(event: React.KeyboardEvent<HTMLDivElement>, cursor: number) {
    if (!event.nativeEvent.isComposing && event.key === "Backspace") {
      const removable = inlineReferenceBeforeCursor(input, cursor);
      if (removable) {
        event.preventDefault();
        removeInlineReference(removable.token, removable.start);
        return;
      }
    }
    if (event.nativeEvent.isComposing || !slashProjectCommand) {
      if (event.key === "Enter" && !event.shiftKey) {
        event.preventDefault();
        event.currentTarget.closest("form")?.requestSubmit();
      }
      return;
    }
    if (event.key === "Escape") {
      event.preventDefault();
      setSlashProjectCommand(null);
      return;
    }
    const slashItems = slashProjectCommand.kind === "documents" ? markdownDocumentOptions : projectReferenceOptions;
    if (slashItems.length > 0 && event.key === "ArrowDown") {
      event.preventDefault();
      setActiveProjectReferenceIndex((index) => (index + 1) % slashItems.length);
      return;
    }
    if (slashItems.length > 0 && event.key === "ArrowUp") {
      event.preventDefault();
      setActiveProjectReferenceIndex((index) => (index - 1 + slashItems.length) % slashItems.length);
      return;
    }
    if (slashItems.length > 0 && (event.key === "Enter" || event.key === "Tab")) {
      event.preventDefault();
      if (slashProjectCommand.kind === "documents") {
        const selected = markdownDocumentOptions[activeProjectReferenceIndex] ?? markdownDocumentOptions[0];
        if (selected) insertMarkdownDocumentReference(selected);
      } else {
        const selected = projectReferenceOptions[activeProjectReferenceIndex] ?? projectReferenceOptions[0];
        if (selected) insertProjectReference(selected);
      }
      return;
    }
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      event.currentTarget.closest("form")?.requestSubmit();
    }
  }

  function insertProjectReference(project: CodeProjectReference) {
    const command = findSlashProjectCommand(input, composerCursor) ?? slashProjectCommand;
    if (!command || command.kind !== "projects") return;
    const token = projectReferenceToken(project);
    const next = `${input.slice(0, command.start)}${token}${input.slice(command.end)}`;
    const cursor = command.start + token.length;
    setInput(next);
    setPendingProjectReferences((current) => addProjectReference(current, project));
    setSlashProjectCommand(null);
    focusComposerAt(cursor);
  }

  function openMarkdownDocumentSearch() {
    if (!slashProjectCommand || slashProjectCommand.kind !== "projects") return;
    const command = "/spec ";
    const next = `${input.slice(0, slashProjectCommand.start)}${command}${input.slice(slashProjectCommand.end)}`;
    const cursor = slashProjectCommand.start + command.length;
    setInput(next);
    setSlashProjectCommand({
      start: slashProjectCommand.start,
      end: cursor,
      query: "",
      kind: "documents",
    });
    setActiveProjectReferenceIndex(0);
    focusComposerAt(cursor);
  }

  function insertMarkdownDocumentReference(document: CodeProjectMarkdownDocument) {
    const command = findSlashProjectCommand(input, composerCursor) ?? slashProjectCommand;
    if (!command || command.kind !== "documents") return;
    const token = markdownDocumentReferenceToken(document);
    const next = `${input.slice(0, command.start)}${token}${input.slice(command.end)}`;
    const cursor = command.start + token.length;
    setInput(next);
    setPendingMarkdownDocuments((current) => addMarkdownDocumentReference(current, document));
    setSlashProjectCommand(null);
    focusComposerAt(cursor);
  }

  function appendMarkdownDocumentReference(document: CodeProjectMarkdownDocument) {
    const token = markdownDocumentReferenceToken(document);
    const cursor = composerCursor;
    const next = `${input.slice(0, cursor)}${token}${input.slice(cursor)}`;
    setInput(next);
    setPendingMarkdownDocuments((current) => addMarkdownDocumentReference(current, document));
    setComposerExpanded(true);
    focusComposerAt(cursor + token.length);
  }

  function removeInlineReference(token: string, start = input.indexOf(token)) {
    if (start < 0) return;
    const next = `${input.slice(0, start)}${input.slice(start + token.length)}`;
    handleComposerChange(next, start);
    focusComposerAt(start);
  }

  function focusComposerAt(cursor: number) {
    setComposerCursor(cursor);
    requestAnimationFrame(() => {
      const composer = activeComposerRef.current;
      if (!composer) return;
      composer.focus();
      setInlineComposerSelection(composer, cursor);
    });
  }

  function prefillAgentMarkdownPrompt(prompt: string) {
    setInput(prompt);
    setComposerExpanded(true);
    setSlashProjectCommand(null);
    focusComposerAt(prompt.length);
  }

  async function sendMessage(
    query: string,
    options?: {
      retryAssistantId?: string;
      attachments?: ChatImagePreview[];
      documentAttachments?: ChatFileAttachment[];
      markdownDocuments?: CodeProjectMarkdownDocument[];
      projectReferences?: CodeProjectReference[];
    },
  ) {
    const attachmentsForTurn = options?.attachments ?? imageAttachments;
    const documentAttachmentsForTurn = options?.documentAttachments ?? documentAttachments;
    const markdownDocumentsForTurn = options?.markdownDocuments ?? pendingMarkdownDocuments;
    const markdownReferences = mergeMarkdownDocumentReferences(extractMarkdownDocumentReferences(query), markdownDocumentsForTurn);
    const projectReferencesForTurn = options?.projectReferences ?? pendingProjectReferences;
    const projectReferenceIds = mergeProjectReferenceIds(extractProjectReferenceIds(query), projectReferencesForTurn);
    if ((!query && attachmentsForTurn.length === 0 && documentAttachmentsForTurn.length === 0 && markdownReferences.length === 0 && projectReferenceIds.length === 0) || sending || uploadingImages || uploadingFiles) return;
    if (selectedAgentId && selectedAgentProvider && !selectedAgentProvider.chat_supported) {
      setError(`${selectedAgentProvider.name} 已检测到，但当前版本尚未适配聊天接管协议。`);
      return;
    }
    if (selectedAgentId && !selectedProjectId) {
      setError("本地代理接管需要先选择项目，以便传递项目目录。");
      return;
    }

    if ((attachmentsForTurn.length > 0 || documentAttachmentsForTurn.length > 0) && selectedAgentId !== "codex") {
      setError("附件目前只能发送给 Codex 本地代理。");
      return;
    }
    if (attachmentsForTurn.length > 0 && !canAttachImages) {
      setError("当前 Codex 模型未启用图片识别，请切换模型或在第三方代理设置中开启相应图片能力。");
      return;
    }

    const messageText = query || (markdownReferences.length > 0 ? "请基于已选项目文档回答。" : projectReferenceIds.length > 0 ? "请结合已引用项目回答。" : documentAttachmentsForTurn.length > 0 ? "请分析我附上的文件。" : "请分析我附上的图片。");

    const sessionId = activeSessionId ?? createClientId();
    if (!activeSessionId) {
      if (debugTraceEnabled) sessionStorage.setItem(chatDebugStorageKey(sessionId), "1");
      pendingSessionIdRef.current = sessionId;
      setActiveSessionId(sessionId);
      if (!embedded) router.replace(`/chat?session=${encodeURIComponent(sessionId)}`);
    }

    if (!options?.retryAssistantId) {
      setMessages((items) => [
        ...items,
        {
          id: createClientId(),
          role: "user",
          content: messageText,
          attachments: attachmentsForTurn,
          documentAttachments: documentAttachmentsForTurn,
          markdownDocuments: markdownDocumentsForTurn,
          projectReferences: projectReferencesForTurn,
        },
      ]);
      setInput("");
      setSlashProjectCommand(null);
      setComposerExpanded(false);
      setImageAttachments([]);
      setDocumentAttachments([]);
      setPendingMarkdownDocuments([]);
      setPendingProjectReferences([]);
    }

    setError(null);
    const assistantId = options?.retryAssistantId ?? createClientId();
    const startedAt = Date.now();
    if (options?.retryAssistantId) {
      setMessages((items) =>
        items.map((message) =>
          message.id === assistantId
            ? {
                ...message,
                content: "",
                thinking: "",
                trace: [],
                citations: undefined,
                label: null,
                status: "streaming",
                startedAt,
              }
            : message,
        ),
      );
    } else {
      setMessages((items) => [
        ...items,
        {
          id: assistantId,
          role: "assistant",
          content: "",
          thinking: "",
          trace: [],
          label: null,
          status: "streaming",
          startedAt,
          agent: selectedAgentId || undefined,
        },
      ]);
    }

    try {
      const streamId = startStream({
        session_id: sessionId,
        message: messageText,
        knowledge_base_name: selectedKbNames[0],
        knowledge_base_names: selectedKbNames,
        code_repository_names: selectedCodeRepositoryNames,
        code_project_id: selectedProjectId ?? undefined,
        project_references: projectReferenceIds.map((project_id) => ({
          project_id,
        })),
        markdown_document_references: markdownReferences,
        model_id: selectedAgentId === "codex" ? undefined : selectedModelId || undefined,
        codex_model_id: selectedAgentId === "codex" ? selectedCodexModelId || undefined : undefined,
        codex_reasoning_effort: selectedAgentId === "codex" && currentCodexModel?.supports_reasoning_effort ? selectedCodexReasoningEffort || undefined : undefined,
        codex_sandbox_mode: selectedAgentId === "codex" ? selectedCodexSandboxMode : undefined,
        top_k: 6,
        mode: "chat",
        agent: selectedAgentId || undefined,
        attachment_ids: attachmentsForTurn.map((attachment) => attachment.id),
        document_attachment_ids: documentAttachmentsForTurn.map((attachment) => attachment.id),
        debug_trace: debugTraceEnabled || undefined,
        trace_id: debugTraceEnabled ? createClientId().replaceAll("-", "") : undefined,
      });
      setMessages((items) => {
        const streamMessageId = `stream:${streamId}`;
        return items.some((message) => message.id === streamMessageId) ? items.filter((message) => message.id !== assistantId) : items.map((message) => (message.id === assistantId ? { ...message, id: streamMessageId } : message));
      });
      window.dispatchEvent(new Event("aiagent:sessions-updated"));
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("chat.errorSearch"));
      setMessages((items) =>
        items.map((message) =>
          message.id === assistantId
            ? {
                ...message,
                content: message.content || t("chat.searchFailed"),
                status: "error",
              }
            : message,
        ),
      );
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await sendMessage(input.trim());
  }

  async function addImages(files: File[]) {
    if (files.length === 0) return;
    if (selectedAgentId !== "codex") {
      setError("请先选择 Codex 本地代理，再添加图片附件。");
      return;
    }
    if (!canAttachImages) {
      setError("当前模型未启用图片识别，无法添加图片附件。");
      return;
    }
    const availableSlots = 4 - imageAttachments.length;
    if (availableSlots <= 0) {
      setError("每轮最多添加 4 张图片。");
      return;
    }
    if (files.length > availableSlots) setError(`本轮最多添加 4 张图片，已选择前 ${availableSlots} 张。`);

    setUploadingImages(true);
    try {
      for (const file of files.slice(0, availableSlots)) {
        const attachment = await uploadChatImage(file);
        const previewUrl = URL.createObjectURL(file);
        attachmentPreviewUrlsRef.current.add(previewUrl);
        setImageAttachments((current) => [...current, { ...attachment, previewUrl }]);
      }
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "图片上传失败，请重试。");
    } finally {
      setUploadingImages(false);
    }
  }

  async function addDocuments(files: File[]) {
    if (files.length === 0) return;
    if (selectedAgentId !== "codex") {
      setError("请先选择 Codex 本地代理，再添加文件附件。");
      return;
    }
    const availableSlots = 4 - documentAttachments.length;
    if (availableSlots <= 0) {
      setError("每轮最多添加 4 个文件附件。");
      return;
    }
    if (files.length > availableSlots) setError(`每轮最多添加 4 个文件，已选择前 ${availableSlots} 个。`);
    setUploadingFiles(true);
    try {
      for (const file of files.slice(0, availableSlots)) {
        const attachment = await uploadChatFile(file);
        setDocumentAttachments((current) => [...current, attachment]);
      }
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "文件上传失败，请重试。");
    } finally {
      setUploadingFiles(false);
    }
  }

  function addAttachments(files: File[]) {
    const images = files.filter((file) => file.type.startsWith("image/"));
    const documents = files.filter((file) => !file.type.startsWith("image/"));
    if (images.length > 0) void addImages(images);
    if (documents.length > 0) void addDocuments(documents);
  }

  async function removeImage(attachment: ChatImagePreview) {
    setImageAttachments((current) => current.filter((item) => item.id !== attachment.id));
    attachmentPreviewUrlsRef.current.delete(attachment.previewUrl);
    URL.revokeObjectURL(attachment.previewUrl);
    try {
      await deleteChatImage(attachment.id);
    } catch {
      // The server will also clean short-lived image uploads after expiry.
    }
  }

  async function removeDocument(attachment: ChatFileAttachment) {
    setDocumentAttachments((current) => current.filter((item) => item.id !== attachment.id));
    try {
      await deleteChatFile(attachment.id);
    } catch {
      // The server will clean short-lived uploads after expiry.
    }
  }

  function previewDocumentExtraction(attachment: ChatFileAttachment) {
    if (attachment.extraction_status !== "ready") return;
    setRequestedDocumentAttachment({ ...attachment });
    openInspector("uploads");
  }

  function stopGenerating() {
    sessionStreams.filter((stream) => stream.status === "streaming").forEach((stream) => cancelStream(stream.id));
  }

  function openInspector(tab: InspectorTab) {
    setRequestedInspectorTab(tab);
    setRightPanelOpen(true);
  }

  function openCodeFile(reference: ChatCodeFileReference) {
    setFileReference(reference);
    openInspector("file");
  }

  async function openProjectMarkdownDocument(reference: string) {
    if (!selectedProjectId) return;
    try {
      const items = await getProjectMarkdownDocuments(selectedProjectId);
      const normalizedReference = normalizeMarkdownDocumentReference(reference);
      const document = items.find((item) => item.path.toLowerCase() === normalizedReference) ?? items.find((item) => `${item.repository_name}/${item.path}`.toLowerCase() === normalizedReference) ?? items.find((item) => item.path.toLowerCase().endsWith(`/${normalizedReference}`)) ?? items.find((item) => item.name.toLowerCase() === normalizedReference.split("/").pop());
      if (!document) {
        setError(`当前项目中找不到 ${reference}。`);
        return;
      }
      setRequestedMarkdownDocument(document);
      openInspector("documents");
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : `无法打开 ${reference}。`);
    }
  }

  return (
    <main className="flex h-full min-h-0 flex-col overflow-hidden bg-[radial-gradient(circle_at_50%_-20%,#eff6ff_0,transparent_38%),#f8fafc]">
      <header className="relative z-[80] flex h-14 shrink-0 items-center justify-between border-b border-slate-200/80 bg-white/75 px-3 backdrop-blur-xl lg:h-16 lg:px-5">
        <div className="flex min-w-0 items-center gap-2 lg:gap-3">
          <button type="button" onClick={() => window.dispatchEvent(new Event("aiagent:mobile-drawer-toggle"))} className="grid h-10 w-10 place-items-center rounded-xl text-slate-600 hover:bg-slate-100 lg:hidden" aria-label="打开工作台抽屉">
            <Menu size={20} />
          </button>
          <div className="hidden h-8 w-8 place-items-center rounded-lg bg-blue-50 text-blue-600 lg:grid">
            <Sparkles size={16} />
          </div>
          <div className="min-w-0">
            <h1 className="truncate text-sm font-semibold text-slate-950">{activeSessionId ? "当前会话" : t("chat.newChat")}</h1>
            <p className="hidden text-[11px] text-slate-400 lg:block">AI 工作台</p>
          </div>
          {currentKnowledgeBase && <span className="hidden rounded-full bg-emerald-50 px-2 py-1 text-[11px] text-emerald-700 sm:inline-flex">{currentKnowledgeBase.display_name || currentKnowledgeBase.name}</span>}
          {selectedProject && (
            <span className="hidden items-center gap-1 rounded-full border border-blue-100 bg-blue-50 px-2 py-1 text-[11px] text-blue-700 sm:inline-flex">
              <Braces size={12} />
              {selectedProject.display_name}
            </span>
          )}
        </div>
        <div className="flex shrink-0 items-center gap-1.5 lg:gap-2">
          <button
            type="button"
            onClick={() =>
              setDebugTraceEnabled((enabled) => {
                const next = !enabled;
                sessionStorage.setItem(chatDebugStorageKey(activeSessionId), next ? "1" : "0");
                return next;
              })
            }
            className={`inline-flex h-8 items-center gap-1 rounded-lg border px-2 text-[11px] font-medium transition ${debugTraceEnabled ? "border-amber-300 bg-amber-50 text-amber-800" : "border-slate-200 bg-white text-slate-500 hover:text-slate-700"}`}
            title="仅当前浏览器会话显示耗时诊断，不记录消息内容"
          >
            <Terminal size={13} />
            Debug
          </button>
          <ClientScanDialog />
          <ChatRuntimeToolbar project={selectedProject} rightPanelOpen={rightPanelOpen} onToggleRightPanel={() => setRightPanelOpen((current) => !current)} onOpenRuntimePanel={() => openInspector("terminal")} />
          <span className="hidden lg:contents">
            <SidePanelTabLauncher onOpen={openInspector} />
          </span>
          <button type="button" onClick={() => void loadBootstrap()} className="hidden h-8 w-8 items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-500 shadow-sm transition hover:border-blue-300 hover:text-blue-600 lg:inline-flex" aria-label={t("knowledge.refresh")}>
            <RefreshCw size={14} />
          </button>
        </div>
      </header>

      <div className="flex min-h-0 flex-1 overflow-hidden">
        <section className="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden px-4 py-5 sm:px-7">
          <div className="mx-auto flex min-h-0 w-full max-w-4xl flex-1 flex-col">
            {displayMessages.length === 0 ? (
              <EmptyState title={t("chat.heroTitle")} />
            ) : (
              <div className="workspace-scroll min-h-0 flex-1 space-y-5 overflow-y-auto pb-6 pt-4">
                {displayMessages.map((message, index) => (
                  <MessageBubble
                    key={message.id}
                    message={message}
                    onPreviewImage={setPreviewingImage}
                    onRetry={
                      message.role === "assistant"
                        ? () => {
                            const userMessage = findPreviousUserMessage(displayMessages, index);
                            if (userMessage)
                              void sendMessage(userMessage.content, {
                                retryAssistantId: message.id,
                                attachments: userMessage.attachments,
                                documentAttachments: userMessage.documentAttachments,
                                markdownDocuments: userMessage.markdownDocuments,
                                projectReferences: userMessage.projectReferences,
                              });
                          }
                        : undefined
                    }
                    onOpenCodeFile={openCodeFile}
                    onOpenProjectMarkdownDocument={openProjectMarkdownDocument}
                    projectId={selectedProjectId}
                    showDebugTrace={debugTraceEnabled}
                    onOpenDiagnostics={openDiagnostics}
                  />
                ))}
                {sending && (
                  <div className="flex items-center gap-2 text-[12px] text-[var(--muted-foreground)]">
                    <Loader2 size={14} className="animate-spin" />
                    {t("chat.thinking")}
                  </div>
                )}
                <div ref={bottomRef} />
              </div>
            )}

            <form
              onSubmit={handleSubmit}
              onPaste={(event) => {
                const filesFromItems = Array.from(event.clipboardData.items)
                  .filter((item) => item.kind === "file")
                  .map((item) => item.getAsFile())
                  .filter((file): file is File => file !== null);
                const files = filesFromItems.length > 0 ? filesFromItems : Array.from(event.clipboardData.files);
                if (files.length > 0) {
                  event.preventDefault();
                  addAttachments(files);
                }
              }}
              onDragOver={(event) => event.preventDefault()}
              onDrop={(event) => {
                event.preventDefault();
                const files = Array.from(event.dataTransfer.files);
                if (files.length === 0) return;
                addAttachments(files);
              }}
              className={`sticky bottom-0 mt-auto relative rounded-[24px] border border-slate-200 bg-white/95 px-2 py-2 shadow-[0_18px_46px_rgba(15,23,42,0.12)] backdrop-blur-xl transition focus-within:border-blue-300 focus-within:shadow-[0_20px_52px_rgba(37,99,235,0.15)] lg:bottom-4 lg:rounded-2xl lg:px-4 lg:py-3 ${composerExpanded ? "lg:rounded-2xl" : ""}`}
            >
              {imageAttachments.length > 0 && (
                <div className="mb-2 flex flex-wrap gap-2 border-b border-slate-100 pb-3">
                  {imageAttachments.map((attachment) => (
                    <div key={attachment.id} className="group relative h-16 w-16 overflow-hidden rounded-lg border border-slate-200 bg-slate-50">
                      <button type="button" onClick={() => setPreviewingImage(attachment)} className="block h-full w-full cursor-zoom-in" aria-label={`放大 ${attachment.file_name}`}>
                        <img src={attachment.previewUrl} alt={attachment.file_name} className="h-full w-full object-cover" />
                      </button>
                      <button type="button" onClick={() => void removeImage(attachment)} className="absolute right-0.5 top-0.5 grid h-5 w-5 place-items-center rounded-full bg-slate-900/75 text-white opacity-0 transition group-hover:opacity-100 focus:opacity-100" aria-label={`移除 ${attachment.file_name}`}>
                        <X size={12} />
                      </button>
                    </div>
                  ))}
                </div>
              )}
              {documentAttachments.length > 0 && (
                <div className="mb-2 flex flex-wrap gap-2 border-b border-slate-100 pb-3">
                  {documentAttachments.map((attachment) => (
                    <div key={attachment.id} className="inline-flex max-w-full items-center gap-1.5 rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1.5 text-xs text-slate-700">
                      <FileText size={15} className="shrink-0 text-blue-600" />
                      <span className="max-w-44 truncate">{attachment.file_name}</span>
                      <button type="button" onDoubleClick={() => void previewDocumentExtraction(attachment)} title={attachment.extraction_status === "ready" ? "双击查看实际提取文本" : "旧 Office 格式需先转换"} className={`shrink-0 select-none text-[10px] ${attachment.extraction_status === "ready" ? "cursor-zoom-in text-emerald-600" : "cursor-default text-amber-600"}`}>
                        {attachment.extraction_status === "ready" ? "文本提取" : "需转换"}
                      </button>
                      <button type="button" onClick={() => void removeDocument(attachment)} className="grid h-5 w-5 place-items-center rounded text-slate-400 hover:bg-slate-200 hover:text-slate-700" aria-label={`移除 ${attachment.file_name}`}>
                        <X size={13} />
                      </button>
                    </div>
                  ))}
                </div>
              )}
              {slashProjectCommand && (slashProjectCommand.kind === "documents" ? <MarkdownDocumentSlashMenu items={markdownDocumentOptions} loading={markdownDocumentLoading} hasProject={Boolean(selectedProjectId)} activeIndex={activeProjectReferenceIndex} onActiveIndexChange={setActiveProjectReferenceIndex} onSelect={insertMarkdownDocumentReference} /> : <ProjectReferenceSlashMenu items={projectReferenceOptions} loading={projectReferenceLoading} showDocumentCategory={slashProjectCommand.query.length === 0} activeIndex={activeProjectReferenceIndex} onActiveIndexChange={setActiveProjectReferenceIndex} onOpenDocuments={openMarkdownDocumentSearch} onSelect={insertProjectReference} />)}
              <div className="flex items-center gap-1 lg:hidden">
                <button type="button" className="grid h-9 w-9 shrink-0 place-items-center rounded-xl text-blue-700 hover:bg-blue-50" aria-label={t("chat.voiceInput")}>
                  <Mic size={18} />
                </button>
                <button type="button" onClick={() => setMobilePicker("model")} className="flex h-9 max-w-[102px] shrink-0 items-center gap-1 rounded-xl px-1.5 text-[12px] text-slate-600 hover:bg-slate-100" aria-label="选择模型">
                  <span className="truncate">{mobileModelLabel}</span>
                  <ChevronDown size={13} className="shrink-0" />
                </button>
                <InlineReferenceComposer value={input} cursor={composerCursor} placeholder="发消息或按住说话" className={`min-w-0 flex-1 px-1 py-2 text-[14px] leading-5 ${composerExpanded ? "min-h-[52px]" : "min-h-9"}`} onValueChange={handleComposerChange} onCursorChange={handleComposerCursorChange} onFocus={handleComposerFocus} onKeyDown={handleComposerKeyDown} onRemove={removeInlineReference} onOpenDocument={openProjectMarkdownDocument} />
                <button type="button" onClick={() => fileInputRef.current?.click()} disabled={sending || uploadingImages || uploadingFiles || selectedAgentId !== "codex"} title="添加图片、PDF、Word、Excel、PowerPoint 或文本文件" className="grid h-9 w-9 shrink-0 place-items-center rounded-xl border border-slate-200 bg-white text-slate-700 disabled:cursor-not-allowed disabled:opacity-40" aria-label={t("chat.addAttachment")}>
                  {uploadingImages || uploadingFiles ? <Loader2 size={17} className="animate-spin" /> : <Plus size={20} />}
                </button>
                {selectedAgentId === "codex" && <CodexExecutionPermissionControl mode={selectedCodexSandboxMode} onChange={setSelectedCodexSandboxMode} mobile />}
                {composerExpanded &&
                  (sending ? (
                    <button type="button" onClick={stopGenerating} className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-rose-600 text-white" aria-label="停止生成">
                      <Square size={14} fill="currentColor" />
                    </button>
                  ) : (
                    <button type="submit" disabled={!input.trim() && imageAttachments.length === 0 && documentAttachments.length === 0 && pendingMarkdownDocuments.length === 0 && pendingProjectReferences.length === 0} className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-blue-600 text-white disabled:bg-slate-300" aria-label={t("chat.send")}>
                      <ArrowUp size={17} />
                    </button>
                  ))}
              </div>
              {composerExpanded && (
                <div className="mt-2 flex gap-2 border-t border-slate-100 pt-2 lg:hidden">
                  <button type="button" onClick={() => setMobilePicker("project")} className="flex min-w-0 flex-1 items-center gap-1 rounded-xl bg-slate-100 px-2.5 text-left text-[12px] text-slate-600">
                    <Braces size={14} className="shrink-0 text-blue-600" />
                    <span className="min-w-0 flex-1 truncate">{selectedProject?.display_name || "选择项目"}</span>
                    <ChevronDown size={13} className="shrink-0" />
                  </button>
                  <label className="flex min-w-0 flex-1 items-center gap-1 rounded-xl bg-slate-100 px-2.5 text-[12px] text-slate-600">
                    <Bot size={14} className="shrink-0 text-violet-600" />
                    <select value={selectedAgentId} onChange={(event) => setSelectedAgentId(event.target.value as "codex" | "deepseek-harness" | "codebuddy" | "")} className="min-w-0 flex-1 truncate bg-transparent outline-none" aria-label="选择智能体">
                      <option value="">云端模型</option>
                      <option value="codex">Codex 本地</option>
                      <option value="deepseek-harness" disabled={!agentProviders.some((provider) => provider.id === "deepseek-harness" && provider.chat_supported)}>
                        DeepSeek Harness
                      </option>
                    </select>
                  </label>
                </div>
              )}
              <InlineReferenceComposer value={input} cursor={composerCursor} placeholder={t("chat.placeholderShort")} className="hidden min-h-[56px] px-1 pt-1 text-[14px] leading-6 lg:block" onValueChange={handleComposerChange} onCursorChange={handleComposerCursorChange} onFocus={handleComposerFocus} onKeyDown={handleComposerKeyDown} onRemove={removeInlineReference} onOpenDocument={openProjectMarkdownDocument} />
              <div className="hidden items-center justify-between gap-3 border-t border-slate-100 pt-2.5 lg:flex">
                <div ref={contextPickerRef} className="flex min-w-0 items-center gap-2">
                  <input
                    type="file"
                    accept="image/png,image/jpeg,image/webp,image/gif,.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.md,.markdown,.txt,.csv"
                    multiple
                    className="hidden"
                    onChange={(event) => {
                      const files = Array.from(event.currentTarget.files ?? []);
                      event.currentTarget.value = "";
                      addAttachments(files);
                    }}
                    ref={(element) => {
                      fileInputRef.current = element;
                    }}
                  />
                  <button type="button" onClick={() => fileInputRef.current?.click()} disabled={sending || uploadingImages || uploadingFiles || selectedAgentId !== "codex"} className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-slate-500 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-40" aria-label={t("chat.addAttachment")} title="添加图片、PDF、Word、Excel、PowerPoint 或文本文件">
                    {uploadingImages || uploadingFiles ? <Loader2 size={16} className="animate-spin" /> : <ImagePlus size={17} />}
                  </button>
                  {selectedAgentId === "codex" && <CodexExecutionPermissionControl mode={selectedCodexSandboxMode} onChange={setSelectedCodexSandboxMode} />}
                </div>

                <div className="flex min-w-0 items-center gap-2">
                  <ContextMultiSelect
                    icon={<Database size={15} />}
                    label={t("knowledge.knowledgeBases")}
                    items={readyKnowledgeBases.map((kb) => ({
                      id: kb.name,
                      label: kb.display_name || kb.name,
                      description: kb.engine_type,
                    }))}
                    selectedIds={selectedKbNames}
                    open={openContextPicker === "knowledge"}
                    disabled={loading || readyKnowledgeBases.length === 0}
                    emptyText={loading ? t("knowledge.loading") : t("chat.noKnowledge")}
                    onToggleOpen={() => setOpenContextPicker((current) => (current === "knowledge" ? null : "knowledge"))}
                    onToggle={(name) => setSelectedKbNames((current) => toggleSelection(current, name))}
                  />
                  <ContextMultiSelect
                    icon={<Braces size={15} />}
                    label="项目"
                    items={codeProjects.map((project) => ({
                      id: String(project.id),
                      label: project.display_name,
                      description: `${project.root_path} · ${project.repository_count} 个代码库`,
                    }))}
                    selectedIds={selectedProjectId ? [String(selectedProjectId)] : []}
                    open={openContextPicker === "project"}
                    searchable
                    emptyText="暂无已配置项目"
                    onToggleOpen={() => setOpenContextPicker((current) => (current === "project" ? null : "project"))}
                    onToggle={(id) => setSelectedProjectId((current) => (current === Number(id) ? null : Number(id)))}
                  />
                  <label className={`hidden h-8 min-w-0 items-center gap-1.5 rounded-lg px-2 text-[12px] font-medium sm:inline-flex ${selectedProjectId ? "text-violet-700 hover:bg-violet-50" : "text-slate-400"}`} title={selectedAgentProvider?.message || "选择本地编码代理"}>
                    <Bot size={15} />
                    <select value={selectedAgentId} onChange={(event) => setSelectedAgentId(event.target.value as "codex" | "deepseek-harness" | "codebuddy" | "")} className="max-w-[150px] truncate bg-transparent outline-none">
                      <option value="">不接管</option>
                      <option value="codex">Codex 本地</option>
                      <option value="deepseek-harness" disabled={!agentProviders.some((provider) => provider.id === "deepseek-harness" && provider.chat_supported)}>
                        DeepSeek Harness
                      </option>
                      <option value="codebuddy" disabled>
                        CodeBuddy CLI（待适配）
                      </option>
                    </select>
                  </label>
                  {selectedAgentId === "codex" && (
                    <label className="hidden h-8 min-w-0 items-center gap-1.5 rounded-lg px-2 text-[12px] text-violet-700 hover:bg-violet-50 sm:inline-flex" title={currentCodexModel?.description || "管理员未配置可用 Codex 模型"}>
                      <Bot size={15} />
                      <select value={selectedCodexModelId} onChange={(event) => setSelectedCodexModelId(event.target.value)} disabled={codexModels.length === 0 || codexModelPolicy?.allow_chat_model_override === false} className="max-w-[180px] truncate bg-transparent outline-none disabled:cursor-not-allowed">
                        {codexModels.map((model) => (
                          <option key={model.id} value={model.id}>
                            {model.name}
                          </option>
                        ))}
                      </select>
                    </label>
                  )}
                  {selectedAgentId === "codex" && currentCodexModel?.supports_reasoning_effort && (
                    <label className="hidden h-8 min-w-0 items-center gap-1.5 rounded-lg px-2 text-[12px] text-violet-700 hover:bg-violet-50 sm:inline-flex" title="Codex reasoning effort">
                      <span className="text-[11px] text-violet-500">推理</span>
                      <select value={selectedCodexReasoningEffort} onChange={(event) => setSelectedCodexReasoningEffort(event.target.value)} disabled={codexReasoningEfforts.length === 0 || codexModelPolicy?.allow_chat_reasoning_effort_override === false} className="max-w-[90px] truncate bg-transparent outline-none disabled:cursor-not-allowed">
                        {codexReasoningEfforts.map((effort) => (
                          <option key={effort} value={effort}>
                            {codexReasoningEffortLabel(effort)}
                          </option>
                        ))}
                      </select>
                    </label>
                  )}
                  <label className={`hidden h-8 min-w-0 items-center gap-1.5 rounded-lg px-2 text-[12px] text-slate-600 hover:bg-slate-100 ${selectedAgentId === "codex" ? "" : "sm:inline-flex"}`}>
                    <PanelRight size={15} />
                    <select value={selectedModelId} onChange={(event) => setSelectedModelId(event.target.value)} disabled={llmModels.length === 0} className="max-w-[170px] truncate bg-transparent outline-none" title={currentModel?.model || currentModel?.name}>
                      <option value="">{t("chat.noModel")}</option>
                      {llmModels.map((model) => (
                        <option key={model.id} value={model.id}>
                          {model.name || model.model}
                        </option>
                      ))}
                    </select>
                  </label>
                  <button type="button" className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-slate-500 hover:bg-slate-100" aria-label={t("chat.voiceInput")}>
                    <Mic size={16} />
                  </button>
                  {sending ? (
                    <button type="button" onClick={stopGenerating} className="inline-flex h-9 w-9 items-center justify-center rounded-xl bg-rose-600 text-white shadow-sm shadow-rose-200 transition hover:bg-rose-700" aria-label="Stop generating" title="Stop generating">
                      <Square size={15} fill="currentColor" />
                    </button>
                  ) : (
                    <button type="submit" disabled={!input.trim() && pendingMarkdownDocuments.length === 0 && pendingProjectReferences.length === 0} className="inline-flex h-9 w-9 items-center justify-center rounded-xl bg-blue-600 text-white shadow-sm shadow-blue-200 transition hover:bg-blue-700 disabled:bg-slate-300" aria-label={t("chat.send")}>
                      <ArrowUp size={17} />
                    </button>
                  )}
                </div>
              </div>
            </form>

            {error && <div className="mt-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-[12px] text-red-700">{error}</div>}
          </div>
        </section>
        <ChatInspectorPanel isOpen={rightPanelOpen} project={selectedProject} fileReference={fileReference} requestedTab={requestedInspectorTab} requestedMarkdownDocument={requestedMarkdownDocument} requestedDocumentAttachment={requestedDocumentAttachment} refreshToken={markdownDocumentsRefreshToken} onInsertMarkdownReference={appendMarkdownDocumentReference} onPrepareAgentMarkdown={prefillAgentMarkdownPrompt} onClose={() => setRightPanelOpen(false)} />
        {diagnosticDialogOpen && <ChatDiagnosticsDialog traces={storedDiagnostics} loading={diagnosticsLoading} onClose={() => setDiagnosticDialogOpen(false)} />}
        {previewingImage && <ImageLightbox attachment={previewingImage} onClose={() => setPreviewingImage(null)} />}
      </div>
      <MobileOptionSheet
        open={mobilePicker !== null}
        title={mobilePicker === "model" ? "选择模型" : "选择项目"}
        items={
          mobilePicker === "model"
            ? selectedAgentId === "codex"
              ? codexModels.map((model) => ({
                  id: model.id,
                  label: model.name,
                  description: model.description || "Codex 本地模型",
                  badge: "推理",
                }))
              : [
                  {
                    id: "",
                    label: "Auto",
                    description: "根据任务自动选择默认模型",
                    badge: "智能",
                  },
                  ...llmModels.map((model) => ({
                    id: model.id,
                    label: model.name || model.model,
                    description: model.model,
                    badge: "推理",
                  })),
                ]
            : [
                {
                  id: "",
                  label: "不绑定项目",
                  description: "发起通用对话，不附带代码库上下文",
                },
                ...codeProjects.map((project) => ({
                  id: String(project.id),
                  label: project.display_name,
                  description: `${project.repository_count} 个代码库 · ${project.root_path}`,
                })),
              ]
        }
        selectedId={mobilePicker === "model" ? (selectedAgentId === "codex" ? selectedCodexModelId : selectedModelId) : selectedProjectId ? String(selectedProjectId) : ""}
        onClose={() => setMobilePicker(null)}
        onSelect={(id) => {
          if (mobilePicker === "model") {
            if (selectedAgentId === "codex") setSelectedCodexModelId(id);
            else setSelectedModelId(id);
          } else setSelectedProjectId(id ? Number(id) : null);
          setMobilePicker(null);
        }}
      />
    </main>
  );
}

function findSlashProjectCommand(value: string, cursor: number): SlashProjectCommand | null {
  const beforeCursor = value.slice(0, cursor);
  const match = /(^|\s)\/([^\s]*)(?:\s([^\n]*))?$/.exec(beforeCursor);
  if (!match) return null;
  const start = beforeCursor.length - match[0].length + match[1].length;
  const command = match[2].toLowerCase();
  return command === "spec" ? { start, end: cursor, query: match[3]?.trim() ?? "", kind: "documents" } : { start, end: cursor, query: match[2], kind: "projects" };
}

function extractProjectReferenceIds(value: string): number[] {
  const ids = new Set<number>();
  for (const match of value.matchAll(/\[\[项目:[^\]|]+\|(\d+)\]\]/g)) {
    const id = Number(match[1]);
    if (Number.isSafeInteger(id) && id > 0) ids.add(id);
  }
  return [...ids];
}

function addProjectReference(items: CodeProjectReference[], project: CodeProjectReference): CodeProjectReference[] {
  return items.some((item) => item.id === project.id) ? items : [...items, project];
}

function projectReferenceToken(project: CodeProjectReference): string {
  return `[[项目:${project.display_name}|${project.id}]]`;
}

function mergeProjectReferenceIds(existing: number[], projects: CodeProjectReference[]): number[] {
  return [...new Set([...existing, ...projects.map((project) => project.id)])];
}

function extractMarkdownDocumentReferences(value: string): Array<{ repository_name: string; path: string }> {
  const references = new Map<string, { repository_name: string; path: string }>();
  for (const match of value.matchAll(/\[\[文档:[^\]|]+\|([^\]|]+)\|([^\]|]+)\]\]/g)) {
    const repository_name = match[1].trim();
    const path = match[2].trim();
    if (repository_name && path)
      references.set(`${repository_name}\u0000${path}`, {
        repository_name,
        path,
      });
  }
  return [...references.values()];
}

function markdownDocumentKey(document: Pick<CodeProjectMarkdownDocument, "repository_name" | "path">): string {
  return `${document.repository_name}\u0000${document.path}`;
}

function normalizeMarkdownDocumentReference(reference: string): string {
  return reference
    .trim()
    .replace(/\\/g, "/")
    .replace(/(?::|#L)[1-9]\d{0,8}$/i, "")
    .replace(/^\.\//, "")
    .toLowerCase();
}

function addMarkdownDocumentReference(items: CodeProjectMarkdownDocument[], document: CodeProjectMarkdownDocument): CodeProjectMarkdownDocument[] {
  return items.some((item) => markdownDocumentKey(item) === markdownDocumentKey(document)) ? items : [...items, document];
}

function markdownDocumentReferenceToken(document: CodeProjectMarkdownDocument): string {
  return `[[文档:${document.name}|${document.repository_name}|${document.path}]]`;
}

function mergeMarkdownDocumentReferences(existing: Array<{ repository_name: string; path: string }>, documents: CodeProjectMarkdownDocument[]): Array<{ repository_name: string; path: string }> {
  const references = new Map(existing.map((item) => [`${item.repository_name}\u0000${item.path}`, item]));
  documents.forEach((document) =>
    references.set(markdownDocumentKey(document), {
      repository_name: document.repository_name,
      path: document.path,
    }),
  );
  return [...references.values()];
}

function markdownDocumentUpdatedAt(document: CodeProjectMarkdownDocument): number {
  const value = document.updated_at ? Date.parse(document.updated_at) : Number.NaN;
  return Number.isFinite(value) ? value : 0;
}

function formatMarkdownDocumentUpdatedAt(document: CodeProjectMarkdownDocument): string {
  const timestamp = markdownDocumentUpdatedAt(document);
  if (!timestamp) return "更新时间未知";
  const elapsed = Date.now() - timestamp;
  if (elapsed >= 0 && elapsed < 60_000) return "刚刚更新";
  if (elapsed >= 0 && elapsed < 3_600_000) return `${Math.max(1, Math.floor(elapsed / 60_000))} 分钟前`;
  if (elapsed >= 0 && elapsed < 86_400_000) return `${Math.max(1, Math.floor(elapsed / 3_600_000))} 小时前`;
  return new Intl.DateTimeFormat("zh-CN", {
    month: "numeric",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(timestamp);
}

type InlineReferenceSegment = {
  token: string;
  kind: "project" | "document";
  label: string;
  documentReference?: string;
};

function inlineReferenceSegments(value: string): Array<string | InlineReferenceSegment> {
  const result: Array<string | InlineReferenceSegment> = [];
  const expression = /\[\[(项目|文档):([^\]|]+)\|([^\]|]+)(?:\|([^\]]+))?\]\]/g;
  let cursor = 0;
  for (const match of value.matchAll(expression)) {
    if (match.index! > cursor) result.push(value.slice(cursor, match.index));
    result.push({
      token: match[0],
      kind: match[1] === "项目" ? "project" : "document",
      label: match[2],
      documentReference: match[1] === "文档" && match[4] ? `${match[3]}/${match[4]}` : undefined,
    });
    cursor = match.index! + match[0].length;
  }
  if (cursor < value.length) result.push(value.slice(cursor));
  return result;
}

function inlineReferenceBeforeCursor(value: string, cursor: number): { token: string; start: number } | null {
  let start = 0;
  for (const segment of inlineReferenceSegments(value)) {
    if (typeof segment === "string") {
      start += segment.length;
      continue;
    }
    if (start + segment.token.length === cursor) return { token: segment.token, start };
    start += segment.token.length;
  }
  return null;
}

function inlineComposerValue(node: Node, appendBlockBreak = false): string {
  if (node.nodeType === Node.TEXT_NODE) return node.textContent ?? "";
  if (node instanceof HTMLElement && node.dataset.inlineReference) return node.dataset.inlineReference;
  if (node instanceof HTMLElement && node.tagName === "BR") return "\n";
  const value = Array.from(node.childNodes)
    .map((child) => inlineComposerValue(child, true))
    .join("");
  return appendBlockBreak && node instanceof HTMLElement && node.tagName === "DIV" && value ? `${value}\n` : value;
}

function inlineComposerCursor(element: HTMLElement): number {
  const selection = window.getSelection();
  if (!selection?.rangeCount) return inlineComposerValue(element).length;
  const range = selection.getRangeAt(0);
  if (!element.contains(range.startContainer)) return inlineComposerValue(element).length;
  const before = range.cloneRange();
  before.selectNodeContents(element);
  before.setEnd(range.startContainer, range.startOffset);
  return inlineComposerValue(before.cloneContents()).length;
}

function inlineComposerNodeLength(node: Node): number {
  return inlineComposerValue(node).length;
}

function setInlineComposerSelection(element: HTMLElement, offset: number) {
  const selection = window.getSelection();
  if (!selection) return;
  const range = document.createRange();
  let remaining = Math.max(0, offset);
  for (const child of Array.from(element.childNodes)) {
    const length = inlineComposerNodeLength(child);
    if (remaining > length) {
      remaining -= length;
      continue;
    }
    if (child.nodeType === Node.TEXT_NODE) range.setStart(child, Math.min(remaining, child.textContent?.length ?? 0));
    else if (child instanceof HTMLElement && child.dataset.inlineReference) remaining === 0 ? range.setStartBefore(child) : range.setStartAfter(child);
    else range.setStartBefore(child);
    range.collapse(true);
    selection.removeAllRanges();
    selection.addRange(range);
    return;
  }
  range.selectNodeContents(element);
  range.collapse(false);
  selection.removeAllRanges();
  selection.addRange(range);
}

function renderInlineComposerValue(element: HTMLElement, value: string) {
  const fragment = document.createDocumentFragment();
  for (const segment of inlineReferenceSegments(value)) {
    if (typeof segment === "string") {
      fragment.append(document.createTextNode(segment));
      continue;
    }
    const wrapper = document.createElement("span");
    wrapper.contentEditable = "false";
    wrapper.dataset.inlineReference = segment.token;
    wrapper.className = "mx-0.5 inline-flex max-w-[220px] align-middle";
    const chip = document.createElement("span");
    if (segment.documentReference) {
      chip.dataset.inlineReferenceOpen = segment.documentReference;
      chip.setAttribute("role", "button");
      chip.tabIndex = 0;
      chip.title = "在右侧项目文档中打开";
    }
    chip.className = `inline-flex max-w-full items-center rounded-lg border border-blue-200 bg-blue-50 py-1 pl-2 text-[11px] text-blue-800 ${segment.documentReference ? "cursor-pointer hover:border-blue-300 hover:bg-blue-100" : ""}`;
    const label = document.createElement("span");
    label.className = "truncate font-medium";
    label.textContent = `${segment.kind === "project" ? "项目" : "文档"}：${segment.label}`;
    const remove = document.createElement("button");
    remove.type = "button";
    remove.dataset.inlineReferenceRemove = segment.token;
    remove.className = "ml-1 grid h-5 w-5 shrink-0 place-items-center rounded text-blue-600 hover:bg-blue-100";
    remove.setAttribute("aria-label", `移除 ${segment.label}`);
    remove.textContent = "×";
    chip.append(label, remove);
    wrapper.append(chip);
    fragment.append(wrapper);
  }
  element.replaceChildren(fragment);
}

function CodexExecutionPermissionControl({ mode, onChange, mobile = false }: { mode: CodexSandboxMode; onChange: (mode: CodexSandboxMode) => void; mobile?: boolean }) {
  const [open, setOpen] = useState(false);
  const controlRef = useRef<HTMLDivElement | null>(null);
  const options: Array<{
    mode: CodexSandboxMode;
    label: string;
    title: string;
  }> = [
    {
      mode: "full-access",
      label: "完全控制",
      title: "不使用沙箱，允许直接修改",
    },
    {
      mode: "workspace-write",
      label: "工作区写入",
      title: "仅允许在当前项目内写入",
    },
    { mode: "read-only", label: "只读分析", title: "只读取代码，不修改文件" },
  ];
  const selected = options.find((option) => option.mode === mode) ?? options[0];
  const Icon = mode === "full-access" ? ShieldAlert : mode === "workspace-write" ? ShieldCheck : Eye;
  const tone = mode === "full-access" ? "border-amber-200 bg-amber-50 text-amber-700 hover:bg-amber-100" : mode === "workspace-write" ? "border-violet-200 bg-violet-50 text-violet-700 hover:bg-violet-100" : "border-slate-200 bg-slate-50 text-slate-600 hover:bg-slate-100";

  useEffect(() => {
    if (!open) return;
    const closeOnOutsidePointerDown = (event: PointerEvent) => {
      if (event.target instanceof Node && !controlRef.current?.contains(event.target)) setOpen(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    document.addEventListener("pointerdown", closeOnOutsidePointerDown);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOnOutsidePointerDown);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [open]);

  return (
    <div ref={controlRef} className="relative shrink-0">
      <button type="button" onClick={() => setOpen((current) => !current)} className={`grid place-items-center rounded-lg border transition ${mobile ? "h-9 w-9 rounded-xl" : "h-8 w-8"} ${tone}`} aria-label={`Codex 执行权限：${selected.label}`} aria-expanded={open} title={`Codex 执行权限：${selected.label}`}>
        <Icon size={mobile ? 17 : 16} />
      </button>
      {open && (
        <div
          className={`absolute bottom-full z-50 mb-2 overflow-hidden rounded-xl border border-slate-200 bg-white p-1.5 shadow-xl ${mobile ? "right-0" : "left-0"}`}
          style={{
            width: "184px",
            minWidth: "184px",
            maxWidth: "calc(100vw - 24px)",
          }}
          role="menu"
          aria-label="选择 Codex 执行权限"
        >
          {options.map((option) => {
            const OptionIcon = option.mode === "full-access" ? ShieldAlert : option.mode === "workspace-write" ? ShieldCheck : Eye;
            const active = option.mode === mode;
            return (
              <button
                key={option.mode}
                type="button"
                role="menuitemradio"
                aria-checked={active}
                onClick={() => {
                  onChange(option.mode);
                  setOpen(false);
                }}
                title={option.title}
                className={`flex h-9 w-full min-w-0 items-center gap-2 rounded-lg px-2.5 text-left text-xs transition ${active ? "bg-violet-50 text-violet-800" : "text-slate-700 hover:bg-slate-50"}`}
              >
                <OptionIcon size={16} className={`shrink-0 ${option.mode === "full-access" ? "text-amber-600" : option.mode === "workspace-write" ? "text-violet-600" : "text-slate-500"}`} />
                <span className="min-w-0 flex-1 truncate whitespace-nowrap font-medium">{option.label}</span>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

function InlineReferenceComposer({ value, cursor, placeholder, className, onValueChange, onCursorChange, onFocus, onKeyDown, onRemove, onOpenDocument }: { value: string; cursor: number; placeholder: string; className: string; onValueChange: (value: string, cursor: number) => void; onCursorChange: (cursor: number) => void; onFocus: (element: HTMLDivElement, cursor: number) => void; onKeyDown: (event: React.KeyboardEvent<HTMLDivElement>, cursor: number) => void; onRemove: (token: string) => void; onOpenDocument: (reference: string) => void }) {
  const editorRef = useRef<HTMLDivElement | null>(null);
  const compositionRef = useRef(false);
  useLayoutEffect(() => {
    const editor = editorRef.current;
    if (!editor || compositionRef.current || inlineComposerValue(editor) === value) return;
    renderInlineComposerValue(editor, value);
    if (document.activeElement === editor) setInlineComposerSelection(editor, cursor);
  }, [value, cursor]);
  const read = (element: HTMLDivElement) => onValueChange(inlineComposerValue(element), inlineComposerCursor(element));
  const handleReferenceClick = (event: React.MouseEvent<HTMLDivElement>) => {
    const target = event.target instanceof Element ? event.target.closest<HTMLButtonElement>("button[data-inline-reference-remove]") : null;
    const token = target?.dataset.inlineReferenceRemove;
    if (token) {
      onRemove(token);
      return;
    }
    const documentReference = event.target instanceof Element ? event.target.closest<HTMLElement>("[data-inline-reference-open]")?.dataset.inlineReferenceOpen : undefined;
    if (documentReference) onOpenDocument(documentReference);
  };
  return (
    <div
      ref={editorRef}
      contentEditable
      suppressContentEditableWarning
      role="textbox"
      aria-multiline="true"
      aria-label="聊天输入，输入斜杠可引用项目"
      data-placeholder={placeholder}
      onCompositionStart={() => {
        compositionRef.current = true;
      }}
      onCompositionEnd={(event) => {
        compositionRef.current = false;
        read(event.currentTarget);
      }}
      onInput={(event) => {
        if (!compositionRef.current) read(event.currentTarget);
      }}
      onPaste={(event) => {
        event.preventDefault();
        const text = event.clipboardData.getData("text/plain");
        if (!text) return;
        document.execCommand("insertText", false, text);
      }}
      onFocus={(event) => onFocus(event.currentTarget, inlineComposerCursor(event.currentTarget))}
      onSelect={(event) => onCursorChange(inlineComposerCursor(event.currentTarget))}
      onKeyDown={(event) => {
        const documentReference = event.target instanceof Element ? event.target.closest<HTMLElement>("[data-inline-reference-open]")?.dataset.inlineReferenceOpen : undefined;
        if (documentReference && (event.key === "Enter" || event.key === " ")) {
          event.preventDefault();
          onOpenDocument(documentReference);
          return;
        }
        onKeyDown(event, inlineComposerCursor(event.currentTarget));
      }}
      onMouseDown={(event) => {
        if ((event.target as Element).closest("button[data-inline-reference-remove], [data-inline-reference-open]")) event.preventDefault();
      }}
      onClick={handleReferenceClick}
      className={`workspace-scroll max-h-36 min-w-0 flex-1 overflow-y-auto whitespace-pre-wrap break-words text-slate-800 outline-none empty:before:pointer-events-none empty:before:content-[attr(data-placeholder)] empty:before:text-slate-400 ${className}`}
    />
  );
}

function InlineReferenceText({ value }: { value: string }) {
  return (
    <>
      {inlineReferenceSegments(value).map((segment, index) =>
        typeof segment === "string" ? (
          segment
        ) : (
          <span key={`${segment.token}-${index}`} className="mx-0.5 inline-flex items-center rounded-md bg-white/15 px-1.5 py-0.5 text-[11px] text-white">
            <span className="mr-1">{segment.kind === "project" ? <Braces size={12} /> : <FileText size={12} />}</span>
            {segment.label}
          </span>
        ),
      )}
    </>
  );
}

function humanizeInlineReferenceTokens(value: string): string {
  return value.replace(/\[\[项目:([^\]|]+)\|[^\]]+\]\]/g, "【项目：$1】").replace(/\[\[文档:([^\]|]+)\|[^\]]+\]\]/g, "【文档：`$1`】");
}

function ProjectReferenceSlashMenu({ items, loading, showDocumentCategory, activeIndex, onActiveIndexChange, onOpenDocuments, onSelect }: { items: CodeProjectReference[]; loading: boolean; showDocumentCategory: boolean; activeIndex: number; onActiveIndexChange: (index: number) => void; onOpenDocuments: () => void; onSelect: (project: CodeProjectReference) => void }) {
  return (
    <div id="chat-project-reference-menu" role="listbox" aria-label="引用其他项目" className="absolute bottom-full left-2 right-2 z-50 mb-2 overflow-hidden rounded-2xl border border-slate-200 bg-white p-1.5 shadow-[0_18px_42px_rgba(15,23,42,0.2)] lg:left-4 lg:right-auto lg:w-[380px]">
      <div className="flex items-center gap-2 px-2.5 py-2 text-[11px] font-semibold text-slate-500">
        <FolderSearch size={14} className="text-blue-600" />
        <span>引用其他项目</span>
        <span className="ml-auto font-normal">↑↓ 选择 · Enter 插入</span>
      </div>
      <div className="max-h-60 overflow-y-auto">
        {showDocumentCategory && (
          <button
            type="button"
            onPointerDown={(event) => {
              event.preventDefault();
              onOpenDocuments();
            }}
            className="mb-1 flex min-h-11 w-full items-center gap-2.5 rounded-xl border border-blue-100 bg-blue-50/70 px-2.5 py-2 text-left text-blue-900 transition hover:bg-blue-100"
          >
            <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-blue-600 text-white">
              <FileText size={15} />
            </span>
            <span className="min-w-0 flex-1">
              <span className="block text-xs font-semibold">项目文档</span>
              <span className="mt-0.5 block text-[11px] text-blue-700">搜索当前项目的 Markdown 文档</span>
            </span>
            <span className="text-[11px] font-medium text-blue-600">/spec</span>
          </button>
        )}
        {loading ? (
          <div className="flex min-h-12 items-center gap-2 px-3 text-xs text-slate-500">
            <Loader2 size={15} className="animate-spin" />
            正在查找可访问项目…
          </div>
        ) : items.length === 0 ? (
          <p className="px-3 py-4 text-center text-xs leading-5 text-slate-500">没有可引用的其他项目。</p>
        ) : (
          items.map((project, index) => {
            const active = index === activeIndex;
            return (
              <button
                key={project.id}
                type="button"
                role="option"
                aria-selected={active}
                onMouseEnter={() => onActiveIndexChange(index)}
                onPointerDown={(event) => {
                  event.preventDefault();
                  onSelect(project);
                }}
                className={`flex min-h-11 w-full items-center gap-2.5 rounded-xl px-2.5 py-2 text-left transition ${active ? "bg-blue-50 text-blue-950" : "hover:bg-slate-50"}`}
              >
                <span className={`grid h-8 w-8 shrink-0 place-items-center rounded-lg text-xs font-bold ${active ? "bg-blue-600 text-white" : "bg-slate-100 text-slate-500"}`}>{project.display_name.slice(0, 1).toUpperCase()}</span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-xs font-semibold">{project.display_name}</span>
                  {project.description && <span className="mt-0.5 block truncate text-[11px] text-slate-500">{project.description}</span>}
                </span>
                <span className="shrink-0 text-[10px] text-slate-400">#{project.id}</span>
              </button>
            );
          })
        )}
      </div>
    </div>
  );
}

function MarkdownDocumentSlashMenu({ items, loading, hasProject, activeIndex, onActiveIndexChange, onSelect }: { items: CodeProjectMarkdownDocument[]; loading: boolean; hasProject: boolean; activeIndex: number; onActiveIndexChange: (index: number) => void; onSelect: (document: CodeProjectMarkdownDocument) => void }) {
  return (
    <div id="chat-project-reference-menu" role="listbox" aria-label="搜索当前项目的 Markdown 文档" className="absolute bottom-full left-2 right-2 z-50 mb-2 overflow-hidden rounded-2xl border border-slate-200 bg-white p-1.5 shadow-[0_18px_42px_rgba(15,23,42,0.2)] lg:left-4 lg:right-auto lg:w-[420px]">
      <div className="flex items-center gap-2 px-2.5 py-2 text-[11px] font-semibold text-slate-500">
        <FileText size={14} className="text-blue-600" />
        <span>项目文档 · /spec</span>
        <span className="ml-auto font-normal text-blue-600">最近更新优先 · ↑↓ 选择 · Enter 插入</span>
      </div>
      <div className="max-h-60 overflow-y-auto">
        {!hasProject ? (
          <p className="px-3 py-4 text-center text-xs leading-5 text-slate-500">请先选择当前项目，再使用 /spec 搜索其 Markdown 文档。</p>
        ) : loading ? (
          <div className="flex min-h-12 items-center gap-2 px-3 text-xs text-slate-500">
            <Loader2 size={15} className="animate-spin" />
            正在搜索项目文档…
          </div>
        ) : items.length === 0 ? (
          <p className="px-3 py-4 text-center text-xs leading-5 text-slate-500">没有符合条件的 Markdown 文档。</p>
        ) : (
          items.map((document, index) => {
            const active = index === activeIndex;
            return (
              <button
                key={`${document.repository_name}:${document.path}`}
                type="button"
                role="option"
                aria-selected={active}
                onMouseEnter={() => onActiveIndexChange(index)}
                onPointerDown={(event) => {
                  event.preventDefault();
                  onSelect(document);
                }}
                className={`flex min-h-11 w-full items-center gap-2.5 rounded-xl px-2.5 py-2 text-left transition ${active ? "bg-blue-50 text-blue-950" : "hover:bg-slate-50"}`}
              >
                <span className={`grid h-8 w-8 shrink-0 place-items-center rounded-lg ${active ? "bg-blue-600 text-white" : "bg-slate-100 text-slate-500"}`}>
                  <FileText size={15} />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-xs font-semibold">{document.name}</span>
                  <span className="mt-0.5 block truncate text-[11px] text-slate-500">
                    {document.repository_name} / {document.path}
                  </span>
                </span>
                <span className="shrink-0 text-[10px] text-slate-400">{formatMarkdownDocumentUpdatedAt(document)}</span>
              </button>
            );
          })
        )}
      </div>
    </div>
  );
}

function codexReasoningEffortLabel(effort: string) {
  return (
    (
      {
        minimal: "极轻",
        low: "轻度",
        medium: "中",
        high: "高",
        xhigh: "极高",
      } as Record<string, string>
    )[effort] ?? effort
  );
}

type ContextPickerItem = {
  id: string;
  label: string;
  description?: string;
};

function SidePanelTabLauncher({ onOpen }: { onOpen: (tab: InspectorTab) => void }) {
  const [open, setOpen] = useState(false);
  const options: Array<{
    tab: InspectorTab;
    label: string;
    shortcut: string;
    icon: typeof FileCode2;
  }> = [
    { tab: "documents", label: "项目文档", shortcut: "", icon: FileText },
    { tab: "file", label: "文件", shortcut: "Ctrl+P", icon: FileCode2 },
    { tab: "tasks", label: "侧边任务", shortcut: "Ctrl+Alt+S", icon: ListTodo },
    { tab: "preview", label: "浏览器", shortcut: "Ctrl+I", icon: Globe2 },
    { tab: "terminal", label: "终端", shortcut: "", icon: Terminal },
  ];

  return (
    <div className="relative">
      <button type="button" onClick={() => setOpen((current) => !current)} className={`inline-flex h-8 w-8 items-center justify-center rounded-lg border bg-white shadow-sm transition ${open ? "border-blue-300 bg-blue-50 text-blue-700" : "border-slate-200 text-slate-600 hover:border-blue-300 hover:text-blue-600"}`} aria-label="Open side panel tab" aria-expanded={open}>
        <Plus size={16} />
      </button>
      {open && (
        <div className="absolute right-0 top-10 z-50 w-72 rounded-xl border border-slate-200 bg-white p-2 shadow-[0_18px_42px_rgba(15,23,42,0.2)]">
          {options.map((option) => {
            const Icon = option.icon;
            return (
              <button
                key={option.tab}
                type="button"
                onClick={() => {
                  onOpen(option.tab);
                  setOpen(false);
                }}
                className="flex h-10 w-full items-center gap-2.5 rounded-lg px-2.5 text-left text-sm text-slate-700 transition hover:bg-slate-100"
              >
                <Icon size={16} className="text-slate-500" />
                <span className="flex-1">{option.label}</span>
                {option.shortcut && <kbd className="rounded bg-slate-100 px-1.5 py-0.5 text-[10px] text-slate-500">{option.shortcut}</kbd>}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

type MobileOption = {
  id: string;
  label: string;
  description?: string;
  badge?: string;
};

function MobileOptionSheet({ open, title, items, selectedId, onClose, onSelect }: { open: boolean; title: string; items: MobileOption[]; selectedId: string; onClose: () => void; onSelect: (id: string) => void }) {
  if (!open || typeof document === "undefined") return null;
  return createPortal(
    <div className="fixed inset-0 z-[160] flex items-end bg-slate-950/55 backdrop-blur-[2px] lg:hidden" role="presentation" onMouseDown={onClose}>
      <section className="max-h-[78dvh] w-full overflow-y-auto rounded-t-[30px] bg-white px-5 pb-[max(1.25rem,env(safe-area-inset-bottom))] pt-2 shadow-[0_-18px_55px_rgba(15,23,42,0.28)]" role="dialog" aria-modal="true" aria-label={title} onMouseDown={(event) => event.stopPropagation()}>
        <div className="mx-auto mb-4 h-1.5 w-12 rounded-full bg-slate-200" />
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-xl font-semibold tracking-tight text-slate-900">{title}</h2>
          <button type="button" onClick={onClose} className="grid h-10 w-10 place-items-center rounded-full text-slate-400 hover:bg-slate-100" aria-label={`关闭${title}`}>
            <X size={19} />
          </button>
        </div>
        <div className="divide-y divide-slate-100">
          {items.map((item) => (
            <button key={item.id || "default"} type="button" onClick={() => onSelect(item.id)} className={`flex min-h-[72px] w-full items-center gap-3 py-3 text-left ${selectedId === item.id ? "text-blue-700" : "text-slate-800"}`}>
              <span className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl text-sm font-bold ${selectedId === item.id ? "bg-blue-600 text-white" : "bg-slate-100 text-slate-500"}`}>{item.label.slice(0, 1).toUpperCase()}</span>
              <span className="min-w-0 flex-1">
                <span className="flex items-center gap-2">
                  <strong className="truncate text-[15px]">{item.label}</strong>
                  {item.badge && <em className="rounded-md bg-violet-100 px-1.5 py-0.5 text-[10px] not-italic font-semibold text-violet-700">{item.badge}</em>}
                </span>
                {item.description && <small className="mt-1 block truncate text-[11px] font-normal text-slate-500">{item.description}</small>}
              </span>
              {selectedId === item.id && <Check size={22} className="shrink-0 text-emerald-500" />}
            </button>
          ))}
        </div>
      </section>
    </div>,
    document.body,
  );
}

function ContextMultiSelect({ icon, label, items, selectedIds, open, disabled, searchable = false, emptyText, onToggleOpen, onToggle }: { icon: ReactNode; label: string; items: ContextPickerItem[]; selectedIds: string[]; open: boolean; disabled?: boolean; searchable?: boolean; emptyText: string; onToggleOpen: () => void; onToggle: (id: string) => void }) {
  const [searchTerm, setSearchTerm] = useState("");
  const uniqueItems = useMemo(() => {
    const seen = new Set<string>();
    return items.filter((item) => {
      if (seen.has(item.id)) return false;
      seen.add(item.id);
      return true;
    });
  }, [items]);
  const visibleItems = useMemo(() => {
    const term = searchTerm.trim().toLocaleLowerCase();
    if (!term) return uniqueItems;
    return uniqueItems.filter((item) => `${item.label} ${item.description ?? ""}`.toLocaleLowerCase().includes(term));
  }, [searchTerm, uniqueItems]);
  const selectedLabel = selectedIds.length === 0 ? label : selectedIds.length === 1 ? (uniqueItems.find((item) => item.id === selectedIds[0])?.label ?? label) : `${label} · ${selectedIds.length}`;

  return (
    <div className="relative">
      <button type="button" disabled={disabled} onClick={onToggleOpen} className="inline-flex h-8 max-w-[164px] items-center gap-1.5 rounded-lg border border-transparent px-2 text-[12px] text-slate-600 transition hover:border-slate-200 hover:bg-slate-50 disabled:cursor-not-allowed disabled:text-slate-400" aria-haspopup="listbox" aria-expanded={open} title={selectedIds.length > 0 ? selectedIds.join(", ") : label}>
        <span className="shrink-0">{icon}</span>
        <span className="truncate">{selectedLabel}</span>
        <ChevronDown size={14} className={`ml-auto shrink-0 text-zinc-400 transition-transform ${open ? "rotate-180" : ""}`} />
      </button>

      {open && (
        <div className="absolute bottom-10 right-0 z-30 w-[290px] overflow-hidden rounded-2xl border border-slate-200 bg-white p-1.5 shadow-[0_18px_42px_rgba(15,23,42,0.18)]">
          <div className="flex items-center justify-between px-2.5 py-2 text-[11px] font-semibold text-zinc-500">
            <span>{label}</span>
            <span>{selectedIds.length}</span>
          </div>
          {searchable && (
            <label className="relative mx-1 mb-1.5 block">
              <Search size={13} className="pointer-events-none absolute left-2.5 top-2.5 text-zinc-400" />
              <input
                autoFocus
                value={searchTerm}
                onChange={(event) => setSearchTerm(event.target.value)}
                onPointerDown={(event) => event.stopPropagation()}
                placeholder="搜索项目名称或路径"
                aria-label="搜索项目名称或路径"
                className="h-8 w-full rounded-lg border border-zinc-200 bg-zinc-50 pl-8 pr-2 text-[12px] font-normal text-zinc-700 outline-none transition placeholder:text-zinc-400 focus:border-blue-300 focus:bg-white focus:ring-2 focus:ring-blue-100"
              />
            </label>
          )}
          <div className="max-h-56 overflow-y-auto">
            {uniqueItems.length === 0 ? (
              <p className="px-2.5 py-4 text-center text-[12px] leading-5 text-zinc-500">{emptyText}</p>
            ) : visibleItems.length === 0 ? (
              <p className="px-2.5 py-4 text-center text-[12px] leading-5 text-zinc-500">没有匹配的项目</p>
            ) : (
              visibleItems.map((item) => {
                const selected = selectedIds.includes(item.id);
                return (
                  <button
                    key={item.id}
                    type="button"
                    role="option"
                    aria-selected={selected}
                    onPointerDown={(event) => {
                      event.preventDefault();
                      event.stopPropagation();
                      onToggle(item.id);
                    }}
                    className={`flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-left transition ${selected ? "bg-blue-50 text-blue-950" : "hover:bg-zinc-50"}`}
                  >
                    <span className={`flex h-4 w-4 shrink-0 items-center justify-center rounded border ${selected ? "border-blue-600 bg-blue-600 text-white" : "border-zinc-300 bg-white"}`}>{selected && <Check size={11} strokeWidth={3} />}</span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-[12px] font-medium">{item.label}</span>
                      {item.description && <span className="mt-0.5 block truncate text-[10px] text-zinc-500">{item.description}</span>}
                    </span>
                  </button>
                );
              })
            )}
          </div>
        </div>
      )}
    </div>
  );
}

function toggleSelection(items: string[], value: string) {
  return items.includes(value) ? items.filter((item) => item !== value) : [...items, value];
}

function EmptyState({ title }: { title: string }) {
  return (
    <div className="flex flex-1 flex-col items-center justify-center pb-8 pt-6 text-center sm:pb-14">
      <div className="relative rounded-3xl border border-slate-200 bg-white/80 px-8 py-8 shadow-[0_18px_48px_rgba(15,23,42,0.06)] sm:px-14">
        <div className="mx-auto mb-5 grid h-12 w-12 place-items-center rounded-2xl bg-gradient-to-br from-blue-600 to-indigo-500 text-white shadow-lg shadow-blue-200">
          <Sparkles size={23} strokeWidth={1.8} />
        </div>
        <p className="mb-2 text-[11px] font-semibold tracking-[0.18em] text-blue-600">AIAGENT WORKSPACE</p>
        <h2 className="font-serif text-[30px] font-semibold tracking-normal text-slate-950 sm:text-[40px]">{title}</h2>
        <p className="mx-auto mt-3 max-w-md text-sm leading-6 text-slate-500">选择项目后，AI 会基于该项目下已登记的代码库协助你阅读、分析和修改代码。</p>
      </div>
    </div>
  );
}

function copyPlainText(value: string) {
  const plainText = value
    .replace(/```[^\n]*\n([\s\S]*?)```/g, "$1")
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, "$1")
    .replace(/\[([^\]]+)\]\([^)]*\)/g, "$1")
    .replace(/<[^>]*>/g, "")
    .replace(/(^|\n)#{1,6}\s+/g, "$1")
    .replace(/(^|\n)\s*(?:[-*+]\s+|\d+[.)]\s+)/g, "$1")
    .replace(/`([^`]+)`/g, "$1")
    .replace(/\*\*([^*]+)\*\*/g, "$1")
    .replace(/__([^_]+)__/g, "$1")
    .replace(/\r\n/g, "\n")
    .trim();
  return navigator.clipboard?.writeText(plainText);
}

function MessageBubble({ message, onRetry, onOpenCodeFile, onOpenProjectMarkdownDocument, onPreviewImage, projectId, showDebugTrace, onOpenDiagnostics }: { message: ChatMessage; onRetry?: () => void; onOpenCodeFile: (reference: ChatCodeFileReference) => void; onOpenProjectMarkdownDocument: (fileName: string) => void; onPreviewImage: (attachment: ChatImagePreview) => void; projectId: number | null; showDebugTrace: boolean; onOpenDiagnostics: () => void }) {
  const { t } = useI18n();
  const isUser = message.role === "user";
  const canCopy = Boolean(message.content.trim());
  const contentRef = useRef<HTMLDivElement | null>(null);
  const selectionRangeRef = useRef<Range | null>(null);
  const [selectionCopy, setSelectionCopy] = useState<{ text: string; top: number; left: number } | null>(null);
  const [copied, setCopied] = useState(false);

  useLayoutEffect(() => {
    if (!selectionCopy || !selectionRangeRef.current || !contentRef.current) return;
    const selection = window.getSelection();
    if (!selection) return;
    selection.removeAllRanges();
    selection.addRange(selectionRangeRef.current);
  }, [selectionCopy]);

  useEffect(() => {
    if (!selectionCopy) return;
    const dismiss = () => setSelectionCopy(null);
    document.addEventListener("pointerdown", dismiss);
    document.addEventListener("keydown", dismiss);
    return () => { document.removeEventListener("pointerdown", dismiss); document.removeEventListener("keydown", dismiss); };
  }, [selectionCopy]);

  function showSelectionCopyMenu() {
    const selection = window.getSelection();
    const text = selection?.toString().trim() ?? "";
    if (!text || !selection?.rangeCount || !contentRef.current?.contains(selection.anchorNode)) {
      setSelectionCopy(null);
      return;
    }
    const range = selection.getRangeAt(0);
    const rect = range.getBoundingClientRect();
    if (!rect.width && !rect.height) return;
    selectionRangeRef.current = range.cloneRange();
    setCopied(false);
    setSelectionCopy({ text, top: Math.max(8, rect.top - 42), left: Math.min(window.innerWidth - 104, Math.max(8, rect.left + rect.width / 2 - 44)) });
  }

  async function copySelection() {
    if (!selectionCopy) return;
    await navigator.clipboard?.writeText(selectionCopy.text);
    setCopied(true);
    window.setTimeout(() => setSelectionCopy(null), 900);
  }

  return (
    <article className={`flex ${isUser ? "justify-end" : "justify-start"}`}>
      <div className={`${isUser ? "max-w-[82%] rounded-2xl bg-blue-600 px-4 py-3 text-[13px] leading-6 text-white" : "w-full max-w-[860px] text-zinc-900"}`}>
        {!isUser && (
          <div className="mb-3 flex flex-wrap items-center gap-2 text-[12px] text-zinc-500">
            <span className={`font-semibold ${message.status === "error" ? "text-red-600" : "text-zinc-900"}`}>{message.status === "streaming" ? (message.agent === "codex" ? "Codex working" : "Working") : message.status === "stopped" ? "Stopped" : message.status === "error" ? "Error" : message.modificationStatus === "completed_changed" ? "Codex 已修改完成" : message.modificationStatus === "completed_no_change" ? "Codex 已完成（未修改文件）" : "Done"}</span>
            {message.elapsedSeconds ? <span>- {message.elapsedSeconds}s</span> : null}
            {message.iteration ? <span>- round {message.iteration}</span> : null}
            {message.totalTokens ? <span>- {formatCompactNumber(message.totalTokens)} tokens</span> : null}
            {message.llmCalls || message.toolCalls ? <span>- {(message.llmCalls ?? 0) + (message.toolCalls ?? 0)} calls</span> : null}
          </div>
        )}
        {isUser ? (
          <>
            <div className="whitespace-pre-wrap break-words">
              <InlineReferenceText value={message.content} />
            </div>
            {message.attachments && message.attachments.length > 0 && (
              <div className="mt-2 flex flex-wrap gap-2">
                {message.attachments.map((attachment) => (
                  <button key={attachment.id} type="button" onClick={() => onPreviewImage(attachment)} className="cursor-zoom-in rounded-lg focus:outline-none focus:ring-2 focus:ring-white/80" aria-label={`放大 ${attachment.file_name}`}>
                    <img src={attachment.previewUrl} alt={attachment.file_name} className="h-24 max-w-48 rounded-lg border border-white/30 object-cover" />
                  </button>
                ))}
              </div>
            )}
            {message.documentAttachments && message.documentAttachments.length > 0 && (
              <div className="mt-2 flex flex-wrap gap-1.5">
                {message.documentAttachments.map((attachment) => (
                  <span key={attachment.id} className="inline-flex max-w-full items-center gap-1 rounded-md bg-white/15 px-2 py-1 text-[11px] text-white">
                    <FileText size={12} className="shrink-0" />
                    <span className="max-w-44 truncate">{attachment.file_name}</span>
                    <span className="opacity-75">{attachment.extraction_status === "ready" ? "文本提取" : "需转换"}</span>
                  </span>
                ))}
              </div>
            )}
          </>
        ) : (
          <div ref={contentRef} onMouseUp={showSelectionCopyMenu} className="rounded-2xl border border-[var(--border)] bg-white px-5 py-4 text-[14px] shadow-sm">{message.content ? <MarkdownMessage content={humanizeInlineReferenceTokens(message.content)} projectId={projectId} onOpenCodeFile={onOpenCodeFile} onOpenProjectMarkdownDocument={onOpenProjectMarkdownDocument} /> : <div className="text-zinc-400">{t("chat.thinking")}</div>}</div>
        )}
        {!isUser && showDebugTrace && message.debugTrace && message.debugTrace.length > 0 && <DebugTraceTimeline events={message.debugTrace} />}
        {!isUser && ((message.trace && message.trace.length > 0) || message.thinking) && (
          <details className="mt-3 rounded-lg border border-zinc-200 bg-white p-2 text-[11px] text-zinc-500" open={!message.content}>
            <summary className="cursor-pointer select-none font-medium">{message.status === "streaming" ? t("chat.traceLive") : t("chat.traceHistory")}</summary>
            {message.trace && message.trace.length > 0 && (
              <div className="mt-2 space-y-1 border-l border-zinc-200 pl-3">
                {message.trace.slice(-12).map((item, index) => (
                  <div key={`${index}-${item}`} className="leading-5">
                    {item}
                  </div>
                ))}
              </div>
            )}
            {message.thinking && <div className="mt-2 whitespace-pre-wrap break-words border-t border-zinc-100 pt-2 leading-5">{message.thinking}</div>}
          </details>
        )}
        {!isUser && message.model && (
          <div className="mt-2 flex items-center gap-1.5 text-[11px] text-zinc-500">
            <UserRound size={12} />
            {message.model}
          </div>
        )}
        {!isUser && message.citations && message.citations.length > 0 && (
          <div className="mt-3 space-y-2 border-t border-zinc-200 pt-3">
            <div className="flex items-center gap-2 text-[11px] font-semibold text-zinc-600">
              <BookOpen size={13} />
              {t("chat.citations")}
            </div>
            {message.citations.slice(0, 5).map((citation, index) => (
              <CitationItem key={`${index}-${citation.score ?? "score"}`} citation={citation} index={index + 1} onOpenCodeFile={onOpenCodeFile} />
            ))}
          </div>
        )}
        {!isUser && (
          <div className="mt-3 flex items-center gap-1 text-zinc-500">
            <button type="button" onClick={onOpenDiagnostics} className="inline-flex h-7 w-7 items-center justify-center rounded-md hover:bg-amber-50 hover:text-amber-700" aria-label="查看诊断日志" title="查看诊断日志">
              <Activity size={14} />
            </button>
            {canCopy && (
              <button type="button" onClick={() => void copyPlainText(message.content)} className="inline-flex h-7 w-7 items-center justify-center rounded-md hover:bg-zinc-100" aria-label="复制纯文本" title="复制纯文本">
                <Copy size={14} />
              </button>
            )}
            {onRetry && (
              <button type="button" onClick={onRetry} className="inline-flex h-7 w-7 items-center justify-center rounded-md hover:bg-zinc-100" aria-label={String(t("common.retry"))}>
                <RefreshCw size={14} />
              </button>
            )}
          </div>
        )}
      </div>
      {selectionCopy && <div style={{ top: selectionCopy.top, left: selectionCopy.left }} className="fixed z-[90] rounded-lg bg-slate-900 p-1 shadow-lg"><button type="button" onMouseDown={(event) => { event.preventDefault(); event.stopPropagation(); }} onClick={() => void copySelection()} className="inline-flex h-8 items-center gap-1.5 rounded-md px-2.5 text-xs font-medium text-white hover:bg-slate-700">{copied ? <Check size={14}/> : <Copy size={14}/>} {copied ? "已复制" : "复制"}</button></div>}
    </article>
  );
}

function DebugTraceTimeline({ events }: { events: ChatDebugTraceEvent[] }) {
  const latestByStage = new Map<string, ChatDebugTraceEvent>();
  for (const event of events) latestByStage.set(event.stage, event);
  return (
    <details className="mt-3 rounded-lg border border-amber-200 bg-amber-50/50 p-2 text-[11px] text-slate-600" open>
      <summary className="cursor-pointer select-none font-medium text-amber-900">诊断时间线（仅当前请求）</summary>
      <div className="mt-2 space-y-1 border-l border-amber-200 pl-3">
        {[...latestByStage.values()].map((event) => (
          <div key={`${event.trace_id}-${event.stage}`} className="flex flex-wrap items-center gap-x-2 leading-5">
            <span className={event.status === "failed" ? "text-rose-700" : event.status === "cancelled" ? "text-amber-700" : "text-slate-700"}>{debugStageLabel(event.stage)}</span>
            <span className="tabular-nums text-slate-500">
              {event.elapsed_ms}ms
              {event.duration_ms == null ? "" : `（${event.duration_ms}ms）`}
            </span>
            <span className="text-slate-400">
              {event.provider} / {event.transport}
            </span>
            {event.error_code && <span className="text-rose-600">{event.error_code}</span>}
          </div>
        ))}
      </div>
    </details>
  );
}

function debugStageLabel(stage: string) {
  const labels: Record<string, string> = {
    browser_submit: "浏览器提交",
    backend_received: "后端接收",
    auth_session_context: "鉴权与上下文",
    request_started: "请求开始",
    provider_request_started: "模型请求",
    first_stream_event: "首个流事件",
    provider_completed: "模型完成",
    persistence: "写入会话",
    frontend_push: "推送前端",
    request_completed: "请求结束",
  };
  return labels[stage] ?? "诊断阶段";
}

function ChatDiagnosticsDialog({ traces, loading, onClose }: { traces: ChatDebugTraceRecord[]; loading: boolean; onClose: () => void }) {
  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-slate-950/45 p-4 backdrop-blur-sm" role="presentation" onMouseDown={onClose}>
      <section className="flex max-h-[80vh] w-full max-w-2xl flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl" role="dialog" aria-modal="true" aria-label="聊天诊断日志" onMouseDown={(event) => event.stopPropagation()}>
        <header className="flex items-center gap-2 border-b border-slate-100 px-4 py-3">
          <span className="grid h-8 w-8 place-items-center rounded-lg bg-amber-50 text-amber-700">
            <Activity size={16} />
          </span>
          <div className="min-w-0 flex-1">
            <h2 className="text-sm font-semibold text-slate-900">聊天诊断日志</h2>
            <p className="mt-0.5 text-[11px] text-slate-500">仅保存脱敏耗时靶点，默认保留 7 天。</p>
          </div>
          <button type="button" onClick={onClose} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="关闭诊断日志">
            <X size={17} />
          </button>
        </header>
        <div className="workspace-scroll min-h-0 flex-1 overflow-y-auto p-4">
          {loading ? (
            <div className="flex min-h-32 items-center justify-center gap-2 text-sm text-slate-500">
              <Loader2 size={16} className="animate-spin" />
              正在读取诊断日志…
            </div>
          ) : traces.length === 0 ? (
            <p className="py-10 text-center text-sm text-slate-500">当前会话没有可用的诊断日志。请先开启 Debug 后发送消息。</p>
          ) : (
            <div className="space-y-4">
              {traces.map((trace) => (
                <section key={trace.trace_id} className="rounded-xl border border-slate-200 bg-slate-50/60 p-3">
                  <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                    <span className="text-xs font-semibold text-slate-800">{new Date(trace.created_at).toLocaleString("zh-CN")}</span>
                    <span className="rounded-full bg-white px-2 py-0.5 text-[10px] text-slate-500">
                      {trace.provider} / {trace.transport}
                    </span>
                  </div>
                  <div className="space-y-1 border-l border-slate-200 pl-3">
                    {trace.events.map((event, index) => (
                      <div key={`${event.stage}-${index}`} className="flex flex-wrap items-center gap-x-2 text-[11px] leading-5">
                        <span className={event.status === "failed" ? "text-rose-700" : event.status === "cancelled" ? "text-amber-700" : "text-slate-700"}>{debugStageLabel(event.stage)}</span>
                        <span className="tabular-nums text-slate-500">
                          {event.elapsed_ms}ms
                          {event.duration_ms == null ? "" : `（${event.duration_ms}ms）`}
                        </span>
                        {event.error_code && <span className="text-rose-600">{event.error_code}</span>}
                      </div>
                    ))}
                  </div>
                </section>
              ))}
            </div>
          )}
        </div>
      </section>
    </div>
  );
}

function ImageLightbox({ attachment, onClose }: { attachment: ChatImagePreview; onClose: () => void }) {
  const [zoom, setZoom] = useState(1);

  useEffect(() => {
    setZoom(1);
  }, [attachment.id]);

  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-slate-950/75 p-4 backdrop-blur-sm" role="presentation" onMouseDown={onClose}>
      <section className="w-[calc(100%-2rem)] max-w-5xl rounded-2xl border border-white/20 bg-slate-900 p-3 shadow-2xl" role="dialog" aria-modal="true" aria-label={`查看图片：${attachment.file_name}`} onMouseDown={(event) => event.stopPropagation()}>
        <div className="mb-3 flex items-center gap-2 text-white">
          <p className="min-w-0 flex-1 truncate text-sm font-medium">{attachment.file_name}</p>
          <button type="button" onClick={() => setZoom((value) => Math.max(1, value - 0.25))} disabled={zoom <= 1} className="grid h-8 w-8 place-items-center rounded-lg bg-white/10 transition hover:bg-white/20 disabled:opacity-40" aria-label="缩小图片">
            <ZoomOut size={16} />
          </button>
          <span className="w-10 text-center text-xs tabular-nums text-slate-300">{Math.round(zoom * 100)}%</span>
          <button type="button" onClick={() => setZoom((value) => Math.min(3, value + 0.25))} disabled={zoom >= 3} className="grid h-8 w-8 place-items-center rounded-lg bg-white/10 transition hover:bg-white/20 disabled:opacity-40" aria-label="放大图片">
            <ZoomIn size={16} />
          </button>
          <button type="button" onClick={onClose} className="ml-1 grid h-8 w-8 place-items-center rounded-lg bg-white/10 transition hover:bg-white/20" aria-label="关闭图片预览">
            <X size={17} />
          </button>
        </div>
        <div className="max-h-[calc(100vh-10rem)] overflow-auto rounded-xl bg-black/30">
          <img src={attachment.previewUrl} alt={attachment.file_name} style={{ width: `${zoom * 100}%`, maxWidth: "none" }} className="block h-auto min-w-full rounded-xl" />
        </div>
      </section>
    </div>
  );
}

function CitationItem({ citation, index, onOpenCodeFile }: { citation: KnowledgeCitation; index: number; onOpenCodeFile: (reference: ChatCodeFileReference) => void }) {
  const source = formatCitationSource(citation.metadata);
  const reference = resolveCodeFileReference(citation);
  return (
    <button type="button" disabled={!reference} onClick={() => reference && onOpenCodeFile(reference)} className={`block w-full rounded-lg border p-2 text-left transition ${reference ? "border-blue-200 bg-blue-50/40 hover:border-blue-400 hover:bg-blue-50" : "cursor-default border-zinc-200 bg-white"}`} title={reference ? "Open in the right file panel" : undefined}>
      <div className="mb-1 flex items-center justify-between gap-2 text-[11px] text-zinc-500">
        <span>
          #{index} {source}
        </span>
        <span className="flex items-center gap-1">
          {reference && <FileCode2 size={12} className="text-blue-600" />}
          {typeof citation.score === "number" && <span>{citation.score.toFixed(3)}</span>}
        </span>
      </div>
      <p className="line-clamp-3 text-[12px] leading-5 text-zinc-700">{citation.text}</p>
    </button>
  );
}

function resolveCodeFileReference(citation: KnowledgeCitation): ChatCodeFileReference | null {
  const metadata = citation.metadata;
  if (!metadata) return null;
  const repositoryName = stringifyMeta(metadata.repository_name) || stringifyMeta(metadata.code_repository_name) || stringifyMeta(metadata.repository) || stringifyMeta(metadata.repo_name);
  const filePath = stringifyMeta(metadata.file_path) || stringifyMeta(metadata.relative_path) || stringifyMeta(metadata.source_path);
  if (!repositoryName || !filePath) return null;
  const rawLine = Number(metadata.line ?? metadata.line_number ?? metadata.start_line);
  return {
    repositoryName,
    filePath,
    line: Number.isFinite(rawLine) && rawLine > 0 ? rawLine : undefined,
  };
}

function resolveLlmModels(catalog: Catalog | null): CatalogModel[] {
  if (!catalog) return [];
  const profile = activeProfile(catalog, "llm");
  return profile?.models ?? [];
}

function formatCitationSource(metadata?: Record<string, unknown> | null) {
  if (!metadata) return "chunk";
  const fileName = stringifyMeta(metadata.file_name) || stringifyMeta(metadata.file_path) || "chunk";
  const page = stringifyMeta(metadata.page_label);
  return page ? `${fileName} p.${page}` : fileName;
}

function stringifyMeta(value: unknown) {
  if (value === null || value === undefined) return "";
  return String(value);
}

function extractRunStats(metadata: Record<string, unknown>): Partial<ChatMessage> {
  return {
    iteration: numberMeta(metadata.iteration),
    llmCalls: numberMeta(metadata.llm_calls),
    toolCalls: numberMeta(metadata.tool_calls),
    totalTokens: numberMeta(metadata.total_tokens),
    elapsedSeconds: numberMeta(metadata.elapsed_seconds),
  };
}

function numberMeta(value: unknown) {
  return typeof value === "number" && Number.isFinite(value) ? value : undefined;
}

function formatCompactNumber(value: number) {
  if (value >= 1000) return `${(value / 1000).toFixed(value >= 10000 ? 1 : 2)}k`;
  return String(value);
}

function findPreviousUserMessage(messages: ChatMessage[], assistantIndex: number) {
  for (let index = assistantIndex - 1; index >= 0; index--) {
    if (messages[index]?.role === "user") return messages[index];
  }
  return null;
}

function appendTrace(trace: string[] | undefined, item: string | null) {
  if (!item) return trace;
  const next = [...(trace ?? [])];
  if (next[next.length - 1] !== item) {
    next.push(item);
  }
  return next.slice(-40);
}

function formatTraceEvent(event: ChatStreamEvent, translate: (key: TranslationKey, params?: Record<string, string | number>) => string) {
  const metadata = event.metadata ?? {};
  if (event.type === "loop") {
    const iteration = numberMeta(metadata.iteration);
    return iteration ? translate("chat.traceRound", { iteration }) : translate("chat.traceNewRound");
  }

  if (event.type === "label") {
    if (event.label === "TOOL") return translate("chat.traceToolReady");
    if (event.label === "FINISH") return translate("chat.traceFinal");
    if (event.label === "THINK") return translate("chat.traceThinking");
  }

  if (event.type === "tool") {
    return event.content || translate("chat.traceToolRunning");
  }

  if (event.type === "tool_request") {
    return translate("chat.traceToolRequest");
  }

  if (event.type === "tool_result") {
    if (event.content) return event.content;
    const citationCount = numberMeta(metadata.citation_count);
    return citationCount !== undefined ? translate("chat.traceToolResultWithCount", { count: citationCount }) : translate("chat.traceToolResult");
  }

  if (event.type === "thinking") {
    return event.content ? translate("chat.traceThinkingEvent") : translate("chat.traceThinkingModel");
  }

  return null;
}
