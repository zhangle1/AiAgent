import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'

test('bundle replaces the base DeepSeek adapter instead of registering a duplicate route', async () => {
  const patch = await readFile(new URL('../cordis.patch.yml', import.meta.url), 'utf8')
  assert.match(patch, /^- id: llm-deepseek$/m)
  assert.match(patch, /^- id: agent-default-model$/m)
  assert.doesNotMatch(patch, /^    - id: aiagent-llm$/m)
  assert.match(patch, /model: !!js process\.env\.AIAGENT_MODEL_ID/)
  assert.match(patch, /baseUrlEnv: AIAGENT_BASE_URL/)
})

test('browser settings entry uses the DSH client-module export contract', async () => {
  const packageJson = JSON.parse(await readFile(new URL('../package.json', import.meta.url), 'utf8'))
  assert.equal(packageJson.exports['./client'].default, './lib/client.js')
  assert.equal(packageJson.dsh.client.platform, 'web')
})
