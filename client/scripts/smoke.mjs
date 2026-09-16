import { access } from 'node:fs/promises';
for (const file of ['dist/main.cjs', 'dist/preload.cjs', 'dist/renderer/index.html']) await access(file);
console.log('Client smoke checks passed.');
