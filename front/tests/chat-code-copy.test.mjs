import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import * as React from "react";
import * as jsxRuntime from "react/jsx-runtime";
import ts from "typescript";

const source = ts.transpileModule(fs.readFileSync(new URL("../components/chat/MarkdownMessage.tsx", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX },
}).outputText;

function renderCode(children) {
  const states = [];
  const module = { exports: {} };
  new Function("require", "module", "exports", source)((name) => {
    if (name === "react/jsx-runtime") return jsxRuntime;
    if (name === "react") return { ...React, useState: (initial) => {
      const index = states.length;
      states.push(initial);
      return [initial, (value) => { states[index] = value; }];
    } };
    if (name.endsWith("ArchitectureDiagram")) return { architectureSourceFromPre: () => null };
    if (name.endsWith("MermaidDiagram")) return { mermaidSourceFromPre: () => null };
    return {};
  }, module, module.exports);
  const markdown = module.exports.MarkdownMessage({ content: "" });
  const block = markdown.props.components.pre({ children });
  const rendered = block.type(block.props);
  return { click: rendered.props.children[1].props.onClick, states };
}

function environment(t, clipboard, fallback = true) {
  const writes = [];
  let textarea;
  let removed = false;
  const replacements = {
    navigator: { clipboard: clipboard === "missing" ? undefined : { writeText: async (text) => {
      if (clipboard === "reject") throw new Error("Permission denied");
      writes.push(text);
    } } },
    window: { setTimeout: () => 0 },
    HTMLElement: class {},
    document: {
      activeElement: null,
      body: { appendChild: (element) => { textarea = element; } },
      createElement: () => ({ style: {}, focus() {}, select() {}, remove() { removed = true; } }),
      execCommand: (command) => {
        assert.equal(command, "copy");
        if (fallback === "throw") throw new Error("Copy blocked");
        if (fallback) writes.push(textarea.value);
        return fallback;
      },
    },
  };
  for (const [key, value] of Object.entries(replacements)) {
    const previous = Object.getOwnPropertyDescriptor(globalThis, key);
    Object.defineProperty(globalThis, key, { configurable: true, value });
    t.after(() => previous ? Object.defineProperty(globalThis, key, previous) : delete globalThis[key]);
  }
  return { writes, removed: () => removed };
}

for (const clipboard of ["available", "missing", "reject"]) {
  test(`copies nested code text via ${clipboard} clipboard API`, async (t) => {
    const env = environment(t, clipboard);
    const expected = "\n  7873603..f42ef76  main -> main\n\t中文 <>&";
    const { click, states } = renderCode(React.createElement("code", {}, [
      "\n  7873603..f42ef76  main -> main\n",
      React.createElement("span", { key: "line" }, "\t中文 <>&"), "\n",
    ]));
    click();
    await new Promise(setImmediate);
    assert.deepEqual(env.writes, [expected]);
    assert.deepEqual(states, [true, false]);
    if (clipboard !== "available") assert.ok(env.removed());
  });
}

for (const fallback of [false, "throw"]) {
  test(`does not report success when fallback returns ${fallback}`, async (t) => {
    const env = environment(t, "reject", fallback);
    const { click, states } = renderCode(React.createElement("code", {}, "content\n"));
    click();
    await new Promise(setImmediate);
    assert.deepEqual(env.writes, []);
    assert.deepEqual(states, [false, true]);
    assert.ok(env.removed());
  });
}
