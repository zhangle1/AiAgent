import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";
const source = ts.transpileModule(fs.readFileSync(new URL("../lib/chat-skill-command.ts", import.meta.url), "utf8"), { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText;
const module = { exports: {} };
new Function("exports", source)(module.exports);
const { skillCommand, filterChatSkills } = module.exports;
const skills = JSON.parse(fs.readFileSync(new URL("../../backed/Resources/ChatSkills/catalog-v1.json", import.meta.url), "utf8"));
test("explicit commands require a top-level exact command and boundary", () => {
  assert.equal(skillCommand("/grill-me 我要改知识库", skills)?.id, "grill-me");
  assert.equal(skillCommand("/grill-me", skills)?.id, "grill-me");
  for (const input of ["grill me 是什么", "请用 grill me", "引用 /grill-me", "```\n/grill-me\n```", "/grill-me-too", "/spec 文档", " /grill-me"]) assert.equal(skillCommand(input, skills), undefined, input);
});
test("all 23 skills can be found by Chinese name or command", () => {
  assert.equal(skills.length, 23);
  for (const skill of skills) {
    assert.ok(filterChatSkills(skills, skill.name).some((item) => item.id === skill.id));
    assert.ok(filterChatSkills(skills, skill.id.toUpperCase()).some((item) => item.id === skill.id));
  }
  assert.equal(filterChatSkills(skills, "skills 追问")[0].id, "grill-me");
  assert.equal(filterChatSkills(skills, "不存在的技能").length, 0);
});
