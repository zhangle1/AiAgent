import type { ChatUploadFile } from "./chat-api";
import type { CodeProjectMarkdownDocument } from "./code-repository-types";
import type { ChatLibraryItem } from "./chat-library-types";

// Documents and visual deliverables only. HTML remains available outside source/build folders.
const extensions = /\.(?:md|markdown|txt|csv|pdf|html?|docx?|xlsx?|xlsm|xlsb|xltx?|xltm|pptx?|rtf|png|jpe?g|gif|webp|bmp)$/i;
const excludedDirectories = /(?:^|\/)(?:node_modules|vendor|\.git|\.next|bin|obj|dist|build|coverage|src|components|pages|app)(?:\/|$)/i;

export function buildChatLibrary(uploads: ChatUploadFile[], documents: CodeProjectMarkdownDocument[]): ChatLibraryItem[] {
  const items: ChatLibraryItem[] = uploads
    .filter((item) => item.kind !== "extracted_text" && extensions.test(item.file_name))
    .map((upload) => ({ id: `upload:${upload.id}`, name: upload.file_name, location: "我的聊天上传", updatedAt: upload.created_at, source: "upload", upload }));
  for (const document of documents) {
    if (document.source === "agent_index" || !extensions.test(document.name)) continue;
    if (excludedDirectories.test(document.path.replace(/\\/g, "/"))) continue;
    items.push({ id: `project:${document.repository_name}:${document.path}`, name: document.name,
      location: `${document.repository_name}/${document.path}`, updatedAt: document.updated_at ?? "",
      source: "project", document });
  }
  return items.sort((a, b) => (Date.parse(b.updatedAt) || 0) - (Date.parse(a.updatedAt) || 0) || a.id.localeCompare(b.id));
}
