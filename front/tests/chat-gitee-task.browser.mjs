// TASK_TEST_TOOLS=<directory containing playwright and esbuild> node tests/chat-gitee-task.browser.mjs
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import http from 'node:http';

const front = fileURLToPath(new URL('../', import.meta.url));
const require = createRequire(path.resolve(process.env.TASK_TEST_TOOLS || front, 'package.json'));
const { chromium } = require('playwright');
const { build } = require('esbuild');
const bundle = await build({
  stdin: { contents: `import React from 'react';import{createRoot}from'react-dom/client';import{EnterpriseIssuePicker}from'./components/tasks/EnterpriseIssuePicker';
    createRoot(document.getElementById('root')).render(<EnterpriseIssuePicker projects={[{id:7,display_name:'Example'}]} initialProjectId={7} onClose={()=>{}} onLinked={t=>document.querySelector('output').textContent='linked:'+t.id} {...(location.search.includes('legacy')?{}:{onProcess:async t=>{await fetch('/handoff');document.querySelector('output').textContent='processed:'+t.id}})}/>);`, resolveDir: front, loader: 'tsx' },
  bundle: true, write: false, platform: 'browser', format: 'iife', jsx: 'automatic', tsconfig: path.join(front, 'tsconfig.json'),
});
let links = 0, fail = false, query = '';
const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://fixture.test');
  res.setHeader('Content-Type', 'application/json');
  if (url.pathname === '/bundle.js') { res.setHeader('Content-Type', 'text/javascript'); return res.end(bundle.outputFiles[0].text); }
  if (url.pathname.endsWith('/issues')) { query = url.searchParams.get('query'); return res.end(JSON.stringify({ items: [{ number: 'I123', title: 'Fix login' }], page: 1, has_more: false })); }
  if (url.pathname.endsWith('/issues/I123')) return res.end(JSON.stringify({ number: 'I123', title: 'Fix login', description: 'Details', attachments: [] }));
  if (url.pathname.endsWith('/link')) {
    links++;
    let body = ''; for await (const chunk of req) body += chunk;
    assert.deepEqual(JSON.parse(body), { project_id: 7, number: 'I123' });
    await new Promise(resolve => setTimeout(resolve, 150));
    if (fail) { res.writeHead(500); return res.end(JSON.stringify({ message: 'fixture failure' })); }
    return res.end(JSON.stringify({ task: { id: 42, project_id: 7, title: 'Fix login' } }));
  }
  if (url.pathname === '/handoff') return res.end('{}');
  res.setHeader('Content-Type', 'text/html'); res.end('<div id="root"></div><output></output><script src="/bundle.js"></script>');
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage();
  const base = `http://127.0.0.1:${server.address().port}`;
  await page.goto(base);
  await page.getByPlaceholder('编号、标题、负责人').fill('login');
  await page.getByPlaceholder('编号、标题、负责人').press('Enter');
  await page.getByRole('button', { name: '处理', exact: true }).waitFor();
  assert.equal(query, 'login');
  await page.getByRole('button', { name: '处理', exact: true }).evaluate(button => { button.click(); button.click(); });
  await page.waitForFunction(() => document.querySelector('output').textContent === 'processed:42');
  assert.equal(links, 1);
  fail = true;
  await page.goto(base);
  await page.getByRole('button', { name: '处理', exact: true }).click();
  await page.getByText('fixture failure', { exact: true }).waitFor();
  assert.equal(await page.getByRole('button', { name: '处理', exact: true }).isEnabled(), true);
  fail = false;
  await page.getByRole('button', { name: '处理', exact: true }).click();
  await page.waitForFunction(() => document.querySelector('output').textContent === 'processed:42');
  await page.goto(`${base}/?legacy`);
  assert.equal(await page.getByRole('button', { name: '处理', exact: true }).count(), 0);
  await page.getByRole('button', { name: /I123/ }).click();
  await page.getByRole('button', { name: '关联此工作项' }).click();
  await page.waitForFunction(() => document.querySelector('output').textContent === 'linked:42');
  console.log('PASS: search, direct process without preview, duplicate clicks, failure/retry, legacy linking');
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
