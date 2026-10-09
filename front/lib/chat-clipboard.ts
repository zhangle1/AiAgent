/** Return uploads only when the clipboard does not contain editable table text. */
export function getChatClipboardFiles(clipboard: Pick<DataTransfer, "items" | "files" | "getData">): File[] {
  const fromItems = Array.from(clipboard.items)
    .filter(item => item.kind === "file")
    .map(item => item.getAsFile())
    .filter((file): file is File => file !== null);
  const files = fromItems.length > 0 ? fromItems : Array.from(clipboard.files);
  const text = clipboard.getData("text/plain");
  // Excel can include a bitmap of copied cells alongside TSV/HTML. Let the
  // textarea's native paste preserve tabs, empty columns and the cursor position.
  const tableText = text.length > 0 && (text.includes("\t") || /<table[\s>]/i.test(clipboard.getData("text/html")));
  if (tableText && files.every(file => file.type.startsWith("image/"))) return [];
  return files;
}
