"use client";

import { useSearchParams } from "next/navigation";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Activity, Archive, ChevronUp, ExternalLink, Filter, FolderGit2, History, LayoutDashboard, LayoutTemplate, ListTodo, LoaderCircle, Lock, Map as MapIcon, MessageSquare, MousePointer2, PanelLeftClose, PanelLeftOpen, Pencil, Pin, Play, Plus, Route, Search, Square, Trash2, WandSparkles, X } from "lucide-react";
import { KnowledgeChatHome } from "@/components/chat/KnowledgeChatHome";
import { PromptTemplateCanvasPicker } from "@/components/prompt-templates/PromptTemplateCanvasPicker";
import { useChatStreams, type ChatStreamRecord } from "@/components/chat/ChatStreamProvider";
import type { ChatCompleteRequest } from "@/lib/chat-api";
import { createSession, deleteSession, listSessions, renameSession, type SessionSummary } from "@/lib/session-api";
import { getCodeProjects } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";
import { createProjectTaskChatSessions, listProjectTasks, type ProjectTask } from "@/lib/project-task-api";
import { getCodexModelPolicy } from "@/lib/agent-provider-api";
import { getSettings } from "@/lib/api";
import { activeProfile } from "@/lib/settings-types";
import { addWorkCanvasNode, archiveWorkCanvas, completeWorkflowRunNode, createWorkflowLink, createWorkCanvas, deleteWorkCanvasLink, getWorkCanvas, listWorkCanvases, listWorkflowRuns, prepareWorkflowExecution, registerCompletedWorkflowNode, removeWorkCanvasNode, renameWorkCanvas, saveWorkCanvasLayout, updateWorkCanvasNode } from "@/lib/work-canvas-api";
import type { NodeExecutionAgent, WorkCanvasNode, WorkCanvasSnapshot, WorkCanvasSummary, WorkflowExecution, WorkflowRun } from "@/lib/work-canvas-types";
import type { PromptTemplate } from "@/lib/prompt-template-types";

type DragState = {
  id: string;
  startX: number;
  startY: number;
  nodeX: number;
  nodeY: number;
};

type ConnectionPreview = { sourceId: string; x1: number; y1: number; x2: number; y2: number };
type NodeModelOption = { id: string; name: string; agent: NodeExecutionAgent };
type WorkflowStreamBinding = { canvasId: string; runId: string; nodeId: string };
type CanvasInteractionMode = "drag" | "connect";
type CanvasViewport = { left: number; top: number; width: number; height: number };

const CANVAS_WIDTH = 2400;
const CANVAS_HEIGHT = 1400;
const NODE_WIDTH = 288;
const NODE_HEIGHT = 266;

function linkPoints(source: WorkCanvasNode, target: WorkCanvasNode) {
  const width = NODE_WIDTH;
  const height = NODE_HEIGHT;
  const sourceCenter = { x: source.position_x + width / 2, y: source.position_y + height / 2 };
  const targetCenter = { x: target.position_x + width / 2, y: target.position_y + height / 2 };
  const dx = targetCenter.x - sourceCenter.x;
  const dy = targetCenter.y - sourceCenter.y;
  if (Math.abs(dx) >= Math.abs(dy)) return dx >= 0
    ? { x1: source.position_x + width, y1: sourceCenter.y, x2: target.position_x, y2: targetCenter.y }
    : { x1: source.position_x, y1: sourceCenter.y, x2: target.position_x + width, y2: targetCenter.y };
  return dy >= 0
    ? { x1: sourceCenter.x, y1: source.position_y + height, x2: targetCenter.x, y2: target.position_y }
    : { x1: sourceCenter.x, y1: source.position_y, x2: targetCenter.x, y2: target.position_y + height };
}

function nodeExecutionAgent(node: WorkCanvasNode) {
  return node.agent ?? node.session.agent?.toLowerCase();
}

function nodeModelOptions(node: WorkCanvasNode, codexModels: NodeModelOption[], llmModels: NodeModelOption[]) {
  void node;
  return [...codexModels, ...llmModels];
}

export function WorkCanvasPage() {
  const searchParams = useSearchParams();
  const { streams, cancelStream, startStream } = useChatStreams();
  const [canvases, setCanvases] = useState<WorkCanvasSummary[]>([]);
  const [canvas, setCanvas] = useState<WorkCanvasSnapshot | null>(null);
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [codeProjects, setCodeProjects] = useState<CodeProject[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(searchParams.get("node"));
  const [query, setQuery] = useState("");
  const [pickerQuery, setPickerQuery] = useState("");
  const [pickerProjectId, setPickerProjectId] = useState("all");
  const [project, setProject] = useState("all");
  const [pickerOpen, setPickerOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [createName, setCreateName] = useState("");
  const [createError, setCreateError] = useState("");
  const [creating, setCreating] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [leftWidth, setLeftWidth] = useState(256);
  const [rightWidth, setRightWidth] = useState(720);
  const [leftOpen, setLeftOpen] = useState(true);
  const [rightPinned, setRightPinned] = useState(true);
  const [sessionContextMenu, setSessionContextMenu] = useState<{ node: WorkCanvasNode; x: number; y: number } | null>(null);
  const [renamingNode, setRenamingNode] = useState<WorkCanvasNode | null>(null);
  const [sessionName, setSessionName] = useState("");
  const [sessionRenameError, setSessionRenameError] = useState("");
  const [sessionRenaming, setSessionRenaming] = useState(false);
  const [actionMenuOpen, setActionMenuOpen] = useState(false);
  const [templatePickerOpen, setTemplatePickerOpen] = useState(false);
  const [taskDialogOpen, setTaskDialogOpen] = useState(false);
  const [tasks, setTasks] = useState<ProjectTask[]>([]);
  const [tasksLoading, setTasksLoading] = useState(false);
  const [taskDialogError, setTaskDialogError] = useState("");
  const [taskPreview, setTaskPreview] = useState<ProjectTask | null>(null);
  const [taskProjectFilter, setTaskProjectFilter] = useState("all");
  const [taskSearch, setTaskSearch] = useState("");
  const [selectedTaskIds, setSelectedTaskIds] = useState<number[]>([]);
  const [creatingTaskSessions, setCreatingTaskSessions] = useState(false);
  const [workflowNode, setWorkflowNode] = useState<WorkCanvasNode | null>(null);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [workflowRuns, setWorkflowRuns] = useState<WorkflowRun[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [codexModelOptions, setCodexModelOptions] = useState<NodeModelOption[]>([]);
  const [llmModelOptions, setLlmModelOptions] = useState<NodeModelOption[]>([]);
  const [connectionPreview, setConnectionPreview] = useState<ConnectionPreview | null>(null);
  const [selectedLinkId, setSelectedLinkId] = useState<string | null>(null);
  const [interactionMode, setInteractionMode] = useState<CanvasInteractionMode>("drag");
  const [formatting, setFormatting] = useState(false);
  const [miniMapVisible, setMiniMapVisible] = useState(true);
  const [canvasViewport, setCanvasViewport] = useState<CanvasViewport>({ left: 0, top: 0, width: 0, height: 0 });
  const dragRef = useRef<DragState | null>(null);
  const connectionRef = useRef<ConnectionPreview | null>(null);
  const workflowStreamsRef = useRef<Record<string, WorkflowStreamBinding>>({});
  const resizeCleanupRef = useRef<(() => void) | null>(null);
  const canvasAreaRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    try {
      setLeftWidth(Number(localStorage.getItem("work-canvas:left-width")) || 256);
      setRightWidth(Number(localStorage.getItem("work-canvas:right-width")) || 720);
      setLeftOpen(localStorage.getItem("work-canvas:left-open") !== "false");
      setRightPinned(localStorage.getItem("work-canvas:right-pinned") !== "false");
    } catch {
      /* storage can be unavailable in private contexts */
    }
    return () => resizeCleanupRef.current?.();
  }, []);
  useEffect(() => {
    if (!sessionContextMenu) return;
    const close = () => setSessionContextMenu(null);
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") close();
    };
    document.addEventListener("pointerdown", close);
    window.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", close);
      window.removeEventListener("keydown", closeOnEscape);
    };
  }, [sessionContextMenu]);

  const persistPanelState = (key: string, value: string) => {
    try {
      localStorage.setItem(key, value);
    } catch {
      /* ignore */
    }
  };
  const beginResize = (side: "left" | "right", event: React.PointerEvent) => {
    event.preventDefault();
    const startX = event.clientX;
    const startWidth = side === "left" ? leftWidth : rightWidth;
    const move = (next: PointerEvent) => {
      const raw = side === "left" ? startWidth + next.clientX - startX : startWidth + startX - next.clientX;
      const width = Math.min(side === "left" ? 480 : Math.max(520, window.innerWidth - 360), Math.max(side === "left" ? 190 : 420, raw));
      if (side === "left") setLeftWidth(width);
      else setRightWidth(width);
    };
    const finish = () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", finish);
      const element = document.documentElement;
      element.style.cursor = "";
      element.style.userSelect = "";
      const current = side === "left" ? document.querySelector<HTMLElement>("[data-canvas-left]")?.offsetWidth : document.querySelector<HTMLElement>("[data-canvas-right]")?.offsetWidth;
      if (current) persistPanelState(`work-canvas:${side}-width`, String(current));
      resizeCleanupRef.current = null;
    };
    document.documentElement.style.cursor = "col-resize";
    document.documentElement.style.userSelect = "none";
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", finish);
    resizeCleanupRef.current = finish;
  };

  const refreshList = async () => {
    const next = await listWorkCanvases();
    setCanvases(next);
    return next;
  };
  useEffect(() => {
    void Promise.all([refreshList(), listSessions(), getCodeProjects()])
      .then(async ([items, available, projects]) => {
        setSessions(available);
        setCodeProjects(projects);
        const requested = searchParams.get("canvas");
        const id = items.find((x) => x.id === requested)?.id ?? items[0]?.id;
        if (id) setCanvas(await getWorkCanvas(id));
      })
      .catch((value) => setError(value instanceof Error ? value.message : "加载失败"))
      .finally(() => setLoading(false));
  }, []);
  useEffect(() => {
    void getCodexModelPolicy()
      .then((policy) => setCodexModelOptions(policy.models.filter((model) => policy.allowed_model_ids.includes(model.id)).map((model) => ({ id: model.id, name: model.name || model.id, agent: "codex" as const }))))
      .catch(() => setCodexModelOptions([]));
    void getSettings()
      .then((settings) => {
        const profile = activeProfile(settings.catalog, "llm");
        setLlmModelOptions((profile?.models ?? []).filter((model) => model.model).map((model) => ({ id: model.id, name: model.name || model.model, agent: "default" as const })));
      })
      .catch(() => setLlmModelOptions([]));
  }, []);
  useEffect(() => {
    if (!canvas) return;
    const url = new URL(window.location.href);
    url.searchParams.set("canvas", canvas.id);
    if (selectedId) url.searchParams.set("node", selectedId);
    else url.searchParams.delete("node");
    window.history.replaceState(null, "", url);
  }, [canvas?.id, selectedId]);

  const streamBySession = useMemo(
    () =>
      Object.values(streams).reduce<Record<string, ChatStreamRecord>>((map, stream) => {
        map[stream.sessionId] = stream;
        return map;
      }, {}),
    [streams],
  );
  const projects = useMemo(() => Array.from(new Map((canvas?.nodes ?? []).filter((x) => x.session.project_id).map((x) => [String(x.session.project_id), x.session.project_name || "未命名项目"]))).sort((a, b) => a[1].localeCompare(b[1])), [canvas]);
  const visibleNodes = useMemo(() => (canvas?.nodes ?? []).filter((node) => (project === "all" || String(node.session.project_id) === project) && [node.session.title, node.session.project_name, node.session.last_message].some((value) => value?.toLowerCase().includes(query.trim().toLowerCase()))), [canvas, project, query]);
  const selected = canvas?.nodes.find((x) => x.id === selectedId) ?? null;
  const selectedLink = canvas?.edges.find((edge) => edge.id === selectedLinkId) ?? null;
  const workflowModelOptions = workflowNode ? nodeModelOptions(workflowNode, codexModelOptions, llmModelOptions) : [];
  const availableSessions = sessions.filter((session) => !canvas?.nodes.some((node) => node.session_id === session.id)).filter((session) => (pickerProjectId === "all" || String(session.project_id) === pickerProjectId) && [session.title, session.project_name, session.last_message].some((value) => value?.toLowerCase().includes(pickerQuery.trim().toLowerCase())));
  const runningCount = Object.values(streams).filter((x) => x.status === "streaming").length;
  const nodeTreeGroups = useMemo(() => {
    const nodesById = new Map(visibleNodes.map((node) => [node.id, node]));
    const inbound = new Map(visibleNodes.map((node) => [node.id, [] as string[]]));
    const outbound = new Map(visibleNodes.map((node) => [node.id, [] as string[]]));
    (canvas?.edges ?? []).forEach((edge) => {
      if (!nodesById.has(edge.source_node_id) || !nodesById.has(edge.target_node_id)) return;
      outbound.get(edge.source_node_id)?.push(edge.target_node_id);
      inbound.get(edge.target_node_id)?.push(edge.source_node_id);
    });
    const depthCache = new Map<string, number>();
    const getDepth = (id: string, path = new Set<string>()): number => {
      if (depthCache.has(id)) return depthCache.get(id)!;
      if (path.has(id)) return 0;
      const parents = inbound.get(id) ?? [];
      const depth = parents.length === 0 ? 0 : Math.min(4, Math.max(...parents.map((parent) => getDepth(parent, new Set([...path, id])))) + 1);
      depthCache.set(id, depth);
      return depth;
    };
    const groups = new Map<string, { id: string; name: string; nodes: Array<{ node: WorkCanvasNode; depth: number; inbound: number; outbound: number }> }>();
    visibleNodes.forEach((node) => {
      const id = node.session.project_id ? String(node.session.project_id) : "unassigned";
      const group = groups.get(id) ?? { id, name: node.session.project_name || "未归属项目", nodes: [] };
      group.nodes.push({ node, depth: getDepth(node.id), inbound: (inbound.get(node.id) ?? []).length, outbound: (outbound.get(node.id) ?? []).length });
      groups.set(id, group);
    });
    return Array.from(groups.values())
      .sort((a, b) => a.name.localeCompare(b.name))
      .map((group) => ({ ...group, nodes: group.nodes.sort((a, b) => a.depth - b.depth || a.node.position_y - b.node.position_y || a.node.session.title.localeCompare(b.node.session.title)) }));
  }, [canvas?.edges, visibleNodes]);

  const openCanvas = async (id: string) => {
    setLoading(true);
    setError("");
    try {
      setCanvas(await getWorkCanvas(id));
      setSelectedId(null);
    } catch (value) {
      setError(value instanceof Error ? value.message : "加载失败");
    } finally {
      setLoading(false);
    }
  };
  const openCreateDialog = () => {
    setCreateName(`工作画布 ${canvases.length + 1}`);
    setCreateError("");
    setCreateOpen(true);
  };
  const createCanvas = async () => {
    const name = createName.trim();
    if (!name) {
      setCreateError("请填写画布名称。");
      return;
    }
    setCreating(true);
    setCreateError("");
    setError("");
    try {
      const created = await createWorkCanvas(name);
      setCanvas(created);
      await refreshList();
      setCreateOpen(false);
      setCreateName("");
    } catch (value) {
      setCreateError(value instanceof Error ? value.message : "创建失败，请稍后重试。");
    } finally {
      setCreating(false);
    }
  };
  const renameCanvas = async () => {
    if (!canvas) return;
    const name = window.prompt("重命名工作画布", canvas.name)?.trim();
    if (!name || name === canvas.name) return;
    setError("");
    try {
      await renameWorkCanvas(canvas.id, name);
      setCanvas(await getWorkCanvas(canvas.id));
      await refreshList();
    } catch (value) {
      setError(value instanceof Error ? value.message : "重命名失败");
    }
  };
  const archiveCanvas = async () => {
    if (!canvas || !window.confirm(`确定归档“${canvas.name}”吗？画布中的会话不会被删除。`)) return;
    setError("");
    try {
      await archiveWorkCanvas(canvas.id);
      const next = await refreshList();
      setSelectedId(null);
      setCanvas(next[0] ? await getWorkCanvas(next[0].id) : null);
    } catch (value) {
      setError(value instanceof Error ? value.message : "归档失败");
    }
  };
  const addSession = async (session: SessionSummary) => {
    if (!canvas) return;
    const index = canvas.nodes.length;
    setError("");
    try {
      await addWorkCanvasNode(canvas.id, session.id, 80 + (index % 3) * 340, 80 + Math.floor(index / 3) * 230);
      setCanvas(await getWorkCanvas(canvas.id));
      await refreshList();
      setPickerOpen(false);
      setPickerQuery("");
    } catch (value) {
      setError(value instanceof Error ? value.message : "添加会话失败");
    }
  };
  const removeNode = async (node: WorkCanvasNode) => {
    if (!canvas) return;
    setError("");
    try {
      await removeWorkCanvasNode(canvas.id, node.id);
      setCanvas(await getWorkCanvas(canvas.id));
      await refreshList();
      setSelectedId(null);
    } catch (value) {
      setError(value instanceof Error ? value.message : "移除会话失败");
    }
  };
  const deleteNodeSession = async (node: WorkCanvasNode) => {
    if (!canvas || !window.confirm(`确定删除会话“${node.session.title}”吗？此操作会删除完整聊天记录，无法恢复。`)) return;
    setSessionContextMenu(null);
    setError("");
    try {
      const result = await deleteSession(node.session_id);
      if (!result.deleted) throw new Error("会话未删除，请稍后重试。");
      await removeWorkCanvasNode(canvas.id, node.id);
      setCanvas(await getWorkCanvas(canvas.id));
      setSessions((current) => current.filter((session) => session.id !== node.session_id));
      setSelectedId((current) => current === node.id ? null : current);
      await refreshList();
    } catch (value) {
      setError(value instanceof Error ? value.message : "删除会话失败，请稍后重试。");
    }
  };
  const openSessionRename = (node: WorkCanvasNode) => {
    setSessionContextMenu(null);
    setRenamingNode(node);
    setSessionName(node.session.title);
    setSessionRenameError("");
  };
  const saveSessionRename = async () => {
    if (!renamingNode) return;
    const name = sessionName.trim();
    if (!name) {
      setSessionRenameError("请填写会话名称。");
      return;
    }
    setSessionRenaming(true);
    setSessionRenameError("");
    try {
      const result = await renameSession(renamingNode.session_id, name);
      if (!result.ok) throw new Error("会话名称未保存，请稍后重试。");
      setCanvas((current) => current ? { ...current, nodes: current.nodes.map((node) => node.session_id === renamingNode.session_id ? { ...node, session: { ...node.session, title: name } } : node) } : current);
      setSessions((current) => current.map((session) => session.id === renamingNode.session_id ? { ...session, title: name } : session));
      setRenamingNode(null);
    } catch (value) {
      setSessionRenameError(value instanceof Error ? value.message : "重命名失败，请稍后重试。");
    } finally {
      setSessionRenaming(false);
    }
  };
  const openTaskSessionDialog = () => {
    setActionMenuOpen(false);
    setTaskDialogOpen(true);
    setTaskDialogError("");
    setTaskPreview(null);
    setTaskProjectFilter("all");
    setTaskSearch("");
    setSelectedTaskIds([]);
    setTasksLoading(true);
    void listProjectTasks()
      .then(setTasks)
      .catch((value) => setTaskDialogError(value instanceof Error ? value.message : "加载任务列表失败，请稍后重试。"))
      .finally(() => setTasksLoading(false));
  };
  const openSessionPicker = () => {
    setActionMenuOpen(false);
    setPickerQuery("");
    setPickerProjectId("all");
    setPickerOpen(true);
  };
  const openTemplatePicker = () => {
    setActionMenuOpen(false);
    setTemplatePickerOpen(true);
  };
  const useTemplateFromCanvas = (result: { template: PromptTemplate; project_id?: number | null; rendered_content: string }) => {
    const handoffId = `${result.template.id}-${Date.now()}`;
    sessionStorage.setItem("aiagent:pending-template-turn", JSON.stringify({ handoff_id: handoffId, template_id: result.template.id, template_name: result.template.name, project_id: result.project_id ?? null, content: result.rendered_content }));
    const query = new URLSearchParams({ template_handoff: handoffId });
    if (result.project_id) query.set("project", String(result.project_id));
    window.location.assign(`/chat?${query.toString()}`);
  };
  const createEmptySession = async (projectId: number) => {
    if (!canvas) throw new Error("请先选择工作画布。");
    const created = await createSession({ project_id: projectId, title: "新会话" });
    try {
      const index = canvas.nodes.length;
      await addWorkCanvasNode(canvas.id, created.id, 80 + (index % 3) * 340, 80 + Math.floor(index / 3) * 230);
      const nextCanvas = await getWorkCanvas(canvas.id);
      setCanvas(nextCanvas);
      setSelectedId(nextCanvas.nodes.find((node) => node.session_id === created.id)?.id ?? null);
      setSessions((current) => [created, ...current]);
      await refreshList();
      setPickerOpen(false);
      setPickerQuery("");
      setPickerProjectId("all");
    } catch (value) {
      try {
        await deleteSession(created.id);
      } catch {
        // Keep the original error; failed cleanup is safe to retry from the chat list.
      }
      throw value;
    }
  };
  const createTaskSessions = async () => {
    if (!canvas || selectedTaskIds.length === 0) return;
    setCreatingTaskSessions(true);
    setTaskDialogError("");
    try {
      const result = await createProjectTaskChatSessions(selectedTaskIds);
      const offset = canvas.nodes.length;
      for (const [index, item] of result.sessions.entries()) {
        await addWorkCanvasNode(canvas.id, item.session.id, 80 + ((offset + index) % 3) * 340, 80 + Math.floor((offset + index) / 3) * 230);
      }
      const nextCanvas = await getWorkCanvas(canvas.id);
      setCanvas(nextCanvas);
      setSessions(await listSessions());
      setSelectedId(nextCanvas.nodes.find((node) => result.sessions.some((item) => item.session.id === node.session_id))?.id ?? null);
      setTaskDialogOpen(false);
      setSelectedTaskIds([]);
    } catch (value) {
      setTaskDialogError(value instanceof Error ? value.message : "创建会话失败，请稍后重试。");
    } finally {
      setCreatingTaskSessions(false);
    }
  };
  const beginDrag = (event: React.PointerEvent, node: WorkCanvasNode) => {
    if (interactionMode !== "drag") return;
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    dragRef.current = {
      id: node.id,
      startX: event.clientX,
      startY: event.clientY,
      nodeX: node.position_x,
      nodeY: node.position_y,
    };
  };
  const moveDrag = (event: React.PointerEvent) => {
    const drag = dragRef.current;
    if (!drag || !canvas) return;
    const x = Math.max(0, drag.nodeX + event.clientX - drag.startX);
    const y = Math.max(0, drag.nodeY + event.clientY - drag.startY);
    setCanvas({
      ...canvas,
      nodes: canvas.nodes.map((node) => (node.id === drag.id ? { ...node, position_x: x, position_y: y } : node)),
    });
  };
  const endDrag = async () => {
    const drag = dragRef.current;
    dragRef.current = null;
    if (!drag || !canvas) return;
    const node = canvas.nodes.find((x) => x.id === drag.id);
    if (!node) return;
    try {
      const result = await saveWorkCanvasLayout(canvas.id, canvas.version, [
        {
          id: node.id,
          position_x: node.position_x,
          position_y: node.position_y,
        },
      ]);
      setCanvas((current) => (current ? { ...current, version: result.version } : current));
    } catch {
      setCanvas(await getWorkCanvas(canvas.id));
    }
  };
  const startPreparedWorkflow = useCallback((prepared: WorkflowExecution) => {
    const agent = supportedAgent(prepared.agent);
    const request: ChatCompleteRequest = {
      session_id: prepared.session_id,
      message: prepared.message,
      code_project_id: prepared.project_id ?? undefined,
      agent,
      model_id: agent === "codex" ? undefined : prepared.model_id ?? undefined,
      codex_model_id: agent === "codex" ? prepared.model_id ?? undefined : undefined,
      top_k: 6,
      mode: "chat",
    };
    const streamId = startStream(request);
    workflowStreamsRef.current[streamId] = { canvasId: canvas?.id ?? "", runId: prepared.run_id, nodeId: prepared.node_id };
    return streamId;
  }, [canvas?.id, startStream]);
  const executeWorkflow = async (target: WorkCanvasNode) => {
    if (!canvas) throw new Error("请先选择工作画布。");
    const prepared = await prepareWorkflowExecution(canvas.id, target.id);
    try { startPreparedWorkflow(prepared); }
    catch (value) {
      await completeWorkflowRunNode(canvas.id, prepared.run_id, prepared.node_id, "failed", value instanceof Error ? value.message : "无法启动聊天执行。");
      throw value;
    }
  };
  useEffect(() => {
    if (!canvas) return;
    const continueRun = async (event: CustomEvent<{ sessionId?: string; streamId?: string; stopped?: boolean }>, status: "completed" | "failed" | "stopped") => {
      const streamId = event.detail?.streamId;
      const binding = streamId ? workflowStreamsRef.current[streamId] : undefined;
      const node = canvas.nodes.find((item) => item.session_id === event.detail?.sessionId);
      if (binding && binding.canvasId === canvas.id) delete workflowStreamsRef.current[streamId!];
      if (!binding && (!node || !canvas.edges.some((edge) => edge.source_node_id === node.id && edge.relation_type === "produces"))) return;
      try {
        const executions = binding
          ? await completeWorkflowRunNode(canvas.id, binding.runId, binding.nodeId, status, status === "completed" ? undefined : "聊天执行未成功完成。")
          : status === "completed" && node ? await registerCompletedWorkflowNode(canvas.id, node.id) : [];
        for (const execution of executions) {
          try { startPreparedWorkflow(execution); }
          catch (value) { await completeWorkflowRunNode(canvas.id, execution.run_id, execution.node_id, "failed", value instanceof Error ? value.message : "无法自动启动下游节点。"); }
        }
        setCanvas(await getWorkCanvas(canvas.id));
        if (historyOpen) setWorkflowRuns(await listWorkflowRuns(canvas.id));
      } catch (value) {
        setError(value instanceof Error ? value.message : "更新流程运行状态失败");
      }
    };
    const complete = (event: Event) => void continueRun(event as CustomEvent<{ sessionId?: string; streamId?: string; stopped?: boolean }>, "completed");
    const failed = (event: Event) => {
      const streamEvent = event as CustomEvent<{ sessionId?: string; streamId?: string; stopped?: boolean }>;
      void continueRun(streamEvent, streamEvent.detail?.stopped ? "stopped" : "failed");
    };
    window.addEventListener("aiagent:chat-stream-complete", complete);
    window.addEventListener("aiagent:chat-stream-failed", failed);
    return () => {
      window.removeEventListener("aiagent:chat-stream-complete", complete);
      window.removeEventListener("aiagent:chat-stream-failed", failed);
    };
  }, [canvas, historyOpen, startPreparedWorkflow]);
  const completeWorkflowLink = async (sourceId: string, targetId: string) => {
    if (!canvas || sourceId === targetId) return;
    setError("");
    try {
      await createWorkflowLink(canvas.id, sourceId, targetId);
      setCanvas(await getWorkCanvas(canvas.id));
    } catch (value) {
      setError(value instanceof Error ? value.message : "建立流程关系失败");
    }
  };
  const canvasPoint = (clientX: number, clientY: number) => {
    const area = canvasAreaRef.current;
    if (!area) return { x: 0, y: 0 };
    const rect = area.getBoundingClientRect();
    return { x: clientX - rect.left + area.scrollLeft, y: clientY - rect.top + area.scrollTop };
  };
  const updateCanvasViewport = useCallback((element: HTMLElement) => {
    setCanvasViewport({ left: element.scrollLeft, top: element.scrollTop, width: element.clientWidth, height: element.clientHeight });
  }, []);
  useEffect(() => {
    if (canvasAreaRef.current) updateCanvasViewport(canvasAreaRef.current);
  }, [canvas?.id, leftOpen, leftWidth, rightWidth, selectedId, updateCanvasViewport]);
  const navigateMiniMap = (x: number, y: number) => {
    const area = canvasAreaRef.current;
    if (!area) return;
    area.scrollLeft = Math.max(0, Math.min(CANVAS_WIDTH - area.clientWidth, x - area.clientWidth / 2));
    area.scrollTop = Math.max(0, Math.min(CANVAS_HEIGHT - area.clientHeight, y - area.clientHeight / 2));
    updateCanvasViewport(area);
  };
  const formatCanvasNodes = async () => {
    if (!canvas || formatting) return;
    setFormatting(true);
    setError("");
    try {
      const nodesById = new Map(canvas.nodes.map((node) => [node.id, node]));
      const parents = new Map(canvas.nodes.map((node) => [node.id, [] as string[]]));
      canvas.edges.forEach((edge) => {
        if (nodesById.has(edge.source_node_id) && nodesById.has(edge.target_node_id)) parents.get(edge.target_node_id)?.push(edge.source_node_id);
      });
      const levelCache = new Map<string, number>();
      const levelOf = (id: string, path = new Set<string>()): number => {
        if (levelCache.has(id)) return levelCache.get(id)!;
        if (path.has(id)) return 0;
        const sourceIds = parents.get(id) ?? [];
        const level = sourceIds.length === 0 ? 0 : Math.max(...sourceIds.map((sourceId) => levelOf(sourceId, new Set([...path, id])))) + 1;
        levelCache.set(id, level);
        return level;
      };
      const layers = new Map<number, WorkCanvasNode[]>();
      canvas.nodes.forEach((node) => {
        const level = Math.min(5, levelOf(node.id));
        const layer = layers.get(level) ?? [];
        layer.push(node);
        layers.set(level, layer);
      });
      const layout = Array.from(layers.entries()).flatMap(([level, nodes]) =>
        nodes.sort((a, b) => (a.session.project_name || "").localeCompare(b.session.project_name || "") || a.session.title.localeCompare(b.session.title)).map((node, index) => ({
          id: node.id,
          position_x: 88 + level * 360,
          position_y: 108 + index * 318,
        })),
      );
      const result = await saveWorkCanvasLayout(canvas.id, canvas.version, layout);
      const positionById = new Map(layout.map((item) => [item.id, item]));
      setCanvas((current) => current ? { ...current, version: result.version, nodes: current.nodes.map((node) => ({ ...node, ...(positionById.get(node.id) ?? {}) })) } : current);
      navigateMiniMap(0, 0);
    } catch (value) {
      setError(value instanceof Error ? value.message : "整理节点失败");
      setCanvas(await getWorkCanvas(canvas.id));
    } finally {
      setFormatting(false);
    }
  };
  const startConnection = (event: React.PointerEvent<HTMLButtonElement>, node: WorkCanvasNode) => {
    event.preventDefault();
    event.stopPropagation();
    const point = canvasPoint(event.clientX, event.clientY);
    event.currentTarget.setPointerCapture(event.pointerId);
    const connection = { sourceId: node.id, x1: point.x, y1: point.y, x2: point.x, y2: point.y };
    connectionRef.current = connection;
    setConnectionPreview(connection);
  };
  const moveConnection = (event: React.PointerEvent<HTMLButtonElement>) => {
    if (!connectionRef.current) return;
    event.preventDefault();
    event.stopPropagation();
    const point = canvasPoint(event.clientX, event.clientY);
    connectionRef.current = { ...connectionRef.current, x2: point.x, y2: point.y };
    setConnectionPreview(connectionRef.current);
  };
  const endConnection = (event: React.PointerEvent<HTMLButtonElement>) => {
    event.preventDefault();
    event.stopPropagation();
    const target = document.elementFromPoint(event.clientX, event.clientY)?.closest<HTMLElement>("[data-canvas-node-id]");
    const targetId = target?.dataset.canvasNodeId;
    const sourceId = connectionRef.current?.sourceId;
    connectionRef.current = null;
    setConnectionPreview(null);
    if (sourceId && targetId && sourceId !== targetId) void completeWorkflowLink(sourceId, targetId);
  };
  const removeSelectedLink = async () => {
    if (!canvas || !selectedLinkId) return;
    setError("");
    try {
      await deleteWorkCanvasLink(canvas.id, selectedLinkId);
      setCanvas(await getWorkCanvas(canvas.id));
      setSelectedLinkId(null);
    } catch (value) {
      setError(value instanceof Error ? value.message : "删除连线失败");
    }
  };
  const saveNodeModel = async (node: WorkCanvasNode, modelId: string) => {
    if (!canvas || node.model_id === modelId) return;
    setError("");
    try {
      await updateWorkCanvasNode(canvas.id, node.id, { role: node.role ?? "", skills: node.skills ?? [], agent: "codex", model_id: modelId || null });
      setCanvas(await getWorkCanvas(canvas.id));
    } catch (value) {
      setError(value instanceof Error ? value.message : "保存节点模型失败");
    }
  };
  const openWorkflowHistory = async () => {
    if (!canvas) return;
    setHistoryOpen(true);
    setHistoryLoading(true);
    try { setWorkflowRuns(await listWorkflowRuns(canvas.id)); }
    catch (value) { setError(value instanceof Error ? value.message : "加载流程历史失败"); }
    finally { setHistoryLoading(false); }
  };

  return (
    <div className="flex h-dvh min-h-0 overflow-hidden bg-slate-100">
      <main className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 shrink-0 items-center gap-3 border-b border-slate-200 bg-white px-4">
          <LayoutDashboard size={18} className="text-blue-600" />
          <select value={canvas?.id ?? ""} onChange={(e) => void openCanvas(e.target.value)} className="max-w-56 rounded-lg border border-slate-200 px-3 py-2 text-sm font-semibold">
            <option value="" disabled>
              选择工作画布
            </option>
            {canvases.map((item) => (
              <option key={item.id} value={item.id}>
                {item.name} · {item.node_count}
              </option>
            ))}
          </select>
          <button className="secondary-button" onClick={openCreateDialog}>
            <Plus size={14} />
            新建画布
          </button>
          <button className="icon-button" title="重命名画布" disabled={!canvas} onClick={() => void renameCanvas()}>
            <Pencil size={15} />
          </button>
          <button className="icon-button text-slate-500" title="归档画布" disabled={!canvas} onClick={() => void archiveCanvas()}>
            <Archive size={15} />
          </button>
          <button className="secondary-button" disabled={!canvas} onClick={() => void openWorkflowHistory()}>
            <History size={14} />
            运行历史
          </button>
          <div className="ml-auto flex items-center gap-2 text-xs text-slate-500">
            <Activity size={14} />
            <span>
              运行槽位 <b className="text-slate-900">{runningCount} / 10</b>
            </span>
          </div>
        </header>
        <div className="flex min-h-0 flex-1">
          {leftOpen ? (
            <>
              <aside data-canvas-left className="relative shrink-0 border-r border-slate-200 bg-white p-3" style={{ width: leftWidth }}>
                <button
                  type="button"
                  onClick={() => {
                    setLeftOpen(false);
                    persistPanelState("work-canvas:left-open", "false");
                  }}
                  className="absolute right-2 top-2 z-10 grid h-7 w-7 place-items-center rounded-md bg-white text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                  title="收起会话栏"
                >
                  <PanelLeftClose size={15} />
                </button>
                <div className="relative pr-8">
                  <Search size={14} className="absolute left-3 top-3 text-slate-400" />
                  <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="搜索会话、项目、内容" className="h-9 w-full rounded-lg border border-slate-200 pl-9 pr-3 text-xs" />
                </div>
                <div className="relative mt-2">
                  <Filter size={14} className="absolute left-3 top-3 text-slate-400" />
                  <select value={project} onChange={(e) => setProject(e.target.value)} className="h-9 w-full rounded-lg border border-slate-200 pl-9 pr-2 text-xs">
                    <option value="all">全部项目</option>
                    {projects.map(([id, name]) => (
                      <option key={id} value={id}>
                        {name}
                      </option>
                    ))}
                  </select>
                </div>
                <p className="mt-5 px-1 text-[10px] font-semibold tracking-widest text-slate-400">项目层级 · 会话关系</p>
                <div className="workspace-scroll mt-2 max-h-[calc(100dvh-190px)] space-y-3 overflow-y-auto pr-1">
                  {nodeTreeGroups.map((group) => (
                    <section key={group.id} className="overflow-hidden rounded-xl border border-slate-100 bg-slate-50/70">
                      <div className="flex items-center gap-2 border-b border-slate-100 bg-white px-2.5 py-2 text-xs font-semibold text-slate-700">
                        <FolderGit2 size={13} className="text-blue-600" />
                        <span className="min-w-0 flex-1 truncate">{group.name}</span>
                        <span className="text-[10px] font-medium text-slate-400">{group.nodes.length}</span>
                      </div>
                      <div className="p-1">
                        {group.nodes.map(({ node, depth, inbound, outbound }) => (
                          <button key={node.id} onClick={() => setSelectedId(node.id)} onContextMenu={(event) => { event.preventDefault(); setSelectedId(node.id); setSessionContextMenu({ node, x: event.clientX, y: event.clientY }); }} className={`flex w-full items-center gap-1.5 rounded-lg py-2 pr-2 text-left text-xs transition ${selectedId === node.id ? "bg-blue-50 text-blue-700" : "hover:bg-white hover:text-slate-900"}`} style={{ paddingLeft: `${8 + Math.min(depth, 4) * 13}px` }} title={`上游 ${inbound} · 下游 ${outbound}`}>
                            <span className="w-3 shrink-0 text-center text-slate-300">{depth > 0 ? "↳" : "•"}</span>
                            <StatusDot stream={streamBySession[node.session_id]} />
                            <span className="min-w-0 flex-1 truncate">{node.session.title}</span>
                            {(inbound > 0 || outbound > 0) && <span className="shrink-0 text-[10px] text-slate-400">{inbound > 0 ? `←${inbound}` : ""}{inbound > 0 && outbound > 0 ? " " : ""}{outbound > 0 ? `→${outbound}` : ""}</span>}
                          </button>
                        ))}
                      </div>
                    </section>
                  ))}
                  {nodeTreeGroups.length === 0 && <p className="px-2 py-6 text-center text-xs text-slate-400">没有匹配的会话节点</p>}
                </div>
              </aside>
              <div role="separator" aria-label="调整会话栏宽度" onPointerDown={(event) => beginResize("left", event)} className="z-30 -mx-0.5 w-1 cursor-col-resize bg-transparent transition hover:bg-blue-400" />
            </>
          ) : (
            <button
              type="button"
              onClick={() => {
                setLeftOpen(true);
                persistPanelState("work-canvas:left-open", "true");
              }}
              className="m-2 grid h-9 w-9 shrink-0 place-items-center rounded-lg border border-slate-200 bg-white text-slate-500 shadow-sm hover:text-blue-600"
              title="展开会话栏"
            >
              <PanelLeftOpen size={17} />
            </button>
          )}
          <section
            ref={canvasAreaRef}
            className="workspace-scroll relative min-w-0 flex-1 overflow-auto bg-slate-50"
            onScroll={(event) => updateCanvasViewport(event.currentTarget)}
            style={{
              backgroundImage: "radial-gradient(#cbd5e1 1px, transparent 1px)",
              backgroundSize: "24px 24px",
            }}
          >
            {loading && (
              <div className="absolute inset-0 z-20 grid place-items-center bg-white/70">
                <LoaderCircle className="animate-spin text-blue-600" />
              </div>
            )}
            {error && <div className="m-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</div>}
            {!loading && !canvas && <Empty onCreate={openCreateDialog} />}{" "}
            {canvas && (
              <div className="relative h-[1400px] w-[2400px]">
                <div className="sticky left-4 top-3 z-30 flex w-[min(640px,calc(100%-2rem))] items-center gap-1.5 rounded-xl border border-slate-200/90 bg-white/95 p-1.5 shadow-[0_10px_28px_rgba(15,23,42,0.10)] backdrop-blur">
                  <button type="button" onClick={() => setInteractionMode("drag")} aria-pressed={interactionMode === "drag"} className={`inline-flex h-8 items-center gap-1.5 rounded-lg px-2.5 text-xs font-medium transition ${interactionMode === "drag" ? "bg-blue-600 text-white shadow-sm" : "text-slate-600 hover:bg-slate-100"}`}><MousePointer2 size={14} />拖拽模式</button>
                  <button type="button" onClick={() => setInteractionMode("connect")} aria-pressed={interactionMode === "connect"} className={`inline-flex h-8 items-center gap-1.5 rounded-lg px-2.5 text-xs font-medium transition ${interactionMode === "connect" ? "bg-blue-600 text-white shadow-sm" : "text-slate-600 hover:bg-slate-100"}`}><Route size={14} />连线模式</button>
                  <span className="mx-0.5 h-5 border-l border-slate-200" />
                  <button type="button" onClick={() => void formatCanvasNodes()} disabled={formatting} className="inline-flex h-8 items-center gap-1.5 rounded-lg px-2.5 text-xs font-medium text-slate-600 transition hover:bg-slate-100 disabled:cursor-wait disabled:opacity-50"><WandSparkles size={14} className="text-violet-600" />{formatting ? "整理中…" : "整理排版"}</button>
                  <button type="button" onClick={() => setMiniMapVisible((value) => !value)} aria-pressed={miniMapVisible} className={`ml-auto inline-flex h-8 items-center gap-1.5 rounded-lg px-2.5 text-xs font-medium transition ${miniMapVisible ? "bg-slate-100 text-slate-700" : "text-slate-400 hover:bg-slate-100"}`}><MapIcon size={14} />小地图</button>
                </div>
                <svg className="absolute inset-0 h-full w-full" aria-label="会话工作流关系">
                  <defs><marker id="delivery-arrow" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8" fill="none" stroke="#64748b" /></marker></defs>
                  {canvas.edges.map((edge) => {
                    const source = canvas.nodes.find((node) => node.id === edge.source_node_id);
                    const target = canvas.nodes.find((node) => node.id === edge.target_node_id);
                    if (!source || !target) return null;
                    const { x1, y1, x2, y2 } = linkPoints(source, target);
                    const active = selectedLinkId === edge.id;
                    return <g key={edge.id}><line x1={x1} y1={y1} x2={x2} y2={y2} stroke="transparent" strokeWidth="14" className="cursor-pointer" onClick={(event) => { event.stopPropagation(); setSelectedLinkId(edge.id); }} /><line x1={x1} y1={y1} x2={x2} y2={y2} stroke={active ? "#dc2626" : edge.relation_type === "produces" ? "#2563eb" : "#64748b"} strokeWidth={active ? "2.5" : "1.5"} strokeDasharray="6 7" markerEnd="url(#delivery-arrow)" pointerEvents="none" /><text x={(x1 + x2) / 2} y={(y1 + y2) / 2 - 8} textAnchor="middle" className="pointer-events-none fill-slate-500 text-[11px]">{edge.relation_type === "produces" ? "上游输入" : edge.label || "关联"}</text></g>;
                  })}
                  {connectionPreview && <line x1={connectionPreview.x1} y1={connectionPreview.y1} x2={connectionPreview.x2} y2={connectionPreview.y2} stroke="#2563eb" strokeWidth="2" strokeDasharray="5 5" markerEnd="url(#delivery-arrow)" pointerEvents="none" />}
                </svg>
                {selectedLink && (() => { const source = canvas.nodes.find((node) => node.id === selectedLink.source_node_id); const target = canvas.nodes.find((node) => node.id === selectedLink.target_node_id); if (!source || !target) return null; const { x1, y1, x2, y2 } = linkPoints(source, target); return <button type="button" onClick={() => void removeSelectedLink()} className="absolute z-20 inline-flex items-center gap-1 rounded-lg border border-rose-200 bg-white px-2 py-1 text-[11px] font-medium text-rose-600 shadow-sm hover:bg-rose-50" style={{ left: (x1 + x2) / 2, top: (y1 + y2) / 2 + 6, transform: "translateX(-50%)" }}><Trash2 size={12} />删除连线</button>; })()}
                {visibleNodes.map((node) => {
                  const stream = streamBySession[node.session_id];
                  const nodeSkills = node.skills ?? [];
                  const availableNodeModels = nodeModelOptions(node, codexModelOptions, llmModelOptions);
                  const presetModel = node.model_id ? availableNodeModels.find((model) => model.id === node.model_id)?.name ?? node.model_id : null;
                  return (
                  <article
                    key={node.id}
                    data-canvas-node-id={node.id}
                    onPointerDown={(e) => beginDrag(e, node)}
                    onPointerMove={moveDrag}
                    onPointerUp={() => void endDrag()}
                    onDoubleClick={() => setSelectedId(node.id)}
                    style={{
                      transform: `translate(${node.position_x}px, ${node.position_y}px)`,
                    }}
                    className={`absolute left-0 top-0 w-72 select-none rounded-2xl border bg-white p-4 shadow-sm transition-shadow ${interactionMode === "drag" ? "cursor-grab active:cursor-grabbing" : "cursor-default"} ${selectedId === node.id ? "border-blue-400 shadow-lg shadow-blue-100" : "border-slate-200 hover:shadow-md"}`}
                  >
                    <div className="flex items-center gap-2">
                      <StatusDot stream={stream} />
                      <span className="text-[10px] font-semibold uppercase tracking-wider text-slate-400">{statusLabel(stream)}</span>
                      {stream?.status === "streaming" && <button type="button" onPointerDown={(event) => event.stopPropagation()} onClick={(event) => { event.stopPropagation(); cancelStream(stream.id); }} className="ml-1 inline-flex items-center gap-1 rounded-md border border-rose-200 bg-rose-50 px-1.5 py-0.5 text-[10px] font-medium text-rose-600 hover:bg-rose-100" title="结束运行"><Square size={9} fill="currentColor" />结束</button>}
                      <span className={`ml-auto rounded-full px-2 py-0.5 text-[10px] ${node.session.priority === "high" ? "bg-rose-50 text-rose-600" : "bg-slate-100 text-slate-500"}`}>{node.session.priority === "high" ? "高优先级" : "普通"}</span>
                    </div>
                    {interactionMode === "connect" && <><button type="button" onPointerDown={(event) => startConnection(event, node)} onPointerMove={moveConnection} onPointerUp={endConnection} className="absolute -left-2 top-1/2 h-4 w-4 -translate-y-1/2 rounded-full border-2 border-blue-500 bg-white shadow-sm transition hover:scale-125" title="从左侧拖出连线" aria-label="从左侧拖出连线" /><button type="button" onPointerDown={(event) => startConnection(event, node)} onPointerMove={moveConnection} onPointerUp={endConnection} className="absolute -right-2 top-1/2 h-4 w-4 -translate-y-1/2 rounded-full border-2 border-blue-500 bg-white shadow-sm transition hover:scale-125" title="从右侧拖出连线" aria-label="从右侧拖出连线" /><button type="button" onPointerDown={(event) => startConnection(event, node)} onPointerMove={moveConnection} onPointerUp={endConnection} className="absolute left-1/2 -top-2 h-4 w-4 -translate-x-1/2 rounded-full border-2 border-blue-500 bg-white shadow-sm transition hover:scale-125" title="从上侧拖出连线" aria-label="从上侧拖出连线" /><button type="button" onPointerDown={(event) => startConnection(event, node)} onPointerMove={moveConnection} onPointerUp={endConnection} className="absolute bottom-[-0.5rem] left-1/2 h-4 w-4 -translate-x-1/2 rounded-full border-2 border-blue-500 bg-white shadow-sm transition hover:scale-125" title="从下侧拖出连线" aria-label="从下侧拖出连线" /></>}
                    <div className="mt-3 flex items-start gap-2"><h2 className="min-w-0 flex-1 line-clamp-2 text-sm font-semibold text-slate-900">{node.session.title}</h2><div className="flex shrink-0 gap-1"><button type="button" onPointerDown={(event) => event.stopPropagation()} onClick={(event) => { event.stopPropagation(); setWorkflowNode(node); }} className="icon-button h-6 w-6" title="配置节点模型、职责与 Skill"><Pencil size={13} /></button><button type="button" onPointerDown={(event) => event.stopPropagation()} onClick={(event) => { event.stopPropagation(); void executeWorkflow(node).catch((value) => setError(value instanceof Error ? value.message : "无法启动节点")); }} className="icon-button h-6 w-6 text-blue-600" title="执行此节点"><Play size={13} fill="currentColor" /></button></div></div>
                    <p className="mt-2 flex items-center gap-1 truncate text-xs text-slate-500">
                      <FolderGit2 size={12} />
                      {node.session.project_name || "未归属项目"}
                    </p>
                    <p className="mt-2 flex items-center gap-1 truncate text-[11px] text-violet-600" title={modelLabel(node.session, stream)}>
                      <Activity size={12} />
                      {modelLabel(node.session, stream)}
                    </p>
                    <button type="button" onPointerDown={(event) => event.stopPropagation()} onClick={(event) => { event.stopPropagation(); setWorkflowNode(node); }} className="mt-1 block max-w-full truncate text-left text-[11px] text-fuchsia-700 hover:text-fuchsia-900 hover:underline" title="点击选择节点执行模型">预设模型：{presetModel || "沿用会话（点击选择）"}</button>
                    <p className="mt-2 truncate text-[11px] text-blue-700" title={node.role || undefined}>职责：{node.role || "未设置"}</p>
                    <p className="mt-1 truncate text-[11px] text-violet-700" title={nodeSkills.join("、") || undefined}>Skill：{nodeSkills.length > 0 ? nodeSkills.join("、") : "未设置"}</p>
                    <p className="mt-3 line-clamp-2 min-h-10 text-xs leading-5 text-slate-500">{node.session.last_message || "暂无会话内容"}</p>
                    <div className="mt-3 border-t border-slate-100 pt-3 text-[10px] text-slate-400">
                      {node.session.message_count} 条消息 · {new Date(node.session.updated_at).toLocaleString()}
                    </div>
                  </article>
                  );
                })}
              </div>
            )}
            {canvas && miniMapVisible && <CanvasMiniMap anchorRef={canvasAreaRef} nodes={canvas.nodes} edges={canvas.edges} viewport={canvasViewport} onNavigate={navigateMiniMap} />}
          </section>
          {selected && (
            <>
              <div role="separator" aria-label="调整会话工作区宽度" onPointerDown={(event) => beginResize("right", event)} className="z-30 -mx-0.5 w-1 cursor-col-resize bg-transparent transition hover:bg-blue-400" />
              <aside
                data-canvas-right
                className={`min-w-0 shrink-0 border-l border-slate-200 bg-white shadow-[-8px_0_24px_rgba(15,23,42,0.06)] ${rightPinned ? "relative" : "absolute inset-y-14 right-0 z-40"}`}
                style={{
                  width: Math.min(rightWidth, typeof window === "undefined" ? rightWidth : window.innerWidth - 80),
                }}
              >
                {(() => {
                  const selectedStream = streamBySession[selected.session_id];
                  return <div className="flex h-11 items-center gap-2 border-b border-slate-200 bg-white px-3">
                  <MessageSquare size={15} className="text-blue-600" />
                  <h2 className="min-w-0 flex-1 truncate text-sm font-semibold">{selected.session.title}</h2>
                  <span className="hidden max-w-44 truncate text-[11px] text-violet-600 sm:inline" title={selected.model_id ? "节点预设模型" : "节点尚未预设模型"}>{selected.model_id ? `节点预设 · ${nodeModelOptions(selected, codexModelOptions, llmModelOptions).find((model) => model.id === selected.model_id)?.name ?? selected.model_id}` : "节点模型未预设"}</span>
                  {selectedStream?.status === "streaming" && <button type="button" onClick={() => cancelStream(selectedStream.id)} className="inline-flex h-7 shrink-0 items-center gap-1 rounded-md border border-rose-200 bg-rose-50 px-2 text-[11px] font-medium text-rose-600 hover:bg-rose-100" title="结束当前会话运行"><Square size={11} fill="currentColor" />结束运行</button>}
                  <button
                    type="button"
                    onClick={() => {
                      const next = !rightPinned;
                      setRightPinned(next);
                      persistPanelState("work-canvas:right-pinned", String(next));
                    }}
                    className={`icon-button ${rightPinned ? "text-blue-600" : "text-slate-400"}`}
                    title={rightPinned ? "已锁定到画布，点击改为浮动抽屉" : "锁定到画布"}
                  >
                    {rightPinned ? <Lock size={15} /> : <Pin size={15} />}
                  </button>
                  <button onClick={() => setSelectedId(null)} className="icon-button" title="关闭会话工作区">
                    <X size={16} />
                  </button>
                  <button onClick={() => void removeNode(selected)} className="icon-button text-rose-500" title="从画布移除">
                    <Trash2 size={15} />
                  </button>
                </div>;
                })()}
                <div className="h-[calc(100%-2.75rem)] min-h-0 overflow-hidden">
                  <KnowledgeChatHome embedded embeddedSessionId={selected.session_id} embeddedCodexModelId={nodeExecutionAgent(selected) === "codex" ? selected.model_id : undefined} onEmbeddedCodexModelChange={(modelId) => void saveNodeModel(selected, modelId)} />
                </div>
              </aside>
            </>
          )}
        </div>
      </main>
      {pickerOpen && (
        <SessionPicker
          sessions={availableSessions}
          projects={codeProjects}
          projectId={pickerProjectId}
          query={pickerQuery}
          onProjectChange={setPickerProjectId}
          onQuery={setPickerQuery}
          onClose={() => {
            setPickerOpen(false);
            setPickerQuery("");
            setPickerProjectId("all");
          }}
          onAdd={addSession}
          onCreate={createEmptySession}
        />
      )}{" "}
      {createOpen && (
        <CreateCanvasDialog
          name={createName}
          error={createError}
          busy={creating}
          onChange={(value) => {
            setCreateName(value);
            setCreateError("");
          }}
          onClose={() => {
            if (!creating) setCreateOpen(false);
          }}
          onSubmit={() => void createCanvas()}
        />
      )}
      {historyOpen && canvas && <WorkflowHistoryDialog runs={workflowRuns} loading={historyLoading} onClose={() => setHistoryOpen(false)} onRefresh={() => void openWorkflowHistory()} onOpenSession={(sessionId) => {
        const node = canvas.nodes.find((item) => item.session_id === sessionId);
        if (node) { setSelectedId(node.id); setHistoryOpen(false); return; }
        window.location.assign(`/chat?session=${encodeURIComponent(sessionId)}`);
      }} />}
      {sessionContextMenu && <SessionContextMenu node={sessionContextMenu.node} x={sessionContextMenu.x} y={sessionContextMenu.y} onRename={openSessionRename} onDelete={(node) => void deleteNodeSession(node)} />}
      {renamingNode && <RenameSessionDialog name={sessionName} error={sessionRenameError} busy={sessionRenaming} onChange={(value) => { setSessionName(value); setSessionRenameError(""); }} onClose={() => { if (!sessionRenaming) setRenamingNode(null); }} onSubmit={() => void saveSessionRename()} />}
      {workflowNode && canvas && <NodeWorkflowDialog node={workflowNode} modelOptions={workflowModelOptions} onClose={() => setWorkflowNode(null)} onSave={async (payload) => { await updateWorkCanvasNode(canvas.id, workflowNode.id, payload); setCanvas(await getWorkCanvas(canvas.id)); setWorkflowNode(null); }} />}
      {canvas && <FloatingActions anchorRef={canvasAreaRef} layoutKey={selectedId ?? ""} open={actionMenuOpen} onOpenChange={setActionMenuOpen} onAddExisting={openSessionPicker} onChooseTemplate={openTemplatePicker} onCreateFromTasks={openTaskSessionDialog} />}
      {templatePickerOpen && <PromptTemplateCanvasPicker onClose={() => setTemplatePickerOpen(false)} onUsed={useTemplateFromCanvas} />}
      {taskDialogOpen && <TaskSessionDialog tasks={tasks} loading={tasksLoading} error={taskDialogError} projectFilter={taskProjectFilter} search={taskSearch} selectedTaskIds={selectedTaskIds} busy={creatingTaskSessions} onProjectFilter={setTaskProjectFilter} onSearch={setTaskSearch} onSelectionChange={setSelectedTaskIds} onPreview={setTaskPreview} onClose={() => { if (!creatingTaskSessions) { setTaskDialogOpen(false); setTaskPreview(null); } }} onSubmit={() => void createTaskSessions()} />}
      {taskPreview && <TaskDetailDialog task={taskPreview} onClose={() => setTaskPreview(null)} />}
    </div>
  );
}

function CanvasMiniMap({ anchorRef, nodes, edges, viewport, onNavigate }: { anchorRef: { current: HTMLElement | null }; nodes: WorkCanvasNode[]; edges: WorkCanvasSnapshot["edges"]; viewport: CanvasViewport; onNavigate: (x: number, y: number) => void }) {
  const [position, setPosition] = useState({ left: 16, top: 16 });
  useEffect(() => {
    const updatePosition = () => {
      const bounds = anchorRef.current?.getBoundingClientRect();
      if (!bounds) return;
      setPosition({ left: Math.max(12, bounds.right - 220), top: Math.max(12, bounds.bottom - 168) });
    };
    updatePosition();
    const observer = typeof ResizeObserver === "undefined" || !anchorRef.current ? null : new ResizeObserver(updatePosition);
    if (observer && anchorRef.current) observer.observe(anchorRef.current);
    window.addEventListener("resize", updatePosition);
    return () => {
      observer?.disconnect();
      window.removeEventListener("resize", updatePosition);
    };
  }, [anchorRef, viewport.height, viewport.width]);
  const moveViewport = (event: React.MouseEvent<HTMLButtonElement>) => {
    const bounds = event.currentTarget.getBoundingClientRect();
    onNavigate(((event.clientX - bounds.left) / bounds.width) * CANVAS_WIDTH, ((event.clientY - bounds.top) / bounds.height) * CANVAS_HEIGHT);
  };
  return (
    <div className="fixed z-40 w-52 overflow-hidden rounded-xl border border-slate-300/90 bg-white/95 p-2 shadow-[0_12px_28px_rgba(15,23,42,0.16)] backdrop-blur" style={position}>
      <div className="mb-1.5 flex items-center gap-1.5 px-0.5 text-[10px] font-semibold tracking-wide text-slate-500"><MapIcon size={12} />画布小地图</div>
      <button type="button" onClick={moveViewport} className="block w-full overflow-hidden rounded-lg bg-slate-50 focus:outline-none focus:ring-2 focus:ring-blue-400" aria-label="点击跳转画布位置">
        <svg viewBox={`0 0 ${CANVAS_WIDTH} ${CANVAS_HEIGHT}`} className="block aspect-[12/7] h-auto w-full">
          <rect width={CANVAS_WIDTH} height={CANVAS_HEIGHT} fill="#f8fafc" />
          {edges.map((edge) => {
            const source = nodes.find((node) => node.id === edge.source_node_id);
            const target = nodes.find((node) => node.id === edge.target_node_id);
            if (!source || !target) return null;
            const { x1, y1, x2, y2 } = linkPoints(source, target);
            return <line key={edge.id} x1={x1} y1={y1} x2={x2} y2={y2} stroke={edge.relation_type === "produces" ? "#60a5fa" : "#94a3b8"} strokeWidth="10" strokeDasharray="20 20" />;
          })}
          {nodes.map((node) => <rect key={node.id} x={node.position_x} y={node.position_y} width={NODE_WIDTH} height={NODE_HEIGHT} rx="34" fill={node.role ? "#dbeafe" : "#e2e8f0"} stroke="#60a5fa" strokeWidth="8" />)}
          <rect x={viewport.left} y={viewport.top} width={Math.min(viewport.width, CANVAS_WIDTH)} height={Math.min(viewport.height, CANVAS_HEIGHT)} fill="rgba(37,99,235,0.08)" stroke="#2563eb" strokeWidth="10" />
        </svg>
      </button>
      <p className="mt-1.5 px-0.5 text-[10px] text-slate-400">点击任意位置快速定位</p>
    </div>
  );
}

function WorkflowHistoryDialog({ runs, loading, onClose, onRefresh, onOpenSession }: { runs: WorkflowRun[]; loading: boolean; onClose: () => void; onRefresh: () => void; onOpenSession: (sessionId: string) => void }) {
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const selectedRun = runs.find((run) => run.id === selectedRunId) ?? runs[0] ?? null;
  const titleByNodeId = new Map<string, string>(selectedRun?.steps.map((step): [string, string] => [step.node_id, step.session_title]) ?? []);
  return <div className="fixed inset-0 z-[80] flex items-center justify-center bg-slate-950/35 p-4" role="presentation" onMouseDown={onClose}>
    <section className="flex max-h-[min(760px,calc(100dvh-32px))] w-full max-w-4xl flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl" role="dialog" aria-modal="true" aria-labelledby="workflow-history-title" onMouseDown={(event) => event.stopPropagation()}>
      <div className="flex items-start gap-3 border-b border-slate-100 p-5"><span className="grid h-10 w-10 place-items-center rounded-xl bg-violet-50 text-violet-600"><History size={19} /></span><div className="min-w-0 flex-1"><h2 id="workflow-history-title" className="text-base font-semibold text-slate-900">流程运行历史</h2><p className="mt-1 text-xs leading-5 text-slate-500">每次画布触发都会记录节点链路、上游输入、执行模型与结果；失败会停止后续自动触发。</p></div><button type="button" className="icon-button" onClick={onClose} aria-label="关闭"><X size={16} /></button></div>
      <div className="grid min-h-0 flex-1 md:grid-cols-[230px_minmax(0,1fr)]">
        <aside className="workspace-scroll border-b border-slate-100 p-3 md:border-b-0 md:border-r md:border-slate-100">
          <button type="button" onClick={onRefresh} disabled={loading} className="secondary-button mb-2 w-full justify-center">{loading ? "刷新中…" : "刷新历史"}</button>
          {runs.length === 0 && <p className="px-2 py-4 text-xs leading-5 text-slate-400">暂无画布流程运行记录。</p>}
          <div className="space-y-1">{runs.map((run) => <button type="button" key={run.id} onClick={() => setSelectedRunId(run.id)} className={`w-full rounded-lg px-3 py-2.5 text-left text-xs ${selectedRun?.id === run.id ? "bg-violet-50 text-violet-800" : "hover:bg-slate-50 text-slate-600"}`}><div className="flex items-center gap-2"><span className={`h-2 w-2 rounded-full ${workflowStatusColor(run.status)}`} /><span className="font-medium">{workflowStatusLabel(run.status)}</span><span className="ml-auto text-[10px] text-slate-400">{run.steps.length} 步</span></div><p className="mt-1 truncate text-[11px] text-slate-400">{formatWorkflowTime(run.started_at)}</p></button>)}</div>
        </aside>
        <div className="workspace-scroll min-h-0 overflow-y-auto p-5">
          {!selectedRun ? <div className="grid h-full min-h-48 place-items-center text-sm text-slate-400">选择一条运行记录查看链路。</div> : <><div className="flex flex-wrap items-center gap-2"><span className={`h-2.5 w-2.5 rounded-full ${workflowStatusColor(selectedRun.status)}`} /><span className="text-sm font-semibold text-slate-800">{workflowStatusLabel(selectedRun.status)}</span><span className="text-xs text-slate-400">开始于 {formatWorkflowTime(selectedRun.started_at)}</span>{selectedRun.finished_at && <span className="text-xs text-slate-400">结束于 {formatWorkflowTime(selectedRun.finished_at)}</span>}</div><div className="mt-5 space-y-0">{selectedRun.steps.map((step, index) => <div key={`${step.node_id}:${step.started_at ?? index}`} className="relative flex gap-3 pb-5 last:pb-0"><div className="flex w-5 shrink-0 flex-col items-center"><span className={`mt-1 h-3 w-3 rounded-full ring-4 ring-white ${workflowStatusColor(step.status)}`} />{index < selectedRun.steps.length - 1 && <span className="mt-1 w-px flex-1 bg-violet-200" />}</div><article className="min-w-0 flex-1 rounded-xl border border-slate-200 bg-slate-50/70 p-3"><div className="flex flex-wrap items-center gap-x-2 gap-y-1"><h3 className="text-sm font-semibold text-slate-800">{step.session_title}</h3><span className="rounded-full bg-white px-2 py-0.5 text-[10px] text-slate-500">{workflowStatusLabel(step.status)}</span>{step.model_id && <span className="text-[11px] text-violet-700">{step.model_id}</span>}{step.session_id && <button type="button" onClick={() => onOpenSession(step.session_id!)} className="ml-auto inline-flex items-center gap-1 text-[11px] text-blue-600 hover:underline"><ExternalLink size={12} />查看日志</button>}</div>{step.source_node_ids.length > 0 && <p className="mt-2 text-xs text-slate-500">上游输入：{step.source_node_ids.map((nodeId) => titleByNodeId.get(nodeId) || nodeId.slice(0, 8)).join("、")}</p>}<p className="mt-2 text-[11px] text-slate-400">{formatWorkflowTime(step.started_at)}{step.finished_at ? ` → ${formatWorkflowTime(step.finished_at)}` : " · 执行中"}</p>{step.error && <p className="mt-2 rounded-lg bg-rose-50 px-2.5 py-2 text-xs text-rose-700">{step.error}</p>}</article></div>)}</div></>}
        </div>
      </div>
    </section>
  </div>;
}

function NodeWorkflowDialog({ node, modelOptions, onClose, onSave }: { node: WorkCanvasNode; modelOptions: NodeModelOption[]; onClose: () => void; onSave: (payload: { role: string; skills: string[]; agent: NodeExecutionAgent | null; model_id: string | null }) => Promise<void> }) {
  const [role, setRole] = useState(node.role ?? "");
  const [skills, setSkills] = useState(node.skills ?? []);
  const [skillInput, setSkillInput] = useState("");
  const [modelId, setModelId] = useState(node.model_id ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const codexOptions = modelOptions.filter((model) => model.agent === "codex");
  const cloudOptions = modelOptions.filter((model) => model.agent === "default");
  const savedModelIsUnavailable = Boolean(modelId && !modelOptions.some((model) => model.id === modelId));
  const addSkill = () => {
    const next = skillInput.trim().replace(/[,，]+$/u, "");
    if (!next) return;
    if (skills.length >= 12) return setError("最多可添加 12 个 Skill。");
    if (skills.some((item) => item.toLocaleLowerCase() === next.toLocaleLowerCase())) return setSkillInput("");
    setSkills((items) => [...items, next]);
    setSkillInput("");
    setError("");
  };
  const save = async () => {
    setBusy(true);
    setError("");
    try {
      const selectedModel = modelOptions.find((model) => model.id === modelId);
      await onSave({ role, skills, agent: selectedModel?.agent ?? null, model_id: modelId || null });
    }
    catch (value) { setError(value instanceof Error ? value.message : "保存失败，请稍后重试。"); }
    finally { setBusy(false); }
  };
  return <div className="fixed inset-0 z-[75] flex items-center justify-center bg-slate-950/30 p-4" role="presentation" onMouseDown={onClose}>
    <section className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-5 shadow-2xl" role="dialog" aria-modal="true" aria-labelledby="node-workflow-title" onMouseDown={(event) => event.stopPropagation()}>
      <div className="flex items-start gap-3"><span className="grid h-10 w-10 place-items-center rounded-xl bg-blue-50 text-blue-600"><Pencil size={18} /></span><div className="min-w-0 flex-1"><h2 id="node-workflow-title" className="text-base font-semibold text-slate-900">配置节点编排</h2><p className="mt-1 truncate text-xs text-slate-500">{node.session.title}</p></div><button type="button" className="icon-button" onClick={onClose} disabled={busy} aria-label="关闭"><X size={16} /></button></div>
      <label className="mt-5 block text-sm font-medium text-slate-700">
        执行模型（节点预设）
        <select value={modelId} onChange={(event) => setModelId(event.target.value)} className="mt-2 h-11 w-full rounded-xl border border-slate-200 bg-white px-3 text-sm outline-none focus:border-blue-500 focus:ring-4 focus:ring-blue-50">
          <option value="">沿用会话最近模型</option>
          {savedModelIsUnavailable && <option value={modelId}>已保存：{modelId}</option>}
          {codexOptions.length > 0 && <optgroup label="Codex 本地">{codexOptions.map((model) => <option key={model.id} value={model.id}>{model.name}</option>)}</optgroup>}
          {cloudOptions.length > 0 && <optgroup label="云端模型">{cloudOptions.map((model) => <option key={model.id} value={model.id}>{model.name}</option>)}</optgroup>}
        </select>
        <span className="mt-1.5 block text-xs font-normal leading-5 text-slate-500">选择 Codex 模型会同时把该节点设为 Codex 执行；执行时优先采用这里设置的模型。</span>
      </label>
      <label className="mt-5 block text-sm font-medium text-slate-700">职责<textarea value={role} onChange={(event) => setRole(event.target.value)} maxLength={2000} rows={3} placeholder="例如：把上游结论整理为可验收的开发任务" className="mt-2 w-full resize-y rounded-xl border border-slate-200 px-3 py-2.5 text-sm leading-6 outline-none focus:border-blue-500 focus:ring-4 focus:ring-blue-50" /></label>
      <label className="mt-4 block text-sm font-medium text-slate-700">Skill（可多选、手工填写）<div className="mt-2 rounded-xl border border-slate-200 bg-white p-2 focus-within:border-blue-500 focus-within:ring-4 focus-within:ring-blue-50"><div className="flex flex-wrap gap-1.5">{skills.map((skill) => <span key={skill} className="inline-flex items-center gap-1 rounded-lg bg-violet-50 px-2 py-1 text-xs text-violet-700">{skill}<button type="button" onClick={() => setSkills((items) => items.filter((item) => item !== skill))} aria-label={`移除 Skill：${skill}`} className="text-violet-400 hover:text-rose-600"><X size={12} /></button></span>)}<input value={skillInput} onChange={(event) => setSkillInput(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter" || event.key === ",") { event.preventDefault(); addSkill(); } }} maxLength={160} placeholder={skills.length ? "继续输入后按 Enter" : "例如：front-creator，按 Enter 添加"} className="h-7 min-w-40 flex-1 border-0 px-1 text-sm outline-none" /></div></div></label>
      <p className="mt-2 text-xs leading-5 text-slate-500">Skill 填写后按 Enter 或逗号添加；按住节点四周的蓝色连接点并拖到另一节点即可连线。</p>
      {error && <p role="alert" className="mt-3 rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700">{error}</p>}
      <div className="mt-5 flex justify-end gap-2 border-t border-slate-100 pt-4"><button type="button" className="secondary-button" onClick={onClose} disabled={busy}>取消</button><button type="button" className="primary-button" onClick={() => void save()} disabled={busy}>{busy ? "保存中…" : "保存配置"}</button></div>
    </section>
  </div>;
}

function SessionContextMenu({ node, x, y, onRename, onDelete }: { node: WorkCanvasNode; x: number; y: number; onRename: (node: WorkCanvasNode) => void; onDelete: (node: WorkCanvasNode) => void }) {
  const pointerActionRef = useRef(false);
  const beginRename = (event: React.PointerEvent<HTMLButtonElement>) => {
    event.preventDefault();
    event.stopPropagation();
    pointerActionRef.current = true;
    onRename(node);
  };
  const beginDelete = (event: React.PointerEvent<HTMLButtonElement>) => {
    event.preventDefault();
    event.stopPropagation();
    pointerActionRef.current = true;
    onDelete(node);
  };
  const clickRename = () => {
    if (pointerActionRef.current) return;
    onRename(node);
  };
  const clickDelete = () => {
    if (pointerActionRef.current) return;
    onDelete(node);
  };
  return (
    <div role="menu" aria-label={`${node.session.title} 的操作`} onPointerDown={(event) => event.stopPropagation()} className="fixed z-[60] min-w-40 rounded-xl border border-slate-200 bg-white p-1.5 shadow-xl" style={{ left: Math.min(x, window.innerWidth - 184), top: Math.min(y, window.innerHeight - 112) }}>
      <button type="button" role="menuitem" onPointerDown={beginRename} onClick={clickRename} className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm text-slate-700 hover:bg-blue-50 hover:text-blue-700 focus:bg-blue-50 focus:outline-none">
        <Pencil size={14} />
        重命名会话
      </button>
      <div className="my-1 border-t border-slate-100" />
      <button type="button" role="menuitem" onPointerDown={beginDelete} onClick={clickDelete} className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm text-rose-600 hover:bg-rose-50 focus:bg-rose-50 focus:outline-none">
        <Trash2 size={14} />
        删除会话
      </button>
    </div>
  );
}

function RenameSessionDialog({ name, error, busy, onChange, onClose, onSubmit }: { name: string; error: string; busy: boolean; onChange: (value: string) => void; onClose: () => void; onSubmit: () => void }) {
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) onClose();
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [busy, onClose]);
  return (
    <div className="fixed inset-0 z-[70] flex items-center justify-center bg-slate-950/45 p-4 backdrop-blur-[2px]" role="presentation" onMouseDown={onClose}>
      <section className="w-full max-w-md rounded-2xl border border-slate-200 bg-white shadow-[0_24px_80px_rgba(15,23,42,0.28)]" role="dialog" aria-modal="true" aria-labelledby="rename-session-title" onMouseDown={(event) => event.stopPropagation()}>
        <form onSubmit={(event) => { event.preventDefault(); onSubmit(); }} className="p-5">
          <div className="flex items-start gap-3">
            <span className="grid h-10 w-10 place-items-center rounded-xl bg-blue-50 text-blue-600"><Pencil size={19} /></span>
            <div className="min-w-0 flex-1">
              <h2 id="rename-session-title" className="text-base font-semibold text-slate-900">重命名会话</h2>
              <p className="mt-1 text-xs leading-5 text-slate-500">仅更新会话名称，不影响已有消息或双击打开会话。</p>
            </div>
          </div>
          <label htmlFor="work-canvas-session-name" className="mt-5 block text-sm font-medium text-slate-800">会话名称</label>
          <input id="work-canvas-session-name" autoFocus value={name} maxLength={160} onChange={(event) => onChange(event.target.value)} className={`mt-2 h-11 w-full rounded-xl border px-3.5 text-sm outline-none focus:ring-4 ${error ? "border-rose-300 focus:border-rose-400 focus:ring-rose-50" : "border-slate-200 focus:border-blue-500 focus:ring-blue-50"}`} aria-describedby="work-canvas-session-name-error" />
          {error && <p id="work-canvas-session-name-error" role="alert" className="mt-3 rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700">{error}</p>}
          <div className="mt-6 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <button type="button" disabled={busy} onClick={onClose} className="h-10 rounded-lg px-4 text-sm font-medium text-slate-600 hover:bg-slate-100 disabled:opacity-40">取消</button>
            <button type="submit" disabled={busy || !name.trim()} className="inline-flex h-10 items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 text-sm font-semibold text-white hover:bg-blue-700 focus:outline-none focus:ring-4 focus:ring-blue-100 disabled:cursor-not-allowed disabled:bg-slate-300">{busy && <LoaderCircle size={16} className="animate-spin" />}保存</button>
          </div>
        </form>
      </section>
    </div>
  );
}

function FloatingActions({ anchorRef, layoutKey, open, onOpenChange, onAddExisting, onChooseTemplate, onCreateFromTasks }: { anchorRef: { current: HTMLElement | null }; layoutKey: string; open: boolean; onOpenChange: (value: boolean) => void; onAddExisting: () => void; onChooseTemplate: () => void; onCreateFromTasks: () => void }) {
  const rootRef = useRef<HTMLDivElement | null>(null);
  const [position, setPosition] = useState({ right: 20, bottom: 20 });
  useEffect(() => {
    const updatePosition = () => {
      const bounds = anchorRef.current?.getBoundingClientRect();
      if (!bounds) return;
      setPosition({ right: Math.max(16, window.innerWidth - bounds.right + 20), bottom: Math.max(16, window.innerHeight - bounds.bottom + 20) });
    };
    updatePosition();
    const observer = typeof ResizeObserver === "undefined" || !anchorRef.current ? null : new ResizeObserver(updatePosition);
    if (observer && anchorRef.current) observer.observe(anchorRef.current);
    window.addEventListener("resize", updatePosition);
    return () => {
      observer?.disconnect();
      window.removeEventListener("resize", updatePosition);
    };
  }, [anchorRef, layoutKey]);
  useEffect(() => {
    if (!open) return;
    const closeWhenOutside = (event: PointerEvent) => {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) onOpenChange(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onOpenChange(false);
    };
    document.addEventListener("pointerdown", closeWhenOutside);
    window.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeWhenOutside);
      window.removeEventListener("keydown", closeOnEscape);
    };
  }, [onOpenChange, open]);
  return (
    <div ref={rootRef} style={position} className="fixed z-40 flex flex-col items-end gap-3">
      <button type="button" onClick={() => onOpenChange(!open)} aria-expanded={open} aria-haspopup="menu" aria-controls="work-canvas-floating-actions" className="order-2 grid h-14 w-14 place-items-center rounded-full bg-blue-600 text-white shadow-[0_10px_24px_rgba(37,99,235,0.36)] transition hover:bg-blue-700 focus:outline-none focus:ring-4 focus:ring-blue-200 active:scale-95" aria-label={open ? "收起画布快捷操作" : "展开画布快捷操作"}>
        {open ? <ChevronUp size={24} /> : <Plus size={25} />}
      </button>
      {open && (
        <div id="work-canvas-floating-actions" role="menu" aria-label="画布快捷操作" className="order-1 w-[min(18rem,calc(100vw-2.5rem))] rounded-2xl border border-slate-200 bg-white p-2 shadow-[0_16px_48px_rgba(15,23,42,0.18)]">
          <button type="button" role="menuitem" onClick={onAddExisting} className="flex w-full items-center gap-3 rounded-xl px-3 py-3 text-left text-sm text-slate-700 transition hover:bg-blue-50 hover:text-blue-700 focus:bg-blue-50 focus:outline-none focus:ring-2 focus:ring-blue-200">
            <span className="grid h-9 w-9 place-items-center rounded-lg bg-slate-100 text-slate-600"><MessageSquare size={18} /></span>
            <span><b className="block font-semibold">添加已有会话</b><span className="mt-0.5 block text-xs text-slate-500">按 AiAgent 项目筛选后，快速加入画布</span></span>
          </button>
          <button type="button" role="menuitem" onClick={onChooseTemplate} className="flex w-full items-center gap-3 rounded-xl px-3 py-3 text-left text-sm text-slate-700 transition hover:bg-blue-50 hover:text-blue-700 focus:bg-blue-50 focus:outline-none focus:ring-2 focus:ring-blue-200">
            <span className="grid h-9 w-9 place-items-center rounded-lg bg-violet-50 text-violet-600"><LayoutTemplate size={18} /></span>
            <span><b className="block font-semibold">从模板创建会话</b><span className="mt-0.5 block text-xs text-slate-500">选择模板并填写变量后，预填到新的聊天草稿</span></span>
          </button>
          <button type="button" role="menuitem" onClick={onCreateFromTasks} className="flex w-full items-center gap-3 rounded-xl px-3 py-3 text-left text-sm text-slate-700 transition hover:bg-blue-50 hover:text-blue-700 focus:bg-blue-50 focus:outline-none focus:ring-2 focus:ring-blue-200">
            <span className="grid h-9 w-9 place-items-center rounded-lg bg-blue-50 text-blue-600"><ListTodo size={18} /></span>
            <span><b className="block font-semibold">从任务创建会话</b><span className="mt-0.5 block text-xs text-slate-500">批量选择已关联项目的任务，先写入草稿</span></span>
          </button>
        </div>
      )}
    </div>
  );
}

function TaskDetailDialog({ task, onClose }: { task: ProjectTask; onClose: () => void }) {
  return (
    <div className="fixed inset-0 z-[80] flex items-end bg-slate-950/45 p-0 backdrop-blur-[2px] sm:items-center sm:justify-center sm:p-4" role="presentation" onMouseDown={onClose}>
      <section className="flex max-h-[min(42rem,100dvh)] w-full flex-col rounded-t-3xl bg-white shadow-[0_24px_80px_rgba(15,23,42,0.32)] sm:max-w-2xl sm:rounded-2xl" role="dialog" aria-modal="true" aria-labelledby="task-detail-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="flex items-start gap-3 border-b border-slate-100 px-4 py-4 sm:px-5">
          <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-blue-50 text-blue-600"><ListTodo size={20} /></span>
          <div className="min-w-0 flex-1"><h2 id="task-detail-title" className="line-clamp-2 text-base font-semibold text-slate-900">{task.title}</h2><p className="mt-1 text-xs text-slate-500">{task.project_name || "未命名项目"}{task.work_item_id ? ` · 工作项 #${task.work_item_id}` : ""}{task.status ? ` · ${task.status}` : ""}</p></div>
          <button type="button" onClick={onClose} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="关闭详情"><X size={17} /></button>
        </div>
        <div className="workspace-scroll min-h-0 overflow-y-auto p-4 sm:p-5">
          <p className="whitespace-pre-wrap break-words text-sm leading-6 text-slate-700">{task.description || "该任务没有填写详情。"}</p>
          {task.external_url && <a href={task.external_url} target="_blank" rel="noreferrer" className="mt-5 inline-flex text-sm font-medium text-blue-600 hover:text-blue-700 hover:underline">打开原始工作项</a>}
        </div>
        <div className="border-t border-slate-100 p-4 text-right sm:px-5"><button type="button" onClick={onClose} className="h-10 rounded-lg px-4 text-sm font-medium text-slate-600 hover:bg-slate-100">关闭</button></div>
      </section>
    </div>
  );
}

function TaskSessionDialog({ tasks, loading, error, projectFilter, search, selectedTaskIds, busy, onProjectFilter, onSearch, onSelectionChange, onPreview, onClose, onSubmit }: { tasks: ProjectTask[]; loading: boolean; error: string; projectFilter: string; search: string; selectedTaskIds: number[]; busy: boolean; onProjectFilter: (value: string) => void; onSearch: (value: string) => void; onSelectionChange: (ids: number[]) => void; onPreview: (task: ProjectTask) => void; onClose: () => void; onSubmit: () => void }) {
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) onClose();
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [busy, onClose]);
  const projects = Array.from(new Map(tasks.filter((task) => task.project_id).map((task) => [String(task.project_id), task.project_name || "未命名项目"]))).sort((left, right) => left[1].localeCompare(right[1]));
  const normalizedSearch = search.trim().toLowerCase();
  const visibleTasks = tasks.filter((task) => (projectFilter === "all" || String(task.project_id) === projectFilter) && [task.title, task.description, task.work_item_id, task.assignee, task.status].some((value) => value?.toLowerCase().includes(normalizedSearch)));
  const selected = new Set(selectedTaskIds);
  const toggleTask = (taskId: number) => onSelectionChange(selected.has(taskId) ? selectedTaskIds.filter((id) => id !== taskId) : [...selectedTaskIds, taskId]);
  const toggleVisible = () => {
    const visibleIds = visibleTasks.map((task) => task.id);
    const allVisibleSelected = visibleIds.length > 0 && visibleIds.every((id) => selected.has(id));
    onSelectionChange(allVisibleSelected ? selectedTaskIds.filter((id) => !visibleIds.includes(id)) : Array.from(new Set([...selectedTaskIds, ...visibleIds])));
  };
  return (
    <div className="fixed inset-0 z-[70] flex items-end bg-slate-950/45 p-0 backdrop-blur-[2px] sm:items-center sm:justify-center sm:p-4" role="presentation" onMouseDown={onClose}>
      <section className="flex max-h-[min(46rem,100dvh)] w-full flex-col rounded-t-3xl bg-white shadow-[0_24px_80px_rgba(15,23,42,0.28)] sm:max-w-3xl sm:rounded-2xl" role="dialog" aria-modal="true" aria-labelledby="task-session-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="flex items-start gap-3 border-b border-slate-100 px-4 py-4 sm:px-5">
          <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-blue-50 text-blue-600"><ListTodo size={20} /></span>
          <div className="min-w-0 flex-1"><h2 id="task-session-title" className="text-base font-semibold text-slate-900">从任务创建会话</h2><p className="mt-1 text-xs leading-5 text-slate-500">每项任务将创建一个关联 AiAgent 项目的空会话；内容和图片只进入草稿，不会自动发送或执行。</p></div>
          <button type="button" onClick={onClose} disabled={busy} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-700 disabled:opacity-40" aria-label="关闭"><X size={17} /></button>
        </div>
        <div className="grid gap-2 border-b border-slate-100 p-4 sm:grid-cols-[12rem_minmax(0,1fr)] sm:px-5">
          <select value={projectFilter} onChange={(event) => onProjectFilter(event.target.value)} className="h-10 rounded-lg border border-slate-200 bg-white px-3 text-sm"><option value="all">全部 AiAgent 项目</option>{projects.map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select>
          <div className="relative"><Search size={15} className="absolute left-3 top-3 text-slate-400" /><input autoFocus value={search} onChange={(event) => onSearch(event.target.value)} placeholder="搜索任务标题、详情或工作项 ID" className="h-10 w-full rounded-lg border border-slate-200 pl-9 pr-3 text-sm" /></div>
        </div>
        {error && <p role="alert" className="mx-4 mt-4 rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700 sm:mx-5">{error}</p>}
        <div className="workspace-scroll min-h-0 flex-1 overflow-y-auto px-4 py-3 sm:px-5">
          {!loading && visibleTasks.length > 0 && <label className="mb-2 flex cursor-pointer items-center gap-2 px-2 py-1 text-xs font-medium text-slate-500"><input type="checkbox" checked={visibleTasks.every((task) => selected.has(task.id))} onChange={toggleVisible} /> 全选当前筛选结果（{visibleTasks.length}）</label>}
          {loading ? <div className="grid h-48 place-items-center text-sm text-slate-500"><LoaderCircle className="animate-spin text-blue-600" /></div> : visibleTasks.length === 0 ? <p className="py-12 text-center text-sm text-slate-400">没有符合条件的已关联任务。</p> : <div className="space-y-1">{visibleTasks.map((task) => <div key={task.id} className="flex items-center gap-3 rounded-xl border border-transparent p-3 hover:border-blue-100 hover:bg-blue-50/60"><label className="flex min-w-0 flex-1 cursor-pointer items-start gap-3"><input type="checkbox" checked={selected.has(task.id)} onChange={() => toggleTask(task.id)} className="mt-1" /><span className="min-w-0 flex-1"><b className="block truncate text-sm text-slate-800">{task.title}</b><span className="mt-1 block truncate text-xs text-slate-500">{task.project_name || "未命名项目"}{task.work_item_id ? ` · #${task.work_item_id}` : ""}{task.status ? ` · ${task.status}` : ""}</span>{task.description && <span className="mt-1 block truncate text-xs text-slate-500">{task.description}</span>}</span></label><button type="button" onClick={() => onPreview(task)} className="shrink-0 rounded-lg px-2.5 py-1.5 text-xs font-medium text-blue-600 hover:bg-blue-100 focus:outline-none focus:ring-2 focus:ring-blue-200">查看详情</button></div>)}</div>}
        </div>
        <div className="flex flex-col-reverse gap-2 border-t border-slate-100 p-4 sm:flex-row sm:items-center sm:justify-between sm:px-5"><span className="text-xs text-slate-500">已选择 <b className="text-slate-800">{selectedTaskIds.length}</b> 项任务</span><div className="flex flex-col-reverse gap-2 sm:flex-row"><button type="button" onClick={onClose} disabled={busy} className="h-10 rounded-lg px-4 text-sm font-medium text-slate-600 hover:bg-slate-100 disabled:opacity-40">取消</button><button type="button" onClick={onSubmit} disabled={busy || selectedTaskIds.length === 0} className="inline-flex h-10 items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 text-sm font-semibold text-white hover:bg-blue-700 focus:outline-none focus:ring-4 focus:ring-blue-100 disabled:cursor-not-allowed disabled:bg-slate-300">{busy && <LoaderCircle size={16} className="animate-spin" />}{busy ? "正在创建…" : `创建 ${selectedTaskIds.length} 个草稿会话`}</button></div></div>
      </section>
    </div>
  );
}

function StatusDot({ stream }: { stream?: { status: string; unread: boolean } }) {
  const color = stream?.status === "streaming" ? "bg-blue-500 animate-pulse" : stream?.status === "error" ? "bg-rose-500" : stream?.unread ? "bg-amber-400" : "bg-emerald-500";
  return <span className={`h-2.5 w-2.5 shrink-0 rounded-full ${color}`} />;
}
function statusLabel(stream?: { status: string; unread: boolean }) {
  return stream?.status === "streaming" ? "运行中" : stream?.status === "error" ? "执行异常" : stream?.unread ? "有新结果" : "已同步";
}
function supportedAgent(agent?: string | null): ChatCompleteRequest["agent"] | undefined {
  return agent === "codex" || agent === "deepseek-harness" || agent === "codebuddy" ? agent : undefined;
}
function workflowStatusLabel(status?: string | null) {
  return status === "completed" ? "已完成" : status === "failed" ? "失败" : status === "stopped" ? "已停止" : status === "running" ? "运行中" : "未知";
}
function workflowStatusColor(status?: string | null) {
  return status === "completed" ? "bg-emerald-500" : status === "failed" ? "bg-rose-500" : status === "stopped" ? "bg-amber-500" : "bg-blue-500";
}
function formatWorkflowTime(value?: string | null) {
  return value ? new Date(value).toLocaleString() : "时间未知";
}
function modelLabel(session: SessionSummary, stream?: ChatStreamRecord) {
  const liveEvent = [...(stream?.events ?? [])].reverse().find((event) => event.model || event.model_id);
  const model = liveEvent?.model || liveEvent?.model_id || session.model || session.model_id;
  const agent = stream?.agent || session.agent;
  const agentLabel = agent === "codex" ? "Codex" : agent === "deepseek-harness" ? "DeepSeek Harness" : agent === "codebuddy" ? "CodeBuddy" : agent;
  return [agentLabel, model].filter(Boolean).join(" · ") || "模型待确定";
}
function Empty({ onCreate }: { onCreate: () => void }) {
  return (
    <div className="grid h-full place-items-center">
      <div className="text-center">
        <LayoutDashboard size={42} className="mx-auto text-slate-300" />
        <h2 className="mt-4 font-semibold">创建第一个多会话工作画布</h2>
        <p className="mt-2 text-sm text-slate-500">把相关会话集中到同一个空间，持续观察运行状态。</p>
        <button className="primary-button mt-5" onClick={onCreate}>
          <Plus size={14} />
          新建画布
        </button>
      </div>
    </div>
  );
}
function CreateCanvasDialog({ name, error, busy, onChange, onClose, onSubmit }: { name: string; error: string; busy: boolean; onChange: (value: string) => void; onClose: () => void; onSubmit: () => void }) {
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) onClose();
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [busy, onClose]);
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/45 p-4 backdrop-blur-[2px]" role="presentation" onMouseDown={onClose}>
      <section className="w-full max-w-md overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-[0_24px_80px_rgba(15,23,42,0.28)]" role="dialog" aria-modal="true" aria-labelledby="create-canvas-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="flex items-start gap-3 border-b border-slate-100 px-5 py-4">
          <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-blue-50 text-blue-600">
            <LayoutDashboard size={20} />
          </span>
          <div className="min-w-0 flex-1">
            <h2 id="create-canvas-title" className="text-base font-semibold text-slate-900">
              新建工作画布
            </h2>
            <p className="mt-1 text-xs leading-5 text-slate-500">为相关会话创建一个可拖拽、可持续追踪的协作空间。</p>
          </div>
          <button type="button" className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 transition hover:bg-slate-100 hover:text-slate-700 disabled:opacity-40" onClick={onClose} disabled={busy} aria-label="关闭">
            <X size={17} />
          </button>
        </div>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            onSubmit();
          }}
          className="p-5"
        >
          <label htmlFor="work-canvas-name" className="block text-sm font-medium text-slate-800">
            画布名称
          </label>
          <input id="work-canvas-name" autoFocus value={name} maxLength={160} onChange={(event) => onChange(event.target.value)} placeholder="例如：产品发布协作" className={`mt-2 h-11 w-full rounded-xl border bg-white px-3.5 text-sm text-slate-900 outline-none transition placeholder:text-slate-400 focus:ring-4 ${error ? "border-rose-300 focus:border-rose-400 focus:ring-rose-50" : "border-slate-200 focus:border-blue-500 focus:ring-blue-50"}`} aria-describedby="work-canvas-name-hint work-canvas-name-error" />
          <div className="mt-2 flex items-center justify-between gap-3">
            <p id="work-canvas-name-hint" className="text-xs text-slate-500">
              名称最多 160 个字符，后续可随时重命名。
            </p>
            <span className="shrink-0 text-xs tabular-nums text-slate-400">{name.length}/160</span>
          </div>
          {error && (
            <p id="work-canvas-name-error" className="mt-3 rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700" role="alert">
              {error}
            </p>
          )}
          <div className="mt-6 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <button type="button" className="h-10 rounded-lg px-4 text-sm font-medium text-slate-600 transition hover:bg-slate-100 disabled:opacity-40" onClick={onClose} disabled={busy}>
              取消
            </button>
            <button type="submit" disabled={busy || !name.trim()} className="inline-flex h-10 items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 text-sm font-semibold text-white shadow-sm transition hover:bg-blue-700 focus:outline-none focus:ring-4 focus:ring-blue-100 disabled:cursor-not-allowed disabled:bg-slate-300">
              {busy ? <LoaderCircle size={16} className="animate-spin" /> : <Plus size={16} />} {busy ? "正在创建…" : "创建画布"}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}
function SessionPicker({ sessions, projects, projectId, query, onProjectChange, onQuery, onClose, onAdd, onCreate }: { sessions: SessionSummary[]; projects: CodeProject[]; projectId: string; query: string; onProjectChange: (value: string) => void; onQuery: (value: string) => void; onClose: () => void; onAdd: (session: SessionSummary) => void; onCreate: (projectId: number) => Promise<void> }) {
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState("");
  const createEmptySession = async () => {
    const selectedProjectId = Number(projectId);
    if (!selectedProjectId) {
      setCreateError("请先选择一个 AiAgent 项目，再新建会话。");
      return;
    }
    setCreating(true);
    setCreateError("");
    try {
      await onCreate(selectedProjectId);
    } catch (value) {
      setCreateError(value instanceof Error ? value.message : "新建会话失败，请稍后重试。");
    } finally {
      setCreating(false);
    }
  };
  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-slate-950/40 p-4">
      <div className="w-full max-w-xl rounded-2xl bg-white shadow-2xl">
        <div className="flex items-center border-b p-4">
          <div className="min-w-0">
            <h2 className="font-semibold">添加或新建会话</h2>
            <p className="mt-0.5 text-xs text-slate-500">已有会话可直接加入，也可以随时创建新的空会话。</p>
          </div>
          <button className="ml-auto icon-button" onClick={onClose}>
            <X size={16} />
          </button>
        </div>
        <div className="p-4">
          <label className="block text-xs font-medium text-slate-600">
            AiAgent 项目
            <select value={projectId} onChange={(event) => { onProjectChange(event.target.value); setCreateError(""); }} className="mt-1.5 h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-sm text-slate-800">
              <option value="all">选择项目以新建或筛选</option>
              {projects.map((project) => <option key={project.id} value={project.id}>{project.display_name || project.name}</option>)}
            </select>
          </label>
          <div className="mt-3 flex flex-col gap-3 rounded-xl border border-blue-100 bg-blue-50/60 p-3 sm:flex-row sm:items-center sm:justify-between">
            <div className="min-w-0">
              <p className="text-sm font-semibold text-slate-800">新建空会话</p>
              <p className="mt-1 text-xs leading-5 text-slate-500">选择项目后创建一个新的会话节点，不会自动发送消息。</p>
            </div>
            <button type="button" onClick={() => void createEmptySession()} disabled={creating || projectId === "all"} className="inline-flex h-9 shrink-0 items-center justify-center gap-1.5 rounded-lg bg-blue-600 px-3 text-xs font-semibold text-white shadow-sm transition hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-200 disabled:cursor-not-allowed disabled:bg-slate-300">
              {creating ? <LoaderCircle size={14} className="animate-spin" /> : <Plus size={14} />}
              {creating ? "正在打开…" : "新建并打开"}
            </button>
          </div>
          {createError && <p role="alert" className="mt-2 rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700">{createError}</p>}
          <div className="relative mt-3">
            <Search size={15} className="absolute left-3 top-3 text-slate-400" />
            <input autoFocus value={query} onChange={(e) => onQuery(e.target.value)} placeholder="筛选会话或内容" className="h-10 w-full rounded-lg border border-slate-200 pl-9 pr-3 text-sm" />
          </div>
          <div className="workspace-scroll mt-3 max-h-96 space-y-1 overflow-y-auto">
            {sessions.map((session) => (
              <button key={session.id} onClick={() => void onAdd(session)} className="flex w-full items-center gap-3 rounded-xl p-3 text-left hover:bg-blue-50">
                <MessageSquare size={16} className="text-blue-500" />
                <span className="min-w-0 flex-1">
                  <b className="block truncate text-sm">{session.title}</b>
                  <span className="mt-1 block truncate text-xs text-slate-400">
                    {session.project_name || "未归属项目"} · {session.message_count} 条消息
                  </span>
                </span>
                <Plus size={15} />
              </button>
            ))}
            {sessions.length === 0 && (
              <div className="py-10 text-center">
                <p className="text-sm text-slate-400">没有可添加的会话</p>
                <p className="mt-1 text-xs text-slate-400">上方的“新建空会话”按钮可直接创建会话节点。</p>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
