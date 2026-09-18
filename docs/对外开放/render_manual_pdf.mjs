import { fileURLToPath, pathToFileURL } from "node:url";
import path from "node:path";

const root = path.dirname(fileURLToPath(import.meta.url));
const { chromium } = await import(pathToFileURL("C:/Users/zhang/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright/index.mjs").href);
const htmlPath = path.join(root, "坤伴Agent协同研发平台介绍手册.html");
const pdfPath = process.env.MANUAL_PDF_OUTPUT
  ? path.resolve(process.env.MANUAL_PDF_OUTPUT)
  : path.join(root, "坤伴Agent协同研发平台介绍手册.pdf");
const browser = await chromium.launch({ headless: true, executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe" });
try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, deviceScaleFactor: 1 });
  await page.goto(pathToFileURL(htmlPath).href, { waitUntil: "networkidle" });
  await page.emulateMedia({ media: "print" });
  await page.pdf({ path: pdfPath, format: "A4", printBackground: true, preferCSSPageSize: true });
} finally {
  await browser.close();
}
