"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { Sparkles } from "lucide-react";
import { login } from "@/lib/auth-api";
import { resolvePostLoginPath } from "@/lib/auth-redirect";

export function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setSubmitting(true);
    setError("");
    try {
      await login(username, password);
      router.replace(resolvePostLoginPath(searchParams.get("next")));
      router.refresh();
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "登录失败，请重试。");
    } finally {
      setSubmitting(false);
    }
  };

  return <main className="flex min-h-screen items-center justify-center bg-zinc-50 px-4"><section className="w-full max-w-md rounded-2xl border border-zinc-200 bg-white p-8 shadow-sm"><div className="mb-8 text-center"><span className="mx-auto flex h-11 w-11 items-center justify-center rounded-xl border border-sky-100 bg-sky-50 text-sky-600"><Sparkles size={20} /></span><h1 className="mt-4 font-serif text-2xl font-semibold">欢迎回来</h1><p className="mt-2 text-sm text-zinc-500">登录后继续你的知识对话</p></div><form onSubmit={submit} className="space-y-4"><Field label="账号" value={username} onChange={setUsername} autoComplete="username" /><Field label="密码" value={password} onChange={setPassword} type="password" autoComplete="current-password" />{error && <p className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}<button disabled={submitting} className="flex h-11 w-full items-center justify-center rounded-lg bg-sky-600 text-sm font-medium text-white hover:bg-sky-700 disabled:bg-zinc-300">{submitting ? "正在登录…" : "登录"}</button></form><p className="mt-5 text-center text-xs leading-5 text-zinc-400">账号由管理员创建。如需开通访问权限，请联系管理员。</p></section></main>;
}

function Field({ label, value, onChange, type = "text", autoComplete }: { label: string; value: string; onChange: (value: string) => void; type?: string; autoComplete: string }) {
  return <label className="block text-sm font-medium text-zinc-700">{label}<input type={type} value={value} onChange={(event) => onChange(event.target.value)} autoComplete={autoComplete} required className="mt-1.5 h-11 w-full rounded-lg border border-zinc-200 px-3 text-sm outline-none transition focus:border-sky-500 focus:ring-2 focus:ring-sky-100" /></label>;
}
