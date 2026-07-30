"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { getChatRuntimeId, heartbeatCodexRuntime, streamCompleteChat, type ChatCompleteRequest, type ChatStreamEvent } from "@/lib/chat-api";

export type ChatStreamStatus = "streaming" | "done" | "stopped" | "error";
export type ChatStreamRecord = {
  id: string;
  sessionId: string;
  status: ChatStreamStatus;
  events: ChatStreamEvent[];
  startedAt: number;
  errorMessage?: string;
  unread: boolean;
  agent?: "codex" | "codebuddy";
};

type ChatStreamContextValue = {
  streams: Record<string, ChatStreamRecord>;
  startStream: (request: ChatCompleteRequest) => string;
  cancelStream: (streamId: string) => void;
  markSessionViewed: (sessionId: string) => void;
  clearFinishedStreams: (sessionId: string) => void;
  activateCodexRuntime: (projectId: number) => void;
};

const ChatStreamContext = createContext<ChatStreamContextValue | null>(null);

function createStreamId() {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

export function ChatStreamProvider({ children }: { children: ReactNode }) {
  const [streams, setStreams] = useState<Record<string, ChatStreamRecord>>({});
  const streamsRef = useRef(streams);
  const controllersRef = useRef(new Map<string, AbortController>());
  const codexProjectIdRef = useRef<number | null>(null);
  useEffect(() => { streamsRef.current = streams; }, [streams]);

  const update = useCallback((streamId: string, transform: (stream: ChatStreamRecord) => ChatStreamRecord) => {
    const current = streamsRef.current[streamId];
    if (!current) return;
    const next = { ...streamsRef.current, [streamId]: transform(current) };
    streamsRef.current = next;
    setStreams(next);
  }, []);

  const startStream = useCallback((request: ChatCompleteRequest) => {
    const sessionId = request.session_id?.trim();
    if (!sessionId) throw new Error("Chat session is required.");
    if (Object.values(streamsRef.current).some((stream) => stream.sessionId === sessionId && stream.status === "streaming"))
      throw new Error("This session already has an active stream.");
    if (Object.values(streamsRef.current).filter((stream) => stream.status === "streaming").length >= 3)
      throw new Error("A user can run at most 3 chat sessions at the same time.");

    const streamId = createStreamId();
    const controller = new AbortController();
    controllersRef.current.set(streamId, controller);
    const record: ChatStreamRecord = { id: streamId, sessionId, status: "streaming", events: [], startedAt: Date.now(), unread: false, agent: request.agent };
    const next = { ...streamsRef.current, [streamId]: record };
    streamsRef.current = next;
    setStreams(next);

    const streamRequest = { ...request, client_runtime_id: getChatRuntimeId() };
    void streamCompleteChat(streamRequest, (event) => {
      update(streamId, (current) => ({
        ...current,
        status: event.type === "error" ? "error" : current.status,
        errorMessage: event.type === "error" ? event.content || "Chat stream failed." : current.errorMessage,
        events: [...current.events, event],
      }));
    }, controller.signal).then(() => {
      if (controller.signal.aborted) return;
      update(streamId, (current) => ({ ...current, status: current.status === "error" ? "error" : "done", unread: true }));
      window.dispatchEvent(new CustomEvent("aiagent:chat-stream-complete", { detail: { sessionId, streamId } }));
      window.dispatchEvent(new Event("aiagent:sessions-updated"));
    }).catch((error) => {
      const stopped = controller.signal.aborted || (error instanceof DOMException && error.name === "AbortError");
      const message = error instanceof Error ? error.message : "Chat stream failed.";
      update(streamId, (current) => ({
        ...current,
        status: stopped ? "stopped" : "error",
        errorMessage: stopped ? current.errorMessage : message,
        unread: !stopped,
        events: stopped || current.events.some((event) => event.type === "error") ? current.events : [...current.events, { type: "error", content: message }],
      }));
      window.dispatchEvent(new Event("aiagent:sessions-updated"));
    }).finally(() => controllersRef.current.delete(streamId));
    return streamId;
  }, [update]);

  const cancelStream = useCallback((streamId: string) => controllersRef.current.get(streamId)?.abort(), []);
  const markSessionViewed = useCallback((sessionId: string) => {
    const current = streamsRef.current;
    const changed = Object.values(current).some((stream) => stream.sessionId === sessionId && stream.unread);
    if (!changed) return;
    const next = Object.fromEntries(Object.entries(current).map(([id, stream]) => [id, stream.sessionId === sessionId ? { ...stream, unread: false } : stream]));
    streamsRef.current = next;
    setStreams(next);
  }, []);

  const clearFinishedStreams = useCallback((sessionId: string) => {
    const current = streamsRef.current;
    const entries = Object.entries(current);
    const remaining = entries.filter(([, stream]) => stream.sessionId !== sessionId || stream.status === "streaming");
    if (remaining.length === entries.length) return;
    const next = Object.fromEntries(remaining);
    streamsRef.current = next;
    setStreams(next);
  }, []);

  const activateCodexRuntime = useCallback((projectId: number) => {
    if (!Number.isFinite(projectId) || projectId <= 0) return;
    codexProjectIdRef.current = projectId;
    void heartbeatCodexRuntime(projectId).catch(() => {
      // Sending remains available; the stream request will surface a real Codex failure if one occurs.
    });
  }, []);

  useEffect(() => {
    const heartbeat = () => {
      const projectId = codexProjectIdRef.current;
      if (projectId) void heartbeatCodexRuntime(projectId).catch(() => {});
    };
    const intervalId = window.setInterval(heartbeat, 25_000);
    document.addEventListener("visibilitychange", heartbeat);
    return () => {
      window.clearInterval(intervalId);
      document.removeEventListener("visibilitychange", heartbeat);
    };
  }, []);

  useEffect(() => () => controllersRef.current.forEach((controller) => controller.abort()), []);
  const value = useMemo(() => ({ streams, startStream, cancelStream, markSessionViewed, clearFinishedStreams, activateCodexRuntime }), [streams, startStream, cancelStream, markSessionViewed, clearFinishedStreams, activateCodexRuntime]);
  return <ChatStreamContext.Provider value={value}>{children}</ChatStreamContext.Provider>;
}

export function useChatStreams() {
  const value = useContext(ChatStreamContext);
  if (!value) throw new Error("ChatStreamProvider is required.");
  return value;
}
