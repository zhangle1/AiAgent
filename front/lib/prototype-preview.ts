const PROTOTYPE_BODY_PREFIX = "prototype-html:v1:";

export function extractPrototypeHtml(content: string) {
  const html = content.trim()
    .replace(/^`{3,}[^\r\n]*\r?\n?/, "")
    .replace(/\r?\n?`{3,}\s*$/, "")
    .replace(/^\s*>\s?/gm, "")
    .trim();
  const start = html.search(/<!doctype\s+html|<html\b/i);
  return start >= 0 && /<html\b/i.test(html) ? html.slice(start).trim() : "";
}

export function encodePrototypeHtml(html: string) {
  const bytes = new TextEncoder().encode(html);
  let binary = "";
  bytes.forEach((byte) => { binary += String.fromCharCode(byte); });
  return `${PROTOTYPE_BODY_PREFIX}${btoa(binary)}`;
}

export function decodePrototypeHtml(body: string) {
  if (!body.startsWith(PROTOTYPE_BODY_PREFIX)) return extractPrototypeHtml(body);
  try {
    const binary = atob(body.slice(PROTOTYPE_BODY_PREFIX.length));
    const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
    return new TextDecoder().decode(bytes);
  } catch {
    return "";
  }
}

export function securePrototypePreview(content: string) {
  const policy = '<meta http-equiv="Content-Security-Policy" content="default-src \'none\'; img-src http: https: data: blob:; style-src \'unsafe-inline\'; script-src \'unsafe-inline\'; font-src data:; connect-src \'none\'; form-action \'none\'">';
  const staticContent = content
    .replace(/<base\b[^>]*>/gi, "")
    .replace(/<meta\b[^>]*http-equiv\s*=\s*(?:"refresh"|'refresh'|refresh)[^>]*>/gi, "")
    .replace(/\son\w+\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+)/gi, "")
    .replace(/<a\b([^>]*)>/gi, (_match, attributes: string) => `<a${attributes.replace(/\shref\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+)/i, ' href="#"').replace(/\starget\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+)/i, "")}>`)
    .replace(/<form\b([^>]*)>/gi, (_match, attributes: string) => `<form${attributes.replace(/\saction\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+)/i, ' action="#"')}>`);
  if (/<head(\s[^>]*)?>/i.test(staticContent)) return staticContent.replace(/<head(\s[^>]*)?>/i, (match) => `${match}${policy}`);
  return staticContent.replace(/<html(\s[^>]*)?>/i, (match) => `${match}<head>${policy}</head>`);
}
