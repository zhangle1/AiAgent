import { cp, mkdir, rm } from 'node:fs/promises'

await mkdir(new URL('../lib/', import.meta.url), { recursive: true })
for (const filename of ['client.mjs', 'prompt.mjs']) {
  await rm(new URL(`../lib/${filename}`, import.meta.url), { force: true })
  await cp(new URL(`../src/${filename}`, import.meta.url), new URL(`../lib/${filename}`, import.meta.url))
}
