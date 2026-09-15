#!/usr/bin/env node
import { spawn } from 'node:child_process'
import { stat } from 'node:fs/promises'
import { createInterface } from 'node:readline/promises'
import { stdin, stdout } from 'node:process'
import { AiAgentClient } from '../src/client.mjs'

const separator = process.argv.indexOf('--')
const command = separator >= 0 ? process.argv.slice(separator + 1) : []
if (command.length === 0) {
  console.error('Usage: aiagent-dsh -- <dsh command and arguments>')
  process.exitCode = 2
} else {
  const rl = createInterface({ input: stdin, output: stdout })
  try {
    const baseUrl = (await rl.question('AiAgent URL: ')).trim()
    const username = (await rl.question('Username: ')).trim()
    const password = await hiddenQuestion('Password: ')
    const client = await AiAgentClient.login(baseUrl, username, password)
    const capabilities = await client.capabilities()
    const models = capabilities.llm_models ?? []
    if (models.length === 0) throw new Error('AiAgent has no available LLM models')
    models.forEach((model, index) => stdout.write(`${index + 1}. ${model.name} (${model.id})${model.is_default ? ' [default]' : ''}\n`))
    const defaultIndex = Math.max(0, models.findIndex(model => model.is_default))
    const selectedText = (await rl.question(`Model [${defaultIndex + 1}]: `)).trim()
    const selectedIndex = selectedText ? Number(selectedText) - 1 : defaultIndex
    if (!Number.isInteger(selectedIndex) || !models[selectedIndex]) throw new Error('Invalid model selection')
    const codexModels = capabilities.codex?.policy?.models ?? []
    let codexModelId = ''
    if (capabilities.codex?.available && codexModels.length > 0) {
      const enableCodex = (await rl.question('Enable remote Codex for local-file tasks? [Y/n]: ')).trim().toLowerCase()
      if (enableCodex !== 'n' && enableCodex !== 'no') {
        stdout.write('\nRemote Codex models:\n')
        codexModels.forEach((model, index) => stdout.write(`${index + 1}. ${model.name} (${model.id})\n`))
        const configuredDefault = capabilities.codex.policy.default_model_id
        const codexDefaultIndex = Math.max(0, codexModels.findIndex(model => model.id === configuredDefault))
        const codexText = (await rl.question(`Codex model [${codexDefaultIndex + 1}]: `)).trim()
        const codexIndex = codexText ? Number(codexText) - 1 : codexDefaultIndex
        if (!Number.isInteger(codexIndex) || !codexModels[codexIndex]) throw new Error('Invalid Codex model selection')
        codexModelId = codexModels[codexIndex].id
      }
    }
    const cwd = (await rl.question('Local folder: ')).trim()
    if (!(await stat(cwd)).isDirectory()) throw new Error('Local folder does not exist')
    const child = spawn(command[0], command.slice(1), {
      cwd, stdio: 'inherit', shell: false,
      env: { ...process.env, AIAGENT_BASE_URL: baseUrl.replace(/\/+$/, ''), AIAGENT_LLM_BASE_URL: `${baseUrl.replace(/\/+$/, '')}/api/v1/deepseek-plugin`, AIAGENT_ACCESS_TOKEN: client.token, AIAGENT_MODEL_ID: models[selectedIndex].id, AIAGENT_CODEX_MODEL_ID: codexModelId },
    })
    process.exitCode = await new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', code => resolve(code ?? 1)) })
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error))
    process.exitCode = 1
  } finally { rl.close() }
}

async function hiddenQuestion(prompt) {
  if (!stdin.isTTY) return (await createInterface({ input: stdin }).question(prompt)).trim()
  stdout.write(prompt)
  stdin.setRawMode(true)
  stdin.resume()
  let value = ''
  try {
    for await (const chunk of stdin) {
      const text = chunk.toString()
      if (text === '\r' || text === '\n') break
      if (text === '\u0003') throw new Error('Cancelled')
      if (text === '\u007f' || text === '\b') value = value.slice(0, -1)
      else value += text
    }
  } finally {
    stdin.setRawMode(false)
    stdin.pause()
    stdout.write('\n')
  }
  return value
}
