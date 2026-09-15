import test from 'node:test'
import assert from 'node:assert/strict'
import { AiAgentClient } from '../src/client.mjs'

test('login keeps credentials in request body and uses returned bearer token', async () => {
  const calls = []
  const fakeFetch = async (url, init) => {
    calls.push({ url, init })
    return new Response(JSON.stringify({ access_token: 'opaque-token' }), { status: 200 })
  }
  const client = await AiAgentClient.login('https://example.test/', 'alice', 'secret', fakeFetch)
  await client.capabilities()
  assert.equal(calls[0].url, 'https://example.test/api/v1/auth/plugin-login')
  assert.deepEqual(JSON.parse(calls[0].init.body), { username: 'alice', password: 'secret' })
  assert.equal(calls[1].init.headers.authorization, 'Bearer opaque-token')
})

test('Codex delegation sends the selected model and preserves the delegation id', async () => {
  const calls = []
  const fakeFetch = async (url, init) => {
    calls.push({ url, init })
    return new Response(JSON.stringify({ delegation_id: 'dsp-123', answer: 'patch plan' }), { status: 200 })
  }
  const client = new AiAgentClient('https://example.test', 'plugin-token', fakeFetch)
  const result = await client.delegateCodex({ prompt: 'change it', context: 'file excerpt', model_id: 'codex-model' })
  assert.equal(calls[0].url, 'https://example.test/api/v1/deepseek-plugin/codex/delegate')
  assert.equal(calls[0].init.headers.authorization, 'Bearer plugin-token')
  assert.deepEqual(JSON.parse(calls[0].init.body), { prompt: 'change it', context: 'file excerpt', model_id: 'codex-model' })
  assert.equal(result.delegation_id, 'dsp-123')
})
