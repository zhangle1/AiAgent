"use client";

import { useState } from "react";
import { saveKnowledgeOrganization } from "@/lib/knowledge-api";
import type { KnowledgeBase } from "@/lib/knowledge-types";

export function KnowledgeOrganizationEditor({ knowledgeBase, onSaved }: { knowledgeBase: KnowledgeBase; onSaved: () => void }) {
  const [company, setCompany] = useState(knowledgeBase.organization?.company || "");
  const [project, setProject] = useState(knowledgeBase.organization?.project || "");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  return <form className="m-6 rounded-xl border p-5" onSubmit={async (event) => {
    event.preventDefault(); setBusy(true); setMessage("");
    try { await saveKnowledgeOrganization(knowledgeBase.name, { company, project }); setMessage("目录归属已保存"); onSaved(); }
    catch (ex) { setMessage(ex instanceof Error ? ex.message : String(ex)); }
    finally { setBusy(false); }
  }}><h2 className="font-semibold">目录归属</h2><p className="mt-1 text-xs text-zinc-500">公司 → 项目 → 知识库；项目留空表示公司公共知识。</p><div className="mt-4 flex flex-wrap items-end gap-4"><label className="text-sm">公司<input maxLength={180} value={company} onChange={(e) => setCompany(e.target.value)} className="mt-1 block rounded border px-3 py-2" /></label><label className="text-sm">项目<input maxLength={180} value={project} onChange={(e) => setProject(e.target.value)} className="mt-1 block rounded border px-3 py-2" /></label><button disabled={busy} className="rounded bg-blue-600 px-4 py-2 text-sm text-white disabled:opacity-50">{busy ? "保存中…" : "保存归属"}</button></div><p role="status" className="mt-3 text-sm">{message}</p></form>;
}
