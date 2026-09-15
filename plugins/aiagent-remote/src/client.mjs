export class AiAgentClient {
  constructor(baseUrl, token, fetchImpl = fetch) {
    this.baseUrl = baseUrl.replace(/\/+$/, '')
    this.token = token
    this.fetch = fetchImpl
  }

  static async login(baseUrl, username, password, fetchImpl = fetch) {
    const response = await fetchImpl(`${baseUrl.replace(/\/+$/, '')}/api/v1/auth/plugin-login`, {
      method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ username, password }),
    })
    const body = await readJson(response)
    if (!response.ok) throw new Error(body.message || `AiAgent login failed (${response.status})`)
    return new AiAgentClient(baseUrl, body.access_token, fetchImpl)
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

async function readJson(response) {
  const text = await response.text()
  try { return text ? JSON.parse(text) : {} } catch { return { message: text.slice(0, 500) } }
}
