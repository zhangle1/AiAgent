"use client";

import { useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import { ArrowRight, CheckCircle2, ClipboardCheck, FileSearch, GitCommit, GitPullRequest, Loader2, RefreshCw, ShieldCheck, XCircle } from "lucide-react";
import { approveCodeChangeSet, createCodeChangeSet, deliverCodeChangeSet, listCodeChangeSets, validateCodeChangeSet } from "@/lib/code-delivery-api";
import type { CodeChangeSet } from "@/lib/code-delivery-types";
import { getCodeProjects } from "@/lib/code-repository-api";
import type { CodeProject } from "@/lib/code-repository-types";

type DeliveryStatus = { label: string; className: string };

const steps = [
  { title: "创建变更集", description: "选择项目，冻结当前已登记仓库中的改动范围。", icon: FileSearch },
  { title: "自动校验", description: "确认改动文件、分支与校验结果，生成审批快照。", icon: ClipboardCheck },
  { title: "人工批准", description: "有代码提交权限的人核对快照并批准本次交付。", icon: ShieldCheck },
  { title: "提交并回写", description: "再次核对仓库未变化后提交、推送，并将关联任务回写完成。", icon: GitCommit },
];

function statusMeta(status?: string | null): DeliveryStatus {
  switch (status) {
    case "pending_approval": return { label: "等待审批", className: "bg-amber-50 text-amber-700 ring-amber-200" };
    case "approved": return { label: "已批准，待交付", className: "bg-emerald-50 text-emerald-700 ring-emerald-200" };
    case "delivering": return { label: "正在提交", className: "bg-blue-50 text-blue-700 ring-blue-200" };
    case "delivered": return { label: "已交付", className: "bg-emerald-50 text-emerald-700 ring-emerald-200" };
    case "validation_failed": return { label: "校验未通过", className: "bg-rose-50 text-rose-700 ring-rose-200" };
    case "delivery_failed": return { label: "交付失败", className: "bg-rose-50 text-rose-700 ring-rose-200" };
    case "rejected": return { label: "已拒绝", className: "bg-slate-100 text-slate-600 ring-slate-200" };
    default: return { label: "草稿", className: "bg-slate-100 text-slate-600 ring-slate-200" };
  }
}

function time(value?: string | null) {
  return value ? new Date(value).toLocaleString() : "尚未执行";
}

function message(value: unknown) {
  return value instanceof Error ? value.message : "交付操作失败。";
}

export function CodeDeliveryPage() {
  const searchParams = useSearchParams();
  const initialProjectId = Number(searchParams.get("projectId") || 0);
  const taskId = Number(searchParams.get("taskId") || 0);
  const [projects, setProjects] = useState<CodeProject[]>([]);
  const [projectId, setProjectId] = useState(initialProjectId);
  const [changeSets, setChangeSets] = useState<CodeChangeSet[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState("");
  const [error, setError] = useState("");

  const refreshChangeSets = async (selectedProjectId = projectId) => {
    if (!selectedProjectId) {
      setChangeSets([]);
      return;
    }
    setError("");
    try {
      setChangeSets(await listCodeChangeSets(selectedProjectId, taskId || undefined));
    } catch (value) {
      setError(message(value));
    }
  };

  useEffect(() => {
    void getCodeProjects()
      .then((available) => {
        setProjects(available);
        setProjectId((current) => current || available[0]?.id || 0);
      })
      .catch((value) => setError(message(value)))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    if (projectId) void refreshChangeSets(projectId);
  }, [projectId, taskId]);

  const act = async (key: string, action: () => Promise<CodeChangeSet>) => {
    setBusy(key);
    setError("");
    try {
      const result = await action();
      setChangeSets((items) => [result, ...items.filter((item) => item.id !== result.id)]);
    } catch (value) {
      setError(message(value));
    } finally {
      setBusy("");
    }
  };

  const reject = (row: CodeChangeSet) => {
    const comment = window.prompt("请填写拒绝原因（会保存在变更集记录中）");
    if (comment === null) return;
    void act(`reject-${row.id}`, () => approveCodeChangeSet(row.id, false, comment));
  };

  const approve = (row: CodeChangeSet) => {
    const comment = window.prompt("审批意见（可选）");
    if (comment === null) return;
    void act(`approve-${row.id}`, () => approveCodeChangeSet(row.id, true, comment));
  };

  return <main className="min-h-screen bg-slate-50 px-4 py-6 sm:px-6">
    <div className="mx-auto max-w-6xl">
      <header className="flex flex-col gap-5 border-b border-slate-200 pb-6 lg:flex-row lg:items-end lg:justify-between">
        <div className="max-w-2xl">
          <p className="text-xs font-semibold tracking-[0.18em] text-blue-600">CODE DELIVERY</p>
          <h1 className="mt-2 text-2xl font-semibold tracking-tight text-slate-950 sm:text-3xl">把 AI 改动变成可审核的代码交付</h1>
          <p className="mt-3 text-sm leading-6 text-slate-600">这里不是“点一下就推送”。每次交付先生成变更快照、执行校验，再由有权限的人批准，最后才提交并推送到 Git。</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <label className="sr-only" htmlFor="delivery-project">交付项目</label>
          <select id="delivery-project" className="h-10 min-w-44 rounded-lg border border-slate-200 bg-white px-3 text-sm text-slate-700 outline-none focus:border-blue-500 focus:ring-4 focus:ring-blue-50" value={projectId} onChange={(event) => setProjectId(Number(event.target.value))}>
            <option value={0}>选择项目</option>
            {projects.map((project) => <option key={project.id} value={project.id}>{project.display_name}</option>)}
          </select>
          <button type="button" onClick={() => void refreshChangeSets()} disabled={!projectId || Boolean(busy)} className="inline-grid h-10 w-10 place-items-center rounded-lg border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50" aria-label="刷新交付记录"><RefreshCw size={16} /></button>
          <button type="button" disabled={!projectId || Boolean(busy)} onClick={() => void act("create", () => createCodeChangeSet({ project_id: projectId }))} className="inline-flex h-10 items-center gap-2 rounded-lg bg-blue-600 px-4 text-sm font-medium text-white shadow-sm hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-slate-300"><GitPullRequest size={16} />创建变更集并校验</button>
        </div>
      </header>

      {error && <div role="alert" className="mt-5 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"><b>无法完成交付操作：</b>{error}</div>}

      <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm sm:p-5" aria-labelledby="delivery-flow-title">
        <div className="flex items-center justify-between gap-3"><div><h2 id="delivery-flow-title" className="font-semibold text-slate-900">交付流程原型</h2><p className="mt-1 text-sm text-slate-500">先看懂流程，再操作；每一步都有可追溯状态。</p></div><span className="rounded-full bg-blue-50 px-2.5 py-1 text-xs font-medium text-blue-700">需要人工批准</span></div>
        <ol className="mt-5 grid gap-3 md:grid-cols-4">{steps.map((step, index) => { const Icon = step.icon; return <li key={step.title} className="relative min-w-0 rounded-xl bg-slate-50 p-3.5"><span className="mb-3 grid h-8 w-8 place-items-center rounded-lg bg-white text-blue-600 shadow-sm ring-1 ring-slate-200"><Icon size={16} /></span><p className="text-sm font-semibold text-slate-800">{index + 1}. {step.title}</p><p className="mt-1 text-xs leading-5 text-slate-500">{step.description}</p>{index < steps.length - 1 && <ArrowRight size={16} className="absolute -right-[0.6rem] top-1/2 z-10 hidden -translate-y-1/2 text-slate-300 md:block" />}</li>; })}</ol>
      </section>

      <section className="mt-4 rounded-2xl border border-blue-100 bg-blue-50/50 p-4 sm:p-5" aria-labelledby="delivery-example-title">
        <div className="flex flex-wrap items-center justify-between gap-3"><div><p className="text-xs font-semibold tracking-[0.14em] text-blue-600">EXAMPLE</p><h2 id="delivery-example-title" className="mt-1 font-semibold text-slate-900">一条真实交付会长这样</h2></div><span className="rounded-full bg-white px-2.5 py-1 text-xs text-slate-600 ring-1 ring-blue-100">示例，不会提交代码</span></div>
        <div className="mt-4 grid gap-3 md:grid-cols-[1.2fr_1fr]"><div className="rounded-xl border border-blue-100 bg-white p-4"><p className="text-sm font-semibold text-slate-800">任务：修复“订单撤销后仍可发货”</p><p className="mt-2 text-xs leading-5 text-slate-500">AI 或开发者已经在“科博”项目的已登记仓库完成改动。你选择项目后点击 <b>创建变更集并校验</b>，系统会生成这一次的文件清单，而不是扫描电脑上的所有目录。</p><div className="mt-3 flex flex-wrap gap-2"><span className="rounded-md bg-slate-100 px-2 py-1 text-xs text-slate-600">OrderService.cs</span><span className="rounded-md bg-slate-100 px-2 py-1 text-xs text-slate-600">OrderController.cs</span><span className="rounded-md bg-slate-100 px-2 py-1 text-xs text-slate-600">OrderTests.cs</span></div></div><div className="rounded-xl border border-blue-100 bg-white p-4"><p className="text-sm font-semibold text-slate-800">随后你会看到</p><ol className="mt-2 space-y-2 text-xs leading-5 text-slate-600"><li><b>1.</b> 文件数、所在分支和自动校验结果。</li><li><b>2.</b> 状态变为“等待审批”，此时还不会 push。</li><li><b>3.</b> 审批人点击“批准”后，才出现“提交并推送”。</li><li><b>4.</b> 推送成功后保留 commit SHA，并回写关联任务。</li></ol></div></div>
      </section>

      <section className="mt-6" aria-labelledby="change-sets-title">
        <div className="flex items-end justify-between gap-3"><div><h2 id="change-sets-title" className="text-lg font-semibold text-slate-900">变更集记录</h2><p className="mt-1 text-sm text-slate-500">一个变更集对应一次可复核的交付范围和审批快照。</p></div>{projectId && <span className="text-xs text-slate-400">{changeSets.length} 条记录</span>}</div>

        {loading && <div className="grid min-h-48 place-items-center"><Loader2 className="animate-spin text-blue-600" /></div>}
        {!loading && !projectId && <div className="mt-4 rounded-2xl border border-dashed border-slate-300 bg-white px-6 py-12 text-center"><FileSearch className="mx-auto text-slate-400" size={28} /><h3 className="mt-3 font-semibold text-slate-800">先选择一个项目</h3><p className="mx-auto mt-2 max-w-md text-sm leading-6 text-slate-500">系统只会扫描这个项目下已登记的代码仓库，不会把未关联项目的本地目录纳入交付。</p></div>}
        {!loading && projectId && changeSets.length === 0 && <div className="mt-4 rounded-2xl border border-dashed border-slate-300 bg-white px-6 py-12 text-center"><GitPullRequest className="mx-auto text-blue-600" size={28} /><h3 className="mt-3 font-semibold text-slate-800">还没有变更集</h3><p className="mx-auto mt-2 max-w-lg text-sm leading-6 text-slate-500">点击“创建变更集并校验”后，系统会读取仓库改动、生成文件清单并等待人工审批；没有改动时不会出现可推送内容。</p></div>}
        <div className="mt-4 space-y-4">{changeSets.map((row) => <ChangeSetCard key={row.id} row={row} busy={busy} onValidate={() => void act(`validate-${row.id}`, () => validateCodeChangeSet(row.id))} onApprove={() => approve(row)} onReject={() => reject(row)} onDeliver={() => void act(`deliver-${row.id}`, () => deliverCodeChangeSet(row.id))} />)}</div>
      </section>
    </div>
  </main>;
}

function ChangeSetCard({ row, busy, onValidate, onApprove, onReject, onDeliver }: { row: CodeChangeSet; busy: string; onValidate: () => void; onApprove: () => void; onReject: () => void; onDeliver: () => void }) {
  const status = statusMeta(row.status);
  const isBusy = Boolean(busy);
  const isCurrentRowBusy = busy.endsWith(`-${row.id}`);
  const canValidate = ["draft", "validation_failed", "rejected", "delivery_failed"].includes(row.status || "draft");
  return <article className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm sm:p-5">
    <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between"><div className="min-w-0"><div className="flex flex-wrap items-center gap-2"><h3 className="truncate font-semibold text-slate-900">{row.title || "AI 代码变更"}</h3><span className={`rounded-full px-2.5 py-1 text-xs font-medium ring-1 ${status.className}`}>{status.label}</span></div><p className="mt-2 break-all text-xs text-slate-500">变更集 #{row.id} · 提交说明：{row.commit_message || "未填写"} · 创建于 {time(row.created_at)}</p></div><div className="flex flex-wrap gap-2"><button type="button" disabled={isBusy || !canValidate} onClick={onValidate} className="rounded-lg border border-slate-200 px-3 py-2 text-xs font-medium text-slate-700 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40">重新校验</button><button type="button" disabled={isBusy || row.status !== "pending_approval"} onClick={onReject} className="rounded-lg border border-rose-200 px-3 py-2 text-xs font-medium text-rose-700 hover:bg-rose-50 disabled:cursor-not-allowed disabled:opacity-40">拒绝</button><button type="button" disabled={isBusy || row.status !== "pending_approval"} onClick={onApprove} className="inline-flex items-center gap-1 rounded-lg bg-emerald-600 px-3 py-2 text-xs font-medium text-white hover:bg-emerald-700 disabled:cursor-not-allowed disabled:bg-slate-300"><ShieldCheck size={14} />批准</button><button type="button" disabled={isBusy || row.status !== "approved"} onClick={onDeliver} className="inline-flex items-center gap-1 rounded-lg bg-blue-600 px-3 py-2 text-xs font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-slate-300"><GitCommit size={14} />提交并推送</button></div></div>
    <div className="mt-4 rounded-xl bg-slate-50 px-3.5 py-3 text-sm leading-6 text-slate-600">{row.summary || row.error_summary || "等待生成变更快照。"}</div>
    <div className="mt-4 grid gap-3 lg:grid-cols-2">{row.repositories.map((repository) => <section key={`${repository.repository_id}:${repository.repository_name}`} className="rounded-xl border border-slate-200 p-3.5"><div className="flex flex-wrap items-center justify-between gap-2"><p className="font-medium text-slate-800">{repository.display_name || repository.repository_name || "未命名仓库"}</p><span className="text-xs text-slate-500">{repository.branch || "未识别分支"}</span></div><p className="mt-1 text-xs text-slate-500">{repository.files.length} 个文件 · {repository.delivery_status || "等待交付"}{repository.commit_sha ? ` · ${repository.commit_sha.slice(0, 8)}` : ""}</p>{repository.files.length > 0 && <p className="mt-2 line-clamp-2 text-xs leading-5 text-slate-500">{repository.files.slice(0, 4).join(" · ")}</p>}<ul className="mt-3 space-y-1.5">{repository.checks.map((check) => <li key={check.name} className={`flex items-start gap-1.5 text-xs leading-5 ${check.passed ? "text-emerald-700" : "text-rose-700"}`}>{check.passed ? <CheckCircle2 className="mt-0.5 shrink-0" size={13} /> : <XCircle className="mt-0.5 shrink-0" size={13} />}<span><b>{check.name}</b>：{check.message}</span></li>)}</ul></section>)}</div>
    {isCurrentRowBusy && <div className="mt-4 flex items-center gap-2 text-xs text-blue-700"><Loader2 className="animate-spin" size={14} />正在处理变更集，请勿重复提交。</div>}
  </article>;
}
