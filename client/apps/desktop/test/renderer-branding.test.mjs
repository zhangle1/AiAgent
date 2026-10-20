import { readAppSource, readComposerSource } from "./helpers/source-contracts.mjs";
import assert from "node:assert/strict";
import { access, readFile } from "node:fs/promises";
import test from "node:test";
import { loadStyles } from "./helpers/styles.mjs";

const read = (path) => readFile(new URL(path, import.meta.url), "utf8");

const [
  english,
  chinese,
  brandLogo,
  icons,
  sidebar,
  app,
  chatSurface,
  composer,
  styles,
  mascotLogo,
  rendererLanguage,
] = await Promise.all([
    read("../../../packages/i18n/src/locales/en/index.ts"),
    read("../../../packages/i18n/src/locales/zh-CN/index.ts"),
    read("../src/components/BrandLogo.tsx"),
    read("../src/components/icons.tsx"),
    read("../src/components/Sidebar.tsx"),
    readAppSource(),
    read("../src/components/ChatSurface.tsx"),
    readComposerSource(),
    loadStyles(),
    read("../src/components/HomeMascotLogo.tsx"),
    read("../src/lib/renderer-language.ts"),
  ]);

test("renderer surfaces the KunBuddy brand instead of the Codex shell brand", () => {
  assert.match(english, /shellName:\s*"KunBuddy"/);
  assert.match(chinese, /shellName:\s*"KunBuddy"/);
  assert.match(english, /placeholder:\s*"Ask KunBuddy to help with anything"/);
  assert.match(chinese, /placeholder:\s*"让 KunBuddy 帮你做任何事"/);
  assert.doesNotMatch(english, /shellName:\s*"Codex"/);
  assert.doesNotMatch(chinese, /shellName:\s*"Codex"/);
  // Codex remains a supported external import source, not the app identity.
  assert.match(english, /importSourceCodex:\s*"Codex"/);
  assert.match(chinese, /importSourceCodex:\s*"Codex"/);
});

test('KunBuddy chrome and home use the shared mark while keeping the composer plain',async()=>{
 assert.match(brandLogo,/assets\/brand\/logo-light\.png/);
 assert.match(brandLogo,/assets\/brand\/logo-dark\.png/);
 assert.match(mascotLogo,/assets\/brand\/kunbuddy\.png/);
 assert.doesNotMatch(mascotLogo,/home-mascot-(dark|light)\.gif/);
 await access(new URL('../src/assets/brand/kunbuddy.png',import.meta.url));
 assert.match(styles,/\.home-mascot-logo img\s*\{[^}]*display:\s*block/);
 assert.match(chatSurface,/<HomeMascotLogo \/>/);
 assert.doesNotMatch(composer,/<BrandLogo/);
 assert.match(sidebar,/<BrandLogo\s+size=\{20\}/);
});
