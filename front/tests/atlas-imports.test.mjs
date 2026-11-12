import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
const code = ts.transpileModule(readFileSync(new URL('../lib/atlas-imports.ts', import.meta.url), 'utf8'), { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText;
const mod = { exports: {} }; new Function('module', 'exports', code)(mod, mod.exports);
const { selectImportNeighborhood: query } = mod.exports;
const graph = { files: ['a', 'b', 'c', 'd'].map(id => ({ id })), edges: [{ id: 'ab', source: 'a', target: 'b' }, { id: 'bc', source: 'b', target: 'c' }, { id: 'db', source: 'd', target: 'b' }, { id: 'ca', source: 'c', target: 'a' }] };
test('forward and reverse queries preserve arrow direction and depth', () => {
  assert.deepEqual(query(graph, 'a', false, 1).files.map(f => f.id), ['a', 'b']);
  assert.deepEqual(query(graph, 'b', true, 1).edges.map(e => e.id), ['ab', 'db']);
  assert.deepEqual(query(graph, 'a', false, 2).files.map(f => f.id), ['a', 'b', 'c']);
  assert.equal(query(graph, 'a', false, 50).files.length, 3);
});
test('traversal bounds fanout while visiting rather than after expanding', () => {
  const large = { files: Array.from({length: 200}, (_, i) => ({ id: String(i) })), edges: Array.from({length: 199}, (_, i) => ({ id: String(i), source: '0', target: String(i + 1) })) };
  const result = query(large, '0', false, 2);
  assert.equal(result.files.length, 60); assert.equal(result.edges.length, 59); assert.equal(result.truncated, true);
});
