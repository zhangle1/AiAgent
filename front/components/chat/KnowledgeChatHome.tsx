"use client";

import { type FormEvent, type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { ArrowUp, BookOpen, Bot, Braces, Check, ChevronDown, Copy, Database, FileCode2, Globe2, ListTodo, Loader2, Mic, PanelRight, Plus, RefreshCw, Sparkles, Square, Terminal, UserRound } from "lucide-react";
import { streamCompleteChat, type ChatStreamEvent } from "@/lib/chat-api";
import { MarkdownMessage } from "@/components/chat/MarkdownMessage";
import { ChatInspectorPanel, type ChatCodeFileReference } from "@/components/chat/ChatInspectorPanel";
import { ChatRuntimeToolbar } from "@/components/chat/ChatRuntimeToolbar";
import { getSettings } from "@/lib/api";
import { getKnowledgeBases } from "@/lib/knowledge-api";
import { getCodeProjects } from "@/lib/code-repository-api";
import { activeModel, activeProfile, type Catalog, type CatalogModel } from "@/lib/settings-types";
import type { TranslationKey } from "@/i18n/dictionaries";
import type { KnowledgeBase, KnowledgeCitation } from "@/lib/knowledge-types";
import type { CodeProject } from "@/lib/code-repository-types";
import { useI18n } from "@/i18n/I18nProvider";
import { getSession } from "@/lib/session-api";

type ChatMode = "chat" | "visualize" | "write";
type InspectorTab = "preview" | "file" | "tasks" | "terminal";

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
  agent?: "codex";
  modificationStatus?: string;
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
  const requestedProjectId = Number(searchParams.get("project")) || null;
  const [knowledgeBases, setKnowledgeBases] = useState<KnowledgeBase[]>([]);
  const [codeProjects, setCodeProjects] = useState<CodeProject[]>([]);
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [selectedKbNames, setSelectedKbNames] = useState<string[]>([]);
  const [selectedProjectId, setSelectedProjectId] = useState<number | null>(requestedProjectId);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(requestedSessionId);
  const [openContextPicker, setOpenContextPicker] = useState<"knowledge" | "project" | null>(null);
  const [selectedModelId, setSelectedModelId] = useState("");
  const [codexEnabled, setCodexEnabled] = useState(true);
  const [mode, setMode] = useState<ChatMode>("chat");
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(true);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [rightPanelOpen, setRightPanelOpen] = useState(false);
  const [requestedInspectorTab, setRequestedInspectorTab] = useState<InspectorTab | null>(null);
  const [fileReference, setFileReference] = useState<ChatCodeFileReference | null>(null);
  const bottomRef = useRef<HTMLDivElement | null>(null);
  const contextPickerRef = useRef<HTMLDivElement | null>(null);
  const pendingSessionIdRef = useRef<string | null>(null);
  const streamAbortControllerRef = useRef<AbortController | null>(null);

  const readyKnowledgeBases = useMemo(
    () => knowledgeBases.filter((kb) => kb.active_version_id && kb.status !== "error"),
    [knowledgeBases],
  );
  const llmModels = useMemo(() => resolveLlmModels(catalog), [catalog]);
  const currentKnowledgeBase = readyKnowledgeBases.find((kb) => kb.name === selectedKbNames[0]);
  const selectedProject = codeProjects.find((project) => project.id === selectedProjectId) ?? null;
  const selectedCodeRepositoryNames = selectedProject?.repositories.map((repository) => repository.name) ?? [];
  const currentModel = llmModels.find((model) => model.id === selectedModelId) ?? llmModels[0] ?? null;

  useEffect(() => {
    void loadBootstrap();
  }, []);

  useEffect(() => {
    if (!requestedSessionId) setSelectedProjectId(requestedProjectId);
  }, [requestedProjectId, requestedSessionId]);

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

  useEffect(() => () => streamAbortControllerRef.current?.abort(), []);

  async function loadBootstrap() {
    setLoading(true);
    setError(null);
    try {
      const [kbRows, projectRows, settings] = await Promise.all([getKnowledgeBases(), getCodeProjects(), getSettings()]);
      setKnowledgeBases(kbRows);
      setCodeProjects(projectRows);
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

  async function sendMessage(query: string, options?: { retryAssistantId?: string }) {
    if (!query || sending) return;
    if (codexEnabled && !selectedProjectId) {
      setError("Codex 接管需要先选择项目，以便传递项目目录。");
      return;
    }

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
    const streamAbortController = new AbortController();
    streamAbortControllerRef.current = streamAbortController;

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
          agent: codexEnabled ? "codex" : undefined,
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
        code_project_id: selectedProjectId ?? undefined,
        model_id: selectedModelId || undefined,
        top_k: 6,
        mode,
        agent: codexEnabled ? "codex" : undefined,
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
              modificationStatus: stringifyMeta(event.metadata?.modification_status) || message.modificationStatus,
              label: event.label ?? message.label,
              status: event.type === "done" ? "done" : message.status,
              elapsedSeconds: stats.elapsedSeconds ?? Math.max(1, Math.round((Date.now() - (message.startedAt ?? startedAt)) / 1000)),
              ...stats,
            };
          }
          return message;
        }));
      }, streamAbortController.signal);

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
      if (streamAbortController.signal.aborted) {
        setMessages((items) => items.map((message) => (
          message.id === assistantId
            ? {
              ...message,
              content: message.content.trim() || "Generation stopped.",
              status: "stopped",
              elapsedSeconds: Math.max(1, Math.round((Date.now() - (message.startedAt ?? startedAt)) / 1000)),
            }
            : message
        )));
        window.dispatchEvent(new Event("aiagent:sessions-updated"));
        return;
      }
      setError(ex instanceof Error ? ex.message : t("chat.errorSearch"));
      setMessages((items) => items.map((message) => (
        message.id === assistantId
          ? { ...message, content: message.content || t("chat.searchFailed"), status: "error" }
          : message
      )));
    } finally {
      if (streamAbortControllerRef.current === streamAbortController) streamAbortControllerRef.current = null;
      setSending(false);
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await sendMessage(input.trim());
  }

  function stopGenerating() {
    streamAbortControllerRef.current?.abort();
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
    <main className="flex h-screen min-h-0 flex-col overflow-hidden bg-[radial-gradient(circle_at_50%_-20%,#eff6ff_0,transparent_38%),#f8fafc]">
      <header className="relative z-[80] flex h-16 shrink-0 items-center justify-between border-b border-slate-200/80 bg-white/75 px-5 backdrop-blur-xl">
        <div className="flex items-center gap-3">
          <div className="grid h-8 w-8 place-items-center rounded-lg bg-blue-50 text-blue-600"><Sparkles size={16}/></div>
          <div><h1 className="text-sm font-semibold text-slate-950">{t("chat.newChat")}</h1><p className="text-[11px] text-slate-400">AI 工作台</p></div>
          {currentKnowledgeBase && (
            <span className="hidden rounded-full bg-emerald-50 px-2 py-1 text-[11px] text-emerald-700 sm:inline-flex">
              {currentKnowledgeBase.display_name || currentKnowledgeBase.name}
            </span>
          )}
          {selectedProject && <span className="hidden items-center gap-1 rounded-full border border-blue-100 bg-blue-50 px-2 py-1 text-[11px] text-blue-700 sm:inline-flex"><Braces size={12}/>{selectedProject.display_name}</span>}
        </div>
        <div className="flex items-center gap-2">
          <ChatRuntimeToolbar project={selectedProject} rightPanelOpen={rightPanelOpen} onToggleRightPanel={() => setRightPanelOpen((current) => !current)} onOpenRuntimePanel={() => openInspector("terminal")}/>
          <SidePanelTabLauncher onOpen={openInspector}/>
          <button type="button" onClick={() => void loadBootstrap()} className="inline-flex h-8 w-8 items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-500 shadow-sm transition hover:border-blue-300 hover:text-blue-600" aria-label={t("knowledge.refresh")}>
            <RefreshCw size={14} />
          </button>
        </div>
      </header>

      <div className="flex min-h-0 flex-1 overflow-hidden">
      <section className="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden px-4 py-5 sm:px-7">
        <div className="mx-auto flex min-h-0 w-full max-w-4xl flex-1 flex-col">
          {messages.length === 0 ? (
            <EmptyState title={t("chat.heroTitle")} />
          ) : (
            <div className="workspace-scroll min-h-0 flex-1 space-y-5 overflow-y-auto pb-6 pt-4">
              {messages.map((message, index) => (
                <MessageBubble
                  key={message.id}
                  message={message}
                    onRetry={message.role === "assistant" ? () => {
                      const userMessage = findPreviousUserMessage(messages, index);
                      if (userMessage) void sendMessage(userMessage.content, { retryAssistantId: message.id });
                    } : undefined}
                    onOpenCodeFile={openCodeFile}
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

          <form onSubmit={handleSubmit} className="sticky bottom-4 mt-auto rounded-2xl border border-slate-200 bg-white/95 px-4 py-3 shadow-[0_18px_46px_rgba(15,23,42,0.12)] backdrop-blur-xl transition focus-within:border-blue-300 focus-within:shadow-[0_20px_52px_rgba(37,99,235,0.15)]">
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
              className="min-h-[56px] w-full resize-none bg-transparent px-1 pt-1 text-[14px] leading-6 text-slate-800 outline-none placeholder:text-slate-400"
            />
            <div className="flex items-center justify-between gap-3 border-t border-slate-100 pt-2.5">
              <div ref={contextPickerRef} className="flex min-w-0 items-center gap-2">
                <label className="inline-flex h-8 items-center gap-1.5 rounded-lg px-2 text-[12px] font-medium text-slate-600 hover:bg-slate-100">
                  <Bot size={16} />
                  <select value={mode} onChange={(event) => setMode(event.target.value as ChatMode)} className="bg-transparent outline-none">
                    <option value="chat">{t("chat.modeChat")}</option>
                    <option value="visualize">{t("chat.modeVisualize")}</option>
                    <option value="write">{t("chat.modeWrite")}</option>
                  </select>
                </label>
                <button type="button" className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-slate-500 hover:bg-slate-100" aria-label={t("chat.addAttachment")}>
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
                  label="项目"
                  items={codeProjects.map((project) => ({ id: String(project.id), label: project.display_name, description: `${project.root_path} · ${project.repository_count} 个代码库` }))}
                  selectedIds={selectedProjectId ? [String(selectedProjectId)] : []}
                  open={openContextPicker === "project"}
                  emptyText="暂无已配置项目"
                  onToggleOpen={() => setOpenContextPicker((current) => current === "project" ? null : "project")}
                  onToggle={(id) => setSelectedProjectId((current) => current === Number(id) ? null : Number(id))}
                />
                <label className={`inline-flex h-8 items-center gap-1.5 rounded-lg px-2 text-[12px] font-medium ${selectedProjectId ? "text-violet-700 hover:bg-violet-50" : "cursor-not-allowed text-slate-400"}`} title={selectedProjectId ? "将当前项目目录和问题交由本机 Codex 处理" : "请先选择项目"}>
                  <input type="checkbox" checked={Boolean(selectedProjectId) && codexEnabled} disabled={!selectedProjectId} onChange={(event) => setCodexEnabled(event.target.checked)} className="accent-violet-600" />
                  Codex 接管
                </label>
                <label className="hidden h-8 min-w-0 items-center gap-1.5 rounded-lg px-2 text-[12px] text-slate-600 hover:bg-slate-100 sm:inline-flex">
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
      </div>
    </main>
  );
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

function MessageBubble({ message, onRetry, onOpenCodeFile }: { message: ChatMessage; onRetry?: () => void; onOpenCodeFile: (reference: ChatCodeFileReference) => void }) {
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
