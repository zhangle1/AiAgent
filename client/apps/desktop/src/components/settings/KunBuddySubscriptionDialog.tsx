import { useEffect, useState } from "react";
import type { ModelBinding, ProviderPublic } from "@kunbuddy/shared";
import { api } from "../../lib/api";
import { Button, Field, Input, OverlayBackdrop, PasswordInput, portalOverlay } from "../ui";

export function KunBuddySubscriptionDialog({ provider, onClose, onSaved, onDisconnected }: {
  provider?: ProviderPublic | null;
  onClose: () => void;
  onSaved: (provider: ProviderPublic, models: ModelBinding[]) => void | Promise<void>;
  onDisconnected: () => void | Promise<void>;
}) {
  const [baseUrl, setBaseUrl] = useState(() => provider?.baseUrl?.replace(/\/api\/v1\/kunbuddy\/profiles\/[^/]+$/, "") ?? "");
  const [username, setUsername] = useState(provider?.oauthAccountLabel ?? "");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [syncStatus, setSyncStatus] = useState<Awaited<ReturnType<typeof api.kunBuddySessionSyncStatus>> | null>(null);
  useEffect(() => {
    if (!provider) return;
    let active = true;
    void api.kunBuddySessionSyncStatus().then(value => { if (active) setSyncStatus(value); }).catch(() => undefined);
    return () => { active = false; };
  }, [provider]);
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === "Escape" && !busy) onClose(); };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [busy, onClose]);
  const connect = async () => {
    if (busy) return;
    setBusy(true); setError("");
    try {
      const result = await api.loginKunBuddySubscription({ baseUrl, username, password });
      setPassword("");
      if (result.providers[0]) await onSaved(result.providers[0], result.providers[0].models);
      onClose();
    } catch (failure) { setError(failure instanceof Error ? failure.message : "订阅登录失败。"); }
    finally { setBusy(false); }
  };
  const disconnect = async () => {
    if (!provider || busy) return;
    setBusy(true); setError("");
    try { await api.logoutKunBuddySubscription(provider.id); await onDisconnected(); onClose(); }
    catch (failure) { setError(failure instanceof Error ? failure.message : "退出订阅失败。"); }
    finally { setBusy(false); }
  };
  return portalOverlay(<OverlayBackdrop className="overlay provider-setup-overlay" role="presentation" onClick={() => { if (!busy) onClose(); }}>
    <section className="dialog kunbuddy-subscription-dialog" role="dialog" aria-modal="true" aria-labelledby="kunbuddy-subscription-title" onClick={event => event.stopPropagation()}>
      <header className="provider-setup-head">
        <h3 id="kunbuddy-subscription-title">坤伴订阅</h3>
        <Button variant="ghost" size="sm" disabled={busy} onClick={onClose}>取消</Button>
      </header>
      <form className="kunbuddy-subscription-body" onSubmit={event => { event.preventDefault(); void connect(); }}>
        <p className="kunbuddy-subscription-description">登录你的 AiAgent，自动同步服务器授权的模型和容量配置。平台模型密钥保留在服务端，本机只保存加密的短期登录凭证。</p>
        <Field label="AiAgent 服务地址" hint="填写服务器地址，例如 https://ai.example.com 或内网地址与端口。">
          <Input autoFocus value={baseUrl} onChange={event => setBaseUrl(event.target.value)} placeholder="https://ai.example.com" disabled={busy} required />
        </Field>
        <Field label="账号"><Input autoComplete="username" value={username} onChange={event => setUsername(event.target.value)} disabled={busy} required /></Field>
        <Field label="密码"><PasswordInput showLabel="显示密码" hideLabel="隐藏密码" autoComplete="current-password" value={password} onChange={event => setPassword(event.target.value)} disabled={busy} required /></Field>
        {baseUrl.trim() && !/^https:\/\//i.test(baseUrl.trim()) && <p className="kunbuddy-subscription-description">HTTP 适合可信内网调试；公网部署请使用 HTTPS。</p>}
        <p className="kunbuddy-subscription-description">登录有效期 8 小时，过期后可在此重新登录。此入口连接现有账号，不包含套餐购买或支付。</p>
        <p className="kunbuddy-subscription-description">使用坤伴订阅的会话会自动保存到平台“人员 → 我的工作区 → 会话”，每个会话一个 Markdown 文件，持续追加原始记录。同步不调用 LLM，也不自动解析、编译或上传附件文件。</p>
        {provider && syncStatus && <div className="kunbuddy-subscription-description" role="status">
          会话同步：{syncStatus.pending ? `${syncStatus.pending} 个会话等待上传` : "没有待上传记录"}
          {syncStatus.sessions.some(item => item.error === "login_required") && "；登录已失效，请重新登录后重试。"}
          {syncStatus.sessions.some(item => item.error && item.error !== "login_required") && "；部分会话尚未同步，请检查网络或查看运行日志。"}
          <Button type="button" variant="ghost" size="sm" disabled={busy} onClick={async () => {
            setBusy(true); setError("");
            try { setSyncStatus(await api.retryKunBuddySessionSync()); }
            catch { setError("会话同步未完成，请检查网络并重试。"); }
            finally { setBusy(false); }
          }}>重试会话同步</Button>
        </div>}
        {error && <p role="alert" className="kunbuddy-subscription-error">{error}</p>}
        <footer className="kunbuddy-subscription-actions">
          {provider && <Button type="button" variant="ghost" disabled={busy} onClick={() => void disconnect()}>退出此账号</Button>}
          <Button type="submit" variant="primary" disabled={busy || !baseUrl.trim() || !username.trim() || !password}>{busy ? "连接中…" : provider ? "重新登录并同步模型" : "登录并同步模型"}</Button>
        </footer>
      </form>
    </section>
  </OverlayBackdrop>);
}
