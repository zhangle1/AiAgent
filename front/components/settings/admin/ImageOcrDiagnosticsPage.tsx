"use client";

import { useEffect, useRef, useState } from "react";
import { CheckCircle2, CircleAlert, ImagePlus, Loader2, ScanText } from "lucide-react";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";
import { getAuthStatus } from "@/lib/auth-api";
import { deleteChatImage, uploadChatImage } from "@/lib/chat-api";
import { diagnoseImageOcr, type ImageOcrDiagnostic } from "@/lib/agent-provider-api";

export function ImageOcrDiagnosticsPage() {
  const [allowed, setAllowed] = useState<boolean | null>(null);
  const [diagnostic, setDiagnostic] = useState<ImageOcrDiagnostic | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [checking, setChecking] = useState(false);
  const [testingImage, setTestingImage] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    void getAuthStatus().then((status) => setAllowed(status.is_admin === true)).catch(() => setAllowed(false));
  }, []);

  async function runDiagnostic(attachmentId?: string) {
    setChecking(true);
    try {
      setDiagnostic(await diagnoseImageOcr(attachmentId));
      setError(null);
    }
    catch (reason) { setError(reason instanceof Error ? reason.message : "PaddleOCR 环境检测失败。"); }
    finally { setChecking(false); }
  }

  async function testImage(file: File | null) {
    if (!file) return;
    setTestingImage(true);
    let attachmentId: string | null = null;
    try {
      const attachment = await uploadChatImage(file);
      attachmentId = attachment.id;
      await runDiagnostic(attachmentId);
    }
    catch (reason) { setError(reason instanceof Error ? reason.message : "图片上传或 OCR 解析失败。"); }
    finally {
      if (attachmentId) await deleteChatImage(attachmentId).catch(() => undefined);
      if (fileInputRef.current) fileInputRef.current.value = "";
      setTestingImage(false);
    }
  }

  if (allowed === null) return <div className="flex min-h-[300px] items-center justify-center text-sm text-slate-400"><Loader2 size={18} className="mr-2 animate-spin" />正在验证管理权限…</div>;
  if (!allowed) return <section className="rounded-2xl border border-amber-200 bg-amber-50 p-8 text-center"><CircleAlert size={28} className="mx-auto text-amber-600" /><h1 className="mt-3 text-lg font-semibold text-amber-900">没有管理权限</h1><p className="mt-2 text-sm text-amber-700">图片 OCR 测试仅对管理员开放。</p></section>;

  return <section>
    <SettingsPageHeader title="图片 OCR 测试" description="独立验证 PaddleOCR 的 Python 环境与实际图片识别链路；测试图片只会临时保存，完成后自动删除。" action={null} />
    <section className="rounded-2xl border border-cyan-200 bg-cyan-50/50 p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div><h2 className="text-sm font-semibold text-slate-800">PaddleOCR 环境与图片解析</h2><p className="mt-1 max-w-2xl text-xs leading-5 text-slate-500">先检查 Python、PaddlePaddle 和 PaddleOCR 配置，再用一张图片验证受控上传、Worker 调度与识别结果。</p></div>
        <div className="flex flex-wrap gap-2">
          <button type="button" onClick={() => void runDiagnostic()} disabled={checking || testingImage} className="inline-flex h-9 items-center gap-2 rounded-md border border-cyan-200 bg-white px-3 text-xs font-medium text-cyan-700 hover:bg-cyan-100 disabled:opacity-50">{checking ? <Loader2 size={14} className="animate-spin" /> : <ScanText size={14} />}检查环境</button>
          <input ref={fileInputRef} type="file" accept="image/png,image/jpeg,image/webp,image/gif" className="hidden" onChange={(event) => void testImage(event.target.files?.[0] ?? null)} />
          <button type="button" onClick={() => fileInputRef.current?.click()} disabled={checking || testingImage} className="inline-flex h-9 items-center gap-2 rounded-md bg-cyan-600 px-3 text-xs font-medium text-white hover:bg-cyan-700 disabled:bg-slate-300">{testingImage ? <Loader2 size={14} className="animate-spin" /> : <ImagePlus size={14} />}上传测试图片</button>
        </div>
      </div>
      {diagnostic && <DiagnosticResult diagnostic={diagnostic} />}
      {error && <p className="mt-4 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-xs text-rose-700">{error}</p>}
    </section>
  </section>;
}

function DiagnosticResult({ diagnostic }: { diagnostic: ImageOcrDiagnostic }) {
  return <div className={`mt-4 rounded-xl border p-3 text-xs ${diagnostic.ready ? "border-emerald-200 bg-white" : "border-rose-200 bg-rose-50"}`}>
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1"><span className={`inline-flex items-center gap-1 font-medium ${diagnostic.ready ? "text-emerald-700" : "text-rose-700"}`}>{diagnostic.ready ? <CheckCircle2 size={14} /> : <CircleAlert size={14} />}{diagnostic.ready ? "环境可用" : "环境不可用"}</span><span className="text-slate-600">Python：{diagnostic.python_configured ? "已配置" : "未配置"}</span><span className="text-slate-600">Worker：{diagnostic.worker_configured ? "已配置" : "未配置"}</span>{diagnostic.paddle_version && <span className="text-slate-600">Paddle {diagnostic.paddle_version}</span>}{diagnostic.paddleocr_version && <span className="text-slate-600">PaddleOCR {diagnostic.paddleocr_version}</span>}</div>
    {diagnostic.error && <p className="mt-2 break-words text-rose-700">{diagnostic.error}</p>}
    {diagnostic.result && <div className="mt-3 rounded-lg bg-slate-950 p-3 text-slate-100"><p className="text-[11px] text-slate-300">{diagnostic.result.engine} · {diagnostic.result.elapsed_ms} ms{diagnostic.result.confidence != null ? ` · 置信度 ${diagnostic.result.confidence}` : ""}</p><pre className="mt-2 max-h-52 overflow-auto whitespace-pre-wrap break-words font-sans text-xs leading-5">{diagnostic.result.text || "未识别到可用文本。"}</pre></div>}
  </div>;
}
