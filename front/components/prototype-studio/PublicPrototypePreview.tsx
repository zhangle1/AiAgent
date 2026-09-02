"use client";

import { useEffect, useMemo, useState } from "react";
import { AlertCircle, Loader2 } from "lucide-react";
import { getPublicPrototypeTemplate, type PublicPrototypeTemplate } from "@/lib/prompt-template-api";
import { decodePrototypeHtml, securePrototypePreview } from "@/lib/prototype-preview";

export function PublicPrototypePreview({ prototypeId }: { prototypeId: number | null }) {
  const [template, setTemplate] = useState<PublicPrototypeTemplate | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!prototypeId) { setError("分享链接缺少原型编号。"); return; }
    let cancelled = false;
    void getPublicPrototypeTemplate(prototypeId)
      .then((item) => { if (!cancelled) setTemplate(item); })
      .catch((value: unknown) => { if (!cancelled) setError(value instanceof Error ? value.message : "原型加载失败。"); });
    return () => { cancelled = true; };
  }, [prototypeId]);

  const preview = useMemo(() => template ? securePrototypePreview(decodePrototypeHtml(template.body)) : "", [template]);

  if (error) return <main className="grid min-h-screen place-items-center bg-slate-100 p-6"><div className="max-w-md rounded-2xl border border-rose-200 bg-white p-6 text-center shadow-sm"><AlertCircle className="mx-auto text-rose-500" size={28} /><h1 className="mt-3 text-base font-semibold text-slate-900">无法打开此原型</h1><p className="mt-2 text-sm leading-6 text-slate-500">{error}</p></div></main>;
  if (!template) return <main className="grid min-h-screen place-items-center bg-slate-100 text-sm text-slate-500"><span className="inline-flex items-center gap-2"><Loader2 className="animate-spin" size={17} />正在加载原型…</span></main>;
  return <main className="flex min-h-screen flex-col bg-slate-100"><header className="flex min-h-12 items-center border-b border-slate-200 bg-white px-4 text-sm font-medium text-slate-700">{template.name}</header><div className="min-h-0 flex-1 p-3 sm:p-5"><iframe title={template.name} sandbox="allow-scripts" srcDoc={preview} className="h-[calc(100vh-5.5rem)] min-h-[480px] w-full rounded-xl border border-slate-200 bg-white shadow-sm" /></div></main>;
}
