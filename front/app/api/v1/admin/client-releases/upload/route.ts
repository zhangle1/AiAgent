export const runtime = "nodejs";
export const dynamic = "force-dynamic";

// A route handler streams this one large multipart request without Next rewrite's
// body-cloning limit. Authentication and package validation remain in the backend.
export async function POST(request: Request) {
  const limit = 2 * 1024 ** 3 + 1024 ** 2;
  const declared = request.headers.get("content-length");
  if (declared && (!/^\d+$/.test(declared) || Number(declared) > limit)) return Response.json({ message: "安装包超过上传限制。" }, { status: 413 });
  if (!request.body) return Response.json({ message: "请选择安装包。" }, { status: 400 });
  const headers = new Headers();
  for (const name of ["content-type", "content-length", "cookie", "authorization"]) {
    const value = request.headers.get(name); if (value) headers.set(name, value);
  }
  let size = 0;
  const body = request.body.pipeThrough(new TransformStream<Uint8Array, Uint8Array>({
    transform(chunk, controller) { size += chunk.byteLength; if (size > limit) throw new Error("upload_limit"); controller.enqueue(chunk); },
  }));
  try {
    const backend = (process.env.NEXT_PUBLIC_AIAGENT_API_BASE_URL || "http://127.0.0.1:5000").replace(/\/$/, "");
    const options: RequestInit & { duplex: "half" } = { method: "POST", headers, body, duplex: "half", redirect: "error", cache: "no-store", signal: AbortSignal.any([request.signal, AbortSignal.timeout(30 * 60 * 1000)]) };
    const response = await fetch(backend + "/api/v1/admin/client-releases/upload", options);
    return new Response(response.body, { status: response.status, headers: { "content-type": response.headers.get("content-type") || "application/json", "cache-control": "no-store" } });
  } catch {
    return Response.json({ message: size > limit ? "安装包超过上传限制。" : "上传连接中断，请检查后端服务和上传超时配置。" }, { status: size > limit ? 413 : 502 });
  }
}
