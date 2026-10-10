import type { ChatUploadFile } from "./chat-api";
import type { CodeProjectMarkdownDocument } from "./code-repository-types";

export type ChatLibraryItem = {
  id: string;
  name: string;
  location: string;
  updatedAt: string;
  source: "upload" | "project";
  upload?: ChatUploadFile;
  document?: CodeProjectMarkdownDocument;
};
