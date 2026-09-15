import type { Context } from '@deepseek-ai/cordis'
import Schema from '@deepseek-ai/schemastery'
import { defineTool } from '@deepseek-ai/dsh-tools'
import { AiAgentClient } from './client.mjs'

export interface Config { baseUrl: string; accessTokenEnv: string; codexModelEnv: string; enableCodex: boolean; maxContextChars: number }
export const Config: Schema<Config> = Schema.object({
  baseUrl: Schema.string().required(),
  accessTokenEnv: Schema.string().default('AIAGENT_ACCESS_TOKEN'),
  codexModelEnv: Schema.string().default('AIAGENT_CODEX_MODEL_ID'),
  enableCodex: Schema.boolean().default(true),
  maxContextChars: Schema.number().min(1).max(200000).default(120000),
})
export const name = 'aiagent-remote'
export const inject = ['tools']

export function apply(ctx: Context, config: Config): void {
  const token = process.env[config.accessTokenEnv]
  if (!token) throw new Error(`aiagent-remote: ${config.accessTokenEnv} is not set; authenticate with the launcher first`)
  const client = new AiAgentClient(config.baseUrl, token)
  if (!config.enableCodex) return
  const defaultCodexModel = process.env[config.codexModelEnv]?.trim() || undefined
  ctx.tools.register(defineTool({
    name: 'aiagent_codex_delegate',
    description: 'Ask the remote AiAgent Codex CLI to analyze an explicit bounded text excerpt. It cannot access local paths or edit local files; apply any accepted result with local Harness tools.',
    parameters: {
      prompt: { type: 'string', description: 'The analysis or patch-planning task.' },
      context: { type: 'string', description: 'Relevant local file excerpts selected by the agent, never a local path.' },
      model_id: { type: 'string', description: 'Optional allowed remote Codex model id.' },
      reasoning_effort: { type: 'string', description: 'Optional allowed reasoning effort.' },
    },
    output: { schema: { type: 'string' }, render: (_args: unknown, value: string) => [{ type: 'text', text: value }] },
    async execute(args, execution) {
      const context = args.context ?? ''
      if (context.length > config.maxContextChars) throw new Error(`Codex context exceeds ${config.maxContextChars} characters`)
      return JSON.stringify(await client.delegateCodex({ prompt: args.prompt, context, model_id: args.model_id || defaultCodexModel, reasoning_effort: args.reasoning_effort }, execution.signal))
    },
  }))
}
