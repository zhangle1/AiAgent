import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
test('KunBuddy has no independent upstream GitHub workflows or publishing target',()=>{
 assert(!existsSync(new URL('../../../.github',import.meta.url)));
 const pkg=JSON.parse(readFileSync(new URL('../package.json',import.meta.url),'utf8'));
 assert.equal(pkg.build.publish,null);
 assert(pkg.scripts['dist:win'].includes('build-desktop-release.mjs win'));
 for(const mode of ['nsis','zip','portable'])assert(pkg.build.win.target.some(t=>t.target===mode));
});
