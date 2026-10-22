export type ClientRelease = { id: string; version: string; platform: "windows" | "macos"; architecture: "x64" | "arm64"; filename: string; size: number; sha256: string; notes: string; published: boolean; created_at: string; download_path: string };
const root = "/api/v1/admin/client-releases";
async function result<T>(response: Response): Promise<T> {
  const value = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(typeof value.message === "string" ? value.message : `请求失败（HTTP ${response.status}）`);
  return value as T;
}
export async function listClientReleases() { return result<ClientRelease[]>(await fetch(root + "/list", { cache: "no-store" })); }
export async function withdrawClientRelease(id: string) { return result(await fetch(`${root}/${encodeURIComponent(id)}/withdraw`, { method: "POST" })); }
export function clientReleaseFileUrl(id: string) { return `${root}/${encodeURIComponent(id)}/file`; }
export function uploadClientRelease(file: File, metadata: { version: string; platform: string; architecture: string; notes: string }, progress: (value: number) => void) {
  return new Promise<ClientRelease>((resolve, reject) => {
    const body = new FormData(); body.append("file", file); for (const [name, value] of Object.entries(metadata)) body.append(name, value);
    const xhr = new XMLHttpRequest(); xhr.open("POST", root + "/upload"); xhr.responseType = "json";
    xhr.upload.onprogress = event => { if (event.lengthComputable) progress(Math.round(event.loaded / event.total * 100)); };
    xhr.onerror = () => reject(new Error("上传连接中断，请检查网络及服务器上传限制。"));
    xhr.onabort = () => reject(new Error("上传已取消。"));
    xhr.onload = () => xhr.status >= 200 && xhr.status < 300 ? resolve(xhr.response) : reject(new Error(xhr.response?.message || `上传失败（HTTP ${xhr.status}）`));
    xhr.send(body);
  });
}
