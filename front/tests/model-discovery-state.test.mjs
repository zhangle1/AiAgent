import assert from "node:assert/strict";
import { register } from "node:module";
import test from "node:test";
register(new URL("../../client/apps/desktop/test/helpers/ts-import-hooks.mjs", import.meta.url));
const { createCatalogOperationQueue, modelProfileSignature, mergeDiscoveredModels } = await import("../lib/model-discovery-state.ts");

function catalog() {
  const services = Object.fromEntries(["llm", "embedding", "search", "tts", "stt", "imagegen", "videogen"].map(service => [service, { profiles: [], active_profile_id: null }]));
  services.llm = { active_profile_id: "p", active_model_id: "existing", profiles: [{ id: "p", name: "Fixture", binding: "openai", base_url: "https://fixture.test", api_key: "fixture-key", api_version: "", models: [{ id: "existing", model: "known", name: "My alias", context_window: "32000", context_window_source: "endpoint", max_output_tokens: "4000", max_output_tokens_source: "endpoint" }] }] };
  return { version: 1, services };
}
const discovered = [{ id: "new", model: "known", name: "Published name", context_window: "1000000", context_window_source: "endpoint", max_output_tokens: "64000", max_output_tokens_source: "model.json" }, { id: "other", model: "other", name: "Other" }];

test("discovery updates automatic limits while preserving row identity and a user alias", () => {
  const before = catalog(); const snapshot = structuredClone(before);
  const result = mergeDiscoveredModels(before, "llm", "p", discovered, modelProfileSignature(before.services.llm.profiles[0]));
  const model = result.services.llm.profiles[0].models[0];
  assert.equal(model.id, "existing"); assert.equal(model.name, "My alias");
  assert.equal(model.context_window, "1000000"); assert.equal(model.max_output_tokens, "64000");
  assert.deepEqual(before, snapshot); assert.equal(result.services.llm.profiles[0].models.length, 2);
});

test("explicit manual context and output capacities survive discovery", () => {
  for (const source of ["manual", "user"]) {
    const before = catalog(); const model = before.services.llm.profiles[0].models[0];
    model.context_window_source = source; model.max_output_tokens_source = source;
    const result = mergeDiscoveredModels(before, "llm", "p", discovered, modelProfileSignature(before.services.llm.profiles[0]));
    assert.equal(result.services.llm.profiles[0].models[0].context_window, "32000");
    assert.equal(result.services.llm.profiles[0].models[0].max_output_tokens, "4000");
  }
});

test("legacy capacities without a source follow fresh detection instead of keeping old seed limits", () => {
  const before = catalog(); const model = before.services.llm.profiles[0].models[0];
  delete model.context_window_source; delete model.max_output_tokens_source;
  const result = mergeDiscoveredModels(before, "llm", "p", discovered, modelProfileSignature(before.services.llm.profiles[0]));
  assert.equal(result.services.llm.profiles[0].models[0].context_window, "1000000");
  assert.equal(result.services.llm.profiles[0].models[0].max_output_tokens, "64000");
});

test("stale discovery never targets a changed endpoint, key, API style or deleted profile", () => {
  for (const field of ["base_url", "api_key", "api_style"]) {
    const before = catalog(); const expected = modelProfileSignature(before.services.llm.profiles[0]);
    before.services.llm.profiles[0][field] = "changed";
    assert.equal(mergeDiscoveredModels(before, "llm", "p", discovered, expected), null);
  }
  const before = catalog(); const expected = modelProfileSignature(before.services.llm.profiles[0]);
  before.services.llm.profiles = [];
  assert.equal(mergeDiscoveredModels(before, "llm", "p", discovered, expected), null);
});

test("unknown metadata does not erase a known capacity or an unfinished user row", () => {
  const before = catalog(); before.services.llm.profiles[0].models.push({ id: "draft", model: "", name: "New model draft" });
  const result = mergeDiscoveredModels(before, "llm", "p", [{ id: "replacement", model: "known", name: "Known" }], modelProfileSignature(before.services.llm.profiles[0]));
  assert.equal(result.services.llm.profiles[0].models[0].context_window, "32000");
  assert.equal(result.services.llm.profiles[0].models[1].id, "draft");
});

test("automatic, manual and apply operations execute in order using the last assigned IDs", async () => {
  const enqueue = createCatalogOperationQueue(); const events = [];
  let release; const gate = new Promise(resolve => { release = resolve; }); let id = "draft";
  const automatic = enqueue(async () => { events.push("automatic:start"); await gate; id = "database-id"; events.push("automatic:end"); });
  const manual = enqueue(async () => { events.push("manual:" + id); });
  const apply = enqueue(async () => { events.push("apply:" + id); });
  await Promise.resolve(); assert.deepEqual(events, ["automatic:start"]);
  release(); await Promise.all([automatic, manual, apply]);
  assert.deepEqual(events, ["automatic:start", "automatic:end", "manual:database-id", "apply:database-id"]);
});

test("a failed catalog operation does not prevent a later retry", async () => {
  const enqueue = createCatalogOperationQueue(); let retried = false;
  await assert.rejects(enqueue(async () => { throw Error("fixture failure"); }), /fixture failure/);
  await enqueue(async () => { retried = true; }); assert.equal(retried, true);
});

test("saving an empty discovery result retains the entered key and existing model configuration", () => {
  const before = catalog(); const snapshot = structuredClone(before);
  const result = mergeDiscoveredModels(before, "llm", "p", [], modelProfileSignature(before.services.llm.profiles[0]));
  assert.equal(result.services.llm.profiles[0].api_key, "fixture-key");
  assert.deepEqual(result.services.llm.profiles[0].models, snapshot.services.llm.profiles[0].models);
  assert.deepEqual(before, snapshot);
});

test("a failed detection cannot save the old key after the connection changes", () => {
  const before = catalog(); const expected = modelProfileSignature(before.services.llm.profiles[0]);
  before.services.llm.profiles[0].api_key = "fixture-replacement-key";
  assert.equal(mergeDiscoveredModels(before, "llm", "p", [], expected), null);
  assert.equal(before.services.llm.profiles[0].api_key, "fixture-replacement-key");
});
