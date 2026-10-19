// Existing frontend with synthetic APIs only: no user settings, images or model calls.
import assert from "node:assert/strict";
import path from "node:path";
import { createRequire } from "node:module";
const require = process.env.PROFILE_TEST_TOOLS ? createRequire(path.resolve(process.env.PROFILE_TEST_TOOLS, "package.json")) : createRequire(import.meta.url);
const { chromium } = require("playwright");
const browser = await chromium.launch({ headless: true, channel: "msedge" });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
const base = process.env.PROFILE_TEST_URL ?? "http://127.0.0.1:3782";
const project = { id: 1, name: "Fixture", description: "", repositories: [], created_at: "2026-10-19T00:00:00Z" };
const profiles = [
  { display_name: "deepseek", profile_name: "deepseek", description: "Vision fixture", supports_reasoning_effort: false, supports_image_input: false, supports_image_ocr: false },
  { display_name: "text-only", profile_name: "text", description: "Text fixture", supports_reasoning_effort: false, supports_image_input: false, supports_image_ocr: false },
];
const projectModels = entries => entries.map(p => ({ id: `profile:${p.profile_name}`, name: p.display_name, description: p.description, profile_name: p.profile_name, supports_reasoning_effort: false, reasoning_efforts: [], is_builtin: false, image_input: p.supports_image_input ? "native" : p.supports_image_ocr ? "ocr" : "none" }));
let policy = { models: projectModels(profiles), profile_models: profiles, allowed_model_ids: ["profile:deepseek", "profile:text"], default_model_id: "profile:deepseek", allow_chat_model_override: true, allowed_reasoning_efforts: ["medium"], default_reasoning_effort: "medium", allow_chat_reasoning_effort_override: true };
let nativeEnabled = true;
let dropNativeCapability = false;
const imageSaves = [];
const saves = []; const uploads = []; const errors = [];
page.on("pageerror", e => errors.push(e.message));
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
await page.route("**/api/**", async route => {
  const name = new URL(route.request().url()).pathname;
  let body = [];
  if (name.endsWith("/auth/status")) body = { authenticated: true, username: "fixture", user_id: "fixture", is_admin: true };
  if (name.endsWith("/sessions/list")) body = { sessions: [] };
  if (name.endsWith("/sessions/project-preferences")) body = { preferences: [] };
  if (name.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "recent" };
  if (name.endsWith("/settings/ui")) body = { preferred_agent: "codex" };
  if (name.endsWith("/settings")) body = { ui: { preferred_agent: "codex" }, catalog: { version: 1, services: Object.fromEntries(["llm", "embedding", "search", "tts", "stt", "imagegen", "videogen"].map(s => [s, { active_profile_id: null, profiles: [] }])) } };
  if (name.endsWith("/code-repositories/projects")) body = [project];
  if (name.endsWith("/agent-providers/environments")) body = [{ id: "codex", name: "Codex", installed: true, command: "fixture", protocol: "app-server", chat_supported: true, message: "Fixture" }];
  if (name.endsWith("/agent-providers/codex-model-policy")) {
    if (route.request().method() === "PUT") {
      const payload = route.request().postDataJSON(); saves.push(payload);
      policy = { ...policy, ...payload, models: projectModels(payload.profile_models) };
      if (dropNativeCapability) policy = { ...policy, profile_models: policy.profile_models.map(p => ({ ...p, supports_image_input: false })), models: policy.models.map(m => ({ ...m, image_input: "none" })) };
    }
    body = policy;
  }
  if (name.endsWith("/agent-providers/image-ocr-policy") && route.request().method() === "PUT") {
    const payload = route.request().postDataJSON(); imageSaves.push(payload); nativeEnabled = payload.native_image_input_enabled;
  }
  if (name.endsWith("/agent-providers/image-ocr-policy")) body = { enabled: true, native_image_input_enabled: nativeEnabled, auto_process_images: true, language: "ch", max_image_bytes: 10485760, max_prompt_characters: 12000, timeout_seconds: 45 };
  if (name.endsWith("/chat/attachments/images")) {
    uploads.push(name);
    body = { id: "a".repeat(32), file_name: "fixture.png", content_type: "image/png", size: 68 };
  }
  await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
});

try {
  await page.goto(`${base}/settings/admin/agents`);
  const deepseek = page.getByRole("checkbox", { name: "deepseek 支持图片上传（原图）", exact: true });
  await deepseek.waitFor();
  assert.equal(await page.getByRole("article", { name: "deepseek 模型配置" }).getByRole("checkbox", { name: "deepseek 支持图片上传（原图）", exact: true }).count(), 1, "Native image checkbox must be visible on the model card");
  assert.equal(await page.getByText(/OCR|PaddleOCR/).count(), 0, "OCR controls must be hidden");
  assert.equal(await deepseek.isChecked(), false);
  await deepseek.check();
  const firstSave = page.waitForResponse(r => r.url().endsWith("/codex-model-policy") && r.request().method() === "PUT");
  await page.getByRole("button", { name: "保存高级配置", exact: true }).click();
  await firstSave;
  await page.getByRole("status").filter({ hasText: "模型与图片上传配置已保存" }).waitFor();
  assert.deepEqual(imageSaves.at(-1), { native_image_input_enabled: true }, "Saving image capability must not write hidden OCR settings");
  assert.equal(saves.at(-1).profile_models[0].supports_image_input, true);
  assert.equal(saves.at(-1).profile_models[1].supports_image_input, false);
  await page.reload();
  await deepseek.waitFor();
  assert.equal(await deepseek.isChecked(), true);

  await page.getByPlaceholder("Profile 名称，例如 deepseek").fill("other-vision");
  await page.getByRole("checkbox", { name: "新 Profile 支持图片上传（原图）", exact: true }).check();
  await page.getByRole("button", { name: "添加 Profile", exact: true }).click();
  assert.equal(await page.getByRole("checkbox", { name: "other-vision 支持图片上传（原图）", exact: true }).isChecked(), true);
  assert.equal(await page.getByRole("checkbox", { name: "新 Profile 支持图片上传（原图）", exact: true }).isChecked(), false);
  const saved = page.waitForResponse(r => r.url().endsWith("/codex-model-policy") && r.request().method() === "PUT");
  await page.getByRole("button", { name: "保存高级配置", exact: true }).click();
  await saved;
  await page.getByRole("status").filter({ hasText: "模型与图片上传配置已保存" }).waitFor();
  assert.equal(saves.at(-1).profile_models[2].supports_image_input, true);
  const globalToggle = page.getByRole("checkbox", { name: "允许聊天上传图片", exact: true });
  await globalToggle.uncheck();
  await page.getByRole("button", { name: "保存模型与图片配置", exact: true }).click();
  await page.getByRole("status").filter({ hasText: "模型与图片上传配置已保存" }).waitFor();
  assert.deepEqual(imageSaves.at(-1), { native_image_input_enabled: false });
  await page.reload();
  await globalToggle.waitFor();
  assert.equal(await globalToggle.isChecked(), false);
  await globalToggle.check();
  await page.getByRole("button", { name: "保存模型与图片配置", exact: true }).click();
  await page.getByRole("status").filter({ hasText: "模型与图片上传配置已保存" }).waitFor();
  assert.deepEqual(imageSaves.at(-1), { native_image_input_enabled: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForFunction(() => document.querySelector("main")?.getBoundingClientRect().left === 0);
  assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), JSON.stringify(await page.evaluate(() => [...document.querySelectorAll("main *")].filter(e => e.getBoundingClientRect().right > innerWidth + 1).slice(0, 12).map(e => ({ tag: e.tagName, cls: e.className, width: e.getBoundingClientRect().width, text: e.textContent?.slice(0, 45) })))));
  await page.screenshot({ path: process.env.PROFILE_TEST_SCREENSHOT ?? "C:/Users/zhang/AppData/Local/Temp/aiagent-profile-image-mobile.png", fullPage: true });
  await page.setViewportSize({ width: 1440, height: 1000 });

  await page.getByRole("article", { name: "deepseek 模型配置", exact: true }).screenshot({ path: "C:/Users/zhang/AppData/Local/Temp/aiagent-deepseek-image-card.png" });
  policy = { ...policy, profile_models: policy.profile_models.map(p => p.profile_name === "deepseek" ? { ...p, supports_image_input: false, supports_image_ocr: true } : p) };
  policy.models = projectModels(policy.profile_models);
  await page.goto(`${base}/chat?projectId=1`);
  const fileInput = page.locator('input[type="file"]').first();
  await fileInput.waitFor({ state: "attached" });
  const select = page.locator("select").filter({ has: page.locator('option[value="profile:deepseek"]') }).first();
  const image = { name: "fixture.png", mimeType: "image/png", buffer: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7L8AAAAASUVORK5CYII=", "base64") };
  await select.waitFor();
  await fileInput.setInputFiles(image);
  await page.getByRole("alert").filter({ hasText: "图片上传未开启：deepseek" }).waitFor();
  assert.equal(uploads.length, 0, "Historical OCR capability must not enable the original-image UI");
  assert.equal(await page.getByRole("link", { name: "前往模型图片设置", exact: true }).getAttribute("href"), "/settings/admin/agents#model-image-settings");
  policy.profile_models[0].supports_image_input = true;
  policy.models = projectModels(policy.profile_models);
  await page.evaluate(() => window.dispatchEvent(new Event("focus")));
  await page.waitForFunction(() => document.querySelector('input[type="file"]')?.accept.includes("image/png"));
  await page.getByRole("alert").filter({ hasText: "图片上传未开启" }).waitFor({ state: "hidden" });
  await fileInput.setInputFiles(image);
  await page.getByRole("button", { name: "移除 fixture.png", exact: true }).waitFor();
  assert.equal(uploads.length, 1);
  await page.getByRole("button", { name: "移除 fixture.png", exact: true }).click();
  await select.selectOption("profile:text");
  assert.equal((await fileInput.getAttribute("accept")).includes("image/png"), false);
  await fileInput.setInputFiles(image);
  await page.getByRole("alert").filter({ hasText: "图片上传未开启" }).waitFor();
  assert.equal(uploads.length, 1, "Unsupported models must not upload images");
  nativeEnabled = false;
  await page.reload();
  await select.waitFor();
  assert.equal((await fileInput.getAttribute("accept")).includes("image/png"), false);
  await fileInput.setInputFiles(image);
  await page.getByRole("alert").filter({ hasText: "图片上传未开启" }).waitFor();
  assert.equal(uploads.length, 1, "Global native-image toggle must be enforced in chat");
  nativeEnabled = true;
  await page.getByRole("button", { name: "刷新图片配置", exact: true }).click();
  await page.waitForFunction(() => document.querySelector('input[type="file"]')?.accept.includes("image/png"));
  await page.goto(`${base}/settings/admin`);
  await page.getByRole("heading", { name: "管理配置", exact: true }).waitFor();
  assert.equal(await page.getByRole("link", { name: /OCR/ }).count(), 0);
  await page.goto(`${base}/settings/admin/image-ocr`);
  await page.waitForURL("**/settings/admin/agents");
  assert.equal(await page.getByText(/OCR|PaddleOCR/).count(), 0);
  // An older backend that ignores the new flag must not produce a false success.
  await deepseek.waitFor();
  await deepseek.uncheck();
  const disableSave = page.waitForResponse(r => r.url().endsWith("/codex-model-policy") && r.request().method() === "PUT");
  await page.getByRole("button", { name: "保存模型与图片配置", exact: true }).click();
  await disableSave;
  await page.getByRole("status").filter({ hasText: "模型与图片上传配置已保存" }).waitFor();
  await deepseek.check();
  dropNativeCapability = true;
  await page.getByRole("button", { name: "保存模型与图片配置", exact: true }).click();
  await page.getByRole("alert").filter({ hasText: "后端未保存原图能力" }).waitFor();
  assert.equal(await page.getByRole("status").filter({ hasText: "模型与图片上传配置已保存" }).count(), 0);
  assert.deepEqual(errors, []);
  console.log("PASS: prominent model-card checkbox, OCR hidden/legacy URL redirect, unified save, new profiles, mobile width, focus/manual refresh, native upload, global gate and old-backend rejection");
} finally { await browser.close(); }
