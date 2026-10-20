import fs from 'node:fs';
import assert from 'node:assert/strict';
const root = new URL('../', import.meta.url);
const docs = ['AGENTS.md','CLAUDE.md'].map(file => fs.readFileSync(new URL(file,root),'utf8'));
assert.equal(docs[0].match(/Policy-Sync: (.+)/)?.[1], docs[1].match(/Policy-Sync: (.+)/)?.[1]);
for(const text of docs) for(const phrase of ['SQLite is owned exclusively by Rust host-core','Electron Main stays a thin orchestrator','com.knowwhy.kunbuddy','explicit user authorization','AiAgent']) assert(text.includes(phrase), phrase);
assert(docs[1].includes('AGENTS.md is authoritative'));
console.log('PASS KunBuddy / AiAgent agent policy mirrors');
