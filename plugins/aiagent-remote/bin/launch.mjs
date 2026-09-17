#!/usr/bin/env node
import { spawn } from 'node:child_process'
import { stat } from 'node:fs/promises'
import { stdout } from 'node:process'
import { AiAgentClient, normalizeAiAgentBaseUrl } from '../lib/client.mjs'
import { askHiddenQuestion, askQuestion } from '../lib/prompt.mjs'

const separator = process.argv.indexOf('--')
const command = separator >= 0 ? process.argv.slice(separator + 1) : []
if (command.length === 0) {
  console.error('Usage: aiagent-dsh -- <dsh command and arguments>')
  process.exitCode = 2
} else {
  try {
    const configuredBaseUrl = process.env.AIAGENT_BASE_URL?.trim() ?? ''
    const addressPrompt = configuredBaseUrl
      ? `AiAgent backend address [${configuredBaseUrl}]: `
      : 'AiAgent backend address (for example 192.168.1.20:5000): '
    const baseUrl = normalizeAiAgentBaseUrl((await askQuestion(addressPrompt)).trim() || configuredBaseUrl)
    if (baseUrl.startsWith('http://')) {
      stdout.write('Warning: HTTP sends login credentials without transport encryption; use it only on a trusted development network.\n')
    }
    const username = (await askQuestion('Username: ')).trim()
    const password = await askHiddenQuestion('Password: ')
    const client = await AiAgentClient.login(baseUrl, username, password)
    const capabilities = await client.capabilities()
    const models = capabilities.llm_models ?? []
    if (models.length === 0) throw new Error('AiAgent has no available LLM models')
    models.forEach((model, index) => stdout.write(`${index + 1}. ${model.name} (${model.id})${model.is_default ? ' [default]' : ''}\n`))
    const defaultIndex = Math.max(0, models.findIndex(model => model.is_default))
    const selectedText = (await askQuestion(`Model [${defaultIndex + 1}]: `)).trim()
    const selectedIndex = selectedText ? Number(selectedText) - 1 : defaultIndex
    if (!Number.isInteger(selectedIndex) || !models[selectedIndex]) throw new Error('Invalid model selection')
    const codexModels = capabilities.codex?.policy?.models ?? []
    let codexModelId = ''
    if (capabilities.codex?.available && codexModels.length > 0) {
      const enableCodex = (await askQuestion('Enable remote Codex for local-file tasks? [Y/n]: ')).trim().toLowerCase()
      if (enableCodex !== 'n' && enableCodex !== 'no') {
        stdout.write('\nRemote Codex models:\n')
        codexModels.forEach((model, index) => stdout.write(`${index + 1}. ${model.name} (${model.id})\n`))
        const configuredDefault = capabilities.codex.policy.default_model_id
        const codexDefaultIndex = Math.max(0, codexModels.findIndex(model => model.id === configuredDefault))
        const codexText = (await askQuestion(`Codex model [${codexDefaultIndex + 1}]: `)).trim()
        const codexIndex = codexText ? Number(codexText) - 1 : codexDefaultIndex
        if (!Number.isInteger(codexIndex) || !codexModels[codexIndex]) throw new Error('Invalid Codex model selection')
        codexModelId = codexModels[codexIndex].id
      }
    }
    const configuredWorkspace = process.env.AIAGENT_WORKSPACE?.trim() ?? ''
    const cwd = configuredWorkspace || (await askQuestion('Local folder: ')).trim()
    if (!(await stat(cwd)).isDirectory()) throw new Error('Local folder does not exist')
    const child = spawn(command[0], command.slice(1), {
      cwd, stdio: 'inherit', shell: false,
      env: { ...process.env, AIAGENT_BASE_URL: baseUrl, AIAGENT_LLM_BASE_URL: `${baseUrl}/api/v1/deepseek-plugin`, AIAGENT_ACCESS_TOKEN: client.token, AIAGENT_MODEL_ID: models[selectedIndex].id, AIAGENT_CODEX_MODEL_ID: codexModelId },
    })
    process.exitCode = await new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', code => resolve(code ?? 1)) })
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error))
    process.exitCode = 1
  }
}
