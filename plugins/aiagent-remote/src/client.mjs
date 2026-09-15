export class AiAgentClient {
  constructor(baseUrl, token, fetchImpl = fetch) {
    this.baseUrl = normalizeAiAgentBaseUrl(baseUrl)
    this.token = token
    this.fetch = fetchImpl
  }

  static async login(baseUrl, username, password, fetchImpl = fetch) {
    const normalizedBaseUrl = normalizeAiAgentBaseUrl(baseUrl)
    const response = await fetchImpl(`${normalizedBaseUrl}/api/v1/auth/plugin-login`, {
      method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ username, password }),
    })
    const body = await readJson(response)
    if (!response.ok) throw new Error(body.message || `AiAgent login failed (${response.status})`)
    return new AiAgentClient(normalizedBaseUrl, body.access_token, fetchImpl)
  }

  async capabilities(signal) { return this.#json('/api/v1/deepseek-plugin/capabilities', { signal }) }
  async delegateCodex(request, signal) {
    return this.#json('/api/v1/deepseek-plugin/codex/delegate', {
      method: 'POST', signal, headers: { 'content-type': 'application/json' }, body: JSON.stringify(request),
    })
  }
  async #json(path, init = {}) {
    const response = await this.fetch(`${this.baseUrl}${path}`, { ...init, headers: { ...init.headers, authorization: `Bearer ${this.token}` } })
    const body = await readJson(response)
    if (!response.ok) throw new Error(body.message || `AiAgent request failed (${response.status})`)
    return body
  }
}

export function normalizeAiAgentBaseUrl(value) {
  const input = String(value ?? '').trim()
  if (!input) throw new Error('AiAgent backend address is required')

  const withProtocol = /^[a-z][a-z\d+.-]*:\/\//i.test(input) ? input : `http://${input}`
  let url
  try { url = new URL(withProtocol) } catch { throw new Error('Invalid AiAgent backend address') }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') throw new Error('AiAgent backend address must use http:// or https://')
  if (url.username || url.password) throw new Error('AiAgent backend address must not contain credentials')
  if (url.search || url.hash) throw new Error('AiAgent backend address must not contain a query or fragment')
  if (url.pathname !== '/' && url.pathname !== '') throw new Error('Enter the AiAgent backend root address without an API path')
  return url.origin
}

async function readJson(response) {
  const text = await response.text()
  try { return text ? JSON.parse(text) : {} } catch { return { message: text.slice(0, 500) } }
}
