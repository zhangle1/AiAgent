"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import {
  type FormEvent,
  type ReactNode,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import {
  Bot,
  ChevronDown,
  ChevronRight,
  CircleStop,
  GitBranch,
  Maximize2,
  Eye,
  FileCode2,
  Folder,
  FolderOpen,
  Loader2,
  MessageSquareText,
  Play,
  Upload,
  Download,
  RefreshCw,
  Send,
  TerminalSquare,
} from "lucide-react";
import { getSettings } from "@/lib/api";
import { streamCompleteChat, type ChatStreamEvent } from "@/lib/chat-api";
import {
  getDashboardApplication,
  bindDashboardApplicationRepository,
  getDashboardFile,
  getDashboardGitStatus,
  getDashboardRuntime,
  getDashboardWorkspaceSnapshot,
  getDashboardTree,
  saveDashboardFile,
  pullDashboardGit,
  pushDashboardGit,
  listDashboardRepositories,
  startDashboardRuntime,
  stopDashboardRuntime,
  type DashboardApplication,
  type DashboardFile,
  type DashboardGitStatus,
  type DashboardRuntime,
  type DashboardRepositoryOption,
  type DashboardTree,
  type DashboardWorkspaceSnapshot,
} from "@/lib/dashboard-application-api";
import {
  activeModel,
  type Catalog,
  type CatalogModel,
} from "@/lib/settings-types";

type ChatMessage = {
  id: string;
  role: "user" | "assistant";
  content: string;
  status?: "streaming" | "done" | "error";
  promptTokens?: number;
  completionTokens?: number;
  calls?: number;
  elapsed?: number;
  trace?: string[];
};

export function DashboardStudio() {
  const id = useParams<{ id: string }>()?.id ?? "";
  const layoutRef = useRef<HTMLDivElement | null>(null);
  const widths = useRef({ left: 292, right: 520, terminal: 220 });
  const [app, setApp] = useState<DashboardApplication | null>(null);
  const [tree, setTree] = useState<DashboardTree | null>(null);
  const [workspaceSnapshot, setWorkspaceSnapshot] =
    useState<DashboardWorkspaceSnapshot | null>(null);
  const [previewRevision, setPreviewRevision] = useState(0);
  const [openDirectories, setOpenDirectories] = useState<
    Record<string, DashboardTree>
  >({});
  const [file, setFile] = useState<DashboardFile | null>(null);
  const [openTabs, setOpenTabs] = useState<DashboardFile[]>([]);
  const [tabMenu, setTabMenu] = useState<{
    path: string;
    x: number;
    y: number;
  } | null>(null);
  const [draft, setDraft] = useState("");
  const [tab, setTab] = useState<"preview" | "editor">("preview");
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [selectedModelId, setSelectedModelId] = useState("");
  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      id: "welcome",
      role: "assistant",
      content: "工作区已连接。AI 的实时工具事件将直接显示在本对话中。",
      status: "done",
    },
  ]);
  const [input, setInput] = useState("");
  const [sending, setSending] = useState(false);
  const [saving, setSaving] = useState(false);
  const [fullscreenPreview, setFullscreenPreview] = useState(false);
  const [runtime, setRuntime] = useState<DashboardRuntime>({
    status: "stopped",
    port: null,
    logs: [],
  });
  const [starting, setStarting] = useState(false);
  const [gitStatus, setGitStatus] = useState<DashboardGitStatus | null>(null);
  const [leftPane, setLeftPane] = useState<"files" | "git">("files");
  const [, setGitOpen] = useState(false);
  const [gitBusy, setGitBusy] = useState<"pull" | "push" | "">("");
  const [gitMessage, setGitMessage] = useState("");
  const [repositories, setRepositories] = useState<DashboardRepositoryOption[]>(
    [],
  );
  const [repositoryName, setRepositoryName] = useState("");
  const [bindingRepository, setBindingRepository] = useState(false);
  const [error, setError] = useState("");
  const models = useMemo(() => listModels(catalog), [catalog]);
  const usage = useMemo(
    () =>
      messages.reduce(
        (total, item) => ({
          prompt: total.prompt + (item.promptTokens ?? 0),
          completion: total.completion + (item.completionTokens ?? 0),
          calls: total.calls + (item.calls ?? 0),
        }),
        { prompt: 0, completion: 0, calls: 0 },
      ),
    [messages],
  );
  const runtimeUrl =
    runtime.port && typeof window !== "undefined"
      ? `${window.location.protocol}//${window.location.hostname}:${runtime.port}`
      : "";
  const isDirty = Boolean(file && file.content !== draft);

  useEffect(() => {
    if (id) void bootstrap();
  }, [id]);
  useEffect(() => {
    if (!file || !isDirty) return;
    const timer = window.setTimeout(() => {
      void save();
    }, 700);
    return () => window.clearTimeout(timer);
  }, [draft, file?.path]);
  useEffect(() => {
    if (!id) return;
    const timer = window.setInterval(() => {
      void refreshTree();
      void refreshRuntime();
    }, 2500);
    return () => window.clearInterval(timer);
  }, [id]);
  async function bootstrap() {
    try {
      const [application, root, snapshot, settings, state, git, codeRepositories] = await Promise.all([
        getDashboardApplication(id),
        getDashboardTree(id),
        getDashboardWorkspaceSnapshot(id),
        getSettings(),
        getDashboardRuntime(id),
        getDashboardGitStatus(id),
        listDashboardRepositories(),
      ]);
      setApp(application);
      setTree(root);
      setWorkspaceSnapshot(snapshot);
      setCatalog(settings.catalog);
      setSelectedModelId(activeModel(settings.catalog, "llm")?.id ?? "");
      setRuntime(state);
      setGitStatus(git);
      setRepositories(codeRepositories);
      setRepositoryName(codeRepositories[0]?.name ?? "");
      const initialEntry =
        snapshot.entryPoints.find((path) => path !== "index.html") ??
        snapshot.entryPoints[0];
      const initial =
        root.files.find(
          (item) => item.name === "src" || item.name === "index.html",
        ) ?? root.files.find((item) => item.editable);
      if (initialEntry) await openFile(initialEntry);
      else if (initial?.editable) await openFile(initial.path);
    } catch (value) {
      setError(messageOf(value, "无法加载应用工作区。"));
    }
  }
  async function refreshTree() {
    try {
      setTree(await getDashboardTree(id));
      setWorkspaceSnapshot(await getDashboardWorkspaceSnapshot(id));
    } catch {}
  }
  async function refreshOpenFile(path: string) {
    try {
      const latest = await getDashboardFile(id, path);
      setOpenTabs((items) =>
        items.map((item) => (item.path === latest.path ? latest : item)),
      );
      if (file?.path === latest.path) {
        setFile(latest);
        setDraft(latest.content);
      }
    } catch {}
  }
  async function refreshRuntime() {
    try {
      setRuntime(await getDashboardRuntime(id));
    } catch {}
  }
  async function refreshGit() {
    try {
      setGitStatus(await getDashboardGitStatus(id));
    } catch {}
  }
  async function pullGit() {
    setGitBusy("pull");
    try {
      const result = await pullDashboardGit(id);
      setGitStatus(result.status);
      setMessages((items) => [
        ...items,
        {
          id: clientId(),
          role: "assistant",
          content: `Git 拉取${result.ok ? "完成" : "失败"}。\n${result.output || "没有新的远程变更。"}`,
          status: result.ok ? "done" : "error",
        },
      ]);
    } catch (value) {
      setError(messageOf(value, "Git 拉取失败。"));
    } finally {
      setGitBusy("");
    }
  }
  async function openGit() {
    setLeftPane("git");
    void refreshGit();
    if (repositories.length) return;
    try {
      const items = await listDashboardRepositories();
      setRepositories(items);
      setRepositoryName((current) => current || items[0]?.name || "");
    } catch (value) {
      setError(messageOf(value, "Unable to load code repositories."));
    }
  }
  async function bindRepository() {
    if (!repositoryName || bindingRepository) return;
    setBindingRepository(true);
    try {
      const next = await bindDashboardApplicationRepository(id, repositoryName);
      setApp(next);
      setTree(await getDashboardTree(id));
      await refreshGit();
    } catch (value) {
      setError(messageOf(value, "Unable to bind the code repository."));
    } finally {
      setBindingRepository(false);
    }
  }
  async function pushGit() {
    setGitBusy("push");
    try {
      const result = await pushDashboardGit(id, gitMessage);
      setGitStatus(result.status);
      setGitMessage("");
      setMessages((items) => [
        ...items,
        {
          id: clientId(),
          role: "assistant",
          content: `Git 提交并推送${result.ok ? "完成" : "失败"}。\n${result.output || "没有需要提交的变更。"}`,
          status: result.ok ? "done" : "error",
        },
      ]);
    } catch (value) {
      setError(messageOf(value, "Git 提交或推送失败。"));
    } finally {
      setGitBusy("");
    }
  }
  async function openFile(path: string) {
    try {
      const opened = openTabs.find((item) => item.path === path);
      const next = opened ?? (await getDashboardFile(id, path));
      if (file && file.path !== path)
        setOpenTabs((items) =>
          items.map((item) =>
            item.path === file.path ? { ...item, content: draft } : item,
          ),
        );
      if (!opened)
        setOpenTabs((items) =>
          items.some((item) => item.path === next.path)
            ? items
            : [...items, next],
        );
      setFile(next);
      setDraft(next.content);
      setTab("editor");
    } catch (value) {
      setError(messageOf(value, "无法打开文件。"));
    }
  }
  function activateFile(next: DashboardFile) {
    if (file && file.path !== next.path)
      setOpenTabs((items) =>
        items.map((item) =>
          item.path === file.path ? { ...item, content: draft } : item,
        ),
      );
    setFile(next);
    setDraft(next.content);
    setTab("editor");
    setTabMenu(null);
  }
  function closeTabs(paths: string[]) {
    const closing = new Set(paths);
    const currentPath = file?.path;
    const currentDraft = file ? { ...file, content: draft } : null;
    const updated = openTabs
      .map((item) =>
        currentDraft && item.path === currentDraft.path ? currentDraft : item,
      )
      .filter((item) => !closing.has(item.path));
    setOpenTabs(updated);
    if (currentPath && closing.has(currentPath)) {
      const fallback = updated.at(-1) ?? null;
      setFile(fallback);
      setDraft(fallback?.content ?? "");
      setTab(fallback ? "editor" : "preview");
    }
    setTabMenu(null);
  }
  async function toggleDirectory(path: string) {
    if (openDirectories[path]) {
      setOpenDirectories((items) => {
        const next = { ...items };
        delete next[path];
        return next;
      });
      return;
    }
    try {
      const child = await getDashboardTree(id, path);
      setOpenDirectories((items) => ({ ...items, [path]: child }));
    } catch (value) {
      setError(messageOf(value, "无法读取目录。"));
    }
  }
  async function save() {
    if (!file || !isDirty) return;
    setSaving(true);
    try {
      const saved = { ...file, content: draft };
      await saveDashboardFile(id, file.path, draft);
      setFile(saved);
      setOpenTabs((items) =>
        items.map((item) => (item.path === saved.path ? saved : item)),
      );
    } catch (value) {
      setError(messageOf(value, "保存失败。"));
    } finally {
      setSaving(false);
    }
  }
  async function run() {
    setStarting(true);
    setError("");
    try {
      setRuntime(await startDashboardRuntime(id));
      setTab("preview");
    } catch (value) {
      const message = messageOf(value, "无法启动服务器预览。");
      setError(message);
      setRuntime((current) => ({
        ...current,
        status: "failed",
        logs: [...current.logs, `[runtime] ${message}`],
      }));
    } finally {
      setStarting(false);
    }
  }
  async function stop() {
    try {
      setRuntime(await stopDashboardRuntime(id));
    } catch (value) {
      setError(messageOf(value, "无法停止预览。"));
    }
  }
  async function send(event: FormEvent) {
    event.preventDefault();
    const prompt = input.trim();
    if (!prompt || sending) return;
    const assistantId = clientId();
    const startedAt = Date.now();
    setInput("");
    setSending(true);
    setMessages((items) => [
      ...items,
      { id: clientId(), role: "user", content: prompt, status: "done" },
      {
        id: assistantId,
        role: "assistant",
        content: "",
        status: "streaming",
        trace: [],
      },
    ]);
    try {
      await streamCompleteChat(
        {
          session_id: `dashboard-${id}`,
          dashboard_application_id: id,
          dashboard_file_path: file?.path,
          dashboard_workspace_revision: workspaceSnapshot?.revision,
          message: prompt,
          model_id: selectedModelId || undefined,
          mode: "write",
          top_k: 6,
        },
        (streamEvent) => {
          const changedFile = streamEvent.content?.match(
            /dashboard_change_(?:applied|validated):([^\s]+)/,
          )?.[1];
          if (streamEvent.type === "tool_result" && changedFile) {
            void refreshTree();
            void refreshOpenFile(changedFile);
            setPreviewRevision((value) => value + 1);
          }
          setMessages((items) =>
            items.map((item) =>
              item.id === assistantId
                ? applyStreamEvent(item, streamEvent, startedAt)
                : item,
            ),
          );
        },
      );
      setMessages((items) =>
        items.map((item) =>
          item.id === assistantId
            ? {
                ...item,
                content: item.content || "代码处理完成。",
                status: "done",
              }
            : item,
        ),
      );
    } catch (value) {
      setMessages((items) =>
        items.map((item) =>
          item.id === assistantId
            ? {
                ...item,
                status: "error",
                content: item.content || messageOf(value, "请求失败。"),
              }
            : item,
        ),
      );
    } finally {
      setSending(false);
    }
  }
  function resize(
    kind: "left" | "right" | "terminal",
    event: React.PointerEvent<HTMLButtonElement>,
  ) {
    event.preventDefault();
    const start = kind === "terminal" ? event.clientY : event.clientX;
    const initial = widths.current[kind];
    let next = initial;
    let frame = 0;
    const apply = (value: number) => {
      next = value;
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() =>
        layoutRef.current?.style.setProperty(`--${kind}-size`, `${value}px`),
      );
    };
    const move = (pointer: PointerEvent) => {
      const delta =
        (kind === "terminal" ? pointer.clientY : pointer.clientX) - start;
      apply(
        kind === "left"
          ? clamp(initial + delta, 220, 480)
          : kind === "right"
            ? clamp(initial - delta, 420, 760)
            : clamp(initial - delta, 140, 440),
      );
    };
    const end = () => {
      cancelAnimationFrame(frame);
      widths.current[kind] = next;
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", end);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", end);
  }
  return (
    <main className="dashboard-studio-shell min-w-[1080px] overflow-hidden bg-[#111318] text-slate-100">
      <header className="flex h-12 items-center justify-between border-b border-white/10 bg-[#191c22] px-4">
        <div className="flex items-center gap-3">
          <Link
            href="/dashboard-applications"
            className="rounded-md border border-white/10 px-2.5 py-1.5 text-[12px] text-slate-300 hover:bg-white/10"
          >
            ← 返回应用列表
          </Link>
          <span className="text-[13px] font-semibold">
            {app?.name ?? "加载工作台…"}
          </span>
          <span
            className={`rounded px-2 py-1 text-[10px] ${runtime.status === "running" ? "bg-emerald-500/15 text-emerald-300" : "bg-slate-500/15 text-slate-400"}`}
          >
            端口：{runtime.port ?? "未启动"} · {runtime.status}
          </span>
        </div>
        <div className="flex gap-2">
          {runtime.status === "running" || runtime.status === "starting" ? (
            <button
              onClick={() => void stop()}
              className="inline-flex h-7 items-center gap-1 rounded border border-red-400/30 px-3 text-[11px] text-red-300"
            >
              <CircleStop size={13} />
              停止
            </button>
          ) : (
            <button
              onClick={() => void run()}
              disabled={starting}
              className="inline-flex h-7 items-center gap-1 rounded bg-blue-600 px-3 text-[11px] font-medium"
            >
              <Play size={13} className={starting ? "animate-pulse" : ""} />
              {starting ? "启动中" : "运行预览"}
            </button>
          )}
        </div>
      </header>
      <div
        ref={layoutRef}
        className="dashboard-layout"
        style={
          {
            "--left-size": "292px",
            "--right-size": "520px",
            "--terminal-size": "220px",
          } as React.CSSProperties
        }
      >
        <aside className="flex min-w-0 bg-[#1a1d23]">
          <div className="flex w-11 shrink-0 flex-col items-center gap-2 border-r border-white/10 py-3">
            <RailIcon
              active={leftPane === "files"}
              icon={<FolderOpen size={18} />}
              onClick={() => setLeftPane("files")}
              label="资源管理器"
            />
            <RailIcon
              active={leftPane === "git"}
              icon={<GitBranch size={18} />}
              label={
                gitStatus?.is_repository
                  ? `Git · ${gitStatus.branch || "detached"}`
                  : "Git 管理"
              }
              onClick={() => void openGit()}
            />
          </div>
          {leftPane === "files" ? (
          <section className="min-w-0 flex-1">
            <div className="flex h-10 items-center justify-between border-b border-white/10 px-3 text-[11px] font-semibold text-slate-400">
              <span>资源管理器</span>
              <button onClick={() => void refreshTree()}>
                <RefreshCw size={13} />
              </button>
            </div>
            <div
              className="border-b border-white/10 bg-black/10 px-3 py-2 text-[10px] text-slate-500"
              title={app?.root_path}
            >
              <div className="text-slate-400">Server workspace</div>
              <div className="truncate font-mono">
                {app?.root_path ?? "Loading..."}
              </div>
            </div>
            <div className="h-[calc(100%-88px)] overflow-auto py-2">
              {tree && (
                <FileTree
                  tree={tree}
                  nested={openDirectories}
                  onDirectory={toggleDirectory}
                  onFile={openFile}
                />
              )}
            </div>
          </section>
          ) : (
            <GitSidePanel
              app={app}
              repositories={repositories}
              repositoryName={repositoryName}
              onRepositoryName={setRepositoryName}
              bindingRepository={bindingRepository}
              gitStatus={gitStatus}
              gitBusy={gitBusy}
              gitMessage={gitMessage}
              onGitMessage={setGitMessage}
              onBind={() => void bindRepository()}
              onPull={() => void pullGit()}
              onPush={() => void pushGit()}
              onRefresh={() => void refreshGit()}
            />
          )}
        </aside>
        <ResizeHandle
          label="调整左侧栏"
          onPointerDown={(event) => resize("left", event)}
        />
        <section
          className="grid min-w-0"
          style={{ gridTemplateRows: "minmax(0,1fr) 6px var(--terminal-size)" }}
        >
          <div className="flex min-h-0 flex-col">
            <div className="flex h-10 items-end overflow-x-auto border-b border-white/10 bg-[#1a1d23]">
              <Tab
                active={tab === "preview"}
                icon={<Eye size={13} />}
                label="运行预览"
                onClick={() => {
                  setTab("preview");
                  setTabMenu(null);
                }}
              />
              {openTabs.map((item, index) => (
                <FileTab
                  key={`tab-${item.path}-${index}`}
                  active={tab === "editor" && file?.path === item.path}
                  label={item.path.split("/").at(-1) ?? item.path}
                  onClick={() => activateFile(item)}
                  onClose={() => closeTabs([item.path])}
                  onContextMenu={(event) => {
                    event.preventDefault();
                    setTabMenu({
                      path: item.path,
                      x: event.clientX,
                      y: event.clientY,
                    });
                  }}
                />
              ))}
            </div>
            <div className="flex h-9 items-center justify-between border-b border-white/10 bg-[#20242c] px-3">
              <span className="truncate text-[11px] text-slate-400">
                {tab === "preview"
                  ? runtimeUrl || "启动 npm 预览后在此显示"
                  : (file?.path ?? "从资源树选择文件")}
              </span>
              {tab === "preview" && runtimeUrl && (
                <button
                  type="button"
                  onClick={() => setFullscreenPreview(true)}
                  className="inline-flex h-6 items-center gap-1 rounded border border-white/10 px-2 text-[11px] text-slate-200 hover:bg-white/10"
                >
                  <Maximize2 size={12} />
                  全屏预览
                </button>
              )}
            </div>
            <div className="min-h-0 flex-1">
              {tab === "preview" ? (
                runtimeUrl ? (
                  <iframe
                    title="运行时预览"
                    key={`${runtimeUrl}-${previewRevision}`}
                    src={runtimeUrl}
                    className="h-full w-full border-0 bg-white"
                  />
                ) : (
                  <RuntimeEmpty onRun={() => void run()} />
                )
              ) : (
                <textarea
                  value={draft}
                  onChange={(event) => setDraft(event.target.value)}
                  spellCheck={false}
                  className="h-full w-full resize-none bg-[#111318] p-5 font-mono text-[12px] leading-6 text-slate-100 outline-none"
                />
              )}
            </div>
            {error && (
              <p className="border-t border-red-400/20 bg-red-950/30 px-3 py-2 text-[11px] text-red-200">
                {error}
              </p>
            )}
          </div>
          <ResizeHandle
            horizontal
            label="调整终端高度"
            onPointerDown={(event) => resize("terminal", event)}
          />
          <TerminalPanel runtime={runtime} url={runtimeUrl} />
        </section>
        <ResizeHandle
          label="调整右侧栏"
          onPointerDown={(event) => resize("right", event)}
        />
        <ChatPanel
          models={models}
          modelId={selectedModelId}
          onModel={setSelectedModelId}
          workspaceName={app?.name}
          workspaceFile={file?.path}
          workspaceEntry={workspaceSnapshot?.entryPoints.join(" · ")}
          workspaceRevision={workspaceSnapshot?.revision}
          usage={usage}
          messages={messages}
          input={input}
          sending={sending}
          onInput={setInput}
          onSubmit={send}
        />
      </div>
      {tabMenu && (
        <TabContextMenu
          x={tabMenu.x}
          y={tabMenu.y}
          onCloseCurrent={() => closeTabs([tabMenu.path])}
          onCloseRight={() => {
            const index = openTabs.findIndex(
              (item) => item.path === tabMenu.path,
            );
            closeTabs(openTabs.slice(index + 1).map((item) => item.path));
          }}
          onCloseOthers={() =>
            closeTabs(
              openTabs
                .filter((item) => item.path !== tabMenu.path)
                .map((item) => item.path),
            )
          }
          onCloseAll={() => closeTabs(openTabs.map((item) => item.path))}
          onDismiss={() => setTabMenu(null)}
        />
      )}
      {false && (
        <div
          className="fixed inset-0 z-40 flex items-start justify-center bg-black/35 pt-20"
          onClick={() => setGitOpen(false)}
        >
          <section
            className="w-[420px] rounded-lg border border-white/10 bg-[#1a1d23] shadow-2xl"
            onClick={(event) => event.stopPropagation()}
          >
            <header className="flex items-center justify-between border-b border-white/10 px-4 py-3">
              <div className="flex items-center gap-2 text-sm font-semibold">
                <GitBranch size={16} />
                Git 管理
              </div>
              <button
                onClick={() => setGitOpen(false)}
                className="text-slate-400 hover:text-white"
              >
                ×
              </button>
            </header>
            <div className="space-y-3 p-4 text-[12px]">
              {gitStatus?.is_repository ? (
                <>
                  <div className="rounded border border-white/10 bg-black/15 p-3 text-slate-300">
                    <div>
                      分支：
                      <span className="text-white">
                        {gitStatus.branch || "detached"}
                      </span>
                    </div>
                    <div className="mt-1">
                      待提交：{gitStatus.changes.length} · ↑ {gitStatus.ahead} ·
                      ↓ {gitStatus.behind}
                    </div>
                    {gitStatus.changes.length > 0 && (
                      <pre className="mt-2 max-h-24 overflow-auto text-[10px] text-amber-200">
                        {gitStatus.changes.join("\n")}
                      </pre>
                    )}
                  </div>
                  <label className="block text-slate-300">
                    提交说明
                    <input
                      value={gitMessage}
                      onChange={(event) => setGitMessage(event.target.value)}
                      placeholder="例如：feat: 更新生产指标"
                      className="mt-1.5 h-9 w-full rounded border border-white/10 bg-[#111318] px-2 text-[12px] text-white outline-none focus:border-blue-500"
                    />
                  </label>
                  <div className="flex justify-end gap-2">
                    <button
                      disabled={Boolean(gitBusy)}
                      onClick={() => void pullGit()}
                      className="inline-flex h-8 items-center gap-1 rounded border border-white/15 px-3 hover:bg-white/10 disabled:opacity-50"
                    >
                      <Download size={13} />
                      {gitBusy === "pull" ? "拉取中" : "拉取"}
                    </button>
                    <button
                      disabled={Boolean(gitBusy)}
                      onClick={() => void pushGit()}
                      className="inline-flex h-8 items-center gap-1 rounded bg-blue-600 px-3 hover:bg-blue-500 disabled:opacity-50"
                    >
                      <Upload size={13} />
                      {gitBusy === "push" ? "提交中" : "提交并推送"}
                    </button>
                  </div>
                </>
              ) : (
                <div className="space-y-3 rounded border border-amber-400/20 bg-amber-400/5 p-3 text-amber-100">
                  <p>
                    Select a registered server repository to migrate this
                    dashboard into its Git workspace.
                  </p>
                  <select
                    value={repositoryName}
                    onChange={(event) => setRepositoryName(event.target.value)}
                    className="h-9 w-full rounded border border-white/10 bg-[#111318] px-2 text-[12px] text-white outline-none"
                  >
                    <option value="">Select a code repository</option>
                    {repositories.map((repository) => (
                      <option key={repository.name} value={repository.name}>
                        {repository.display_name} · {repository.root_path}
                      </option>
                    ))}
                  </select>
                  <button
                    disabled={!repositoryName || bindingRepository}
                    onClick={() => void bindRepository()}
                    className="inline-flex h-8 items-center rounded bg-blue-600 px-3 text-[11px] text-white disabled:opacity-50"
                  >
                    {bindingRepository
                      ? "Binding..."
                      : "Bind and migrate workspace"}
                  </button>
                  当前工作区不在 Git
                  仓库中。请在“新建应用”时选择已克隆的代码库，模板将复制到该仓库下的{" "}
                  <code>.aiagent-dashboard</code> 目录。
                </div>
              )}
            </div>
          </section>
        </div>
      )}
      {fullscreenPreview && runtimeUrl && (
        <div className="fixed inset-0 z-50 bg-[#0d0f13] p-3">
          <div className="flex h-9 items-center justify-between text-[12px] text-slate-200">
            <span className="truncate">运行预览 · {runtimeUrl}</span>
            <button
              type="button"
              onClick={() => setFullscreenPreview(false)}
              className="rounded border border-white/15 px-3 py-1 hover:bg-white/10"
            >
              退出全屏
            </button>
          </div>
          <iframe
            title="全屏运行时预览"
            src={runtimeUrl}
            className="h-[calc(100%-36px)] w-full border-0 bg-white"
          />
        </div>
      )}
    </main>
  );
}

function GitSidePanel({
  app,
  repositories,
  repositoryName,
  onRepositoryName,
  bindingRepository,
  gitStatus,
  gitBusy,
  gitMessage,
  onGitMessage,
  onBind,
  onPull,
  onPush,
  onRefresh,
}: {
  app: DashboardApplication | null;
  repositories: DashboardRepositoryOption[];
  repositoryName: string;
  onRepositoryName: (value: string) => void;
  bindingRepository: boolean;
  gitStatus: DashboardGitStatus | null;
  gitBusy: "pull" | "push" | "";
  gitMessage: string;
  onGitMessage: (value: string) => void;
  onBind: () => void;
  onPull: () => void;
  onPush: () => void;
  onRefresh: () => void;
}) {
  const isRepository = Boolean(gitStatus?.is_repository);
  return (
    <section className="flex min-w-0 flex-1 flex-col">
      <div className="flex h-10 items-center justify-between border-b border-white/10 px-3 text-[11px] font-semibold text-slate-300">
        <span className="inline-flex items-center gap-1.5"><GitBranch size={13} />Git Management</span>
        <button type="button" onClick={onRefresh} className="text-slate-400 hover:text-white" title="Refresh Git status"><RefreshCw size={13} /></button>
      </div>
      <div className="workspace-scroll min-h-0 flex-1 space-y-3 overflow-auto p-3 text-[11px]">
        <div className="rounded border border-white/10 bg-black/10 p-2.5 text-slate-400">
          <div className="text-slate-500">Server workspace</div>
          <div className="mt-1 break-all font-mono text-[10px] text-slate-300">{app?.root_path ?? "Loading..."}</div>
        </div>
        {isRepository ? (
          <>
            <div className="rounded border border-white/10 bg-black/10 p-2.5 text-slate-300">
              <div>Branch: <span className="text-white">{gitStatus?.branch || "detached"}</span></div>
              <div className="mt-1 text-slate-400">Changes {gitStatus?.changes.length ?? 0} · Ahead {gitStatus?.ahead ?? 0} · Behind {gitStatus?.behind ?? 0}</div>
              {gitStatus?.changes.length ? <pre className="workspace-scroll mt-2 max-h-32 overflow-auto whitespace-pre-wrap text-[10px] text-amber-200">{gitStatus.changes.join("\n")}</pre> : null}
            </div>
            <label className="block text-slate-400">Commit message
              <input value={gitMessage} onChange={(event) => onGitMessage(event.target.value)} className="mt-1.5 h-8 w-full rounded border border-white/10 bg-[#111318] px-2 text-[11px] text-white outline-none focus:border-blue-500" placeholder="feat: update dashboard" />
            </label>
            <div className="grid grid-cols-2 gap-2">
              <button disabled={Boolean(gitBusy)} onClick={onPull} className="inline-flex h-8 items-center justify-center gap-1 rounded border border-white/15 text-slate-200 hover:bg-white/10 disabled:opacity-50"><Download size={12} />{gitBusy === "pull" ? "Pulling..." : "Pull"}</button>
              <button disabled={Boolean(gitBusy)} onClick={onPush} className="inline-flex h-8 items-center justify-center gap-1 rounded bg-blue-600 text-white hover:bg-blue-500 disabled:opacity-50"><Upload size={12} />{gitBusy === "push" ? "Pushing..." : "Commit & Push"}</button>
            </div>
          </>
        ) : (
          <>
            <p className="rounded border border-amber-400/20 bg-amber-400/5 p-2.5 leading-5 text-amber-100">This dashboard is not inside a Git repository. Choose a registered server repository to migrate it into that repository's <code>.aiagent-dashboard</code> directory.</p>
            <label className="block text-slate-400">Registered server repository
              <select value={repositoryName} onChange={(event) => onRepositoryName(event.target.value)} className="mt-1.5 h-8 w-full rounded border border-white/10 bg-[#111318] px-2 text-[11px] text-white outline-none">
                <option value="">Select repository</option>
                {repositories.map((repository) => <option key={repository.name} value={repository.name}>{repository.display_name}</option>)}
              </select>
            </label>
            <button disabled={!repositoryName || bindingRepository} onClick={onBind} className="inline-flex h-8 w-full items-center justify-center rounded bg-blue-600 text-[11px] text-white disabled:opacity-50">{bindingRepository ? "Migrating..." : "Bind and migrate workspace"}</button>
          </>
        )}
      </div>
    </section>
  );
}

function ChatPanel({
  models,
  modelId,
  onModel,
  workspaceName,
  workspaceFile,
  workspaceEntry,
  workspaceRevision,
  usage,
  messages,
  input,
  sending,
  onInput,
  onSubmit,
}: {
  models: CatalogModel[];
  modelId: string;
  onModel: (value: string) => void;
  workspaceName?: string;
  workspaceFile?: string;
  workspaceEntry?: string;
  workspaceRevision?: string;
  usage: { prompt: number; completion: number; calls: number };
  messages: ChatMessage[];
  input: string;
  sending: boolean;
  onInput: (value: string) => void;
  onSubmit: (event: FormEvent) => void;
}) {
  return (
    <aside className="flex h-full min-h-0 min-w-0 flex-col overflow-hidden bg-[#1a1d23]">
      <div className="flex h-auto min-h-10 items-center justify-between gap-2 border-b border-white/10 px-3 py-2">
        <div className="flex items-center gap-2 text-[12px] font-semibold">
          <MessageSquareText size={14} />
          AI 对话
        </div>
        <select
          value={modelId}
          onChange={(event) => onModel(event.target.value)}
          className="max-w-[230px] rounded border border-white/10 bg-[#111318] px-2 py-1 text-[10px]"
        >
          <option value="">默认模型</option>
          {models.map((model) => (
            <option key={model.id} value={model.id}>
              {model.name}
            </option>
          ))}
        </select>
      </div>
      <div className="grid grid-cols-3 border-b border-white/10 bg-white/5 text-center text-[10px]">
        <Metric
          label="输入/输出"
          value={`${usage.prompt}/${usage.completion}`}
        />
        <Metric label="调用" value={String(usage.calls)} />
        <Metric label="状态" value={sending ? "生成中" : "就绪"} />
      </div>
      <div className="border-b border-white/10 bg-black/10 px-3 py-2 text-[10px] leading-5 text-slate-400">
        <div className="truncate text-slate-200">工作区：{workspaceName ?? "加载中"}</div>
        <div className="truncate">当前文件：{workspaceFile ?? "未选择"}</div>
        <div className="truncate">入口：{workspaceEntry || "等待识别"}</div>
        {workspaceRevision ? <div className="font-mono text-slate-500">版本：{workspaceRevision.slice(0, 12)}</div> : null}
      </div>
      <div className="workspace-scroll min-h-0 flex-1 space-y-3 overflow-y-auto overscroll-contain p-4">
        {messages.map((message) => (
          <ChatBubble key={message.id} message={message} />
        ))}
      </div>
      <form onSubmit={onSubmit} className="border-t border-white/10 p-3">
        <textarea
          value={input}
          onChange={(event) => onInput(event.target.value)}
          placeholder="描述 React + ECharts 大屏需求…"
          className="h-32 w-full resize-none rounded-lg border border-white/10 bg-[#111318] p-3 text-[12px] outline-none"
        />
        <div className="mt-2 flex justify-end">
          <button
            disabled={!input.trim() || sending}
            className="inline-flex h-8 items-center gap-1.5 rounded bg-blue-600 px-3 text-[11px] disabled:bg-slate-700"
          >
            {sending ? (
              <Loader2 size={13} className="animate-spin" />
            ) : (
              <Send size={13} />
            )}
            发送
          </button>
        </div>
      </form>
    </aside>
  );
}
function TerminalPanel({
  runtime,
  url,
}: {
  runtime: DashboardRuntime;
  url: string;
}) {
  return (
    <section className="min-h-0 overflow-hidden bg-[#0d0f13]">
      <div className="flex h-8 items-center gap-4 border-b border-white/10 px-3 text-[10px]">
        <span className="inline-flex items-center gap-1 text-slate-200">
          <TerminalSquare size={12} />
          终端
        </span>
        <span className="border-l border-white/10 pl-3 text-slate-400">
          端口
        </span>
        <span
          className={
            runtime.status === "failed" ? "text-red-300" : "text-emerald-300"
          }
        >
          {runtime.port ?? "-"} · {runtime.status}
        </span>
        {url && (
          <a
            href={url}
            target="_blank"
            className="text-blue-300 hover:underline"
          >
            打开 {url}
          </a>
        )}
      </div>
      <pre className="workspace-scroll h-[calc(100%-32px)] overflow-auto p-3 font-mono text-[11px] leading-5 text-emerald-300">
        {runtime.logs.length
          ? runtime.logs.map(stripTerminalControlCodes).join("\n")
          : "等待 npm 运行任务。首次启动会自动执行 npm ci 并输出日志。"}
      </pre>
    </section>
  );
}
function RuntimeEmpty({ onRun }: { onRun: () => void }) {
  return (
    <div className="flex h-full flex-col items-center justify-center text-slate-500">
      <Play size={26} className="mb-3" />
      <p className="text-[13px]">预览由服务器 npm 进程提供</p>
      <button
        onClick={onRun}
        className="mt-3 rounded bg-blue-600 px-3 py-2 text-[11px] text-white"
      >
        启动运行时
      </button>
    </div>
  );
}
function FileTree({
  tree,
  nested,
  onDirectory,
  onFile,
  depth = 0,
}: {
  tree: DashboardTree;
  nested: Record<string, DashboardTree>;
  onDirectory: (path: string) => void;
  onFile: (path: string) => void;
  depth?: number;
}) {
  return (
    <div>
      {tree.directories.map((directory, index) => (
        <div key={`directory-${directory.path}-${index}`}>
          <button
            onClick={() => onDirectory(directory.path)}
            className="flex h-7 w-full items-center gap-1 px-3 text-left text-[12px] text-slate-300 hover:bg-white/5"
            style={{ paddingLeft: 10 + depth * 14 }}
          >
            <span className="text-slate-500">
              {nested[directory.path] ? (
                <ChevronDown size={13} />
              ) : (
                <ChevronRight size={13} />
              )}
            </span>
            {nested[directory.path] ? (
              <FolderOpen size={14} className="text-sky-400" />
            ) : (
              <Folder size={14} className="text-sky-400" />
            )}
            <span className="truncate">{directory.name}</span>
          </button>
          {nested[directory.path] && (
            <FileTree
              tree={nested[directory.path]}
              nested={nested}
              onDirectory={onDirectory}
              onFile={onFile}
              depth={depth + 1}
            />
          )}
        </div>
      ))}
      {tree.files.map((item, index) => (
        <button
          key={`file-${item.path}-${index}`}
          disabled={!item.editable}
          onClick={() => item.editable && onFile(item.path)}
          className={`flex h-7 w-full items-center gap-1 px-3 text-left text-[12px] ${item.editable ? "text-slate-400 hover:bg-white/5 hover:text-white" : "text-slate-600"}`}
          style={{ paddingLeft: 27 + depth * 14 }}
        >
          <FileCode2 size={13} />
          <span className="truncate">{item.name}</span>
        </button>
      ))}
    </div>
  );
}
function ResizeHandle({
  onPointerDown,
  label,
  horizontal = false,
}: {
  onPointerDown: (event: React.PointerEvent<HTMLButtonElement>) => void;
  label: string;
  horizontal?: boolean;
}) {
  return (
    <button
      aria-label={label}
      onPointerDown={onPointerDown}
      className={
        horizontal
          ? "cursor-row-resize bg-[#111318] hover:bg-blue-500/60"
          : "cursor-col-resize bg-[#111318] hover:bg-blue-500/60"
      }
    />
  );
}
function RailIcon({
  icon,
  label,
  active = false,
  onClick,
}: {
  icon: ReactNode;
  label: string;
  active?: boolean;
  onClick?: () => void;
}) {
  return (
    <button
      title={label}
      onClick={onClick}
      className={`rounded-md p-2 ${active ? "bg-blue-500/20 text-blue-300" : "text-slate-500"}`}
    >
      {icon}
    </button>
  );
}
function stripTerminalControlCodes(value: string) {
  return value
    .replace(
      /[\u001B\u009B][[\]()#;?]*(?:(?:(?:[a-zA-Z\d]*(?:;[a-zA-Z\d\/#&.:=?%@~_]+)*)?\u0007)|(?:(?:\d{1,4}(?:;\d{0,4})*)?[\dA-PR-TZcf-nq-uy=><~]))/g,
      "",
    )
    .replace(/\r/g, "");
}
function Tab({
  active,
  icon,
  label,
  onClick,
}: {
  active: boolean;
  icon: ReactNode;
  label: string;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      className={`flex h-10 max-w-[280px] items-center gap-2 border-r border-white/10 px-3 text-[11px] ${active ? "bg-[#20242c] text-white" : "text-slate-500"}`}
    >
      {icon}
      <span className="truncate">{label}</span>
    </button>
  );
}
function FileTab({
  active,
  label,
  onClick,
  onClose,
  onContextMenu,
}: {
  active: boolean;
  label: string;
  onClick: () => void;
  onClose: () => void;
  onContextMenu: (event: React.MouseEvent<HTMLDivElement>) => void;
}) {
  return (
    <div
      role="tab"
      tabIndex={0}
      onClick={onClick}
      onContextMenu={onContextMenu}
      onKeyDown={(event) => {
        if (event.key === "Enter" || event.key === " ") onClick();
      }}
      className={`group flex h-10 min-w-0 max-w-[220px] cursor-pointer items-center gap-2 border-r border-white/10 px-3 text-[11px] ${active ? "bg-[#20242c] text-white" : "text-slate-500 hover:bg-white/5"}`}
    >
      <FileCode2 size={13} className="shrink-0" />
      <span className="truncate">{label}</span>
      <button
        type="button"
        aria-label={`关闭 ${label}`}
        onPointerDown={(event) => event.stopPropagation()}
        onClick={(event) => {
          event.stopPropagation();
          onClose();
        }}
        className="ml-1 hidden h-4 w-4 shrink-0 rounded text-slate-400 hover:bg-white/15 hover:text-white group-hover:block"
      >
        ×
      </button>
    </div>
  );
}
function TabContextMenu({
  x,
  y,
  onCloseCurrent,
  onCloseRight,
  onCloseOthers,
  onCloseAll,
  onDismiss,
}: {
  x: number;
  y: number;
  onCloseCurrent: () => void;
  onCloseRight: () => void;
  onCloseOthers: () => void;
  onCloseAll: () => void;
  onDismiss: () => void;
}) {
  const action = (callback: () => void) => () => callback();
  return (
    <div
      className="fixed inset-0 z-50"
      onClick={onDismiss}
      onContextMenu={(event) => {
        event.preventDefault();
        onDismiss();
      }}
    >
      <div
        role="menu"
        onClick={(event) => event.stopPropagation()}
        className="absolute w-36 overflow-hidden rounded-md border border-white/10 bg-[#252a33] py-1 shadow-2xl"
        style={{ left: x, top: y }}
      >
        <ContextMenuItem onClick={action(onCloseCurrent)}>
          关闭当前
        </ContextMenuItem>
        <ContextMenuItem onClick={action(onCloseRight)}>
          关闭右侧
        </ContextMenuItem>
        <ContextMenuItem onClick={action(onCloseOthers)}>
          关闭其他
        </ContextMenuItem>
        <ContextMenuItem onClick={action(onCloseAll)}>关闭全部</ContextMenuItem>
      </div>
    </div>
  );
}
function ContextMenuItem({
  children,
  onClick,
}: {
  children: ReactNode;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      role="menuitem"
      onClick={onClick}
      className="flex w-full px-3 py-1.5 text-left text-[11px] text-slate-200 hover:bg-blue-500/25"
    >
      {children}
    </button>
  );
}
function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="px-1 py-2">
      <div className="text-slate-500">{label}</div>
      <div className="mt-0.5 truncate text-slate-200">{value}</div>
    </div>
  );
}
function ChatBubble({ message }: { message: ChatMessage }) {
  const total = (message.promptTokens ?? 0) + (message.completionTokens ?? 0);
  return (
    <article
      className={`rounded-xl px-3 py-2.5 text-[12px] leading-5 ${message.role === "user" ? "ml-12 bg-blue-600 text-white" : "mr-5 bg-white/7 text-slate-200"}`}
    >
      <div className="whitespace-pre-wrap">
        {message.content || "AI 正在思考…"}
      </div>
      {message.trace?.length ? (
        <div className="mt-2 border-t border-white/10 pt-2 text-[10px] text-slate-400">
          {message.trace.slice(-5).map((item, index) => (
            <div key={`${item}-${index}`}>{item}</div>
          ))}
        </div>
      ) : null}
      <div className="mt-2 text-[10px] text-slate-400">
        {message.status === "streaming" ? "实时流中" : ""}
        {total ? ` · ${total} tokens` : ""}
        {message.calls ? ` · ${message.calls} calls` : ""}
      </div>
    </article>
  );
}
function listModels(catalog: Catalog | null): CatalogModel[] {
  return (
    catalog?.services.llm?.profiles.flatMap((profile) => profile.models) ?? []
  );
}
function applyStreamEvent(
  message: ChatMessage,
  event: ChatStreamEvent,
  startedAt: number,
): ChatMessage {
  const stats = event.metadata ?? {};
  const trace =
    event.type === "loop" ||
    event.type === "tool" ||
    event.type === "tool_request" ||
    event.type === "tool_result"
      ? [
          ...(message.trace ?? []).slice(-11),
          `${event.type}: ${decodeToolOutput(event.content ?? "")}`.trim(),
        ]
      : message.trace;
  const next = {
    ...message,
    promptTokens: numberValue(stats.prompt_tokens),
    completionTokens: numberValue(stats.completion_tokens),
    calls:
      (numberValue(stats.llm_calls) ?? 0) +
      (numberValue(stats.tool_calls) ?? 0),
    elapsed:
      numberValue(stats.elapsed_seconds) ??
      Math.max(1, Math.round((Date.now() - startedAt) / 1000)),
    trace,
  };
  if (event.type === "content")
    return { ...next, content: `${message.content}${event.content ?? ""}` };
  if (event.type === "done")
    return {
      ...next,
      content: message.content || event.content || "",
      status: "done",
    };
  return next;
}
function decodeToolOutput(value: string) {
  return value
    .replace(/\\u([\dA-Fa-f]{4})/g, (_, hex: string) =>
      String.fromCharCode(Number.parseInt(hex, 16)),
    )
    .replace(/\\n/g, "\n")
    .replace(/\\r/g, "\r")
    .replace(/\\t/g, "\t")
    .replace(/\\\"/g, '"')
    .replace(/\\\\/g, "\\");
}
function numberValue(value: unknown): number | undefined {
  const parsed = typeof value === "number" ? value : Number(value);
  return Number.isFinite(parsed) ? parsed : undefined;
}
function clamp(value: number, min: number, max: number) {
  return Math.max(min, Math.min(max, value));
}
function messageOf(value: unknown, fallback: string) {
  return value instanceof Error ? value.message : fallback;
}
function clientId() {
  return (
    globalThis.crypto?.randomUUID?.() ??
    `${Date.now()}-${Math.random().toString(36).slice(2)}`
  );
}
