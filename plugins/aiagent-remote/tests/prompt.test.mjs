import test from 'node:test'
import assert from 'node:assert/strict'
import { PassThrough } from 'node:stream'
import { askHiddenQuestion } from '../src/prompt.mjs'

test('hidden prompt owns the TTY input and never writes the password to output', async () => {
  const input = new PassThrough()
  input.isTTY = true
  const rawModes = []
  input.setRawMode = value => rawModes.push(value)
  const output = new PassThrough()
  let visible = ''
  output.on('data', chunk => { visible += chunk })

  const answer = askHiddenQuestion('Password: ', { input, output })
  input.write('secret\r')

  assert.equal(await answer, 'secret')
  assert.deepEqual(rawModes, [true, false])
  assert.equal(visible, 'Password: \n')
})
