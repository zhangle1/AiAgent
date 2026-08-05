"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Check, ChevronLeft, ChevronRight, FileDiff, FolderGit2, MessageSquare, X, type LucideIcon } from "lucide-react";

const storageKey = "aiagent.frontend-onboarding.completed";

type GuideStep = {
  title: string;
  description: string;
  detail: string;
  actionLabel?: string;
  actionPath?: string;
  icon: LucideIcon;
  iconClassName: string;
};

const steps: GuideStep[] = [
  {
    title: "先选择项目",
    description: "让 AI 了解当前代码上下文",
    detail: "进入聊天后，在输入框下方点击“项目”，选择已配置的项目。AI 会读取该项目下已登记的代码库来辅助分析和修改。",
    actionLabel: "打开聊天",
    actionPath: "/chat",
    icon: FolderGit2,
    iconClassName: "bg-blue-50 text-blue-600",
  },
  {
    title: "描述你的目标并开始对话",
    description: "把目标、限制和期望结果说清楚",
    detail: "例如说明要修改的功能、涉及的文件、验收标准和不能改动的范围。需要连续处理时，可以继续在同一个会话里补充要求。",
    actionLabel: "打开聊天",
    actionPath: "/chat",
    icon: MessageSquare,
    iconClassName: "bg-violet-50 text-violet-600",
  },
  {
    title: "查看差异，再提交代码",
    description: "确认修改后再推送到远程仓库",
    detail: "选中项目后，在聊天顶部的运行工具栏点击“差异”查看文件变更；确认无误后使用“提交推送”。也可以在“工具与设置 → Git 管理”中查看工作区、待推送和待拉取差异。",
    actionLabel: "打开 Git 管理",
    actionPath: "/settings/git",
    icon: FileDiff,
    iconClassName: "bg-emerald-50 text-emerald-600",
  },
];

export function FrontendOnboarding() {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [stepIndex, setStepIndex] = useState(0);

  useEffect(() => {
    try {
      setOpen(window.localStorage.getItem(storageKey) !== "true");
    } catch {
      setOpen(true);
    }
    const reopen = () => {
      setStepIndex(0);
      setOpen(true);
    };
    window.addEventListener("aiagent:onboarding-open", reopen);
    return () => window.removeEventListener("aiagent:onboarding-open", reopen);
  }, []);

  useEffect(() => {
    if (!open) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => { document.body.style.overflow = previousOverflow; };
  }, [open]);

  function completeGuide() {
    try { window.localStorage.setItem(storageKey, "true"); } catch { /* Browser privacy settings can disable local storage. */ }
    setOpen(false);
  }

  function openStepDestination() {
    const current = steps[stepIndex];
    if (!current.actionPath) return;
    setOpen(false);
    router.push(current.actionPath);
  }

  if (!open) return null;
  const current = steps[stepIndex];
  const Icon = current.icon;
  const isLast = stepIndex === steps.length - 1;

  return <div className="fixed inset-0 z-[110] flex items-end bg-slate-950/40 p-3 sm:items-center sm:justify-center sm:p-6" role="dialog" aria-modal="true" aria-labelledby="frontend-onboarding-title">
    <section className="w-full max-w-xl overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl">
      <header className="flex items-start justify-between gap-4 border-b border-slate-100 px-5 py-4 sm:px-6">
        <div><p className="text-xs font-semibold tracking-[.14em] text-blue-600">快速上手</p><h2 id="frontend-onboarding-title" className="mt-1 text-lg font-semibold text-slate-900">3 步开始使用 AiAgent</h2></div>
        <button type="button" onClick={completeGuide} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 transition hover:bg-slate-100 hover:text-slate-700" aria-label="跳过新手引导" title="跳过新手引导"><X size={17}/></button>
      </header>
      <div className="px-5 pt-5 sm:px-6"><div className="flex items-center gap-2" aria-label={`第 ${stepIndex + 1} 步，共 ${steps.length} 步`}>{steps.map((step, index) => <span key={step.title} className={`h-1.5 flex-1 rounded-full ${index <= stepIndex ? "bg-blue-600" : "bg-slate-100"}`}/>)}</div></div>
      <div className="px-5 py-6 sm:px-6 sm:py-7"><div className={`grid h-12 w-12 place-items-center rounded-2xl ${current.iconClassName}`}><Icon size={23}/></div><p className="mt-5 text-xs font-medium text-slate-400">步骤 {stepIndex + 1} / {steps.length}</p><h3 className="mt-1 text-xl font-semibold text-slate-900">{current.title}</h3><p className="mt-2 text-sm font-medium text-slate-600">{current.description}</p><p className="mt-4 text-sm leading-6 text-slate-500">{current.detail}</p>{current.actionLabel && <button type="button" onClick={openStepDestination} className="mt-5 inline-flex h-9 items-center gap-1.5 rounded-lg border border-blue-200 bg-blue-50 px-3 text-xs font-semibold text-blue-700 transition hover:bg-blue-100"><Icon size={14}/>{current.actionLabel}</button>}</div>
      <footer className="flex items-center justify-between gap-3 border-t border-slate-100 px-5 py-4 sm:px-6"><button type="button" onClick={() => setStepIndex((currentIndex) => Math.max(0, currentIndex - 1))} disabled={stepIndex === 0} className="inline-flex h-9 items-center gap-1 rounded-lg px-2 text-xs font-medium text-slate-500 transition hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-0"><ChevronLeft size={15}/>上一步</button><div className="flex items-center gap-2"><button type="button" onClick={completeGuide} className="hidden h-9 rounded-lg px-3 text-xs font-medium text-slate-500 hover:bg-slate-100 sm:inline-flex">跳过</button>{isLast ? <button type="button" onClick={completeGuide} className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-blue-600 px-3 text-xs font-semibold text-white transition hover:bg-blue-700"><Check size={15}/>完成</button> : <button type="button" onClick={() => setStepIndex((currentIndex) => currentIndex + 1)} className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-blue-600 px-3 text-xs font-semibold text-white transition hover:bg-blue-700">下一步<ChevronRight size={15}/></button>}</div></footer>
    </section>
  </div>;
}
