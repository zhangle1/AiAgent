import { useEffect, useState } from 'react'
import type { Context } from '@deepseek-ai/cordis'
import contribution, { type AiAgentCodexTest, type AiAgentRemoteStatus } from './remote-client.js'

type RemoteResult<T> = { ok: true; value: T } | { ok: false; error: { message: string } }
type Remote = {
  status(): Promise<RemoteResult<AiAgentRemoteStatus>>
  login(baseUrl: string, username: string, password: string): Promise<RemoteResult<AiAgentRemoteStatus>>
  logout(): Promise<RemoteResult<void>>
  testCodex(): Promise<RemoteResult<AiAgentCodexTest>>
}
type ClientContext = Context & {
  remote: { $mount(value: typeof contribution): Promise<() => Promise<void>>; aiagentRemote?: Remote }
  slots: {
    inject(name: string, callback: () => unknown): void
    register(options: Record<string, unknown>, component: () => JSX.Element): unknown
  }
}

export const inject = ['slots', 'remote']

function unwrap<T>(result: RemoteResult<T>): T {
  if (!result.ok) throw new Error(result.error.message)
  return result.value
}

export async function apply(ctx: Context): Promise<void> {
  const client = ctx as ClientContext
  await client.remote.$mount(contribution)
  ctx.inject(['remote.aiagentRemote'], (remoteCtx) => {
    const mountedRemote = (remoteCtx as ClientContext).remote.aiagentRemote
    if (mountedRemote === undefined) throw new Error('AiAgent Remote API did not mount.')
    const api = mountedRemote
    function AiAgentSettings() {
    const [status, setStatus] = useState<AiAgentRemoteStatus | undefined>()
    const [baseUrl, setBaseUrl] = useState('http://127.0.0.1:5000')
    const [username, setUsername] = useState('')
    const [password, setPassword] = useState('')
    const [error, setError] = useState('')
    const [busy, setBusy] = useState(false)
    const [codexTest, setCodexTest] = useState<AiAgentCodexTest | undefined>()
    useEffect(() => { void api.status().then(unwrap).then(value => { setStatus(value); setBaseUrl(value.baseUrl) }, () => {}) }, [])
    const login = async () => {
      setBusy(true); setError('')
      try { setStatus(unwrap(await api.login(baseUrl, username, password))); setPassword(''); setCodexTest(undefined) }
      catch (cause) { setError(cause instanceof Error ? cause.message : '登录失败。') }
      finally { setBusy(false) }
    }
    const testCodex = async () => {
      setBusy(true); setError(''); setCodexTest(undefined)
      try { setCodexTest(unwrap(await api.testCodex())) }
      catch (cause) { setError(cause instanceof Error ? cause.message : '服务器 Codex CLI 测试失败。') }
      finally { setBusy(false) }
    }
    const logout = async () => {
      setBusy(true); setError('')
      try { unwrap(await api.logout()); setStatus({ configured: false, baseUrl, models: [], codexAvailable: false }); setCodexTest(undefined) }
      catch (cause) { setError(cause instanceof Error ? cause.message : '退出失败。') }
      finally { setBusy(false) }
    }
    return <section style={{ padding: 16, maxWidth: 520 }}>
      <h2>AiAgent Remote</h2>
      <p>登录信息仅用于换取短期令牌；密码不会写入 DSH 配置文件。</p>
      <label style={{ display: 'block', marginTop: 12 }}>AiAgent 地址<input value={baseUrl} onChange={event => setBaseUrl(event.target.value)} disabled={busy} style={{ display: 'block', width: '100%' }} /></label>
      <label style={{ display: 'block', marginTop: 12 }}>用户名<input value={username} onChange={event => setUsername(event.target.value)} disabled={busy} style={{ display: 'block', width: '100%' }} /></label>
      <label style={{ display: 'block', marginTop: 12 }}>密码<input type="password" value={password} onChange={event => setPassword(event.target.value)} disabled={busy} style={{ display: 'block', width: '100%' }} /></label>
      <p role="status">{status?.configured ? `已登录；可用模型：${status.models.map(model => model.name).join('、') || '正在加载'}` : '尚未登录'}</p>
      <section style={{ borderTop: '1px solid #666', marginTop: 20, paddingTop: 16 }}>
        <h3>服务器 Codex CLI</h3>
        <p>{status?.configured ? status.codexAvailable ? '已检测到服务器 Codex CLI；可供 aiagent_codex_delegate 工具委托。' : '服务器未检测到可用的 Codex CLI。' : '登录后可检测服务器 Codex CLI。'}</p>
        <button type="button" onClick={() => { void testCodex() }} disabled={busy || status?.codexAvailable !== true}>测试服务器 Codex CLI</button>
        {codexTest ? <p role="status">测试成功（{codexTest.modelId}）：{codexTest.answer}</p> : null}
        <p>测试不会传送本机文件，服务器 Codex 以只读方式运行。</p>
      </section>
      {error ? <p role="alert">{error}</p> : null}
      <button type="button" onClick={() => { void login() }} disabled={busy}>登录并同步模型</button>
      {status?.configured ? <button type="button" onClick={() => { void logout() }} disabled={busy} style={{ marginLeft: 8 }}>退出</button> : null}
    </section>
    }
    client.slots.inject('settings.plugins.tab', () => client.slots.register({
      name: 'settings.plugins.tab', id: 'aiagent-remote', order: 10, label: () => 'AiAgent Remote',
    }, AiAgentSettings))
  })
}
