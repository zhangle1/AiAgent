"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import {
  ArrowUpRight,
  Boxes,
  Code2,
  LayoutDashboard,
  Loader2,
  Plus,
  Sparkles,
  Trash2,
  X,
} from "lucide-react";
import {
  createDashboardApplication,
  deleteDashboardApplication,
  listDashboardApplications,
  listDashboardRepositories,
  listDashboardTemplates,
  type DashboardApplication,
  type DashboardRepositoryOption,
  type DashboardTemplate,
} from "@/lib/dashboard-application-api";

export function DashboardApplicationsPage() {
  const [apps, setApps] = useState<DashboardApplication[]>([]);
  const [repositories, setRepositories] = useState<DashboardRepositoryOption[]>(
    [],
  );
  const [templates, setTemplates] = useState<DashboardTemplate[]>([]);
  const [loading, setLoading] = useState(true);
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [repositoryName, setRepositoryName] = useState("");
  const [templateId, setTemplateId] = useState("");
  const [creating, setCreating] = useState(false);
  const [deletingId, setDeletingId] = useState("");
  const [error, setError] = useState("");

  async function load() {
    setLoading(true);
    try {
      const [appRows, repositoryRows, templateRows] = await Promise.all([
        listDashboardApplications(),
        listDashboardRepositories(),
        listDashboardTemplates(),
      ]);
      setApps(appRows);
      setRepositories(repositoryRows);
      setTemplates(templateRows);
      setTemplateId((value) => value || templateRows[0]?.id || "");
      setRepositoryName((value) => value || repositoryRows[0]?.name || "");
    } catch (value) {
      setError(value instanceof Error ? value.message : "无法加载看板应用。");
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => {
    void load();
  }, []);
  async function create() {
    if (!name.trim() || (!templateId && !repositoryName)) {
      setError("请输入应用名称，并选择模板或代码库。");
      return;
    }
    setCreating(true);
    setError("");
    try {
      const app = await createDashboardApplication({
        name: name.trim(),
        template_id: templateId || undefined,
        repository_name: repositoryName || undefined,
      });
      setApps((items) => [app, ...items]);
      setOpen(false);
      setName("");
    } catch (value) {
      setError(value instanceof Error ? value.message : "创建失败。");
    } finally {
      setCreating(false);
    }
  }
  async function remove(app: DashboardApplication) {
    if (
      !window.confirm(
        `删除“${app.name}”吗？模板工作区也会一并移除；关联代码库本身不会删除。`,
      )
    )
      return;
    setDeletingId(app.id);
    setError("");
    try {
      await deleteDashboardApplication(app.id);
      setApps((items) => items.filter((item) => item.id !== app.id));
    } catch (value) {
      setError(value instanceof Error ? value.message : "删除失败。");
    } finally {
      setDeletingId("");
    }
  }

  return (
    <main className="min-h-screen bg-[#f8fafc] px-5 py-7 sm:px-8">
      <div className="mx-auto max-w-6xl">
        <header className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <div className="mb-2 flex items-center gap-2 text-[12px] font-semibold uppercase tracking-[0.12em] text-blue-600">
              <Sparkles size={14} /> AI application studio
            </div>
            <h1 className="text-2xl font-semibold tracking-tight text-slate-900">
              看板应用生成
            </h1>
            <p className="mt-2 max-w-2xl text-[13px] leading-6 text-slate-500">
              选择模板和代码库后，将模板复制到代码库的独立 Git 工作区供 AI
              联动。
            </p>
          </div>
          <button
            type="button"
            onClick={() => {
              setError("");
              setOpen(true);
            }}
            className="inline-flex h-10 items-center gap-2 rounded-lg bg-blue-600 px-4 text-[13px] font-medium text-white shadow-sm hover:bg-blue-700"
          >
            <Plus size={16} />
            新建应用
          </button>
        </header>
        {error && (
          <p className="mt-5 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-[12px] text-red-700">
            {error}
          </p>
        )}
        <section className="mt-7 grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {loading ? (
            <div className="col-span-full flex h-52 items-center justify-center text-[13px] text-slate-500">
              <Loader2 size={16} className="mr-2 animate-spin" />
              加载应用工作区…
            </div>
          ) : (
            apps.map((app) => (
              <article
                key={app.id}
                className="group relative rounded-2xl border border-slate-200 bg-white shadow-[0_8px_30px_rgba(15,23,42,0.05)] transition hover:-translate-y-0.5 hover:border-blue-300 hover:shadow-[0_16px_38px_rgba(37,99,235,0.12)]"
              >
                <Link
                  href={`/dashboard-applications/${encodeURIComponent(app.id)}`}
                  className="block p-5"
                >
                  <div className="flex items-start justify-between">
                    <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-blue-50 text-blue-600">
                      <LayoutDashboard size={20} />
                    </span>
                    <ArrowUpRight
                      size={17}
                      className="text-slate-300 transition group-hover:text-blue-600"
                    />
                  </div>
                  <h2 className="mt-5 truncate text-[15px] font-semibold text-slate-900">
                    {app.name}
                  </h2>
                  <p className="mt-1 h-10 text-[12px] leading-5 text-slate-500">
                    {app.description}
                  </p>
                  <div className="mt-5 flex items-center gap-2 border-t border-slate-100 pt-3 text-[11px] text-slate-500">
                    {app.template_id ? (
                      <>
                        <Boxes size={13} />
                        {app.template_id}
                      </>
                    ) : (
                      <>
                        <Code2 size={13} />
                        {app.repository_name}
                      </>
                    )}
                  </div>
                </Link>
                <button
                  type="button"
                  aria-label={`删除 ${app.name}`}
                  disabled={deletingId === app.id}
                  onClick={() => void remove(app)}
                  className="absolute right-4 top-4 rounded p-1.5 text-slate-300 hover:bg-red-50 hover:text-red-600 disabled:opacity-50"
                >
                  {deletingId === app.id ? (
                    <Loader2 size={16} className="animate-spin" />
                  ) : (
                    <Trash2 size={16} />
                  )}
                </button>
              </article>
            ))
          )}
        </section>
      </div>
      {open && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/35 p-4">
          <section className="w-full max-w-md rounded-2xl bg-white p-5 shadow-2xl">
            <div className="flex items-center justify-between">
              <div>
                <h2 className="font-semibold">新建看板应用</h2>
                <p className="mt-1 text-[12px] text-slate-500">
                  模板库会实时加载；模板可由服务器运行 npm 预览。
                </p>
              </div>
              <button
                onClick={() => setOpen(false)}
                className="rounded p-1 text-slate-500 hover:bg-slate-100"
              >
                <X size={17} />
              </button>
            </div>
            <label className="mt-5 block text-[12px] font-medium">
              应用名称
              <input
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="例如：生产排程看板"
                className="mt-1.5 h-10 w-full rounded-lg border border-slate-200 px-3 text-[13px] outline-none focus:border-blue-500"
              />
            </label>
            <label className="mt-4 block text-[12px] font-medium">
              预载大屏模板
              <select
                value={templateId}
                onChange={(event) => setTemplateId(event.target.value)}
                className="mt-1.5 h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-[13px] outline-none focus:border-blue-500"
              >
                <option value="">不复制模板（直接打开代码库）</option>
                {templates.map((template) => (
                  <option key={template.id} value={template.id}>
                    {template.name} · {template.technology}
                  </option>
                ))}
              </select>
            </label>
            <label className="mt-4 block text-[12px] font-medium">
              Git 代码库（推荐）
              <select
                value={repositoryName}
                onChange={(event) => setRepositoryName(event.target.value)}
                className="mt-1.5 h-10 w-full rounded-lg border border-slate-200 bg-white px-3 text-[13px] outline-none focus:border-blue-500"
              >
                <option value="">不关联代码库（使用 AiAgent 工作区）</option>
                {repositories.map((repository) => (
                  <option key={repository.name} value={repository.name}>
                    {repository.display_name} · {repository.root_path}
                  </option>
                ))}
              </select>
            </label>
            <div className="mt-6 flex justify-end gap-2">
              <button
                onClick={() => setOpen(false)}
                className="h-9 rounded-lg px-3 text-[12px] hover:bg-slate-100"
              >
                取消
              </button>
              <button
                disabled={creating || (!templateId && !repositoryName)}
                onClick={() => void create()}
                className="inline-flex h-9 items-center gap-2 rounded-lg bg-blue-600 px-4 text-[12px] font-medium text-white hover:bg-blue-700 disabled:bg-slate-300"
              >
                {creating && <Loader2 size={14} className="animate-spin" />}
                创建工作区
              </button>
            </div>
          </section>
        </div>
      )}
    </main>
  );
}
