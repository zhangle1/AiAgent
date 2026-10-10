import assert from "node:assert/strict";
import fs from "node:fs";
import { createRequire } from "node:module";
import test from "node:test";
import ts from "typescript";

const require = createRequire(import.meta.url);
const compiled = ts.transpileModule(fs.readFileSync(new URL("../components/chat/ChatRuntimeToolbar.tsx", import.meta.url), "utf8") + "\nexport { ProjectGitOverview };", {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX },
}).outputText;

function mount(runtimeRequest, gitRequest) {
  const states = [], effects = [];
  const component = {};
  new Function("require", "exports", compiled)((name) => {
    if (name === "react") return {
      useState(initial) { const index = states.length; states.push(initial); return [initial, value => { states[index] = value; }]; },
      useRef: value => ({ current: value }),
      useEffect: effect => effects.push(effect),
    };
    if (name === "react/jsx-runtime") return require(name);
    if (name === "@/lib/code-runtime-api") return { getCodeProjectRuntime: runtimeRequest };
    if (name === "@/lib/code-repository-api") return { getProjectGitStatus: gitRequest };
    return {};
  }, component);
  component.ChatRuntimeToolbar({ project: { id: 7, repositories: [] } });
  return { states, refresh: effects[0], overview: component.ProjectGitOverview };
}

const gitState = { project_id: 7, state: "synced", repositories: [{ repository_id: 1, status: { is_repository: true, changes: [], ahead: 0, behind: 0 } }] };
const runtimeState = { runs: [], profiles: [] };
const flush = () => new Promise(resolve => setImmediate(resolve));

test("runtime failure preserves successful Git status and reports the runtime error", async () => {
  const { states, refresh } = mount(async () => { throw new Error("runtime HTTP 500"); }, async () => gitState);
  refresh(); await flush();
  assert.equal(states[3], null);
  assert.equal(states[8], gitState);
  assert.equal(states[7][1], gitState.repositories[0].status);
  assert.match(states[6], /runtime HTTP 500/);
  assert.equal(states[4], false);
});

test("Git failure preserves runtime results and displays the specific Git error", async () => {
  const { states, refresh } = mount(async () => runtimeState, async () => { throw new Error("Git HTTP 403"); });
  refresh(); await flush();
  assert.equal(states[3], runtimeState);
  assert.match(states[8].message, /Git HTTP 403/);
  assert.deepEqual(states[7], {});
  assert.equal(states[6], null);
  assert.equal(states[4], false);
});

test("a previous project's late response cannot overwrite the current refresh", async () => {
  let resolveOld;
  const old = new Promise(resolve => { resolveOld = resolve; });
  let requests = 0;
  const { states, refresh } = mount(() => ++requests === 1 ? old : Promise.resolve(runtimeState), async () => gitState);
  refresh(); refresh(); await flush();
  resolveOld({ runs: [{ run_id: "stale" }] }); await flush();
  assert.equal(states[3], runtimeState);
});

test("failed or unknown Git checks do not claim there is no pending work", () => {
  const { overview } = mount(async () => runtimeState, async () => gitState);
  const render = rows => JSON.stringify(overview({ state: "attention", rows, busy: false }));
  const emptyMessage = "没有待拉取或待推送";
  assert.equal(render([]).includes(emptyMessage), false);
  assert.equal(render([{ state: "failed" }]).includes(emptyMessage), false);
  assert.equal(render([{ status: { ...gitState.repositories[0].status, remote_refresh_error: "fetch failed" } }]).includes(emptyMessage), false);
  assert.equal(render(gitState.repositories).includes(emptyMessage), true);
});
