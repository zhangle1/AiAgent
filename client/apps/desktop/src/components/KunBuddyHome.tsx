import { useEffect, useState } from "react";
import type { KunBuddyAccountStatus } from "@kunbuddy/shared";
import logo from "../assets/brand/kunbuddy.png";
import { IconFolder, IconShield, IconSparkles } from "./icons";
import { Button, Field, Input, PasswordInput } from "./ui";

export function KunBuddyHome({ account, checking, onLogin, onRetry }: {
  account: KunBuddyAccountStatus | null;
  checking: boolean;
  onLogin: (input: { baseUrl: string; username: string; password: string }) => Promise<void>;
  onRetry: () => Promise<void>;
}) {
  const [baseUrl, setBaseUrl] = useState(account?.baseUrl ?? "");
  const [username, setUsername] = useState(account?.username ?? "");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => { if (account?.baseUrl) setBaseUrl(account.baseUrl); }, [account?.baseUrl]);
  useEffect(() => { if (account?.username) setUsername(account.username); }, [account?.username]);
  const pending = busy || !account;
  return <main className="kunbuddy-home" data-testid="kunbuddy-login-home">
    <header className="kunbuddy-home-titlebar"><img src={logo} alt="" /><span>KunBuddy <span className="kunbuddy-home-brand-note">坤伴</span></span></header>
    <div className="kunbuddy-home-content">
      <section className="kunbuddy-home-intro" aria-labelledby="kunbuddy-home-heading">
        <span className="kunbuddy-home-eyebrow">你的 AI 工作伙伴</span>
        <h1 id="kunbuddy-home-heading">连接你的工作空间，<br />让想法接着发生。</h1>
        <p className="kunbuddy-home-lead">从一次对话开始，把资料、代码与灵感连在一起。登录后，继续你熟悉的工作。</p>
        <ul className="kunbuddy-home-features">
          <li><IconSparkles size={19} /><div><strong>模型，就绪</strong><span>自动同步平台配置的模型，打开即可开始。</span></div></li>
          <li><IconFolder size={19} /><div><strong>思考，有迹可循</strong><span>订阅会话原文持续保存到你的个人工作区。</span></div></li>
          <li><IconShield size={19} /><div><strong>账号连接，安心协作</strong><span>平台管理模型密钥，本机加密保存登录凭证。</span></div></li>
        </ul>
      </section>
      <section className="kunbuddy-home-login" aria-labelledby="kunbuddy-home-login-title">
        <div className="kunbuddy-home-login-head"><span className="kunbuddy-home-eyebrow">欢迎回来</span><h2 id="kunbuddy-home-login-title">登录坤伴</h2><p>连接 AiAgent，同步你的坤伴订阅。</p></div>
        <form onSubmit={event => {
          event.preventDefault(); if (pending) return;
          setBusy(true); setError("");
          void onLogin({ baseUrl, username, password }).then(() => setPassword(""))
            .catch(failure => setError(failure instanceof Error ? failure.message : "登录未完成，请重试。"))
            .finally(() => setBusy(false));
        }}>
          {account?.state === "expired" && <p className="kunbuddy-home-notice" role="status">登录已过期，请重新登录。你的本地会话仍然保留。</p>}
          {account?.state === "unavailable" && <div className="kunbuddy-home-notice" role="status">暂时无法确认登录状态，请检查平台地址和网络。
            <Button type="button" variant="ghost" size="sm" disabled={checking || busy} onClick={() => void onRetry()}>{checking ? "检查中…" : "重新检查"}</Button>
          </div>}
          <Field label="平台地址"><Input name="baseUrl" autoFocus={!baseUrl} placeholder="https://ai.example.com 或 IP:端口" value={baseUrl} onChange={e => setBaseUrl(e.target.value)} required disabled={pending} maxLength={2048} /></Field>
          <Field label="用户名"><Input name="username" autoFocus={!!baseUrl} autoComplete="username" placeholder="输入你的账号" value={username} onChange={e => setUsername(e.target.value)} required disabled={pending} maxLength={128} /></Field>
          <Field label="密码"><PasswordInput name="password" showLabel="显示密码" hideLabel="隐藏密码" autoComplete="current-password" placeholder="输入密码" value={password} onChange={e => setPassword(e.target.value)} required disabled={pending} maxLength={4096} /></Field>
          {error && <p className="kunbuddy-home-error" role="alert">{error}</p>}
          <Button type="submit" variant="primary" className="kunbuddy-home-submit" disabled={pending || !baseUrl.trim() || !username.trim() || !password}>{busy ? "正在登录并同步…" : !account ? "正在检查登录…" : "登录并进入工作区"}</Button>
          <p className="kunbuddy-home-form-note">登录后自动更新“坤伴订阅”。仅记住平台地址和账号，密码不会保存在本机。</p>
        </form>
      </section>
    </div>
    <footer className="kunbuddy-home-footer"><span>KunBuddy · 让工作多一个伙伴</span><span>公网连接请使用 HTTPS</span></footer>
  </main>;
}
