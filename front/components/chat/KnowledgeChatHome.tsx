"use client";

import { type FormEvent, type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useRouter, useSearchParams } from "next/navigation";
import { ArrowUp, BookOpen, Bot, Braces, Check, ChevronDown, Copy, Database, FileCode2, Globe2, ImagePlus, ListTodo, Loader2, Menu, Mic, PanelRight, Plus, RefreshCw, Sparkles, Square, Terminal, UserRound, X, ZoomIn, ZoomOut } from "lucide-react";
import { deleteChatImage, persistedChatImageUrl, uploadChatImage, type ChatImageAttachment, type ChatStreamEvent } from "@/lib/chat-api";
import { useChatStreams, type ChatStreamRecord } from "@/components/chat/ChatStreamProvider";
import { MarkdownMessage } from "@/components/chat/MarkdownMessage";
import { ChatInspectorPanel, type ChatCodeFileReference } from "@/components/chat/ChatInspectorPanel";
import { ChatRuntimeToolbar } from "@/components/chat/ChatRuntimeToolbar";
import { ClientScanDialog } from "@/components/chat/ClientScanDialog";
import { getSettings } from "@/lib/api";
import { getKnowledgeBases } from "@/lib/knowledge-api";
import { getCodeProjects } from "@/lib/code-repository-api";
import { activeModel, activeProfile, type Catalog, type CatalogModel } from "@/lib/settings-types";
import type { TranslationKey } from "@/i18n/dictionaries";
import type { KnowledgeBase, KnowledgeCitation } from "@/lib/knowledge-types";
import type { CodeProject } from "@/lib/code-repository-types";
import { useI18n } from "@/i18n/I18nProvider";
import { getSession, type SessionDetail } from "@/lib/session-api";
import { getAgentProviderEnvironments, getCodexModelPolicy, getImageOcrPolicy, type AgentProviderEnvironment, type CodexModelPolicy, type ImageOcrPolicy } from "@/lib/agent-provider-api";

type InspectorTab = "preview" | "file" | "tasks" | "terminal";

type ChatImagePreview = ChatImageAttachment & { previewUrl: string };

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
  agent?: "codex" | "codebuddy";
  modificationStatus?: string;
  attachments?: ChatImagePreview[];
};

function createClientId(): string {
  return globalThis.crypto?.randomUUID?.()
    ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

function toHistoryMessages(session: SessionDetail): ChatMessage[] {
  return session.messages.map((message) => ({
    id: String(message.id), role: message.role, content: message.content, thinking: message.thinking ?? undefined,
    citations: message.citations ?? undefined, model: message.metadata?.model ?? null,
    attachments: message.metadata?.attachments?.map((attachment) => ({ ...attachment, previewUrl: persistedChatImageUrl(session.id, attachment.id) })),
    status: "done",
  }));
}

function applyStreamEvent(message: ChatMessage, event: ChatStreamEvent, t: (key: TranslationKey) => string): ChatMessage {
  const stats = event.metadata ? extractRunStats(event.metadata) : {};
  if (event.type === "label") return { ...message, label: event.label ?? null, trace: appendTrace(message.trace, formatTraceEvent(event, t)), ...stats };
  if (event.type === "thinking") return { ...message, thinking: `${message.thinking ?? ""}${event.content ?? ""}`, trace: appendTrace(message.trace, formatTraceEvent(event, t)), ...stats };
  if (event.type === "content") return { ...message, content: `${message.content}${event.content ?? ""}`, model: event.model ?? message.model, ...stats };
  if (event.type === "loop" || event.type === "tool" || event.type === "tool_result" || event.type === "tool_request") return { ...message, trace: appendTrace(message.trace, formatTraceEvent(event, t)), ...stats };
  if (event.type === "sources" || event.type === "done") return { ...message, content: message.content || event.content || "", citations: event.citations ?? message.citations, model: event.model ?? message.model, modificationStatus: stringifyMeta(event.metadata?.modification_status) || message.modificationStatus, label: event.label ?? message.label, status: event.type === "done" ? "done" : message.status, elapsedSeconds: stats.elapsedSeconds ?? Math.max(1, Math.round((Date.now() - (message.startedAt ?? Date.now())) / 1000)), ...stats };
  if (event.type === "error") return { ...message, content: message.content || event.content || t("chat.searchFailed"), status: "error" };
  return message;
}

function mergeStreamMessages(items: ChatMessage[], streams: ChatStreamRecord[], t: (key: TranslationKey) => string): ChatMessage[] {
  const next = [...items];
  for (const stream of streams) {
    const id = `stream:${stream.id}`;
    const initial: ChatMessage = { id, role: "assistant", content: "", thinking: "", trace: [], label: null, status: "streaming", startedAt: stream.startedAt, agent: stream.agent };
    const message = stream.events.reduce((current, event) => applyStreamEvent(current, event, t), initial);
    const candidate = { ...message, status: stream.status === "streaming" ? message.status : stream.status, content: stream.status === "done" ? message.content.trim() || t("chat.emptyAnswer") : message.content, elapsedSeconds: message.elapsedSeconds ?? (stream.status === "streaming" ? undefined : Math.max(1, Math.round((Date.now() - stream.startedAt) / 1000))) };
    const index = next.findIndex((item) => item.id === id);
    if (index >= 0) next[index] = { ...next[index], ...candidate };
    else {
      const alreadyPersisted = stream.status === "done" && candidate.content.trim().length > 0 && next.some((item) => item.role === "assistant" && item.status === "done" && item.content.trim() === candidate.content.trim());
      if (!alreadyPersisted) next.push(candidate);
    }
  }
  return next;
}

export function KnowledgeChatHome() {
  const { t } = useI18n();
  const router = useRouter();
  const searchParams = useSearchParams();
  const requestedSessionId = searchParams.get("session");
  const requestedTemplateHandoff = searchParams.get("template_handoff");
  const requestedProjectId = Number(searchParams.get("project")) || null;
  const [knowledgeBases, setKnowledgeBases] = useState<KnowledgeBase[]>([]);
  const [codeProjects, setCodeProjects] = useState<CodeProject[]>([]);
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [selectedKbNames, setSelectedKbNames] = useState<string[]>([]);
  const [selectedProjectId, setSelectedProjectId] = useState<number | null>(requestedProjectId);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(requestedSessionId);
  const [openContextPicker, setOpenContextPicker] = useState<"knowledge" | "project" | null>(null);
  const [selectedModelId, setSelectedModelId] = useState("");
  const [selectedCodexModelId, setSelectedCodexModelId] = useState("");
  const [selectedCodexReasoningEffort, setSelectedCodexReasoningEffort] = useState("");
  const [selectedAgentId, setSelectedAgentId] = useState<"codex" | "codebuddy" | "">("");
  const [agentProviders, setAgentProviders] = useState<AgentProviderEnvironment[]>([]);
  const [codexModelPolicy, setCodexModelPolicy] = useState<CodexModelPolicy | null>(null);
  const [imageOcrPolicy, setImageOcrPolicy] = useState<ImageOcrPolicy | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [composerExpanded, setComposerExpanded] = useState(false);
  const [mobilePicker, setMobilePicker] = useState<"model" | "project" | null>(null);
  const [imageAttachments, setImageAttachments] = useState<ChatImagePreview[]>([]);
  const [previewingImage, setPreviewingImage] = useState<ChatImagePreview | null>(null);
  const [uploadingImages, setUploadingImages] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [rightPanelOpen, setRightPanelOpen] = useState(false);
  const [requestedInspectorTab, setRequestedInspectorTab] = useState<InspectorTab | null>(null);
  const [fileReference, setFileReference] = useState<ChatCodeFileReference | null>(null);
  const bottomRef = useRef<HTMLDivElement | null>(null);
  const contextPickerRef = useRef<HTMLDivElement | null>(null);
  const imageFileInputRef = useRef<HTMLInputElement | null>(null);
  const pendingSessionIdRef = useRef<string | null>(null);
  const activeSessionIdRef = useRef<string | null>(activeSessionId);
  const attachmentPreviewUrlsRef = useRef(new Set<string>());
  const { streams, startStream, cancelStream, markSessionViewed, clearFinishedStreams, activateCodexRuntime } = useChatStreams();

  const readyKnowledgeBases = useMemo(
    () => knowledgeBases.filter((kb) => kb.active_version_id && kb.status !== "error"),
    [knowledgeBases],
  );
  const llmModels = useMemo(() => resolveLlmModels(catalog), [catalog]);
  const currentKnowledgeBase = readyKnowledgeBases.find((kb) => kb.name === selectedKbNames[0]);
  const selectedProject = codeProjects.find((project) => project.id === selectedProjectId) ?? null;
  const selectedCodeRepositoryNames = selectedProject?.repositories.map((repository) => repository.name) ?? [];
  const currentModel = llmModels.find((model) => model.id === selectedModelId) ?? llmModels[0] ?? null;
  const codexModels = codexModelPolicy?.models.filter((model) => codexModelPolicy.allowed_model_ids.includes(model.id)) ?? [];
  const currentCodexModel = codexModels.find((model) => model.id === selectedCodexModelId) ?? codexModels[0] ?? null;
  const imageInput = selectedAgentId === "codex" ? currentCodexModel?.image_input ?? "none" : "none";
  const canAttachImages = imageInput === "native"
    ? imageOcrPolicy?.native_image_input_enabled === true
    : imageInput === "ocr" && imageOcrPolicy?.enabled === true;
  const imageAttachmentHint = imageInput === "native"
    ? "原生 Codex 原图识图"
    : imageInput === "ocr"
      ? "第三方 Profile 使用本地 PaddleOCR 识别图片文字"
      : "当前模型未启用图片识别";
  const codexReasoningEfforts = currentCodexModel?.supports_reasoning_effort ? codexModelPolicy?.allowed_reasoning_efforts ?? [] : [];
  const mobileModelLabel = selectedAgentId === "codex"
    ? `${currentCodexModel?.name ?? "Auto"}${currentCodexModel?.supports_reasoning_effort && selectedCodexReasoningEffort ? ` · ${codexReasoningEffortLabel(selectedCodexReasoningEffort)}` : ""}`
    : currentModel?.name || currentModel?.model || "Auto";
  const selectedAgentProvider = agentProviders.find((provider) => provider.id === selectedAgentId) ?? null;
  const sessionStreams = useMemo(() => Object.values(streams).filter((stream) => stream.sessionId === activeSessionId), [activeSessionId, streams]);
  const displayMessages = useMemo(() => mergeStreamMessages(messages, sessionStreams, t), [messages, sessionStreams, t]);
  const sending = sessionStreams.some((stream) => stream.status === "streaming");

  useEffect(() => {
    void loadBootstrap();
  }, []);

  useEffect(() => {
    void getAgentProviderEnvironments().then(setAgentProviders).catch(() => setAgentProviders([]));
    void getCodexModelPolicy().then((policy) => {
      setCodexModelPolicy(policy);
      setSelectedCodexModelId((current) => current || policy.default_model_id);
      setSelectedCodexReasoningEffort((current) => current || policy.default_reasoning_effort);
    }).catch(() => setCodexModelPolicy(null));
    void getImageOcrPolicy().then(setImageOcrPolicy).catch(() => setImageOcrPolicy(null));
  }, []);

  useEffect(() => {
    if (agentProviders.length === 0 || !selectedAgentId) return;
    const selected = agentProviders.find((provider) => provider.id === selectedAgentId);
    if (selected?.chat_supported) return;
    setSelectedAgentId(agentProviders.find((provider) => provider.id === "codex" && provider.chat_supported) ? "codex" : "");
  }, [agentProviders, selectedAgentId]);

  useEffect(() => {
    if (selectedAgentId === "codex" && selectedProjectId) activateCodexRuntime(selectedProjectId, selectedCodexModelId || undefined, currentCodexModel?.supports_reasoning_effort ? selectedCodexReasoningEffort || undefined : undefined);
  }, [activateCodexRuntime, currentCodexModel?.supports_reasoning_effort, selectedAgentId, selectedCodexModelId, selectedCodexReasoningEffort, selectedProjectId]);

  useEffect(() => {
    if (!requestedSessionId) setSelectedProjectId(requestedProjectId);
  }, [requestedProjectId, requestedSessionId]);

  useEffect(() => {
    if (requestedSessionId || !requestedTemplateHandoff) return;
    const raw = sessionStorage.getItem("aiagent:pending-template-turn");
    if (!raw) return;
    try {
      const pending = JSON.parse(raw) as { handoff_id?: string; content?: string; project_id?: number | null };
      if (pending.handoff_id !== requestedTemplateHandoff) return;
      if (typeof pending.content === "string" && pending.content.trim()) setInput(pending.content);
      if (typeof pending.project_id === "number") setSelectedProjectId(pending.project_id);
    } catch {
      // Invalid browser-local handoff data is ignored rather than being sent to the chat API.
      sessionStorage.removeItem("aiagent:pending-template-turn");
    }
  }, [requestedSessionId, requestedTemplateHandoff]);

  useEffect(() => { activeSessionIdRef.current = activeSessionId; }, [activeSessionId]);

  useEffect(() => {
    let cancelled = false;
    if (!requestedSessionId) {
      setActiveSessionId(null);
      setMessages([]);
      return;
    }
    if (requestedSessionId === pendingSessionIdRef.current) {
      pendingSessionIdRef.current = null;
      return;
    }
    void getSession(requestedSessionId).then((session) => {
      if (cancelled) return;
      setActiveSessionId(session.id);
      setSelectedProjectId(session.project_id ?? null);
      setMessages(toHistoryMessages(session));
      clearFinishedStreams(session.id);
    }).catch((ex) => { if (!cancelled) setError(ex instanceof Error ? ex.message : t("chat.errorLoadKnowledge")); });
    return () => { cancelled = true; };
  }, [clearFinishedStreams, requestedSessionId, t]);

  useEffect(() => {
    const refreshCompletedSession = (event: Event) => {
      const sessionId = (event as CustomEvent<{ sessionId?: string }>).detail?.sessionId;
      if (!sessionId || sessionId !== activeSessionIdRef.current) return;
      window.setTimeout(() => {
        void getSession(sessionId).then((session) => {
          if (activeSessionIdRef.current !== session.id) return;
          setSelectedProjectId(session.project_id ?? null);
          setMessages(toHistoryMessages(session));
          clearFinishedStreams(session.id);
        }).catch(() => {
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

  async function sendMessage(query: string, options?: { retryAssistantId?: string; attachments?: ChatImagePreview[] }) {
    const attachmentsForTurn = options?.attachments ?? imageAttachments;
    if ((!query && attachmentsForTurn.length === 0) || sending || uploadingImages) return;
    if (selectedAgentId && selectedAgentProvider && !selectedAgentProvider.chat_supported) {
      setError(`${selectedAgentProvider.name} 已检测到，但当前版本尚未适配聊天接管协议。`);
      return;
    }
    if (selectedAgentId && !selectedProjectId) {
      setError("本地代理接管需要先选择项目，以便传递项目目录。");
      return;
    }

    if (attachmentsForTurn.length > 0 && selectedAgentId !== "codex") {
      setError("图片附件目前只能发送给 Codex 本地代理。");
      return;
    }
    if (attachmentsForTurn.length > 0 && !canAttachImages) {
      setError("当前 Codex 模型未启用图片识别，请切换模型或在第三方代理设置中开启相应图片能力。");
      return;
    }

    const messageText = query || "请分析我附上的图片。";

    const sessionId = activeSessionId ?? createClientId();
    if (!activeSessionId) {
      pendingSessionIdRef.current = sessionId;
      setActiveSessionId(sessionId);
      router.replace(`/chat?session=${encodeURIComponent(sessionId)}`);
    }

    if (!options?.retryAssistantId) {
      setMessages((items) => [
        ...items,
        {
          id: createClientId(),
          role: "user",
          content: messageText,
          attachments: attachmentsForTurn,
        },
      ]);
      setInput("");
      setComposerExpanded(false);
      setImageAttachments([]);
    }

    setError(null);
    const assistantId = options?.retryAssistantId ?? createClientId();
    const startedAt = Date.now();
    if (options?.retryAssistantId) {
      setMessages((items) => items.map((message) => (
        message.id === assistantId
          ? { ...message, content: "", thinking: "", trace: [], citations: undefined, label: null, status: "streaming", startedAt }
          : message
      )));
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
        model_id: selectedAgentId === "codex" ? undefined : selectedModelId || undefined,
        codex_model_id: selectedAgentId === "codex" ? selectedCodexModelId || undefined : undefined,
        codex_reasoning_effort: selectedAgentId === "codex" && currentCodexModel?.supports_reasoning_effort ? selectedCodexReasoningEffort || undefined : undefined,
        top_k: 6,
        mode: "chat",
        agent: selectedAgentId || undefined,
        attachment_ids: attachmentsForTurn.map((attachment) => attachment.id),
      });
      setMessages((items) => {
        const streamMessageId = `stream:${streamId}`;
        return items.some((message) => message.id === streamMessageId)
          ? items.filter((message) => message.id !== assistantId)
          : items.map((message) => message.id === assistantId ? { ...message, id: streamMessageId } : message);
      });
      window.dispatchEvent(new Event("aiagent:sessions-updated"));
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("chat.errorSearch"));
      setMessages((items) => items.map((message) => (
        message.id === assistantId
          ? { ...message, content: message.content || t("chat.searchFailed"), status: "error" }
          : message
      )));
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

  return (
    <main className="flex h-full min-h-0 flex-col overflow-hidden bg-[radial-gradient(circle_at_50%_-20%,#eff6ff_0,transparent_38%),#f8fafc]">
      <header className="relative z-[80] flex h-14 shrink-0 items-center justify-between border-b border-slate-200/80 bg-white/75 px-3 backdrop-blur-xl lg:h-16 lg:px-5">
        <div className="flex min-w-0 items-center gap-2 lg:gap-3">
          <button type="button" onClick={() => window.dispatchEvent(new Event("aiagent:mobile-drawer-toggle"))} className="grid h-10 w-10 place-items-center rounded-xl text-slate-600 hover:bg-slate-100 lg:hidden" aria-label="打开工作台抽屉"><Menu size={20}/></button>
          <div className="hidden h-8 w-8 place-items-center rounded-lg bg-blue-50 text-blue-600 lg:grid"><Sparkles size={16}/></div>
          <div className="min-w-0"><h1 className="truncate text-sm font-semibold text-slate-950">{activeSessionId ? "当前会话" : t("chat.newChat")}</h1><p className="hidden text-[11px] text-slate-400 lg:block">AI 工作台</p></div>
          {currentKnowledgeBase && (
            <span className="hidden rounded-full bg-emerald-50 px-2 py-1 text-[11px] text-emerald-700 sm:inline-flex">
              {currentKnowledgeBase.display_name || currentKnowledgeBase.name}
            </span>
          )}
          {selectedProject && <span className="hidden items-center gap-1 rounded-full border border-blue-100 bg-blue-50 px-2 py-1 text-[11px] text-blue-700 sm:inline-flex"><Braces size={12}/>{selectedProject.display_name}</span>}
        </div>
        <div className="flex shrink-0 items-center gap-1.5 lg:gap-2">
          <ClientScanDialog />
          <ChatRuntimeToolbar project={selectedProject} rightPanelOpen={rightPanelOpen} onToggleRightPanel={() => setRightPanelOpen((current) => !current)} onOpenRuntimePanel={() => openInspector("terminal")}/>
          <span className="hidden lg:contents"><SidePanelTabLauncher onOpen={openInspector}/></span>
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
                    onRetry={message.role === "assistant" ? () => {
                      const userMessage = findPreviousUserMessage(displayMessages, index);
                      if (userMessage) void sendMessage(userMessage.content, { retryAssistantId: message.id, attachments: userMessage.attachments });
                    } : undefined}
                    onOpenCodeFile={openCodeFile}
                    projectId={selectedProjectId}
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

          <form onSubmit={handleSubmit} onPaste={(event) => {
            const imagesFromItems = Array.from(event.clipboardData.items)
              .filter((item) => item.kind === "file" && item.type.startsWith("image/"))
              .map((item) => item.getAsFile())
              .filter((file): file is File => file !== null);
            const images = imagesFromItems.length > 0
              ? imagesFromItems
              : Array.from(event.clipboardData.files).filter((file) => file.type.startsWith("image/"));
            if (images.length > 0) {
              event.preventDefault();
              void addImages(images);
            }
          }} className={`sticky bottom-0 mt-auto rounded-[24px] border border-slate-200 bg-white/95 px-2 py-2 shadow-[0_18px_46px_rgba(15,23,42,0.12)] backdrop-blur-xl transition focus-within:border-blue-300 focus-within:shadow-[0_20px_52px_rgba(37,99,235,0.15)] lg:bottom-4 lg:rounded-2xl lg:px-4 lg:py-3 ${composerExpanded ? "lg:rounded-2xl" : ""}`}>
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
            <div className="flex items-center gap-1 lg:hidden">
              <button type="button" className="grid h-9 w-9 shrink-0 place-items-center rounded-xl text-blue-700 hover:bg-blue-50" aria-label={t("chat.voiceInput")}><Mic size={18}/></button>
              <button type="button" onClick={() => setMobilePicker("model")} className="flex h-9 max-w-[102px] shrink-0 items-center gap-1 rounded-xl px-1.5 text-[12px] text-slate-600 hover:bg-slate-100" aria-label="选择模型"><span className="truncate">{mobileModelLabel}</span><ChevronDown size={13} className="shrink-0"/></button>
              <textarea value={input} onFocus={() => setComposerExpanded(true)} onChange={(event) => setInput(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter" && !event.shiftKey) { event.preventDefault(); event.currentTarget.form?.requestSubmit(); } }} placeholder="发消息或按住说话" className={`min-w-0 flex-1 resize-none bg-transparent px-1 py-2 text-[14px] leading-5 text-slate-800 outline-none placeholder:text-slate-400 ${composerExpanded ? "min-h-[52px]" : "h-9 min-h-9"}`} aria-label="聊天输入" />
              <button type="button" onClick={() => imageFileInputRef.current?.click()} disabled={sending || uploadingImages || !canAttachImages} title={imageAttachmentHint} className="grid h-9 w-9 shrink-0 place-items-center rounded-xl border border-slate-200 bg-white text-slate-700 disabled:cursor-not-allowed disabled:opacity-40" aria-label={t("chat.addAttachment")}>{uploadingImages ? <Loader2 size={17} className="animate-spin" /> : <Plus size={20}/>}</button>
              {composerExpanded && (sending ? <button type="button" onClick={stopGenerating} className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-rose-600 text-white" aria-label="停止生成"><Square size={14} fill="currentColor"/></button> : <button type="submit" disabled={!input.trim()} className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-blue-600 text-white disabled:bg-slate-300" aria-label={t("chat.send")}><ArrowUp size={17}/></button>)}
            </div>
            {composerExpanded && <div className="mt-2 flex gap-2 border-t border-slate-100 pt-2 lg:hidden">
              <button type="button" onClick={() => setMobilePicker("project")} className="flex min-w-0 flex-1 items-center gap-1 rounded-xl bg-slate-100 px-2.5 text-left text-[12px] text-slate-600"><Braces size={14} className="shrink-0 text-blue-600"/><span className="min-w-0 flex-1 truncate">{selectedProject?.display_name || "选择项目"}</span><ChevronDown size={13} className="shrink-0"/></button>
              <label className="flex min-w-0 flex-1 items-center gap-1 rounded-xl bg-slate-100 px-2.5 text-[12px] text-slate-600"><Bot size={14} className="shrink-0 text-violet-600"/><select value={selectedAgentId} onChange={(event) => setSelectedAgentId(event.target.value as "codex" | "codebuddy" | "")} className="min-w-0 flex-1 truncate bg-transparent outline-none" aria-label="选择智能体"><option value="">云端模型</option><option value="codex">Codex 本地</option></select></label>
            </div>}
            <textarea
              value={input}
              onChange={(event) => setInput(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "Enter" && !event.shiftKey) {
                  event.preventDefault();
                  event.currentTarget.form?.requestSubmit();
                }
              }}
              placeholder={t("chat.placeholderShort")}
              className="hidden min-h-[56px] w-full resize-none bg-transparent px-1 pt-1 text-[14px] leading-6 text-slate-800 outline-none placeholder:text-slate-400 lg:block"
            />
            <div className="hidden items-center justify-between gap-3 border-t border-slate-100 pt-2.5 lg:flex">
              <div ref={contextPickerRef} className="flex min-w-0 items-center gap-2">
                <input
                  type="file"
                  accept="image/png,image/jpeg,image/webp,image/gif"
                  multiple
                  className="hidden"
                  onChange={(event) => {
                    const files = Array.from(event.currentTarget.files ?? []);
                    event.currentTarget.value = "";
                    void addImages(files);
                  }}
                  ref={(element) => { imageFileInputRef.current = element; }}
                />
                <button type="button" onClick={() => imageFileInputRef.current?.click()} disabled={sending || uploadingImages || !canAttachImages} className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-slate-500 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-40" aria-label={t("chat.addAttachment")} title={imageAttachmentHint}>
                  {uploadingImages ? <Loader2 size={16} className="animate-spin" /> : <ImagePlus size={17} />}
                </button>
              </div>

              <div className="flex min-w-0 items-center gap-2">
                <ContextMultiSelect
                  icon={<Database size={15} />}
                  label={t("knowledge.knowledgeBases")}
                  items={readyKnowledgeBases.map((kb) => ({ id: kb.name, label: kb.display_name || kb.name, description: kb.engine_type }))}
                  selectedIds={selectedKbNames}
                  open={openContextPicker === "knowledge"}
                  disabled={loading || readyKnowledgeBases.length === 0}
                  emptyText={loading ? t("knowledge.loading") : t("chat.noKnowledge")}
                  onToggleOpen={() => setOpenContextPicker((current) => current === "knowledge" ? null : "knowledge")}
                  onToggle={(name) => setSelectedKbNames((current) => toggleSelection(current, name))}
                />
                <ContextMultiSelect
                  icon={<Braces size={15} />}
                  label="项目"
                  items={codeProjects.map((project) => ({ id: String(project.id), label: project.display_name, description: `${project.root_path} · ${project.repository_count} 个代码库` }))}
                  selectedIds={selectedProjectId ? [String(selectedProjectId)] : []}
                  open={openContextPicker === "project"}
                  emptyText="暂无已配置项目"
                  onToggleOpen={() => setOpenContextPicker((current) => current === "project" ? null : "project")}
                  onToggle={(id) => setSelectedProjectId((current) => current === Number(id) ? null : Number(id))}
                />
                <label className={`hidden h-8 min-w-0 items-center gap-1.5 rounded-lg px-2 text-[12px] font-medium sm:inline-flex ${selectedProjectId ? "text-violet-700 hover:bg-violet-50" : "text-slate-400"}`} title={selectedAgentProvider?.message || "选择本地编码代理"}>
                  <Bot size={15}/>
                  <select value={selectedAgentId} onChange={(event) => setSelectedAgentId(event.target.value as "codex" | "codebuddy" | "")} className="max-w-[150px] truncate bg-transparent outline-none">
                    <option value="">不接管</option>
                    <option value="codex">Codex 本地</option>
                    <option value="codebuddy" disabled>CodeBuddy CLI（待适配）</option>
                  </select>
                </label>
                {selectedAgentId === "codex" && <label className="hidden h-8 min-w-0 items-center gap-1.5 rounded-lg px-2 text-[12px] text-violet-700 hover:bg-violet-50 sm:inline-flex" title={currentCodexModel?.description || "管理员未配置可用 Codex 模型"}>
                  <Bot size={15} />
                  <select value={selectedCodexModelId} onChange={(event) => setSelectedCodexModelId(event.target.value)} disabled={codexModels.length === 0 || codexModelPolicy?.allow_chat_model_override === false} className="max-w-[180px] truncate bg-transparent outline-none disabled:cursor-not-allowed">
                    {codexModels.map((model) => <option key={model.id} value={model.id}>{model.name}</option>)}
                  </select>
                </label>}
                {selectedAgentId === "codex" && currentCodexModel?.supports_reasoning_effort && <label className="hidden h-8 min-w-0 items-center gap-1.5 rounded-lg px-2 text-[12px] text-violet-700 hover:bg-violet-50 sm:inline-flex" title="Codex reasoning effort">
                  <span className="text-[11px] text-violet-500">推理</span>
                  <select value={selectedCodexReasoningEffort} onChange={(event) => setSelectedCodexReasoningEffort(event.target.value)} disabled={codexReasoningEfforts.length === 0 || codexModelPolicy?.allow_chat_reasoning_effort_override === false} className="max-w-[90px] truncate bg-transparent outline-none disabled:cursor-not-allowed">
                    {codexReasoningEfforts.map((effort) => <option key={effort} value={effort}>{codexReasoningEffortLabel(effort)}</option>)}
                  </select>
                </label>}
                <label className={`hidden h-8 min-w-0 items-center gap-1.5 rounded-lg px-2 text-[12px] text-slate-600 hover:bg-slate-100 ${selectedAgentId === "codex" ? "" : "sm:inline-flex"}`}>
                  <PanelRight size={15} />
                  <select
                    value={selectedModelId}
                    onChange={(event) => setSelectedModelId(event.target.value)}
                    disabled={llmModels.length === 0}
                    className="max-w-[170px] truncate bg-transparent outline-none"
                    title={currentModel?.model || currentModel?.name}
                  >
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
                  <button
                    type="button"
                    onClick={stopGenerating}
                    className="inline-flex h-9 w-9 items-center justify-center rounded-xl bg-rose-600 text-white shadow-sm shadow-rose-200 transition hover:bg-rose-700"
                    aria-label="Stop generating"
                    title="Stop generating"
                  >
                    <Square size={15} fill="currentColor" />
                  </button>
                ) : (
                  <button
                    type="submit"
                    disabled={!input.trim()}
                    className="inline-flex h-9 w-9 items-center justify-center rounded-xl bg-blue-600 text-white shadow-sm shadow-blue-200 transition hover:bg-blue-700 disabled:bg-slate-300"
                    aria-label={t("chat.send")}
                  >
                    <ArrowUp size={17} />
                  </button>
                )}
              </div>
            </div>
          </form>

          {error && (
            <div className="mt-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-[12px] text-red-700">
              {error}
            </div>
          )}
        </div>
      </section>
      <ChatInspectorPanel isOpen={rightPanelOpen} project={selectedProject} fileReference={fileReference} requestedTab={requestedInspectorTab} onClose={() => setRightPanelOpen(false)}/>
      {previewingImage && <ImageLightbox attachment={previewingImage} onClose={() => setPreviewingImage(null)}/>}
      </div>
      <MobileOptionSheet
        open={mobilePicker !== null}
        title={mobilePicker === "model" ? "选择模型" : "选择项目"}
        items={mobilePicker === "model"
          ? (selectedAgentId === "codex"
            ? codexModels.map((model) => ({ id: model.id, label: model.name, description: model.description || "Codex 本地模型", badge: "推理" }))
            : [{ id: "", label: "Auto", description: "根据任务自动选择默认模型", badge: "智能" }, ...llmModels.map((model) => ({ id: model.id, label: model.name || model.model, description: model.model, badge: "推理" }))])
          : [{ id: "", label: "不绑定项目", description: "发起通用对话，不附带代码库上下文" }, ...codeProjects.map((project) => ({ id: String(project.id), label: project.display_name, description: `${project.repository_count} 个代码库 · ${project.root_path}` }))]}
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

function codexReasoningEffortLabel(effort: string) {
  return ({ minimal: "极轻", low: "轻度", medium: "中", high: "高", xhigh: "极高" } as Record<string, string>)[effort] ?? effort;
}

type ContextPickerItem = {
  id: string;
  label: string;
  description?: string;
};

function SidePanelTabLauncher({ onOpen }: { onOpen: (tab: InspectorTab) => void }) {
  const [open, setOpen] = useState(false);
  const options: Array<{ tab: InspectorTab; label: string; shortcut: string; icon: typeof FileCode2 }> = [
    { tab: "file", label: "文件", shortcut: "Ctrl+P", icon: FileCode2 },
    { tab: "tasks", label: "侧边任务", shortcut: "Ctrl+Alt+S", icon: ListTodo },
    { tab: "preview", label: "浏览器", shortcut: "Ctrl+I", icon: Globe2 },
    { tab: "terminal", label: "终端", shortcut: "", icon: Terminal },
  ];

  return <div className="relative">
    <button type="button" onClick={() => setOpen((current) => !current)} className={`inline-flex h-8 w-8 items-center justify-center rounded-lg border bg-white shadow-sm transition ${open ? "border-blue-300 bg-blue-50 text-blue-700" : "border-slate-200 text-slate-600 hover:border-blue-300 hover:text-blue-600"}`} aria-label="Open side panel tab" aria-expanded={open}><Plus size={16}/></button>
    {open && <div className="absolute right-0 top-10 z-50 w-72 rounded-xl border border-slate-200 bg-white p-2 shadow-[0_18px_42px_rgba(15,23,42,0.2)]">
      {options.map((option) => {
        const Icon = option.icon;
        return <button key={option.tab} type="button" onClick={() => { onOpen(option.tab); setOpen(false); }} className="flex h-10 w-full items-center gap-2.5 rounded-lg px-2.5 text-left text-sm text-slate-700 transition hover:bg-slate-100"><Icon size={16} className="text-slate-500"/><span className="flex-1">{option.label}</span>{option.shortcut && <kbd className="rounded bg-slate-100 px-1.5 py-0.5 text-[10px] text-slate-500">{option.shortcut}</kbd>}</button>;
      })}
    </div>}
  </div>;
}

type MobileOption = { id: string; label: string; description?: string; badge?: string };

function MobileOptionSheet({ open, title, items, selectedId, onClose, onSelect }: { open: boolean; title: string; items: MobileOption[]; selectedId: string; onClose: () => void; onSelect: (id: string) => void }) {
  if (!open || typeof document === "undefined") return null;
  return createPortal(<div className="fixed inset-0 z-[160] flex items-end bg-slate-950/55 backdrop-blur-[2px] lg:hidden" role="presentation" onMouseDown={onClose}>
    <section className="max-h-[78dvh] w-full overflow-y-auto rounded-t-[30px] bg-white px-5 pb-[max(1.25rem,env(safe-area-inset-bottom))] pt-2 shadow-[0_-18px_55px_rgba(15,23,42,0.28)]" role="dialog" aria-modal="true" aria-label={title} onMouseDown={(event) => event.stopPropagation()}>
      <div className="mx-auto mb-4 h-1.5 w-12 rounded-full bg-slate-200" />
      <div className="mb-3 flex items-center justify-between"><h2 className="text-xl font-semibold tracking-tight text-slate-900">{title}</h2><button type="button" onClick={onClose} className="grid h-10 w-10 place-items-center rounded-full text-slate-400 hover:bg-slate-100" aria-label={`关闭${title}`}><X size={19}/></button></div>
      <div className="divide-y divide-slate-100">
        {items.map((item) => <button key={item.id || "default"} type="button" onClick={() => onSelect(item.id)} className={`flex min-h-[72px] w-full items-center gap-3 py-3 text-left ${selectedId === item.id ? "text-blue-700" : "text-slate-800"}`}>
          <span className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl text-sm font-bold ${selectedId === item.id ? "bg-blue-600 text-white" : "bg-slate-100 text-slate-500"}`}>{item.label.slice(0, 1).toUpperCase()}</span>
          <span className="min-w-0 flex-1"><span className="flex items-center gap-2"><strong className="truncate text-[15px]">{item.label}</strong>{item.badge && <em className="rounded-md bg-violet-100 px-1.5 py-0.5 text-[10px] not-italic font-semibold text-violet-700">{item.badge}</em>}</span>{item.description && <small className="mt-1 block truncate text-[11px] font-normal text-slate-500">{item.description}</small>}</span>
          {selectedId === item.id && <Check size={22} className="shrink-0 text-emerald-500" />}
        </button>)}
      </div>
    </section>
  </div>, document.body);
}

function ContextMultiSelect({
  icon,
  label,
  items,
  selectedIds,
  open,
  disabled,
  emptyText,
  onToggleOpen,
  onToggle,
}: {
  icon: ReactNode;
  label: string;
  items: ContextPickerItem[];
  selectedIds: string[];
  open: boolean;
  disabled?: boolean;
  emptyText: string;
  onToggleOpen: () => void;
  onToggle: (id: string) => void;
}) {
  const uniqueItems = useMemo(() => {
    const seen = new Set<string>();
    return items.filter((item) => {
      if (seen.has(item.id)) return false;
      seen.add(item.id);
      return true;
    });
  }, [items]);
  const selectedLabel = selectedIds.length === 0
    ? label
    : selectedIds.length === 1
      ? uniqueItems.find((item) => item.id === selectedIds[0])?.label ?? label
      : `${label} · ${selectedIds.length}`;

  return (
    <div className="relative">
      <button
        type="button"
        disabled={disabled}
        onClick={onToggleOpen}
        className="inline-flex h-8 max-w-[164px] items-center gap-1.5 rounded-lg border border-transparent px-2 text-[12px] text-slate-600 transition hover:border-slate-200 hover:bg-slate-50 disabled:cursor-not-allowed disabled:text-slate-400"
        aria-haspopup="listbox"
        aria-expanded={open}
        title={selectedIds.length > 0 ? selectedIds.join(", ") : label}
      >
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
          <div className="max-h-56 overflow-y-auto">
            {uniqueItems.length === 0 ? (
              <p className="px-2.5 py-4 text-center text-[12px] leading-5 text-zinc-500">{emptyText}</p>
            ) : (
              uniqueItems.map((item) => {
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
                    <span className={`flex h-4 w-4 shrink-0 items-center justify-center rounded border ${selected ? "border-blue-600 bg-blue-600 text-white" : "border-zinc-300 bg-white"}`}>
                      {selected && <Check size={11} strokeWidth={3} />}
                    </span>
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

function MessageBubble({ message, onRetry, onOpenCodeFile, onPreviewImage, projectId }: { message: ChatMessage; onRetry?: () => void; onOpenCodeFile: (reference: ChatCodeFileReference) => void; onPreviewImage: (attachment: ChatImagePreview) => void; projectId: number | null }) {
  const { t } = useI18n();
  const isUser = message.role === "user";
  const canCopy = Boolean(message.content.trim());
  return (
    <article className={`flex ${isUser ? "justify-end" : "justify-start"}`}>
      <div className={`${isUser ? "max-w-[82%] rounded-2xl bg-blue-600 px-4 py-3 text-[13px] leading-6 text-white" : "w-full max-w-[860px] text-zinc-900"}`}>
        {!isUser && (
          <div className="mb-3 flex flex-wrap items-center gap-2 text-[12px] text-zinc-500">
            <span className={`font-semibold ${message.status === "error" ? "text-red-600" : "text-zinc-900"}`}>
              {message.status === "streaming" ? (message.agent === "codex" ? "Codex working" : "Working") : message.status === "stopped" ? "Stopped" : message.status === "error" ? "Error" : message.modificationStatus === "completed_changed" ? "Codex 已修改完成" : message.modificationStatus === "completed_no_change" ? "Codex 已完成（未修改文件）" : "Done"}
            </span>
            {message.elapsedSeconds ? <span>- {message.elapsedSeconds}s</span> : null}
            {message.iteration ? <span>- round {message.iteration}</span> : null}
            {message.totalTokens ? <span>- {formatCompactNumber(message.totalTokens)} tokens</span> : null}
            {message.llmCalls || message.toolCalls ? <span>- {(message.llmCalls ?? 0) + (message.toolCalls ?? 0)} calls</span> : null}
          </div>
        )}
        {isUser ? (
          <>
            <div className="whitespace-pre-wrap break-words">{message.content}</div>
            {message.attachments && message.attachments.length > 0 && (
              <div className="mt-2 flex flex-wrap gap-2">
                {message.attachments.map((attachment) => (
                  <button key={attachment.id} type="button" onClick={() => onPreviewImage(attachment)} className="cursor-zoom-in rounded-lg focus:outline-none focus:ring-2 focus:ring-white/80" aria-label={`放大 ${attachment.file_name}`}>
                    <img src={attachment.previewUrl} alt={attachment.file_name} className="h-24 max-w-48 rounded-lg border border-white/30 object-cover" />
                  </button>
                ))}
              </div>
            )}
          </>
        ) : (
          <div className="rounded-2xl border border-[var(--border)] bg-white px-5 py-4 text-[14px] shadow-sm">
            {message.content ? <MarkdownMessage content={message.content} projectId={projectId} onOpenCodeFile={onOpenCodeFile} /> : <div className="text-zinc-400">{t("chat.thinking")}</div>}
          </div>
        )}
        {!isUser && ((message.trace && message.trace.length > 0) || message.thinking) && (
          <details className="mt-3 rounded-lg border border-zinc-200 bg-white p-2 text-[11px] text-zinc-500" open={!message.content}>
            <summary className="cursor-pointer select-none font-medium">{message.status === "streaming" ? t("chat.traceLive") : t("chat.traceHistory")}</summary>
            {message.trace && message.trace.length > 0 && (
              <div className="mt-2 space-y-1 border-l border-zinc-200 pl-3">
                {message.trace.slice(-12).map((item, index) => (
                  <div key={`${index}-${item}`} className="leading-5">{item}</div>
                ))}
              </div>
            )}
            {message.thinking && (
              <div className="mt-2 whitespace-pre-wrap break-words border-t border-zinc-100 pt-2 leading-5">{message.thinking}</div>
            )}
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
              <CitationItem key={`${index}-${citation.score ?? "score"}`} citation={citation} index={index + 1} onOpenCodeFile={onOpenCodeFile}/>
            ))}
          </div>
        )}
        {!isUser && (
          <div className="mt-3 flex items-center gap-1 text-zinc-500">
            {canCopy && (
              <button type="button" onClick={() => void navigator.clipboard?.writeText(message.content)} className="inline-flex h-7 w-7 items-center justify-center rounded-md hover:bg-zinc-100" aria-label="Copy">
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
    </article>
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
          <button type="button" onClick={() => setZoom((value) => Math.max(1, value - 0.25))} disabled={zoom <= 1} className="grid h-8 w-8 place-items-center rounded-lg bg-white/10 transition hover:bg-white/20 disabled:opacity-40" aria-label="缩小图片"><ZoomOut size={16}/></button>
          <span className="w-10 text-center text-xs tabular-nums text-slate-300">{Math.round(zoom * 100)}%</span>
          <button type="button" onClick={() => setZoom((value) => Math.min(3, value + 0.25))} disabled={zoom >= 3} className="grid h-8 w-8 place-items-center rounded-lg bg-white/10 transition hover:bg-white/20 disabled:opacity-40" aria-label="放大图片"><ZoomIn size={16}/></button>
          <button type="button" onClick={onClose} className="ml-1 grid h-8 w-8 place-items-center rounded-lg bg-white/10 transition hover:bg-white/20" aria-label="关闭图片预览"><X size={17}/></button>
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
        <span>#{index} {source}</span>
        <span className="flex items-center gap-1">{reference && <FileCode2 size={12} className="text-blue-600"/>}{typeof citation.score === "number" && <span>{citation.score.toFixed(3)}</span>}</span>
      </div>
      <p className="line-clamp-3 text-[12px] leading-5 text-zinc-700">{citation.text}</p>
    </button>
  );
}

function resolveCodeFileReference(citation: KnowledgeCitation): ChatCodeFileReference | null {
  const metadata = citation.metadata;
  if (!metadata) return null;
  const repositoryName = stringifyMeta(metadata.repository_name)
    || stringifyMeta(metadata.code_repository_name)
    || stringifyMeta(metadata.repository)
    || stringifyMeta(metadata.repo_name);
  const filePath = stringifyMeta(metadata.file_path)
    || stringifyMeta(metadata.relative_path)
    || stringifyMeta(metadata.source_path);
  if (!repositoryName || !filePath) return null;
  const rawLine = Number(metadata.line ?? metadata.line_number ?? metadata.start_line);
  return { repositoryName, filePath, line: Number.isFinite(rawLine) && rawLine > 0 ? rawLine : undefined };
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

function formatTraceEvent(
  event: ChatStreamEvent,
  translate: (key: TranslationKey, params?: Record<string, string | number>) => string,
) {
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
    return citationCount !== undefined
      ? translate("chat.traceToolResultWithCount", { count: citationCount })
      : translate("chat.traceToolResult");
  }

  if (event.type === "thinking") {
    return event.content ? translate("chat.traceThinkingEvent") : translate("chat.traceThinkingModel");
  }

  return null;
}
