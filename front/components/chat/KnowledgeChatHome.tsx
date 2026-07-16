"use client";

import { type FormEvent, type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { ArrowUp, BookOpen, Bot, Braces, Check, ChevronDown, Copy, Database, Loader2, Mic, PanelRight, Plus, RefreshCw, Sparkles, UserRound } from "lucide-react";
import { streamCompleteChat, type ChatStreamEvent } from "@/lib/chat-api";
import { MarkdownMessage } from "@/components/chat/MarkdownMessage";
import { getSettings } from "@/lib/api";
import { getKnowledgeBases } from "@/lib/knowledge-api";
import { getCodeRepositories } from "@/lib/code-repository-api";
import { activeModel, activeProfile, type Catalog, type CatalogModel } from "@/lib/settings-types";
import type { TranslationKey } from "@/i18n/dictionaries";
import type { KnowledgeBase, KnowledgeCitation } from "@/lib/knowledge-types";
import type { CodeRepository } from "@/lib/code-repository-types";
import { useI18n } from "@/i18n/I18nProvider";
import { getSession } from "@/lib/session-api";

type ChatMode = "chat" | "visualize" | "write";

type ChatMessage = {
  id: string;
  role: "user" | "assistant";
  content: string;
  thinking?: string;
  label?: string | null;
  citations?: KnowledgeCitation[];
  model?: string | null;
  status?: "streaming" | "done" | "error";
  startedAt?: number;
  elapsedSeconds?: number;
  iteration?: number;
  llmCalls?: number;
  toolCalls?: number;
  totalTokens?: number;
  trace?: string[];
};

function createClientId(): string {
  return globalThis.crypto?.randomUUID?.()
    ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

export function KnowledgeChatHome() {
  const { t } = useI18n();
  const router = useRouter();
  const searchParams = useSearchParams();
  const requestedSessionId = searchParams.get("session");
  const [knowledgeBases, setKnowledgeBases] = useState<KnowledgeBase[]>([]);
  const [codeRepositories, setCodeRepositories] = useState<CodeRepository[]>([]);
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [selectedKbNames, setSelectedKbNames] = useState<string[]>([]);
  const [selectedCodeRepositoryNames, setSelectedCodeRepositoryNames] = useState<string[]>([]);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(requestedSessionId);
  const [openContextPicker, setOpenContextPicker] = useState<"knowledge" | "code" | null>(null);
  const [selectedModelId, setSelectedModelId] = useState("");
  const [mode, setMode] = useState<ChatMode>("chat");
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(true);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const bottomRef = useRef<HTMLDivElement | null>(null);
  const contextPickerRef = useRef<HTMLDivElement | null>(null);
  const pendingSessionIdRef = useRef<string | null>(null);

  const readyKnowledgeBases = useMemo(
    () => knowledgeBases.filter((kb) => kb.active_version_id && kb.status !== "error"),
    [knowledgeBases],
  );
  const llmModels = useMemo(() => resolveLlmModels(catalog), [catalog]);
  const currentKnowledgeBase = readyKnowledgeBases.find((kb) => kb.name === selectedKbNames[0]);
  const currentModel = llmModels.find((model) => model.id === selectedModelId) ?? llmModels[0] ?? null;

  useEffect(() => {
    void loadBootstrap();
  }, []);

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
      setMessages(session.messages.map((message) => ({
        id: String(message.id), role: message.role, content: message.content, thinking: message.thinking ?? undefined,
        citations: message.citations ?? undefined, model: message.metadata?.model ?? null, status: "done",
      })));
    }).catch((ex) => { if (!cancelled) setError(ex instanceof Error ? ex.message : t("chat.errorLoadKnowledge")); });
    return () => { cancelled = true; };
  }, [requestedSessionId, t]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, sending]);

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

  async function loadBootstrap() {
    setLoading(true);
    setError(null);
    try {
      const [kbRows, repositoryRows, settings] = await Promise.all([getKnowledgeBases(), getCodeRepositories(), getSettings()]);
      setKnowledgeBases(kbRows);
      setCodeRepositories(repositoryRows);
      setCatalog(settings.catalog);

      const active = activeModel(settings.catalog, "llm");
      setSelectedModelId((current) => current || active?.id || "");
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("chat.errorLoadKnowledge"));
    } finally {
      setLoading(false);
    }
  }

  async function sendMessage(query: string, options?: { retryAssistantId?: string }) {
    if (!query || sending) return;

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
          content: query,
        },
      ]);
      setInput("");
    }

    setSending(true);
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
        },
      ]);
    }

    try {
      await streamCompleteChat({
        session_id: sessionId,
        message: query,
        knowledge_base_name: selectedKbNames[0],
        knowledge_base_names: selectedKbNames,
        code_repository_names: selectedCodeRepositoryNames,
        model_id: selectedModelId || undefined,
        top_k: 6,
        mode,
      }, (event) => {
        if (event.type === "error") {
          throw new Error(event.content || t("chat.errorSearch"));
        }

        setMessages((items) => items.map((message) => {
          if (message.id !== assistantId) return message;
          const stats = event.metadata ? extractRunStats(event.metadata) : {};
          if (event.type === "label") {
            return { ...message, label: event.label ?? null, trace: appendTrace(message.trace, formatTraceEvent(event, t)), ...stats };
          }
          if (event.type === "thinking") {
            return {
              ...message,
              thinking: `${message.thinking ?? ""}${event.content ?? ""}`,
              trace: appendTrace(message.trace, formatTraceEvent(event, t)),
              ...stats,
            };
          }
          if (event.type === "content") {
            return {
              ...message,
              content: `${message.content}${event.content ?? ""}`,
              model: event.model ?? message.model,
              ...stats,
            };
          }
          if (event.type === "loop" || event.type === "tool" || event.type === "tool_result" || event.type === "tool_request") {
            return { ...message, trace: appendTrace(message.trace, formatTraceEvent(event, t)), ...stats };
          }
          if (event.type === "sources" || event.type === "done") {
            return {
              ...message,
              content: message.content || event.content || "",
              citations: event.citations ?? message.citations,
              model: event.model ?? message.model,
              label: event.label ?? message.label,
              status: event.type === "done" ? "done" : message.status,
              elapsedSeconds: stats.elapsedSeconds ?? Math.max(1, Math.round((Date.now() - (message.startedAt ?? startedAt)) / 1000)),
              ...stats,
            };
          }
          return message;
        }));
      });

      setMessages((items) => items.map((message) => {
        if (message.id !== assistantId) return message;
        return {
          ...message,
          content: message.content.trim() || t("chat.emptyAnswer"),
          status: "done",
          elapsedSeconds: message.elapsedSeconds ?? Math.max(1, Math.round((Date.now() - (message.startedAt ?? startedAt)) / 1000)),
        };
      }));
      window.dispatchEvent(new Event("aiagent:sessions-updated"));
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("chat.errorSearch"));
      setMessages((items) => items.map((message) => (
        message.id === assistantId
          ? { ...message, content: message.content || t("chat.searchFailed"), status: "error" }
          : message
      )));
    } finally {
      setSending(false);
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await sendMessage(input.trim());
  }

  function startNewChat() {
    pendingSessionIdRef.current = null;
    setActiveSessionId(null);
    setMessages([]);
    setInput("");
    setError(null);
    router.replace("/chat");
  }

  return (
    <main className="flex min-h-screen flex-col bg-white">
      <header className="flex h-14 items-center justify-between border-b border-[var(--border)] px-5">
        <div className="flex items-center gap-3">
          <h1 className="font-serif text-[18px] font-semibold">{t("chat.newChat")}</h1>
          {currentKnowledgeBase && (
            <span className="hidden rounded-full bg-emerald-50 px-2 py-1 text-[11px] text-emerald-700 sm:inline-flex">
              {currentKnowledgeBase.display_name || currentKnowledgeBase.name}
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <button type="button" onClick={startNewChat} className="inline-flex h-8 w-8 items-center justify-center rounded-md border border-[var(--border)] hover:border-blue-300" aria-label={t("chat.new")}>
            <Plus size={15} />
          </button>
          <button type="button" onClick={() => void loadBootstrap()} className="inline-flex h-8 w-8 items-center justify-center rounded-md border border-[var(--border)] hover:border-blue-300" aria-label={t("knowledge.refresh")}>
            <RefreshCw size={14} />
          </button>
        </div>
      </header>

      <section className="flex flex-1 flex-col px-4 py-6">
        <div className="mx-auto flex w-full max-w-5xl flex-1 flex-col">
          {messages.length === 0 ? (
            <EmptyState title={t("chat.heroTitle")} />
          ) : (
            <div className="flex-1 space-y-4 pb-6">
              {messages.map((message, index) => (
                <MessageBubble
                  key={message.id}
                  message={message}
                  onRetry={message.role === "assistant" ? () => {
                    const userMessage = findPreviousUserMessage(messages, index);
                    if (userMessage) void sendMessage(userMessage.content, { retryAssistantId: message.id });
                  } : undefined}
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

          <form onSubmit={handleSubmit} className="sticky bottom-4 mt-auto rounded-3xl border border-[var(--border)] bg-white px-4 py-3 shadow-[0_18px_50px_rgba(15,23,42,0.10)]">
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
              className="min-h-[62px] w-full resize-none bg-transparent text-[15px] leading-6 outline-none placeholder:text-zinc-400"
            />
            <div className="flex items-center justify-between gap-3 pt-2">
              <div ref={contextPickerRef} className="flex min-w-0 items-center gap-2">
                <label className="inline-flex h-8 items-center gap-1.5 rounded-md px-1.5 text-[13px] font-medium hover:bg-zinc-100">
                  <Bot size={16} />
                  <select value={mode} onChange={(event) => setMode(event.target.value as ChatMode)} className="bg-transparent outline-none">
                    <option value="chat">{t("chat.modeChat")}</option>
                    <option value="visualize">{t("chat.modeVisualize")}</option>
                    <option value="write">{t("chat.modeWrite")}</option>
                  </select>
                </label>
                <button type="button" className="inline-flex h-8 w-8 items-center justify-center rounded-md hover:bg-zinc-100" aria-label={t("chat.addAttachment")}>
                  <Plus size={17} />
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
                  label={t("nav.codeRepositories")}
                  items={codeRepositories.map((repository) => ({ id: repository.name, label: repository.display_name || repository.name, description: repository.languages.join(" / ") || repository.root_path }))}
                  selectedIds={selectedCodeRepositoryNames}
                  open={openContextPicker === "code"}
                  emptyText={t("codeRepository.empty")}
                  onToggleOpen={() => setOpenContextPicker((current) => current === "code" ? null : "code")}
                  onToggle={(name) => setSelectedCodeRepositoryNames((current) => toggleSelection(current, name))}
                />
                <label className="hidden h-8 min-w-0 items-center gap-1.5 rounded-md px-1.5 text-[12px] hover:bg-zinc-100 sm:inline-flex">
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
                <button type="button" className="inline-flex h-8 w-8 items-center justify-center rounded-md hover:bg-zinc-100" aria-label={t("chat.voiceInput")}>
                  <Mic size={16} />
                </button>
                <button
                  type="submit"
                  disabled={sending || !input.trim()}
                  className="inline-flex h-9 w-9 items-center justify-center rounded-xl bg-blue-600 text-white hover:bg-blue-700 disabled:bg-zinc-300"
                  aria-label={t("chat.send")}
                >
                  {sending ? <Loader2 size={16} className="animate-spin" /> : <ArrowUp size={17} />}
                </button>
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
    </main>
  );
}

type ContextPickerItem = {
  id: string;
  label: string;
  description?: string;
};

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
  const selectedLabel = selectedIds.length === 0
    ? label
    : selectedIds.length === 1
      ? items.find((item) => item.id === selectedIds[0])?.label ?? label
      : `${label} · ${selectedIds.length}`;

  return (
    <div className="relative">
      <button
        type="button"
        disabled={disabled}
        onClick={onToggleOpen}
        className="inline-flex h-8 max-w-[154px] items-center gap-1.5 rounded-md px-2 text-[12px] hover:bg-zinc-100 disabled:cursor-not-allowed disabled:text-zinc-400"
        aria-haspopup="listbox"
        aria-expanded={open}
        title={selectedIds.length > 0 ? selectedIds.join(", ") : label}
      >
        <span className="shrink-0">{icon}</span>
        <span className="truncate">{selectedLabel}</span>
        <ChevronDown size={14} className={`ml-auto shrink-0 text-zinc-400 transition-transform ${open ? "rotate-180" : ""}`} />
      </button>

      {open && (
        <div className="absolute bottom-10 right-0 z-30 w-[276px] overflow-hidden rounded-xl border border-zinc-200 bg-white p-1.5 shadow-[0_18px_42px_rgba(15,23,42,0.18)]">
          <div className="flex items-center justify-between px-2.5 py-2 text-[11px] font-semibold text-zinc-500">
            <span>{label}</span>
            <span>{selectedIds.length}</span>
          </div>
          <div className="max-h-56 overflow-y-auto">
            {items.length === 0 ? (
              <p className="px-2.5 py-4 text-center text-[12px] leading-5 text-zinc-500">{emptyText}</p>
            ) : (
              items.map((item) => {
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
    <div className="flex flex-1 flex-col items-center justify-center pb-14 text-center">
      <div className="mb-5 flex h-12 w-12 items-center justify-center rounded-2xl border border-[var(--border)] bg-zinc-50">
        <Sparkles size={24} strokeWidth={1.6} />
      </div>
      <h2 className="font-serif text-[32px] font-semibold tracking-normal text-zinc-950 sm:text-[42px]">{title}</h2>
    </div>
  );
}

function MessageBubble({ message, onRetry }: { message: ChatMessage; onRetry?: () => void }) {
  const { t } = useI18n();
  const isUser = message.role === "user";
  const canCopy = Boolean(message.content.trim());
  return (
    <article className={`flex ${isUser ? "justify-end" : "justify-start"}`}>
      <div className={`${isUser ? "max-w-[82%] rounded-2xl bg-blue-600 px-4 py-3 text-[13px] leading-6 text-white" : "w-full max-w-[860px] text-zinc-900"}`}>
        {!isUser && (
          <div className="mb-3 flex flex-wrap items-center gap-2 text-[12px] text-zinc-500">
            <span className={`font-semibold ${message.status === "error" ? "text-red-600" : "text-zinc-900"}`}>
              {message.status === "streaming" ? "Working" : message.status === "error" ? "Error" : "Done"}
            </span>
            {message.elapsedSeconds ? <span>- {message.elapsedSeconds}s</span> : null}
            {message.iteration ? <span>- round {message.iteration}</span> : null}
            {message.totalTokens ? <span>- {formatCompactNumber(message.totalTokens)} tokens</span> : null}
            {message.llmCalls || message.toolCalls ? <span>- {(message.llmCalls ?? 0) + (message.toolCalls ?? 0)} calls</span> : null}
          </div>
        )}
        {isUser ? (
          <div className="whitespace-pre-wrap break-words">{message.content}</div>
        ) : (
          <div className="rounded-2xl border border-[var(--border)] bg-white px-5 py-4 text-[14px] shadow-sm">
            {message.content ? <MarkdownMessage content={message.content} /> : <div className="text-zinc-400">{t("chat.thinking")}</div>}
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
              <CitationItem key={`${index}-${citation.score ?? "score"}`} citation={citation} index={index + 1} />
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

function CitationItem({ citation, index }: { citation: KnowledgeCitation; index: number }) {
  const source = formatCitationSource(citation.metadata);
  return (
    <div className="rounded-lg border border-zinc-200 bg-white p-2">
      <div className="mb-1 flex items-center justify-between gap-2 text-[11px] text-zinc-500">
        <span>#{index} {source}</span>
        {typeof citation.score === "number" && <span>{citation.score.toFixed(3)}</span>}
      </div>
      <p className="line-clamp-3 text-[12px] leading-5 text-zinc-700">{citation.text}</p>
    </div>
  );
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
