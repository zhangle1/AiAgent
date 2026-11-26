"use client";

import { type CSSProperties, useEffect, useId, useRef, useState } from "react";
import {
  Boxes,
  CircleCheck,
  Database,
  FileText,
  FolderTree,
  MessageSquareText,
  MoreHorizontal,
  Globe,
  Plus,
  RefreshCw,
  X,
} from "lucide-react";
import { formatOntologyContext, ontologyApi } from "@/lib/ontology-api";
import type {
  OntologyAttachment,
  OntologyModel,
  OntologySource,
  OntologySummary,
} from "@/lib/ontology-types";
import styles from "./ObjectModelWorkspace.module.css";

const treeViews = [
  { id: "models", label: "对象模型", Icon: Boxes },
  { id: "components", label: "组件树", Icon: FolderTree },
] as const;

const drivers: Record<string, [string, number]> = {
  sqlserver: ["SQL Server", 1433],
  oracle: ["Oracle", 2484],
  postgresql: ["PostgreSQL", 5432],
  mysql: ["MySQL", 3306],
  sqlite: ["SQLite", 0],
  http: ["HTTP API", 0],
};
type Props = {
  projectId: number | null;
  repositoryNames: string[];
  onClose: () => void;
  onInsert: (text: string) => void;
  onAttach?: (attachment: OntologyAttachment) => void;
  onModelChange?: (previous: OntologySummary, next: OntologyModel | null) => void;
};
type Selection = { name: string; version: number; ids: string[]; hasNetworkDatabase: boolean };

function selectionFor(model: OntologyModel, ids: string[]): Selection {
  return {
    name: model.name,
    version: model.version,
    ids,
    hasNetworkDatabase:
      model.sources?.some(
        ({ definition }) =>
          ids.includes(definition.id) && !["http", "note", "sqlite"].includes(definition.connection.type),
      ) ?? false,
  };
}

function WidthDivider({
  side,
  controls,
  width,
  onResize,
}: {
  side: "tree" | "config";
  controls: string;
  width: number;
  onResize: (value: number) => void;
}) {
  const start = useRef<{ pointerId: number; x: number; width: number; available: number } | null>(null);
  const tree = side === "tree";
  const min = tree ? 15 : 22;
  const max = tree ? 32 : 40;
  const direction = tree ? 1 : -1;
  const resize = (value: number) => onResize(Math.min(max, Math.max(min, value)));
  return (
    <div
      role="separator"
      tabIndex={0}
      aria-label={tree ? "调整本体树宽度" : "调整组件配置宽度"}
      aria-controls={controls}
      aria-orientation="vertical"
      aria-valuemin={min}
      aria-valuemax={max}
      aria-valuenow={Math.round(width)}
      aria-valuetext={Math.round(width) + "%"}
      className={tree ? styles.treeDivider : styles.configDivider}
      onPointerDown={(event) => {
        const available = event.currentTarget.closest("fieldset")?.clientWidth;
        if (!available || event.button !== 0 || !event.isPrimary) return;
        event.preventDefault();
        event.currentTarget.focus();
        event.currentTarget.setPointerCapture(event.pointerId);
        const paneWidth = event.currentTarget.parentElement!.getBoundingClientRect().width;
        start.current = {
          pointerId: event.pointerId,
          x: event.clientX,
          width: (paneWidth * 100) / available,
          available,
        };
      }}
      onPointerMove={(event) => {
        const drag = start.current;
        if (drag?.pointerId === event.pointerId)
          resize(drag.width + (direction * (event.clientX - drag.x) * 100) / drag.available);
      }}
      onLostPointerCapture={() => {
        start.current = null;
      }}
      onKeyDown={(event) => {
        if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
        event.preventDefault();
        if (event.key === "Home") resize(min);
        else if (event.key === "End") resize(max);
        else {
          const available = event.currentTarget.closest("fieldset")!.clientWidth;
          const visible =
            (event.currentTarget.parentElement!.getBoundingClientRect().width * 100) / available;
          resize(visible + (event.key === "ArrowRight" ? 2 : -2) * direction);
        }
      }}
    />
  );
}

function componentKind(type: string) {
  return type === "note" || type === "http" ? type : "database";
}
function SourceIcon({ type }: { type: string }) {
  return type === "note" ? (
    <FileText size={15} />
  ) : type === "http" ? (
    <Globe size={15} />
  ) : (
    <Database size={15} />
  );
}

function ModelRow({
  item,
  active,
  attached,
  onSelect,
  onRename,
  onDelete,
}: {
  item: OntologySummary;
  active: boolean;
  attached: boolean;
  onSelect: () => void;
  onRename: (name: string, done: () => void) => void;
  onDelete: () => void;
}) {
  const menu = useRef<HTMLDivElement>(null);
  const [edit, setEdit] = useState<string | null>(null);
  function open(x: number, y: number) {
    if (!menu.current) return;
    menu.current.style.left = Math.max(0, Math.min(x, window.innerWidth - 130)) + "px";
    menu.current.style.top = Math.max(0, Math.min(y, window.innerHeight - 88)) + "px";
    menu.current.showPopover();
    menu.current.querySelector("button")?.focus();
  }
  return (
    <div
      className={styles.modelRow}
      onContextMenu={(e) => {
        if (edit !== null) return;
        e.preventDefault();
        open(e.clientX, e.clientY);
      }}
    >
      {edit === null ? (
        <>
          <button className={styles.file} aria-pressed={active} onClick={onSelect}>
            <FileText size={14} />
            {item.name}
            {attached ? " ✓" : ""}
          </button>
          <button
            aria-label={item.name + "的操作"}
            onClick={(e) => {
              const rect = e.currentTarget.getBoundingClientRect();
              open(rect.left, rect.bottom);
            }}
          >
            <MoreHorizontal size={14} />
          </button>
        </>
      ) : (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (edit.trim() === item.name) setEdit(null);
            else if (edit.trim()) onRename(edit.trim(), () => setEdit(null));
          }}
        >
          <label>
            新模型名称
            <input
              autoFocus
              maxLength={100}
              value={edit}
              onFocus={(e) => e.target.select()}
              onChange={(e) => setEdit(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Escape") setEdit(null);
              }}
            />
          </label>
          <button disabled={!edit.trim()}>保存名称</button>
          <button type="button" onClick={() => setEdit(null)}>
            取消
          </button>
        </form>
      )}
      <div popover="auto" ref={menu} className={styles.modelMenu} aria-label={item.name + "操作"}>
        <button
          onClick={() => {
            menu.current?.hidePopover();
            setEdit(item.name);
          }}
        >
          重命名
        </button>
        <button
          onClick={() => {
            menu.current?.hidePopover();
            onDelete();
          }}
        >
          删除模型
        </button>
      </div>
    </div>
  );
}

function newSource(): OntologySource {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  return {
    id: Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join(""),
    label: "数据库连接",
    connection: { type: "sqlserver", host: "", port: 1433, database: "", username: "" },
    relation: "",
    keyColumn: "",
    valueColumn: "",
  };
}

export function ObjectModelWorkspace(props: Props) {
  if (!props.projectId)
    return (
      <section className={styles.empty}>
        选择聊天项目后即可打开本体工作区。<button onClick={props.onClose}>关闭</button>
      </section>
    );
  return <ModelProject key={props.projectId} {...props} projectId={props.projectId} />;
}

function ModelProject({
  projectId,
  repositoryNames,
  onClose,
  onInsert,
  onAttach,
  onModelChange,
}: Props & { projectId: number }) {
  const [models, setModels] = useState<OntologySummary[]>([]);
  const [model, setModel] = useState<OntologyModel | null>(null);
  const [name, setName] = useState("");
  const [treeTab, setTreeTab] = useState<"models" | "components">("models");
  const [creating, setCreating] = useState(false);
  const [promptOpen, setPromptOpen] = useState(false);
  const [treeWidth, setTreeWidth] = useState(20);
  const [configWidth, setConfigWidth] = useState(27);
  const workspaceId = useId();
  const promptButton = useRef<HTMLButtonElement>(null);
  const [source, setSource] = useState<OntologySource | null>(null);
  const [password, setPassword] = useState("");
  const [dirty, setDirty] = useState(false);
  const [testStatus, setTestStatus] = useState<"passed" | "failed" | "testing" | null>(null);
  const tested = testStatus === "passed";
  const [selected, setSelected] = useState<Record<string, Selection>>({});
  const [key, setKey] = useState("");
  const [includeDatabasePasswords, setIncludeDatabasePasswords] = useState(false);
  const [preview, setPreview] = useState("");
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const [error, setError] = useState("");
  const generation = useRef(0);
  const lock = useRef(false);

  function inspect(item: OntologySource | null) {
    setSource(item);
    setPassword("");
    setTestStatus(null);
    setDirty(false);
  }
  function show(next: OntologyModel | null, preserveSelection = false) {
    setModel(next);
    if (next)
      setModels((items) =>
        items.map((item) =>
          item.id === next.id ? { id: next.id, name: next.name, version: next.version } : item,
        ),
      );
    inspect(next?.sources?.[0]?.definition ?? null);
    setPreview("");
    if (next && !preserveSelection)
      setSelected((old) => {
        if (!old[next.id] || old[next.id].version === next.version) return old;
        const updated = { ...old };
        delete updated[next.id];
        return updated;
      });
  }
  async function run<T>(task: () => Promise<T>, done: (value: T) => void, failed?: () => void) {
    if (lock.current) return;
    lock.current = true;
    const epoch = generation.current;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const value = await task();
      if (epoch === generation.current) done(value);
    } catch (e) {
      if (epoch === generation.current) {
        setError(e instanceof Error ? e.message : "请求失败");
        failed?.();
      }
    } finally {
      if (epoch === generation.current) {
        lock.current = false;
        setBusy(false);
      }
    }
  }
  function refresh() {
    void run(
      async () => {
        const items = await ontologyApi.list(projectId);
        const id = items.find((item) => item.id === model?.id)?.id ?? items[0]?.id;
        return { items, next: id ? await ontologyApi.get(projectId, id) : null };
      },
      ({ items, next }) => {
        setModels(items);
        show(next);
        setCreating(!items.length);
      },
    );
  }
  useEffect(() => {
    refresh();
    return () => {
      generation.current++;
      lock.current = false;
    };
    // A new project mounts a new workspace.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId]);
  function discard() {
    return !dirty || window.confirm("放弃当前组件的未保存修改？");
  }
  function change(next: OntologySource, passwordChanged = false) {
    setSource(next);
    setDirty(true);
    if (passwordChanged || next.connection !== source?.connection || next.connection.type === "http")
      setTestStatus(null);
    setPreview("");
    setNotice("");
    setError("");
  }
  function selectModel(id: string) {
    if (!discard()) return;
    void run(
      () => ontologyApi.get(projectId, id),
      (next) => {
        show(next);
        setCreating(false);
      },
    );
  }
  function choose(ids: string[]) {
    if (!model) return;
    if (
      ids.length +
        Object.entries(selected)
          .filter(([id]) => id !== model.id)
          .reduce((n, [, s]) => n + s.ids.length, 0) >
      8
    ) {
      setError("每轮最多选择 8 个来源。");
      return;
    }
    setPreview("");
    setIncludeDatabasePasswords(false);
    setError("");
    setSelected((old) => {
      const next = { ...old };
      if (ids.length) next[model.id] = selectionFor(model, ids);
      else delete next[model.id];
      return next;
    });
  }
  const entries = model?.sources ?? [];
  const nodes = [
    ...entries.map((s) => (s.definition.id === source?.id ? source : s.definition)),
    ...(source && !entries.some((s) => s.definition.id === source.id) ? [source] : []),
  ];
  function updateSelection(previous: OntologySummary, saved: OntologyModel | null) {
    setSelected((old) => {
      const next = { ...old };
      const entry = next[previous.id];
      if (!entry) return old;
      const ids = entry.ids.filter((id) => saved?.sources?.some((item) => item.definition.id === id));
      if (saved && entry.version === previous.version && ids.length) {
        next[previous.id] = selectionFor(saved, ids);
      } else {
        delete next[previous.id];
      }
      return next;
    });
    onModelChange?.(previous, saved);
    setPreview("");
    setIncludeDatabasePasswords(false);
  }
  function focusNode(item: OntologySource | null) {
    if (item?.id !== source?.id) {
      if (!discard()) return;
      inspect(item);
    }
    const element = document.getElementById(workspaceId + "-node-" + (item?.id ?? "root"));
    element?.focus({ preventScroll: true });
    element?.scrollIntoView({ block: "nearest", inline: "nearest" });
  }
  const chosen = model ? (selected[model.id]?.ids ?? []) : [];
  const canIncludePasswords = Object.values(selected).some((item) => item.hasNetworkDatabase);
  const count = Object.values(selected).reduce((sum, item) => sum + item.ids.length, 0);
  const textField = (title: string, value: string, update: (value: string) => void, placeholder = "") => (
    <label>
      {title}
      <input
        value={value}
        onChange={(e) => update(e.target.value)}
        placeholder={placeholder}
        maxLength={128}
      />
    </label>
  );
  const queryFields = source && (
    <>
      {textField(
        source.connection.type === "http" ? "项目接口别名" : "表或视图",
        source.relation,
        (relation) => change({ ...source, relation }),
        "schema.table",
      )}
      {textField("查询键字段", source.keyColumn, (keyColumn) => change({ ...source, keyColumn }), "id")}
      {textField(
        "返回字段",
        source.valueColumn,
        (valueColumn) => change({ ...source, valueColumn }),
        "status",
      )}
    </>
  );
  return (
    <section aria-label="本体模型工作区" className={styles.shell}>
      <header className={styles.bar}>
        <strong>
          <Boxes size={17} /> 本体模型
        </strong>
        <small className={styles.repos}>
          {repositoryNames.length ? "代码库 · " + repositoryNames.join("、") : "未关联代码库"}
        </small>
        <span className={styles.spacer} />
        <button
          type="button"
          ref={promptButton}
          aria-expanded={promptOpen}
          aria-controls={workspaceId + "-prompt"}
          onClick={() => setPromptOpen((value) => !value)}
        >
          <MessageSquareText size={15} /> 预览与注入
        </button>
        <button
          disabled={busy}
          aria-label="刷新本体模型"
          onClick={() => {
            if (discard()) refresh();
          }}
        >
          <RefreshCw size={15} />
        </button>
        <button
          aria-label="关闭本体模型"
          onClick={() => {
            if (discard()) onClose();
          }}
        >
          <X size={15} />
        </button>
      </header>
      {promptOpen && (
        <fieldset
          id={workspaceId + "-prompt"}
          aria-label="预览与注入"
          disabled={busy}
          className={styles.chat}
          onKeyDown={(event) => {
            if (event.key === "Escape") {
              setPromptOpen(false);
              promptButton.current?.focus();
            }
          }}
        >
          <div className={styles.row}>
            <h3>选择本体来源</h3>
            <small>已选 {count} 个组件</small>
          </div>
          {!entries.length && <small>在画布中添加并保存组件后，可选择查询来源。</small>}
          {entries.length > 0 && (
            <div className={styles.options}>
              <label>
                <input
                  type="checkbox"
                  checked={chosen.length === entries.length}
                  onChange={(e) => choose(e.target.checked ? entries.map((s) => s.definition.id) : [])}
                />
                整份本体
              </label>
              {entries.map(({ definition }) => (
                <label key={definition.id}>
                  <input
                    type="checkbox"
                    checked={chosen.includes(definition.id)}
                    onChange={(e) =>
                      choose(
                        e.target.checked
                          ? [...chosen, definition.id]
                          : chosen.filter((id) => id !== definition.id),
                      )
                    }
                  />
                  {definition.label}
                </label>
              ))}
            </div>
          )}
          {count > 0 && (
            <small>
              {Object.values(selected)
                .map((s) => s.name)
                .join("、")}
            </small>
          )}
          <div className={styles.actions}>
            <input
              aria-label="记录键值"
              maxLength={256}
              value={key}
              onChange={(e) => {
                setKey(e.target.value);
                setPreview("");
              }}
              placeholder="记录键值（可选）"
            />
            <button
              disabled={!count || dirty}
              onClick={() =>
                void run(async () => {
                  setPreview("");
                  const parts = await Promise.all(
                    Object.entries(selected).map(([id, s]) =>
                      ontologyApi.lookup(projectId, id, s.version, s.ids, key),
                    ),
                  );
                  return formatOntologyContext(parts);
                }, setPreview)
              }
            >
              {key.trim() ? "查询并预览" : "预览 JSON-LD"}
            </button>
          </div>
          {onAttach && canIncludePasswords && (
            <div>
              <label>
                <input
                  type="checkbox"
                  checked={includeDatabasePasswords}
                  onChange={(event) => setIncludeDatabasePasswords(event.target.checked)}
                />
                本轮附带数据库密码
              </label>
              <small>预览隐藏密码。勾选后，密码随消息发给 AI，并保存在聊天记录中。</small>
            </div>
          )}
          {onAttach && (
            <button
              className={styles.primary}
              disabled={!count || dirty}
              onClick={() => {
                onAttach({
                  projectId,
                  key,
                  includeDatabasePasswords: canIncludePasswords && includeDatabasePasswords,
                  models: Object.entries(selected).map(([id, item]) => ({
                    id,
                    name: item.name,
                    version: item.version,
                    ids: item.ids,
                  })),
                });
                setIncludeDatabasePasswords(false);
                setNotice("已挂载到左侧输入框，发送时读取所选来源。");
                setPromptOpen(false);
                promptButton.current?.focus();
              }}
            >
              挂载到本轮提问
            </button>
          )}
          {preview && (
            <>
              <textarea aria-label="Prompt 预览" readOnly value={preview} />
              <button
                onClick={() => {
                  onInsert(preview);
                  onClose();
                }}
              >
                填入聊天草稿
              </button>
            </>
          )}
        </fieldset>
      )}
      <fieldset
        disabled={busy}
        className={styles.body}
        style={{ "--tree-width": treeWidth + "%", "--config-width": configWidth + "%" } as CSSProperties}
      >
        <div role="tablist" aria-label="本体导航" aria-orientation="vertical" className={styles.tabs}>
          {treeViews.map(({ id, label, Icon }) => (
            <button
              key={id}
              type="button"
              role="tab"
              id={workspaceId + "-tab-" + id}
              aria-label={label}
              title={label}
              aria-selected={treeTab === id}
              aria-controls={workspaceId + "-tree"}
              tabIndex={treeTab === id ? 0 : -1}
              onClick={() => setTreeTab(id)}
              onKeyDown={(event) => {
                let target: typeof treeTab;
                switch (event.key) {
                  case "Home":
                    target = "models";
                    break;
                  case "End":
                    target = "components";
                    break;
                  case "ArrowUp":
                  case "ArrowDown":
                    target = id === "models" ? "components" : "models";
                    break;
                  default:
                    return;
                }
                event.preventDefault();
                document.getElementById(workspaceId + "-tab-" + target)?.focus();
                setTreeTab(target);
              }}
            >
              <Icon size={18} aria-hidden="true" />
            </button>
          ))}
        </div>
        <aside
          role="tabpanel"
          aria-labelledby={workspaceId + "-tab-" + treeTab}
          id={workspaceId + "-tree"}
          className={styles.tree}
        >
          <div className={styles.row}>
            <strong>{treeTab === "models" ? "对象模型" : "组件树"}</strong>
            <button
              title="新建模型"
              aria-label="新建模型"
              onClick={() => {
                setTreeTab("models");
                setCreating(true);
              }}
            >
              <Plus size={14} />
            </button>
          </div>
          {treeTab === "models" ? (
            models.map((item) => (
              <ModelRow
                key={item.id}
                item={item}
                active={model?.id === item.id}
                attached={!!selected[item.id]}
                onSelect={() => selectModel(item.id)}
                onRename={(name, done) => {
                  void run(
                    () => ontologyApi.rename(projectId, item, name),
                    (next) => {
                      setModels((items) =>
                        items.map((m) =>
                          m.id === item.id ? { id: next.id, name: next.name, version: next.version } : m,
                        ),
                      );
                      if (model?.id === item.id) setModel(next);
                      updateSelection(item, next);
                      done();
                    },
                  );
                }}
                onDelete={() => {
                  if (!window.confirm("删除模型「" + item.name + "」及其中所有组件和连接凭据？")) return;
                  void run(
                    () => ontologyApi.delete(projectId, item),
                    () => {
                      setModels((items) => items.filter((m) => m.id !== item.id));
                      if (model?.id === item.id) show(null);
                      updateSelection(item, null);
                      setCreating(models.length === 1);
                      setNotice("模型已删除。");
                    },
                  );
                }}
              />
            ))
          ) : model ? (
            <>
              <button className={styles.file} aria-pressed={!source} onClick={() => focusNode(null)}>
                <Boxes size={14} />
                {model.name}
              </button>
              {nodes.map((item) => (
                <button
                  className={styles.child}
                  key={item.id}
                  aria-pressed={item.id === source?.id}
                  onClick={() => focusNode(item)}
                >
                  <SourceIcon type={item.connection.type} />
                  {item.label}
                </button>
              ))}
              {!nodes.length && <small>当前模型还没有组件。</small>}
            </>
          ) : (
            <small>请先选择对象模型。</small>
          )}
          {creating && treeTab === "models" && (
            <form
              onSubmit={(e) => {
                e.preventDefault();
                if (!name.trim() || !discard()) return;
                void run(
                  () => ontologyApi.create(projectId, name.trim()),
                  (next) => {
                    setModels((old) => [...old, { id: next.id, name: next.name, version: next.version }]);
                    show(next);
                    setCreating(false);
                    setName("");
                  },
                );
              }}
            >
              <label>
                模型名称
                <input
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  maxLength={100}
                  placeholder="输入名称"
                />
              </label>
              <button disabled={!name.trim()}>创建模型</button>
            </form>
          )}
          <WidthDivider
            side="tree"
            controls={workspaceId + "-tree"}
            width={treeWidth}
            onResize={setTreeWidth}
          />
        </aside>
        <main className={styles.canvas}>
          <div className={styles.row}>
            <h3>对象模型画布</h3>
            <button
              disabled={!model}
              onClick={() => {
                if (discard()) {
                  inspect(newSource());
                  setPreview("");
                  setDirty(true);
                  setError("");
                  setNotice("");
                }
              }}
            >
              <Plus size={13} /> 添加组件
            </button>
          </div>
          {model ? (
            <div className={styles.diagram}>
              <div tabIndex={-1} id={workspaceId + "-node-root"} className={styles.rootNode}>
                <Boxes size={20} />
                <strong>{model.name}</strong>
                <small>对象模型 · v{model.version}</small>
              </div>
              <div className={styles.branches}>
                {nodes.map((item) => {
                  const saved = entries.find((entry) => entry.definition.id === item.id);
                  const active = source?.id === item.id;
                  const status =
                    (active && testStatus) ||
                    (item.connection.type !== "note" && saved?.checkedAt && !(active && dirty)
                      ? "saved"
                      : null);
                  return (
                    <button
                      data-connection-status={status ?? undefined}
                      className={styles.node}
                      id={workspaceId + "-node-" + item.id}
                      key={item.id}
                      aria-pressed={source?.id === item.id}
                      onClick={() => {
                        if (source?.id !== item.id && discard()) {
                          inspect(item);
                          setError("");
                          setNotice("");
                        }
                      }}
                    >
                      <SourceIcon type={item.connection.type} />
                      <span>
                        <strong>{item.label}</strong>
                        <small>
                          {item.connection.type === "note" ? "文本说明" : drivers[item.connection.type]?.[0]}{" "}
                          · {saved ? (dirty && active ? "未保存" : "已保存") : "草稿"}
                        </small>
                        {status && (
                          <small className={styles.connectionStatus}>
                            {(status === "passed" || status === "saved") && <CircleCheck size={12} />}
                            {status === "passed"
                              ? "测试通过"
                              : status === "saved"
                                ? "上次保存时已验证"
                                : status === "testing"
                                  ? "正在测试…"
                                  : "测试失败"}
                          </small>
                        )}
                      </span>
                    </button>
                  );
                })}
              </div>
              {!entries.length && !source && <small>点击「添加组件」创建第一个组件。</small>}
            </div>
          ) : (
            <p className={styles.empty}>{busy ? "正在加载模型…" : "在本体树中新建或选择模型。"}</p>
          )}
        </main>
        {source && model && (
          <aside id={workspaceId + "-config"} className={styles.inspector}>
            <WidthDivider
              side="config"
              controls={workspaceId + "-config"}
              width={configWidth}
              onResize={setConfigWidth}
            />
            <h3>
              <SourceIcon type={source.connection.type} /> 组件配置
            </h3>
            {textField("组件名称", source.label, (label) => change({ ...source, label }))}
            <label>
              组件类型
              <select
                value={componentKind(source.connection.type)}
                onChange={(e) => {
                  const type = e.target.value === "database" ? "sqlserver" : e.target.value;
                  const defaults = newSource();
                  const labels: Record<string, string> = {
                    sqlserver: "数据库连接",
                    http: "HTTP 接口",
                    note: "文本说明",
                  };
                  setPassword("");
                  change({
                    ...defaults,
                    id: source.id,
                    label: ["数据库连接", "HTTP 接口", "文本说明"].includes(source.label)
                      ? labels[type]
                      : source.label,
                    connection: { ...defaults.connection, type, port: drivers[type]?.[1] ?? 0 },
                  });
                }}
              >
                <option value="database">数据库连接</option>
                <option value="http">HTTP 接口</option>
                <option value="note">文本说明</option>
              </select>
            </label>
            {source.connection.type === "note" ? (
              <label>
                说明内容
                <textarea
                  rows={5}
                  maxLength={4000}
                  value={source.note ?? ""}
                  onChange={(e) => change({ ...source, note: e.target.value })}
                />
              </label>
            ) : (
              <>
                {source.connection.type !== "http" && (
                  <label>
                    数据库驱动
                    <select
                      value={source.connection.type}
                      onChange={(e) => {
                        setPassword("");
                        const type = e.target.value;
                        change({
                          ...source,
                          connection: {
                            ...source.connection,
                            type,
                            port: drivers[type][1],
                            trustServerCertificate: false,
                          },
                        });
                      }}
                    >
                      {Object.entries(drivers)
                        .filter(([id]) => id !== "http")
                        .map(([id, [title]]) => (
                          <option key={id} value={id}>
                            {title}
                          </option>
                        ))}
                    </select>
                  </label>
                )}
                {!["sqlite", "http"].includes(source.connection.type) && (
                  <>
                    {textField("主机", source.connection.host, (host) =>
                      change({ ...source, connection: { ...source.connection, host } }),
                    )}
                    <label>
                      端口
                      <input
                        type="number"
                        min="1"
                        max="65535"
                        value={source.connection.port || ""}
                        onChange={(e) =>
                          change({
                            ...source,
                            connection: { ...source.connection, port: Number(e.target.value) },
                          })
                        }
                      />
                    </label>
                  </>
                )}
                {source.connection.type !== "http" &&
                  textField(
                    source.connection.type === "sqlite"
                      ? "项目数据库别名"
                      : source.connection.type === "oracle"
                        ? "服务名（Service Name）"
                        : "数据库名",
                    source.connection.database,
                    (database) => change({ ...source, connection: { ...source.connection, database } }),
                  )}
                {!["sqlite", "http"].includes(source.connection.type) && (
                  <>
                    {textField("数据库账号", source.connection.username, (username) =>
                      change({ ...source, connection: { ...source.connection, username } }),
                    )}
                    <label>
                      密码
                      <input
                        type="password"
                        autoComplete="new-password"
                        value={password}
                        placeholder="相同连接留空沿用"
                        onChange={(e) => {
                          change(source, true);
                          setPassword(e.target.value);
                        }}
                      />
                    </label>
                  </>
                )}
                {source.connection.type === "sqlserver" && (
                  <label className={styles.certificateOption}>
                    <input
                      type="checkbox"
                      checked={source.connection.trustServerCertificate ?? false}
                      onChange={(event) =>
                        change({
                          ...source,
                          connection: {
                            ...source.connection,
                            trustServerCertificate: event.target.checked,
                          },
                        })
                      }
                    />
                    信任服务器证书
                    <small>启用后连接仍加密，服务器证书将跳过校验。</small>
                  </label>
                )}
                {source.connection.type === "http" && (
                  <>
                    <label>
                      Bearer Token（可选）
                      <input
                        type="password"
                        autoComplete="new-password"
                        value={password}
                        onChange={(e) => {
                          change(source, true);
                          setPassword(e.target.value);
                        }}
                      />
                    </label>
                    {textField("测试记录键值", source.probeKey ?? "", (probeKey) =>
                      change({ ...source, probeKey }),
                    )}
                  </>
                )}
                {source.connection.type === "http" ? (
                  queryFields
                ) : (
                  <details key={source.id} className={styles.mapping}>
                    <summary>按键查询配置（可选）</summary>
                    {queryFields}
                  </details>
                )}
              </>
            )}
            <div className={styles.row}>
              {source.connection.type !== "note" && (
                <button
                  onClick={() => {
                    setTestStatus("testing");
                    void run(
                      () => ontologyApi.testSource(projectId, model, source, password),
                      () => {
                        setTestStatus("passed");
                        setNotice(
                          source.connection.type === "http"
                            ? "接口读取成功，可以保存组件。"
                            : "数据库连接成功，可以保存组件。",
                        );
                      },
                      () => setTestStatus("failed"),
                    );
                  }}
                >
                  测试连接
                </button>
              )}
              <button
                className={styles.primary}
                disabled={
                  source.connection.type === "note"
                    ? !dirty || !source.note?.trim() || !source.label.trim()
                    : !tested
                }
                onClick={() =>
                  void run(
                    () => ontologyApi.saveSource(projectId, model, source, password),
                    (next) => {
                      show(next, true);
                      updateSelection(model, next);
                      inspect(next.sources?.find((s) => s.definition.id === source.id)?.definition ?? null);
                      setNotice("组件已保存，可勾选带入聊天。");
                    },
                  )
                }
              >
                保存组件
              </button>
            </div>
            <button
              onClick={() => {
                if (!entries.some((s) => s.definition.id === source.id)) {
                  if (discard()) inspect(null);
                  return;
                }
                if (window.confirm("从本体模型移除该组件及已保存的连接凭据？")) {
                  void run(
                    () => ontologyApi.removeSource(projectId, model, source.id),
                    (next) => {
                      show(next, true);
                      updateSelection(model, next);
                      setNotice("组件已移除。");
                    },
                  );
                }
              }}
            >
              移除组件
            </button>
          </aside>
        )}
      </fieldset>
      {(busy || notice || error) && (
        <div className={styles.status} role={error ? "alert" : "status"}>
          {busy ? "正在处理…" : error || notice}
        </div>
      )}
    </section>
  );
}
