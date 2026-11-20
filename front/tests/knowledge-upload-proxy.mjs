// Exercise the installed Next.js body-cloning code used by /api rewrites.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { Readable } from "node:stream";
import { createHash } from "node:crypto";
const require = createRequire(import.meta.url);
const config = require("../next.config.js");
const { getCloneableBody } = require("next/dist/server/body-streams");
const payload = Buffer.alloc(Math.ceil(16.1 * 1024 * 1024), 32);
const chunks = [];
for (let offset = 0; offset < payload.length; offset += 64 * 1024) chunks.push(payload.subarray(offset, offset + 64 * 1024));
const request = Readable.from(chunks);
request.url = "/api/v1/knowledge-resources/upload";
const stream = getCloneableBody(request, config.experimental?.proxyClientMaxBodySize).cloneBodyStream();
let length = 0;
const hash = createHash("sha256");
for await (const chunk of stream) { length += chunk.length; hash.update(chunk); }
assert.equal(length, payload.length, "Next.js rewrite must not truncate a 16.1 MB book before it reaches the backend");
assert.equal(hash.digest("hex"), createHash("sha256").update(payload).digest("hex"));
console.log(`PASS: Next.js rewrite clone preserves all ${length} bytes and SHA-256 of the synthetic book.`);
