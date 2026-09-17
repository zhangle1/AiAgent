import { defineConfig } from 'tsdown'

const moduleTable = new Set([
  'react',
  'react/jsx-runtime',
  'react-dom',
  'react-dom/client',
  '@deepseek-ai/cordis',
  '@deepseek-ai/dsh-client-store',
  '@deepseek-ai/dsh-client-ui-slots',
  '@deepseek-ai/dsh-client-ui-primitives',
  '@deepseek-ai/dsh-client-ui-dockkit',
])

/**
 * DSH Web loads client plugins through its module table, rather than native ESM.
 * The factory wrapper is deliberately kept here so package consumers receive a
 * browser-safe `./client` artifact without requiring the Harness source tree.
 */
export default defineConfig({
  entry: { client: 'src/client.tsx' },
  outDir: 'lib',
  format: 'cjs',
  platform: 'browser',
  target: 'es2022',
  dts: false,
  sourcemap: true,
  clean: false,
  deps: {
    neverBundle: specifier => moduleTable.has(specifier),
    alwaysBundle: specifier => !moduleTable.has(specifier),
  },
  outputOptions: {
    entryFileNames: 'client.js',
    banner: 'window.__ModuleLoader__.load({ id: "@aiagent/deepseek-plugin-remote", factory: (require) => {',
    footer: 'return module.exports; } });',
    intro: 'var module = { exports: {} }; var exports = module.exports;',
  },
})
