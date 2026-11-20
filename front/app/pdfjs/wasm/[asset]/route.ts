import { readFile } from "node:fs/promises";
import path from "node:path";

const decoders = new Map([
  ["openjpeg.wasm", { file: path.join(process.cwd(), "node_modules/pdfjs-dist/wasm/openjpeg.wasm"), type: "application/wasm" }],
  ["qcms_bg.wasm", { file: path.join(process.cwd(), "node_modules/pdfjs-dist/wasm/qcms_bg.wasm"), type: "application/wasm" }],
  ["openjpeg_nowasm_fallback.js", { file: path.join(process.cwd(), "node_modules/pdfjs-dist/wasm/openjpeg_nowasm_fallback.js"), type: "text/javascript" }],
]);

export async function GET(_request: Request, { params }: { params: Promise<{ asset: string }> }) {
  const decoder = decoders.get((await params).asset);
  if (!decoder) return new Response(null, { status: 404 });
  const bytes = await readFile(decoder.file);
  return new Response(new Uint8Array(bytes), {
    headers: { "Content-Type": decoder.type, "Cache-Control": "public, max-age=3600" },
  });
}
