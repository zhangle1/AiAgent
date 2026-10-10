import { myChatUploadContentUrl } from "./chat-api";
import { projectMarkdownDocumentDownloadUrl } from "./code-repository-api";
import type { ChatLibraryItem } from "./chat-library-types";

export async function readChatLibraryFile(item: ChatLibraryItem, projectId: number | null, signal?: AbortSignal): Promise<File> {
  const url = item.upload ? myChatUploadContentUrl(item.upload.id)
    : item.document && projectId ? projectMarkdownDocumentDownloadUrl(projectId, item.document.repository_name, item.document.path) : null;
  if (!url) throw new Error("请先选择资料所属项目。");
  const response = await fetch(url, { cache: "no-store", signal });
  if (!response.ok) throw new Error(`读取资料失败（${response.status}），文件可能已被移动或无访问权限。`);
  return new File([await response.blob()], item.name, { type: item.upload?.content_type ?? response.headers.get("content-type") ?? "application/octet-stream" });
}

export function saveChatLibraryFile(file: File) {
  const url = URL.createObjectURL(file);
  const link = document.createElement("a");
  link.href = url;
  link.download = file.name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
