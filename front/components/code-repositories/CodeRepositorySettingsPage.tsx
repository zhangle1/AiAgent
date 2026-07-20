"use client";

import { useEffect, useState } from "react";
import {
  ArrowLeft,
  Braces,
  CheckCircle2,
  Folder,
  FolderOpen,
  GitBranch,
  GitFork,
  Loader2,
  Plus,
  RefreshCw,
  Trash2,
  X,
} from "lucide-react";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";
import {
  browseCodeRepositoryDirectories,
  createCodeRepository,
  deleteCodeRepository,
  getCodeRepositories,
  inspectCodeRepository,
  indexCodeRepository,
  updateCodeRepository,
  cloneCodeRepositoryViaWebSocket,
  type CodeRepositoryCloneEvent,
} from "@/lib/code-repository-api";
import type { CodeRepository, CodeRepositoryDirectoryBrowser, CodeRepositoryInspection } from "@/lib/code-repository-types";
import { listGitAccounts, type GitAccount } from "@/lib/git-account-api";
import { useI18n } from "@/i18n/I18nProvider";

type RepositoryDraft = {
  name: string;
  displayName: string;
  rootPath: string;
  description: string;
};

const emptyDraft: RepositoryDraft = {
  name: "",
  displayName: "",
  rootPath: "",
  description: "",
};

const inputClassName = "w-full rounded-md border border-[var(--border)] bg-white px-3 py-2 text-[13px] outline-none placeholder:text-zinc-400 focus:border-blue-400 disabled:bg-zinc-50 disabled:text-zinc-500";

export function CodeRepositorySettingsPage() {
  const { t } = useI18n();
  const [repositories, setRepositories] = useState<CodeRepository[]>([]);
  const [draft, setDraft] = useState<RepositoryDraft>(emptyDraft);
  const [editingName, setEditingName] = useState<string | null>(null);
  const [inspection, setInspection] = useState<CodeRepositoryInspection | null>(null);
  const [browser, setBrowser] = useState<CodeRepositoryDirectoryBrowser | null>(null);
  const [loading, setLoading] = useState(true);
  const [checking, setChecking] = useState(false);
  const [saving, setSaving] = useState(false);
  const [indexing, setIndexing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cloneOpen, setCloneOpen] = useState(false);
  const editingRepository = editingName ? repositories.find((item) => item.name === editingName) ?? null : null;

  useEffect(() => {
    void loadRepositories();
  }, []);

  async function loadRepositories() {
    setLoading(true);
    try {
      setRepositories(await getCodeRepositories());
    } catch (ex) {
      setError(errorMessage(ex));
    } finally {
      setLoading(false);
    }
  }

  async function inspectDirectory(): Promise<CodeRepositoryInspection | null> {
    if (!draft.rootPath.trim()) {
      setError(t("codeRepository.pathRequired"));
      return null;
    }

    setChecking(true);
    setError(null);
    try {
      const result = await inspectCodeRepository(draft.rootPath.trim());
      setInspection(result);
      setDraft((current) => ({
        ...current,
        rootPath: result.root_path,
        name: current.name || result.suggested_name,
        displayName: current.displayName || result.suggested_display_name,
      }));
      return result;
    } catch (ex) {
      setInspection(null);
      setError(errorMessage(ex));
      return null;
    } finally {
      setChecking(false);
    }
  }

  async function openBrowser(path?: string) {
    setError(null);
    try {
      setBrowser(await browseCodeRepositoryDirectories(path));
    } catch (ex) {
      setError(errorMessage(ex));
    }
  }

  async function saveRepository() {
    let currentInspection = inspection;
    if (!inspection || inspection.root_path !== draft.rootPath) {
      currentInspection = await inspectDirectory();
    }
    if (!currentInspection) return;
    const verifiedInspection = currentInspection;

    setSaving(true);
    setError(null);
    try {
      const payload = {
        name: draft.name || verifiedInspection.suggested_name,
        display_name: draft.displayName || verifiedInspection.suggested_display_name,
        root_path: verifiedInspection.root_path,
        description: draft.description,
      };
      const saved = editingName
        ? await updateCodeRepository(editingName, payload)
        : await createCodeRepository(payload);
      await loadRepositories();
      selectRepository(saved);
    } catch (ex) {
      setError(errorMessage(ex));
    } finally {
      setSaving(false);
    }
  }

  async function removeRepository(repository: CodeRepository) {
    if (!window.confirm(t("codeRepository.deleteConfirm", { name: repository.display_name }))) return;
    try {
      await deleteCodeRepository(repository.name);
      if (editingName === repository.name) startNew();
      await loadRepositories();
    } catch (ex) {
      setError(errorMessage(ex));
    }
  }

  async function buildIndex() {
    if (!editingRepository) return;
    setIndexing(true);
    setError(null);
    try {
      const result = await indexCodeRepository(editingRepository.name);
      setError(null);
      window.alert(result.status === "started" ? "代码索引任务已启动，可在代码库中心查看进度。" : t("codeRepository.indexed", { count: 0 }));
      await loadRepositories();
    } catch (ex) {
      setError(errorMessage(ex));
    } finally {
      setIndexing(false);
    }
  }

  function selectRepository(repository: CodeRepository) {
    setEditingName(repository.name);
    setDraft({
      name: repository.name,
      displayName: repository.display_name,
      rootPath: repository.root_path,
      description: repository.description ?? "",
    });
    setInspection({
      root_path: repository.root_path,
      suggested_name: repository.name,
      suggested_display_name: repository.display_name,
      languages: repository.languages,
      build_systems: repository.build_systems,
      is_git_repository: repository.is_git_repository,
      branch: repository.branch,
      marker_files: [],
      solution_files: repository.solution_files,
      configuration_files: repository.configuration_files,
    });
    setError(null);
  }

  function startNew() {
    setEditingName(null);
    setDraft(emptyDraft);
    setInspection(null);
    setError(null);
  }

  return (
    <section>
      <SettingsPageHeader
        title={t("codeRepository.settingsTitle")}
        description={t("codeRepository.settingsDescription")}
        action={(
          <div className="flex gap-2"><button type="button" onClick={() => setCloneOpen(true)} className="inline-flex h-9 items-center gap-2 rounded-md border border-blue-200 px-3 text-[12px] font-medium text-blue-700 hover:bg-blue-50"><GitFork size={15} />克隆代码</button><button type="button" onClick={startNew} className="inline-flex h-9 items-center gap-2 rounded-md bg-blue-600 px-3 text-[12px] font-medium text-white hover:bg-blue-700"><Plus size={15} />{t("codeRepository.new")}</button></div>
        )}
      />

      <div className="grid gap-5 xl:grid-cols-[300px_minmax(0,1fr)]">
        <aside className="rounded-lg border border-[var(--border)] bg-white p-3">
          <div className="mb-3 flex items-center justify-between px-1">
            <span className="text-[12px] font-semibold">{t("codeRepository.registered", { count: repositories.length })}</span>
            <button type="button" onClick={() => void loadRepositories()} className="inline-flex h-7 w-7 items-center justify-center rounded-md hover:bg-zinc-100" aria-label={t("knowledge.refresh")}>
              <RefreshCw size={14} className={loading ? "animate-spin" : ""} />
            </button>
          </div>
          {loading ? (
            <div className="flex items-center gap-2 px-2 py-4 text-[12px] text-[var(--muted-foreground)]"><Loader2 size={14} className="animate-spin" />{t("common.loadingSettings")}</div>
          ) : repositories.length === 0 ? (
            <div className="px-2 py-6 text-[12px] leading-5 text-[var(--muted-foreground)]">{t("codeRepository.empty")}</div>
          ) : (
            <div className="space-y-1">
              {repositories.map((repository) => (
                <button
                  key={repository.id}
                  type="button"
                  onClick={() => selectRepository(repository)}
                  className={`w-full rounded-md px-3 py-2.5 text-left transition ${editingName === repository.name ? "bg-blue-50 text-blue-950" : "hover:bg-zinc-50"}`}
                >
                  <div className="flex items-center gap-2">
                    <Braces size={15} className="shrink-0" />
                    <span className="truncate text-[13px] font-medium">{repository.display_name}</span>
                  </div>
                  <div className="mt-1 truncate pl-6 text-[11px] text-[var(--muted-foreground)]">{repository.root_path}</div>
                </button>
              ))}
            </div>
          )}
        </aside>

        <div className="min-w-0 rounded-lg border border-[var(--border)] bg-white p-5">
          <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="text-[16px] font-semibold">{editingName ? t("codeRepository.edit") : t("codeRepository.new")}</h2>
              <p className="mt-1 text-[12px] text-[var(--muted-foreground)]">{t("codeRepository.readOnlyHint")}</p>
            </div>
            {editingRepository && (
              <div className="flex items-center gap-2">
                <button type="button" onClick={() => void buildIndex()} disabled={indexing} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-blue-200 px-2.5 text-[12px] text-blue-700 hover:bg-blue-50 disabled:text-zinc-400">
                  {indexing ? <Loader2 size={14} className="animate-spin" /> : <RefreshCw size={14} />}
                  {t("codeRepository.buildIndex")}
                </button>
                <button type="button" onClick={() => void removeRepository(editingRepository)} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-red-200 px-2.5 text-[12px] text-red-700 hover:bg-red-50">
                  <Trash2 size={14} />
                  {t("common.delete")}
                </button>
              </div>
            )}
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <Field label={t("codeRepository.name")}> 
              <input value={draft.name} disabled={Boolean(editingName)} onChange={(event) => setDraft((current) => ({ ...current, name: event.target.value }))} placeholder={t("codeRepository.namePlaceholder")} className={inputClassName} />
            </Field>
            <Field label={t("codeRepository.displayName")}>
              <input value={draft.displayName} onChange={(event) => setDraft((current) => ({ ...current, displayName: event.target.value }))} placeholder={t("codeRepository.displayNamePlaceholder")} className={inputClassName} />
            </Field>
          </div>

          <Field label={t("codeRepository.rootPath")} description={t("codeRepository.rootPathHint")}>
            <div className="flex gap-2">
              <input value={draft.rootPath} onChange={(event) => { setDraft((current) => ({ ...current, rootPath: event.target.value })); setInspection(null); }} placeholder="E:\\项目\\know-why\\codex" className={`${inputClassName} min-w-0 flex-1 font-mono`} />
              <button type="button" onClick={() => void openBrowser(draft.rootPath || undefined)} className="inline-flex h-10 shrink-0 items-center gap-1.5 rounded-md border border-[var(--border)] px-3 text-[12px] hover:bg-zinc-50">
                <FolderOpen size={15} />
                {t("codeRepository.browse")}
              </button>
              <button type="button" onClick={() => void inspectDirectory()} disabled={checking || !draft.rootPath.trim()} className="inline-flex h-10 shrink-0 items-center gap-1.5 rounded-md border border-[var(--border)] px-3 text-[12px] hover:bg-zinc-50 disabled:cursor-not-allowed disabled:text-zinc-400">
                {checking ? <Loader2 size={15} className="animate-spin" /> : <CheckCircle2 size={15} />}
                {t("codeRepository.checkPath")}
              </button>
            </div>
          </Field>

          <Field label={t("codeRepository.description")}>
            <textarea value={draft.description} onChange={(event) => setDraft((current) => ({ ...current, description: event.target.value }))} rows={3} placeholder={t("codeRepository.descriptionPlaceholder")} className={`${inputClassName} resize-y`} />
          </Field>

          {inspection && (
            <section className="mt-5 rounded-md border border-emerald-200 bg-emerald-50/50 p-4">
              <div className="flex items-center gap-2 text-[13px] font-semibold text-emerald-900"><CheckCircle2 size={16} />{t("codeRepository.detected")}</div>
              <div className="mt-3 grid gap-3 text-[12px] text-emerald-950 sm:grid-cols-2">
                <MetaRow label={t("codeRepository.languages")} value={inspection.languages.join(" / ") || t("codeRepository.notDetected")} />
                <MetaRow label={t("codeRepository.buildSystems")} value={inspection.build_systems.join(" / ") || t("codeRepository.notDetected")} />
                <MetaRow label={t("codeRepository.git")} value={inspection.is_git_repository ? `${t("common.ready")}${inspection.branch ? ` · ${inspection.branch}` : ""}` : t("codeRepository.notDetected")} icon={<GitBranch size={14} />} />
                <MetaRow label={t("codeRepository.markers")} value={inspection.marker_files.join(", ") || t("codeRepository.notDetected")} />
              </div>
            </section>
          )}

          {error && <div className="mt-4 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-[12px] text-red-700">{error}</div>}

          <div className="mt-6 flex justify-end border-t border-[var(--border)] pt-4">
            <button type="button" onClick={() => void saveRepository()} disabled={saving || !draft.rootPath.trim()} className="inline-flex h-9 items-center gap-2 rounded-md bg-blue-600 px-4 text-[12px] font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-zinc-300">
              {saving ? <Loader2 size={15} className="animate-spin" /> : <CheckCircle2 size={15} />}
              {editingName ? t("codeRepository.save") : t("codeRepository.register")}
            </button>
          </div>
        </div>
      </div>

      {browser && (
        <DirectoryBrowser
          browser={browser}
          onClose={() => setBrowser(null)}
          onOpen={(path) => void openBrowser(path)}
          onChoose={() => {
            setDraft((current) => ({ ...current, rootPath: browser.path }));
            setInspection(null);
            setBrowser(null);
          }}
        />
      )}
      {cloneOpen && <CloneRepositoryDialog defaultParentPath={draft.rootPath} onClose={() => setCloneOpen(false)} onCompleted={(path) => { setCloneOpen(false); setDraft((current) => ({ ...current, rootPath: path, name: "", displayName: "" })); void inspectCodeRepository(path).then((result) => { setInspection(result); setDraft((current) => ({ ...current, rootPath: result.root_path, name: result.suggested_name, displayName: result.suggested_display_name })); }).catch((ex) => setError(errorMessage(ex))); }} />}
    </section>
  );
}

function CloneRepositoryDialog({ defaultParentPath, onClose, onCompleted }: { defaultParentPath: string; onClose: () => void; onCompleted: (path: string) => void }) {
  const [repositoryUrl, setRepositoryUrl] = useState("");
  const [parentPath, setParentPath] = useState(defaultParentPath);
  const [accounts, setAccounts] = useState<GitAccount[]>([]);
  const [accountId, setAccountId] = useState<number>(0);
  const [logs, setLogs] = useState<string[]>([]);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => { void listGitAccounts().then((items) => { setAccounts(items.filter((item) => item.token_configured)); const selected = items.find((item) => item.is_active && item.token_configured) ?? items.find((item) => item.token_configured); setAccountId(selected?.id ?? 0); }).catch((ex) => setError(errorMessage(ex))); }, []);
  const append = (event: CodeRepositoryCloneEvent) => { const line = event.line ?? event.message; if (line) setLogs((items) => [...items, `${event.stream === "stderr" ? "! " : ""}${line}`].slice(-300)); };
  async function runClone() {
    if (!repositoryUrl.trim() || !parentPath.trim() || !accountId) { setError("请填写仓库地址、目标文件夹，并选择已配置令牌的 Git 账号。"); return; }
    setRunning(true); setError(""); setLogs([]);
    try {
      const result = await cloneCodeRepositoryViaWebSocket({ repository_url: repositoryUrl.trim(), destination_parent_path: parentPath.trim(), git_account_id: accountId }, append);
      if (result.success && result.destination_path) onCompleted(result.destination_path);
      else setError(result.message ?? "克隆失败，请查看终端输出。");
    } catch (ex) { setError(errorMessage(ex)); } finally { setRunning(false); }
  }
  return <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/35 p-4" role="dialog" aria-modal="true"><div className="flex max-h-[88vh] w-full max-w-2xl flex-col rounded-lg bg-white shadow-xl"><div className="flex items-center justify-between border-b border-[var(--border)] px-5 py-4"><div><h2 className="text-[15px] font-semibold">克隆代码仓库</h2><p className="mt-1 text-[12px] text-[var(--muted-foreground)]">通过已保存的 Git 账号克隆；终端输出会实时显示。</p></div><button type="button" onClick={onClose} disabled={running} className="rounded p-1 hover:bg-zinc-100"><X size={16} /></button></div><div className="space-y-4 overflow-y-auto p-5"><Field label="HTTPS 仓库地址"><input value={repositoryUrl} onChange={(event) => setRepositoryUrl(event.target.value)} placeholder="https://gitee.com/owner/repository.git" className={inputClassName} /></Field><Field label="目标父文件夹" description="克隆后会在该文件夹下自动创建仓库目录。"><input value={parentPath} onChange={(event) => setParentPath(event.target.value)} placeholder="E:\\项目" className={`${inputClassName} font-mono`} /></Field><Field label="Git 账号"><select value={accountId} onChange={(event) => setAccountId(Number(event.target.value))} className={inputClassName}><option value={0}>选择已配置令牌的账号</option>{accounts.map((account) => <option key={account.id} value={account.id}>{account.provider === "gitee" ? "Gitee" : "GitHub"} · {account.display_name} (@{account.username})</option>)}</select></Field><div><div className="text-[12px] font-medium">实时终端</div><pre className="mt-1.5 min-h-36 max-h-64 overflow-auto rounded-md bg-zinc-950 p-3 font-mono text-[11px] leading-5 text-zinc-100">{logs.length ? logs.join("\n") : "等待开始克隆…"}</pre></div>{error && <div className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-[12px] text-red-700">{error}</div>}</div><div className="flex justify-end gap-2 border-t border-[var(--border)] px-5 py-3"><button type="button" onClick={onClose} disabled={running} className="h-9 rounded-md px-3 text-[12px] hover:bg-zinc-100">取消</button><button type="button" onClick={() => void runClone()} disabled={running} className="inline-flex h-9 items-center gap-2 rounded-md bg-blue-600 px-4 text-[12px] font-medium text-white hover:bg-blue-700 disabled:bg-zinc-300">{running ? <Loader2 size={15} className="animate-spin" /> : <GitFork size={15} />}{running ? "克隆中…" : "开始克隆"}</button></div></div></div>;
}

function DirectoryBrowser({ browser, onClose, onOpen, onChoose }: { browser: CodeRepositoryDirectoryBrowser; onClose: () => void; onOpen: (path?: string) => void; onChoose: () => void }) {
  const { t } = useI18n();
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/35 p-4" role="dialog" aria-modal="true">
      <div className="flex max-h-[78vh] w-full max-w-2xl flex-col rounded-lg bg-white shadow-xl">
        <div className="flex items-center justify-between border-b border-[var(--border)] px-5 py-4">
          <div>
            <h2 className="text-[15px] font-semibold">{t("codeRepository.chooseDirectory")}</h2>
            <p className="mt-1 break-all text-[11px] text-[var(--muted-foreground)]">{browser.path}</p>
          </div>
          <button type="button" onClick={onClose} className="inline-flex h-8 w-8 items-center justify-center rounded-md hover:bg-zinc-100" aria-label={t("knowledge.cancel")}><X size={16} /></button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto p-3">
          {browser.parent_path && (
            <button type="button" onClick={() => onOpen(browser.parent_path ?? undefined)} className="mb-2 flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-[13px] hover:bg-zinc-50"><ArrowLeft size={15} />..</button>
          )}
          {browser.directories.map((path) => (
            <button key={path} type="button" onClick={() => onOpen(path)} className="flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-[13px] hover:bg-zinc-50">
              <Folder size={16} className="text-amber-500" />
              <span className="truncate">{path.split(/[\\/]/).filter(Boolean).pop()}</span>
            </button>
          ))}
        </div>
        <div className="flex items-center justify-between gap-3 border-t border-[var(--border)] px-5 py-3">
          <span className="text-[11px] text-[var(--muted-foreground)]">{t("codeRepository.allowedRoots", { count: browser.allowed_roots.length })}</span>
          <div className="flex gap-2">
            <button type="button" onClick={onClose} className="h-8 rounded-md px-3 text-[12px] hover:bg-zinc-100">{t("knowledge.cancel")}</button>
            <button type="button" onClick={onChoose} className="inline-flex h-8 items-center gap-1.5 rounded-md bg-blue-600 px-3 text-[12px] font-medium text-white hover:bg-blue-700"><FolderOpen size={14} />{t("codeRepository.useDirectory")}</button>
          </div>
        </div>
      </div>
    </div>
  );
}

function Field({ label, description, children }: { label: string; description?: string; children: React.ReactNode }) {
  return (
    <div className="mt-4 block">
      <span className="text-[12px] font-medium">{label}</span>
      {description && <span className="mt-1 block text-[11px] leading-5 text-[var(--muted-foreground)]">{description}</span>}
      <div className="mt-1.5">{children}</div>
    </div>
  );
}

function MetaRow({ label, value, icon }: { label: string; value: string; icon?: React.ReactNode }) {
  return <div><div className="mb-1 flex items-center gap-1.5 text-[11px] text-emerald-800">{icon}{label}</div><div className="break-words">{value}</div></div>;
}

function errorMessage(ex: unknown) {
  return ex instanceof Error ? ex.message : "Unexpected error";
}
