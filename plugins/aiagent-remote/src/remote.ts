import type { Context } from '@deepseek-ai/cordis'
import { credentialRef } from '@deepseek-ai/dsh-credentials'
import type {} from '@deepseek-ai/dsh-settings'
import { Remote, RemoteError, TypertRemoteService } from '@deepseek-ai/dsh-typert-protocol'
import type { Config } from './index.js'
import { AiAgentClient, normalizeAiAgentBaseUrl } from './client.mjs'

export interface AiAgentRemoteStatus { configured: boolean; baseUrl: string; models: Array<{ id: string; name: string }>; codexAvailable: boolean }
export interface AiAgentCodexTest { modelId: string; answer: string }

function contextWindowOf(value: unknown): number {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : 128000
}

function codexAvailableOf(capabilities: unknown): boolean {
  if (capabilities === null || typeof capabilities !== 'object') return false
  const codex = (capabilities as Record<string, unknown>).codex
  return codex !== null && typeof codex === 'object' && (codex as Record<string, unknown>).available === true
}

function statusOf(configured: boolean, baseUrl: string, capabilities?: unknown): AiAgentRemoteStatus {
  const rawModels = capabilities !== null && typeof capabilities === 'object'
    && Array.isArray((capabilities as Record<string, unknown>).llm_models)
    ? (capabilities as Record<string, unknown>).llm_models as Array<{ id: string; name?: string }>
    : []
  return {
    configured, baseUrl, codexAvailable: codexAvailableOf(capabilities),
    models: rawModels.map(model => ({ id: model.id, name: model.name ?? model.id })),
  }
}

export class AiAgentRemoteController extends TypertRemoteService {
  constructor(ctx: Context, private readonly config: Config) {
    super(ctx, 'aiagentRemoteController', { namespace: 'aiagentRemote' })
  }

  @Remote
  async status(): Promise<AiAgentRemoteStatus> {
    const credentials = this.ctx.credentials
    const [tokenInfo, token, baseUrl] = await Promise.all([
      credentials.describe(credentialRef(this.config.accessTokenEnv)),
      credentials.resolve(credentialRef(this.config.accessTokenEnv)),
      credentials.resolve(credentialRef(this.config.baseUrlEnv)),
    ])
    const resolvedBaseUrl = baseUrl?.value ?? this.config.baseUrl
    if (!tokenInfo.configured || token === undefined) return statusOf(false, resolvedBaseUrl)
    try {
      return statusOf(true, resolvedBaseUrl, await new AiAgentClient(resolvedBaseUrl, token.value).capabilities())
    } catch {
      return statusOf(true, resolvedBaseUrl)
    }
  }

  @Remote
  async login(baseUrl: string, username: string, password: string): Promise<AiAgentRemoteStatus> {
    if (username.trim().length === 0 || password.length === 0) {
      throw new RemoteError('gateway/bad-request', 'AiAgent username and password are required.', {})
    }
    const normalized = normalizeAiAgentBaseUrl(baseUrl)
    try {
      const client = await AiAgentClient.login(normalized, username.trim(), password)
      const capabilities = await client.capabilities()
      const models = Array.isArray(capabilities.llm_models) ? capabilities.llm_models : []
      await this.ctx.credentials.set(credentialRef(this.config.accessTokenEnv), client.token)
      await this.ctx.credentials.set(credentialRef(this.config.baseUrlEnv), normalized)
      const settings = this.ctx.get('settings')
      if (settings !== undefined) {
        await settings.update('llm-deepseek', {
          baseURL: `${normalized}/api/v1/deepseek-plugin`,
          models: models.map((model: { id: string; name?: string; context_window?: unknown }) => ({
            id: model.id, name: model.name ?? model.id, contextWindow: contextWindowOf(model.context_window), maxTokens: 8192,
          })),
        })
      }
      return statusOf(true, normalized, capabilities)
    } catch (error) {
      throw new RemoteError('gateway/internal', error instanceof Error ? error.message : 'AiAgent login failed.', {})
    }
  }

  @Remote
  async logout(): Promise<void> {
    await this.ctx.credentials.unset(credentialRef(this.config.accessTokenEnv))
  }

  @Remote
  async testCodex(): Promise<AiAgentCodexTest> {
    const token = await this.ctx.credentials.resolve(credentialRef(this.config.accessTokenEnv))
    const baseUrl = await this.ctx.credentials.resolve(credentialRef(this.config.baseUrlEnv))
    if (token === undefined) throw new RemoteError('gateway/bad-request', 'Sign in before testing the server Codex CLI.', {})
    const client = new AiAgentClient(baseUrl?.value ?? this.config.baseUrl, token.value)
    if (!codexAvailableOf(await client.capabilities())) {
      throw new RemoteError('gateway/bad-request', 'The AiAgent server has no available Codex CLI.', {})
    }
    const result = await client.delegateCodex({
      prompt: 'Reply with one short sentence confirming that the server Codex CLI is available. Do not access files.',
    }) as { model_id?: unknown; model?: unknown; answer?: unknown }
    return {
      modelId: typeof result.model_id === 'string' ? result.model_id : typeof result.model === 'string' ? result.model : 'server default',
      answer: typeof result.answer === 'string' ? result.answer.slice(0, 500) : 'Server Codex CLI returned a response.',
    }
  }
}
