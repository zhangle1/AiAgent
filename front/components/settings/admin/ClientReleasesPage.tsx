"use client";
import { useEffect, useRef, useState } from "react";
import { Download, Package, RefreshCw, UploadCloud } from "lucide-react";
import { getAuthStatus } from "@/lib/auth-api";
import { clientReleaseFileUrl, listClientReleases, uploadClientRelease, withdrawClientRelease, type ClientRelease } from "@/lib/client-release-api";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";

export function ClientReleasesPage() {
  const [allowed, setAllowed] = useState<boolean | null>(null), [rows, setRows] = useState<ClientRelease[]>([]);
  const [version, setVersion] = useState(""), [platform, setPlatform] = useState("windows"), [architecture, setArchitecture] = useState("x64"), [notes, setNotes] = useState("");
  const [file, setFile] = useState<File | null>(null), [busy, setBusy] = useState(false), [progress, setProgress] = useState(0), [message, setMessage] = useState(""), [loading, setLoading] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);
  const load = async () => { setLoading(true); try { setRows(await listClientReleases()); } catch (error) { setMessage(error instanceof Error ? error.message : "版本列表读取失败"); } finally { setLoading(false); } };
  useEffect(() => { let active = true; void getAuthStatus().then(status => { if (active) setAllowed(status.is_admin === true); }).catch(() => { if (active) setAllowed(false); }); return () => { active = false; }; }, []);
  useEffect(() => { if (allowed) void load(); }, [allowed]);
  if (allowed === null) return <p className="p-6 text-sm text-slate-500">正在检查管理权限…</p>;
  if (!allowed) return <p className="rounded-xl border border-amber-200 bg-amber-50 p-6 text-sm text-amber-800">安装包发布仅对管理员开放。</p>;
  const field = "mt-2 w-full rounded-lg border border-slate-200 bg-white px-3 py-2.5 text-sm outline-none focus:border-blue-500 focus:ring-2 focus:ring-blue-100 disabled:opacity-50";
  return <section>
    <SettingsPageHeader title="坤伴客户端发布" description="上传 Windows / macOS 安装包。客户端从当前登录的平台检查版本；切换平台即替换更新来源。" action={<button disabled={busy || loading} onClick={() => void load()} className="inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-sm disabled:opacity-50"><RefreshCw size={15} />刷新</button>} />
    {message && <p role="status" className="mb-5 rounded-xl border border-blue-100 bg-blue-50 p-4 text-sm text-blue-800">{message}</p>}
    <form className="mb-6 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm" onSubmit={event => {
      event.preventDefault(); if (!file || busy) return;
      if (file.size > 2 * 1024 ** 3) { setMessage("安装包不能超过 2 GiB。"); return; }
      setBusy(true); setMessage(""); setProgress(0);
      void uploadClientRelease(file, { version, platform, architecture, notes }, setProgress).then(async () => {
        setMessage(`版本 ${version} 已发布，客户端下次检查时即可发现。`); setFile(null); if (fileInput.current) fileInput.current.value = ""; await load();
      }).catch(error => setMessage(error instanceof Error ? error.message : "上传失败")).finally(() => setBusy(false));
    }}>
      <h2 className="flex items-center gap-2 font-semibold"><UploadCloud size={19} className="text-blue-600" />上传并发布</h2>
      <div className="mt-5 grid gap-4 md:grid-cols-3">
        <label className="text-sm text-slate-600">版本号<input className={field} placeholder="0.20.2" value={version} onChange={e => setVersion(e.target.value)} pattern="(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)" required disabled={busy} /></label>
        <label className="text-sm text-slate-600">操作系统<select className={field} value={platform} disabled={busy} onChange={e => { setPlatform(e.target.value); setFile(null); if (fileInput.current) fileInput.current.value = ""; }}><option value="windows">Windows · EXE</option><option value="macos">macOS · DMG / ZIP</option></select></label>
        <label className="text-sm text-slate-600">架构<select className={field} value={architecture} disabled={busy} onChange={e => setArchitecture(e.target.value)}><option value="x64">x64 / Intel</option><option value="arm64">arm64 / Apple Silicon</option></select></label>
      </div>
      <label className="mt-4 block text-sm text-slate-600">安装包<input ref={fileInput} className={field} type="file" accept={platform === "windows" ? ".exe" : ".dmg,.zip"} onChange={e => setFile(e.target.files?.[0] ?? null)} required disabled={busy} /></label>
      <p className="mt-2 text-xs leading-5 text-slate-500">上传本项目打包的 KunBuddy 安装包，版本号须与包内版本一致。同平台、架构的版本不可覆盖。Windows 推荐 NSIS 安装包；Mac ZIP 必须含 KunBuddy.app。</p>
      <label className="mt-4 block text-sm text-slate-600">更新说明<textarea className={field} rows={3} maxLength={10000} value={notes} disabled={busy} onChange={e => setNotes(e.target.value)} /></label>
      {busy && <div className="mt-4" role="status"><progress aria-label="安装包上传进度" max={100} value={progress} className="w-full" /><p className="mt-1 text-xs text-slate-500">{progress < 100 ? `上传中 ${progress}%` : "上传完成，正在校验并发布…"}</p></div>}
      <button type="submit" disabled={busy || !file || !version} className="mt-4 inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2.5 text-sm font-medium text-white disabled:opacity-50"><Package size={16} />{busy ? "正在发布…" : "上传并发布"}</button>
    </form>
    <div className="overflow-x-auto rounded-2xl border border-slate-200 bg-white"><table className="w-full text-left text-sm"><thead className="bg-slate-50 text-slate-500"><tr>{["版本 / 文件", "平台 / 架构", "大小", "状态", "操作"].map(label => <th key={label} className="px-5 py-3 font-medium">{label}</th>)}</tr></thead><tbody className="divide-y divide-slate-100">{rows.map(row => <tr key={row.id}>
      <td className="max-w-xs px-5 py-4"><strong>{row.version}</strong><span className="mt-1 block truncate text-xs text-slate-500" title={row.filename}>{row.filename}</span><span className="mt-1 block text-xs text-slate-400">{new Date(row.created_at).toLocaleString()}</span></td><td className="px-5 py-4">{row.platform === "windows" ? "Windows" : "macOS"} · {row.architecture}</td><td className="whitespace-nowrap px-5 py-4">{(row.size / 1024 ** 2).toFixed(1)} MB</td><td className="px-5 py-4"><span className={row.published ? "text-emerald-700" : "text-slate-400"}>{row.published ? "已发布" : "已撤回"}</span></td><td className="whitespace-nowrap px-5 py-4"><a href={clientReleaseFileUrl(row.id)} className="mr-4 inline-flex items-center gap-1 text-blue-600"><Download size={14} />下载</a>{row.published && <button disabled={busy} className="text-red-600 disabled:opacity-50" onClick={() => { if (!window.confirm(`撤回版本 ${row.version}？客户端将不再获取该安装包。`)) return; setBusy(true); void withdrawClientRelease(row.id).then(load).catch(error => setMessage(error instanceof Error ? error.message : "撤回失败")).finally(() => setBusy(false)); }}>撤回</button>}</td>
    </tr>)}{!rows.length && <tr><td colSpan={5} className="px-5 py-12 text-center text-slate-400">{loading ? "正在读取版本…" : "暂无发布版本，上传第一个安装包开始。"}</td></tr>}</tbody></table></div>
  </section>;
}
