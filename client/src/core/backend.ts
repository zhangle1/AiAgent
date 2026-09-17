import { readAnalysisStream } from './analysis-stream';
import { analyzeSchema, loginSchema, type LoginInput } from '../shared/contracts';

export function normalizeAddress(input: string, allowHttp: boolean) {
  const raw = input.trim();
  let url: URL;
  try { url = new URL(raw.includes('://') ? raw : `https://${raw}`); } catch { throw new Error('后端地址无效'); }
  if (!['https:', 'http:'].includes(url.protocol) || url.username || url.password || url.search || url.hash || url.pathname !== '/') throw new Error('请输入后端根地址，不包含路径或凭据');
  if (url.protocol === 'http:' && !allowHttp) throw new Error('HTTP 需显式启用开发模式');
  return url.origin;
}

export class BackendClient {
  private session?: { address: string; token: string };
  private active = new Set<AbortController>();
  private analysis?: AbortController;
  private generation = 0;
  constructor(private fetchImpl: typeof fetch = fetch) {}
  logout() {
    this.generation++;
    this.session = undefined;
    for (const request of this.active) request.abort();
    this.active.clear();
  }
  cancelAnalysis() { this.analysis?.abort(); }
  private async json(address: string, route: string, body: unknown, token: string | undefined, controller: AbortController, timeout: number): Promise<any> {
    this.active.add(controller);
    const timer = setTimeout(() => controller.abort(), timeout);
    try {
      const response = await this.fetchImpl(address + route, { method: body === undefined ? 'GET' : 'POST', headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) }, body: body === undefined ? undefined : JSON.stringify(body), redirect: 'error', signal: controller.signal });
      if (!response.ok) {
        await response.body?.cancel();
        if (response.status === 401 || response.status === 403) throw new Error('登录失效或权限不足，请重新登录');
        throw new Error(`后端请求失败（HTTP ${response.status}）`);
      }
      const reader = response.body?.getReader();
      if (!reader) throw new Error('后端响应为空');
      const chunks: Uint8Array[] = []; let length = 0;
      while (true) {
        const { value, done } = await reader.read(); if (done) break;
        length += value.length;
        if (length > 2_000_000) { await reader.cancel(); throw new Error('后端响应超出大小限制'); }
        chunks.push(value);
      }
      return JSON.parse(Buffer.concat(chunks).toString('utf8'));
    } catch (error) {
      if (controller.signal.aborted) throw new Error('请求已取消或超时');
      if (error instanceof Error && /^(后端|登录)/.test(error.message)) throw error;
      throw new Error('无法连接后端或响应格式错误，请检查地址与网络');
    } finally { clearTimeout(timer); this.active.delete(controller); }
  }
  async login(input: LoginInput) {
    const values = loginSchema.parse(input);
    const address = normalizeAddress(values.address, values.allowHttp);
    this.logout(); const generation = this.generation;
    const body = await this.json(address, '/api/v1/auth/plugin-login', { username: values.username, password: values.password }, undefined, new AbortController(), 30000);
    if (typeof body.access_token !== 'string' || !body.access_token || body.access_token.length > 16000) throw new Error('后端未返回有效登录会话');
    if (generation !== this.generation) throw new Error('登录已取消');
    const capabilities = await this.json(address, '/api/v1/deepseek-plugin/capabilities', undefined, body.access_token, new AbortController(), 30000);
    if (generation !== this.generation) throw new Error('登录已取消');
    this.session = { address, token: body.access_token };
    return { username: values.username, codexAvailable: capabilities.codex?.available === true };
  }
  async analyze(input: { prompt: string; context: string }, onDelta?: (text: string) => void) {
    const payload = analyzeSchema.parse(input);
    const session = this.session;
    if (!session) throw new Error('请先登录后端');
    if (this.analysis) throw new Error('已有分析正在进行');
    const controller = new AbortController(); this.analysis = controller;
    try {
      let result: { answer: string };
      if (onDelta) {
        this.active.add(controller);
        const timer = setTimeout(() => controller.abort(), 300000);
        try {
          const response = await this.fetchImpl(session.address + '/api/v1/deepseek-plugin/codex/delegate', {
            method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${session.token}` },
            body: JSON.stringify({ ...payload, stream: true }), redirect: 'error', signal: controller.signal,
          });
          if (!response.ok) { await response.body?.cancel(); throw new Error(`HTTP ${response.status}`); }
          result = await readAnalysisStream(response, onDelta);
        } finally { clearTimeout(timer); this.active.delete(controller); }
      } else {
        result = await this.json(session.address, '/api/v1/deepseek-plugin/codex/delegate', payload, session.token, controller, 300000);
      }
      if (this.session !== session) throw new Error('登录已取消');
      if (typeof result.answer !== 'string') throw new Error('后端未返回分析结果');
      return { answer: result.answer };
    } finally { if (this.analysis === controller) this.analysis = undefined; }
  }
}
