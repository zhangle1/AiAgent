import type { Layer } from "ag-psd";

export const PSD_VIEWPORTS = { desktop: { width: 1440, label: "桌面" }, tablet: { width: 768, label: "平板" }, mobile: { width: 390, label: "移动端" } } as const;
export type PsdViewport = keyof typeof PSD_VIEWPORTS;

/** Static, script-free rendering. Never grant scripts and same-origin together. */
export async function generatePrototypePsd(html: string, viewport: PsdViewport, progress: (message: string) => void): Promise<Blob> {
  const [{ default: render }, { writePsd }] = await Promise.all([import("html2canvas"), import("ag-psd")]);
  const source = new DOMParser().parseFromString(html, "text/html");
  source.querySelectorAll("script, iframe, object, embed, base, meta[http-equiv], link").forEach((node) => node.remove());
  source.querySelectorAll("*").forEach((node) => {
    for (const attr of Array.from(node.attributes)) if (/^on/i.test(attr.name)) node.removeAttribute(attr.name);
  });
  const policy = source.createElement("meta");
  policy.httpEquiv = "Content-Security-Policy";
  policy.content = "default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src data: https: http: blob:; font-src data:; form-action 'none'";
  source.head.prepend(policy);
  const style = source.createElement("style");
  style.textContent = "*,*::before,*::after{animation:none!important;transition:none!important;caret-color:transparent!important}";
  source.head.append(style);
  const frame = document.createElement("iframe");
  frame.setAttribute("sandbox", "allow-same-origin");
  frame.setAttribute("aria-hidden", "true");
  frame.style.cssText = `position:fixed;left:-20000px;top:0;width:${PSD_VIEWPORTS[viewport].width}px;height:900px;border:0;pointer-events:none`;
  try {
    progress("正在排版…");
    await new Promise<void>((resolve, reject) => {
      const timer = window.setTimeout(() => reject(new Error("页面加载超时，请重试。")), 15000);
      frame.onload = () => { window.clearTimeout(timer); resolve(); };
      frame.srcdoc = "<!doctype html>" + source.documentElement.outerHTML;
      document.body.append(frame);
    });
    const doc = frame.contentDocument!;
    await doc.fonts.ready;
    const width = PSD_VIEWPORTS[viewport].width;
    const height = Math.max(900, doc.documentElement.scrollHeight, doc.body.scrollHeight);
    if (height > 12000 || width * height > 16000000) throw new Error("页面过长，请拆分为多个界面后导出 PSD（最多 12000px 高、1600 万像素）。");
    const options = { width, height, windowWidth: width, windowHeight: 900, scale: 1, useCORS: true, allowTaint: false, logging: false, imageTimeout: 10000 };
    const canvas = await render(doc.body, options);
    // Descend through layout-only wrappers to get useful semantic region layers.
    let container: Element = doc.body;
    const visibleChildren = (node: Element) => Array.from(node.children).filter((child) => !["STYLE", "SCRIPT"].includes(child.tagName) && child.getBoundingClientRect().width && child.getBoundingClientRect().height);
    while (visibleChildren(container).length === 1 && !Array.from(container.childNodes).some((node) => node.nodeType === 3 && node.textContent?.trim())) {
      const child = visibleChildren(container)[0];
      if (!visibleChildren(child).length) break;
      container = child;
    }
    const regions = visibleChildren(container);
    if (regions.length > 40) throw new Error("页面区域超过 40 个，请用 section 分组后重试。");
    regions.forEach((node, index) => node.setAttribute("data-psd-region", String(index)));
    const children: Layer[] = [];
    let retainedPixels = width * height * 2;
    // Keep an accurate composite reference, hidden so it does not obscure editable regions.
    children.push({ name: "完整预览（参考，默认隐藏）", canvas, hidden: true });
    const background = await render(doc.body, { ...options, onclone: (clone) => { clone.querySelectorAll("[data-psd-region]").forEach((node) => { (node as HTMLElement).style.visibility = "hidden"; }); } });
    children.push({ name: "页面背景", canvas: background });
    for (let index = 0; index < regions.length; index++) {
      progress(`正在生成图层 ${index + 1}/${regions.length}…`);
      const region = regions[index];
      const bitmap = await render(doc.body, { ...options, backgroundColor: null, onclone: (clone) => {
        clone.querySelectorAll("[data-psd-region]").forEach((node) => {
          if (node.getAttribute("data-psd-region") !== String(index)) (node as HTMLElement).style.setProperty("visibility", "hidden", "important");
        });
        let parent: HTMLElement | null = clone.querySelector(`[data-psd-region="${index}"]`)?.parentElement ?? null;
        while (parent) { parent.style.setProperty("background", "transparent", "important"); parent = parent.parentElement; }
      } });
      const name = region.getAttribute("data-psd-name") || region.getAttribute("aria-label") || region.id || `${region.tagName.toLowerCase()} ${index + 1}`;
      const layer = trimLayer(bitmap, `${name}（像素图层）`);
      retainedPixels += layer.canvas!.width * layer.canvas!.height;
      if (retainedPixels > 48000000) throw new Error("图层占用过大，请拆分页面后导出。");
      children.push({ name, opened: true, children: [layer] });
    }
    progress("正在编码 PSD…");
    return new Blob([writePsd({ width, height, canvas, children })], { type: "image/vnd.adobe.photoshop" });
  } finally { frame.remove(); }
}

function trimLayer(canvas: HTMLCanvasElement, name: string): Layer {
  const context = canvas.getContext("2d")!;
  const { width, height } = canvas;
  const pixels = context.getImageData(0, 0, width, height).data;
  let left = width, top = height, right = -1, bottom = -1;
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    if (pixels[(y * width + x) * 4 + 3]) {
      left = Math.min(left, x); top = Math.min(top, y); right = Math.max(right, x); bottom = Math.max(bottom, y);
    }
  }
  const cropped = document.createElement("canvas");
  cropped.width = Math.max(1, right - left + 1);
  cropped.height = Math.max(1, bottom - top + 1);
  if (right >= 0) cropped.getContext("2d")!.drawImage(canvas, left, top, cropped.width, cropped.height, 0, 0, cropped.width, cropped.height);
  canvas.width = canvas.height = 1;
  return { name, left: right >= 0 ? left : 0, top: right >= 0 ? top : 0, canvas: cropped };
}

export function downloadPsd(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = name.replace(/[<>:"/\\|?*\u0000-\u001f]/g, "_");
  anchor.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 30000);
}
