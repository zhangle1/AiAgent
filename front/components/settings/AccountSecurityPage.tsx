"use client";

import { useState } from "react";
import { KeyRound, Loader2, LogOut, ShieldCheck } from "lucide-react";
import { useRouter } from "next/navigation";
import { changePassword } from "@/lib/auth-api";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";

export function AccountSecurityPage() {
  const router = useRouter();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault(); setError("");
    if (newPassword.length < 6) return setError("新密码至少需要 6 位。");
    if (newPassword !== confirmPassword) return setError("两次输入的新密码不一致。");
    setSaving(true);
    try {
      await changePassword(currentPassword, newPassword);
      router.replace("/login?password_changed=1");
    } catch (reason) { setError(reason instanceof Error ? reason.message : "修改密码失败，请稍后重试。"); }
    finally { setSaving(false); }
  };

  return <section><SettingsPageHeader title="账号安全" description="修改密码后，当前账号的全部登录会话都会失效；请使用新密码重新登录。" action={null} />
    <form onSubmit={submit} className="max-w-xl rounded-2xl border border-slate-200 bg-white p-6 shadow-sm"><div className="flex items-start gap-3 rounded-xl border border-blue-100 bg-blue-50 p-4 text-sm text-blue-800"><ShieldCheck size={18} className="mt-0.5 shrink-0" /><p>为保护账号安全，需要先验证当前密码。新密码保存成功后将自动退出登录。</p></div><div className="mt-5 space-y-4"><PasswordField label="当前密码" value={currentPassword} onChange={setCurrentPassword} autoComplete="current-password" /><PasswordField label="新密码" value={newPassword} onChange={setNewPassword} autoComplete="new-password" /><PasswordField label="确认新密码" value={confirmPassword} onChange={setConfirmPassword} autoComplete="new-password" />{error && <p className="rounded-lg border border-red-100 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}</div><div className="mt-6 flex justify-end border-t border-slate-100 pt-5"><button disabled={saving} className="inline-flex h-10 items-center gap-2 rounded-lg bg-blue-600 px-4 text-sm font-medium text-white hover:bg-blue-700 disabled:bg-slate-300">{saving ? <Loader2 size={16} className="animate-spin" /> : <KeyRound size={16} />}{saving ? "保存中…" : "修改密码并退出"}<LogOut size={15} /></button></div></form></section>;
}

function PasswordField({ label, value, onChange, autoComplete }: { label: string; value: string; onChange: (value: string) => void; autoComplete: string }) {
  return <label className="block text-sm font-medium text-slate-700">{label}<input required minLength={6} type="password" value={value} onChange={(event) => onChange(event.target.value)} autoComplete={autoComplete} className="mt-1.5 h-10 w-full rounded-lg border border-slate-200 px-3 text-sm outline-none focus:border-blue-400 focus:ring-2 focus:ring-blue-50" /></label>;
}
