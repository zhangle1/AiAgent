import { createInterface } from 'node:readline/promises'
import { stdin, stdout } from 'node:process'

export async function askQuestion(prompt, { input = stdin, output = stdout } = {}) {
  const readline = createInterface({ input, output })
  try {
    return await readline.question(prompt)
  } finally {
    readline.close()
  }
}

export function askHiddenQuestion(prompt, { input = stdin, output = stdout } = {}) {
  if (!input.isTTY) return askQuestion(prompt, { input, output }).then(value => value.trim())

  output.write(prompt)
  input.setRawMode(true)
  input.resume()

  return new Promise((resolve, reject) => {
    let value = ''
    let settled = false

    const cleanup = () => {
      input.off('data', onData)
      input.off('error', onError)
      input.setRawMode(false)
      input.pause()
    }
    const settle = callback => {
      if (settled) return
      settled = true
      cleanup()
      output.write('\n')
      callback()
    }
    const onData = chunk => {
      for (const character of chunk.toString()) {
        if (character === '\r' || character === '\n') return settle(() => resolve(value))
        if (character === '\u0003') return settle(() => reject(new Error('Cancelled')))
        if (character === '\u007f' || character === '\b') value = value.slice(0, -1)
        else value += character
      }
    }
    const onError = error => settle(() => reject(error))

    input.on('data', onData)
    input.once('error', onError)
  })
}
