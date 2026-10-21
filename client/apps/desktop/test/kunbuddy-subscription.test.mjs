import assert from "node:assert/strict";
import { register } from "node:module";
import test from "node:test";
register(new URL("./helpers/ts-import-hooks.mjs", import.meta.url));
const { normalizeKunBuddyUrl, connectKunBuddySubscription, disconnectKunBuddySubscription, subscriptionBindings } = await import("../electron/main/kunbuddy-subscription.ts");

test("subscription URL supports server IPs, UI reverse proxy roots and subpaths without URL credentials", () => {
  assert.equal(normalizeKunBuddyUrl("192.168.1.20:3782"), "http://192.168.1.20:3782");
  assert.equal(normalizeKunBuddyUrl("https://example.test/ai/api/v1/"), "https://example.test/ai");
  for (const value of ["file:///private", "https://u:p@example.test", "https://example.test/?key=secret", "https://example.test/#secret"])
    assert.throws(() => normalizeKunBuddyUrl(value));
});

test("server windows are preserved and unknown capacities defer to runtime defaults", () => {
  const [known, unknown] = subscriptionBindings([{ id: "1", context_window: "1000000", max_output_tokens: 65536, supports_images: true }, { id: "2" }]);
  assert.equal(known.contextWindow, 1000000); assert.equal(known.maxTokens, 65536); assert.equal(known.supportsImages, true);
  assert.equal(unknown.contextWindow, 0); assert.equal(unknown.maxTokens, 0); assert.equal(unknown.contextWindowSource, undefined);
});

test("long Unicode model labels fit the host alias limit without changing the routed model id", () => {
  const [binding] = subscriptionBindings([{ id: "stable-model", name: "🟦".repeat(90), context_window: 1000000 }]);
  assert.equal(Array.from(binding.alias).length, 60); assert.equal(binding.id, "stable-model");
  assert.equal(binding.contextWindow, 1000000);
});

function harness({ failAt = 0, failUpdateAt = 0, responseStatus = 200 } = {}) {
  const providers = []; const secrets = new Map(); const calls = []; let created = 0; let revoked = 0; let updates = 0;
  let styles = ["chat_completions", "responses", "anthropic_messages", "google_generative_ai"];
  const host = { async call(method, input) {
    calls.push([method, input]);
    if (method === "providers.list") return { providers: structuredClone(providers) };
    if (method === "providers.getSecret") return { value: secrets.get(input.id) };
    if (method === "providers.setSecret") {
      if (input.secretValue) secrets.set(input.id, input.secretValue); else secrets.delete(input.id);
      const provider = providers.find(item => item.id === input.id); if (provider) provider.hasSecret = secrets.has(input.id);
      return { provider };
    }
    if (method === "providers.delete") { const index = providers.findIndex(item => item.id === input.id); if (index >= 0) providers.splice(index, 1); secrets.delete(input.id); return {}; }
    if (method === "providers.create" || method === "providers.update") {
      if (method === "providers.create" && ++created === failAt) throw Error("Fixture save failure");
      if (method === "providers.update" && ++updates === failUpdateAt) throw Error("Fixture update failure");
      const { secretValue, ...publicFields } = input;
      const id = input.id ?? String(created);
      const index = providers.findIndex(item => item.id === id);
      if (secretValue) secrets.set(id, secretValue);
      const provider = { ...(index >= 0 ? providers[index] : {}), enabled: true, ...publicFields, id, hasSecret: secrets.has(id) };
      if (index >= 0) providers[index] = provider; else providers.push(provider);
      return { provider };
    }
    throw Error(method);
  } };
  const fetch = async (url, init) => {
    assert.equal(init.redirect, "error");
    if (url.endsWith("/kunbuddy-login")) return new Response(JSON.stringify({ access_token: "fixture-session", expires_at: "2026-10-21T12:00:00Z" }), { status: responseStatus });
    if (url.endsWith("/logout")) { revoked++; return new Response("{}"); }
    assert.equal(init.headers.Authorization, "Bearer fixture-session");
    return new Response(JSON.stringify({ profiles: styles.map((api_style, index) => ({ id: String(index), name: api_style, api_style, models: [{ id: `m${index}`, context_window: 1000000 }] })) }));
  };
  return { host, fetch, providers, secrets, calls, setStyles(value) { styles = value; }, get revoked() { return revoked; } };
}
const credentials = { baseUrl: "https://server.test", username: "fixture-user", password: "fixture-password" };

test("login imports all authorized protocol groups and never returns a password or token", async () => {
  const fixture = harness(); const result = await connectKunBuddySubscription(credentials, fixture);
  assert.equal(result.providers.length, 4);
  assert.deepEqual(result.providers.map(item => item.apiStyle), ["chat_completions", "responses", "anthropic_messages", "google_generative_ai"]);
  assert(!JSON.stringify(result).includes("fixture-session")); assert(!JSON.stringify(result).includes("fixture-password"));
  assert.equal(fixture.secrets.get(result.providers[0].id), "fixture-session");
  assert(result.providers.every(item => item.baseUrl.startsWith("https://server.test/api/v1/kunbuddy/profiles/")));
  assert(!JSON.stringify(fixture.calls).includes("fixture-password"));
});

test("relogin updates existing account rows instead of duplicating providers", async () => {
  const fixture = harness(); await connectKunBuddySubscription(credentials, fixture); await connectKunBuddySubscription(credentials, fixture);
  assert.equal(fixture.providers.length, 4); assert.equal(fixture.calls.filter(([method]) => method === "providers.update").length, 4);
});

test("a partial import is rolled back and its server session revoked", async () => {
  const fixture = harness({ failAt: 2 });
  await assert.rejects(() => connectKunBuddySubscription(credentials, fixture), /Fixture save failure/);
  assert.equal(fixture.providers.length, 0); assert.equal(fixture.secrets.size, 0); assert.equal(fixture.revoked, 1);
});

test("rollback clears a newly written credential when the previous account row had none", async () => {
  const fixture = harness({ failUpdateAt: 2 }); await connectKunBuddySubscription(credentials, fixture);
  fixture.secrets.clear(); for (const provider of fixture.providers) provider.hasSecret = false;
  await assert.rejects(() => connectKunBuddySubscription(credentials, fixture), /Fixture update failure/);
  assert.equal(fixture.secrets.size, 0); assert.equal(fixture.providers.length, 4);
  assert(fixture.providers.every(provider => !provider.hasSecret)); assert.equal(fixture.revoked, 1);
});

test("relogin disables withdrawn profiles and removes their local credentials", async () => {
  const fixture = harness(); const initial = await connectKunBuddySubscription(credentials, fixture);
  fixture.setStyles(["chat_completions"]);
  const result = await connectKunBuddySubscription(credentials, fixture);
  assert.equal(result.providers.length, 1); assert.equal(result.providers[0].id, initial.providers[0].id);
  const unavailable = fixture.providers.filter(provider => provider.id !== result.providers[0].id);
  assert.equal(unavailable.length, 3); assert(unavailable.every(provider => provider.enabled === false && !provider.hasSecret));
  assert.equal(fixture.secrets.size, 1);
});

test("failed credentials do not create a provider or expose the submitted password", async () => {
  const fixture = harness({ responseStatus: 401 });
  await assert.rejects(() => connectKunBuddySubscription(credentials, fixture), error => !error.message.includes("fixture-password") && error.message.includes("登录"));
  assert.equal(fixture.providers.length, 0);
});

test("signing out revokes the session and removes every profile using that session", async () => {
  const fixture = harness(); const result = await connectKunBuddySubscription(credentials, fixture);
  await disconnectKunBuddySubscription(result.providers[0].id, fixture);
  assert.equal(fixture.providers.length, 0); assert.equal(fixture.secrets.size, 0); assert.equal(fixture.revoked, 1);
});

test("signing out clears older local sessions for this account and preserves a different account", async () => {
  const fixture = harness(); const result = await connectKunBuddySubscription(credentials, fixture);
  fixture.secrets.set(result.providers[0].id, "fixture-older-session");
  fixture.providers.push({ ...result.providers[0], id: "another", oauthAccountLabel: "another-user" });
  fixture.secrets.set("another", "fixture-other-session");
  await disconnectKunBuddySubscription(result.providers[0].id, fixture);
  assert.equal(fixture.revoked, 2); assert.equal(fixture.providers.length, 1);
  assert.equal(fixture.providers[0].id, "another"); assert.equal(fixture.secrets.get("another"), "fixture-other-session");
});
