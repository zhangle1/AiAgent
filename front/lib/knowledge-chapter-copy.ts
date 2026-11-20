import { fromMarkdown } from "mdast-util-from-markdown";
import { knowledgeResourceFileUrl, MAX_KNOWLEDGE_UPLOAD_BYTES, readKnowledgeChapter, type KnowledgeChapterReference } from "./knowledge-api";

const pngDataPrefix = "data:image/png;base64,";

export function isEmbeddedChapterImage(url: string) {
  return url.startsWith(pngDataPrefix) && /^[a-zA-Z0-9+/]+={0,2}$/.test(url.slice(pngDataPrefix.length));
}

function imageDataUrl(image: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(new Error("无法读取插图，请重试。"));
    reader.readAsDataURL(image.slice(0, image.size, "image/png"));
  });
}

export async function createKnowledgeChapterCopy(chapter: KnowledgeChapterReference, signal?: AbortSignal): Promise<File> {
  const { uri, text, parsedDocumentId } = await readKnowledgeChapter(chapter, signal);
  const marker = "/_parsed/";
  const parsedRoot = uri.indexOf(marker);
  const bookRootEnd = uri.indexOf("/", parsedRoot + marker.length);
  if (parsedRoot < 0 || bookRootEnd < 0) throw new Error("无法定位章节的解析目录。");
  const assetRoot = uri.slice(0, bookRootEnd) + "/assets/";
  const pending = [...fromMarkdown(text).children];
  const images: { offset: number; uri: string }[] = [];
  const counts = new Map<string, number>();
  while (pending.length) {
    const node = pending.pop()!;
    if ("children" in node) pending.push(...node.children);
    if (node.type !== "image" || !node.url.startsWith(assetRoot) || !node.url.endsWith(".png")) continue;
    // The parsed-document reader emits canonical inline images with no destination title.
    const end = node.position?.end.offset;
    if (end == null || text.slice(end - node.url.length - 2, end) !== `(${node.url})`) continue;
    images.push({ offset: end - node.url.length - 1, uri: node.url });
    counts.set(node.url, (counts.get(node.url) ?? 0) + 1);
  }

  let size = new Blob([text]).size;
  const checkSize = () => {
    if (size > MAX_KNOWLEDGE_UPLOAD_BYTES) throw new Error("章节和插图合计超过资料库单文件 25 MB 上限。");
  };
  checkSize();
  const dataUrls = new Map<string, string>();
  for (const [imageUri, count] of counts) {
    const response = await fetch(knowledgeResourceFileUrl(imageUri, false, parsedDocumentId), { cache: "no-store", signal });
    if (!response.ok) throw new Error(`读取章节插图失败（HTTP ${response.status}），请重新打开章节后重试。`);
    const image = await response.blob();
    if (!image.size || image.type.split(";")[0] !== "image/png") throw new Error("章节插图格式无效，请重新解析原文后重试。");
    size += (pngDataPrefix.length + 4 * Math.ceil(image.size / 3) - new Blob([imageUri]).size) * count;
    checkSize();
    dataUrls.set(imageUri, await imageDataUrl(image));
    signal?.throwIfAborted();
  }

  const parts: string[] = [];
  let offset = 0;
  for (const image of images.sort((left, right) => left.offset - right.offset)) {
    parts.push(text.slice(offset, image.offset), dataUrls.get(image.uri)!);
    offset = image.offset + image.uri.length;
  }
  parts.push(text.slice(offset));
  return new File(parts, chapter.name, { type: "text/markdown" });
}
