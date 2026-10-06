import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import { createRequire } from "node:module";
import ts from "typescript";

const require = createRequire(import.meta.url);
function load(path, mocks = {}) {
  const source = ts.transpileModule(fs.readFileSync(new URL(path, import.meta.url), "utf8"), {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX },
  }).outputText;
  const module = { exports: {} };
  new Function("require", "module", "exports", source)((name) => {
    if (name in mocks) return mocks[name];
    if (name === "react/jsx-runtime") return require(name);
    return {};
  }, module, module.exports);
  return module.exports;
}
const visual = load("../lib/chat-visualization.ts");

test("text mode preserves the original input, including inline references", () => {
  const query = "查看 @项目 文档\n```ts\nconst a = 1;\n```";
  assert.equal(visual.buildVisualizationMessage(query, null), query);
  assert.equal(visual.buildVisualizationMessage("", "architecture"), "");
  assert.equal(visual.buildVisualizationMessage("  ", "sequence"), "  ");
});

test("every diagram choice produces a self-contained persisted request", () => {
  for (const type of visual.diagramTypes) {
    const query = "依据已选文档画调用关系";
    const message = visual.buildVisualizationMessage(query, type.id);
    assert.ok(message.startsWith(query + "\n\n"));
    assert.ok(message.includes(type.label));
    assert.ok(message.includes(type.syntax));
    assert.match(message, /mermaid/);
    assert.match(message, /来源不足/);
    assert.match(message, /不修改项目文件/);
    assert.equal(visual.buildVisualizationMessage(message, null), message);
  }
});

test("toolbar switches mode without submitting the enclosing form", () => {
  const { VisualizationToolbar } = load("../components/chat/visualization/VisualizationToolbar.tsx", { "@/lib/chat-visualization": visual });
  let selected = null;
  const toolbar = VisualizationToolbar({ value: null, onChange: (value) => { selected = value; }, disabled: false });
  const buttons = toolbar.props.children[0].props.children;
  assert.equal(buttons[0].props["aria-pressed"], true);
  assert.equal(buttons[1].props.type, "button");
  buttons[1].props.onClick();
  assert.equal(selected, "auto");
  buttons[0].props.onClick();
  assert.equal(selected, null);
  const busy = VisualizationToolbar({ value: "sequence", onChange: () => {}, disabled: true });
  assert.ok(busy.props.children[0].props.children.every((button) => button.props.disabled));
});

test("chat diagram renderer stays mounted across message updates", () => {
  const { MarkdownMessage } = load("../components/chat/MarkdownMessage.tsx");
  const first = MarkdownMessage({ content: "```mermaid\ngraph LR; A-->B\n```" });
  const updated = MarkdownMessage({ content: "```mermaid\ngraph LR; A-->B\n```\n来源：当前方案" });
  assert.equal(first.props.components.pre, updated.props.components.pre);
});
