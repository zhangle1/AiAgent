export function isChatImageFile(file: File): boolean {
  return file.type.startsWith("image/") || /\.(png|jpe?g|webp|gif)$/i.test(file.name);
}

/** Return uploads only when the clipboard does not contain editable table text. */
export function getChatClipboardFiles(clipboard: Pick<DataTransfer, "items" | "files" | "getData">): File[] {
  const fromItems = Array.from(clipboard.items)
    .filter(item => item.kind === "file")
    .map(item => item.getAsFile())
    .filter((file): file is File => file !== null);
  const fromFileList = Array.from(clipboard.files);
  // Clipboard items may expose a zero-byte placeholder while FileList carries the file.
  const itemCount = fromItems.filter(file => file.size > 0).length;
  const listedCount = fromFileList.filter(file => file.size > 0).length;
  const files = fromFileList.length > 0 && listedCount >= itemCount ? fromFileList : fromItems;
  const text = clipboard.getData("text/plain");
  // Excel can include a bitmap of copied cells alongside TSV/HTML. Let the
  // textarea's native paste preserve tabs, empty columns and the cursor position.
  const tableText = text.length > 0 && (text.includes("\t") || /<table[\s>]/i.test(clipboard.getData("text/html")));
  if (tableText && files.every(isChatImageFile)) return [];
  return files;
}
