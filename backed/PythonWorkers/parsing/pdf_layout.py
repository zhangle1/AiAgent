"""Deterministic local PDF conversion. Original files are never modified.

Headings prefer PDF outlines; font sizes provide a fallback. Figures are rendered
from page regions so overlays and vector content in those regions survive.
"""
import collections
import hashlib
import math
import re
from pathlib import Path


def convert_pdf(file_path, output_dir, write_images=True):
    import fitz

    output_dir = Path(output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    assets, pages, scanned_pages = [], [], []
    image_bytes = 0
    with fitz.open(str(file_path)) as doc:
        if doc.needs_pass:
            raise ValueError("PDF is encrypted; provide an unencrypted copy.")
        if doc.page_count > 2000:
            raise ValueError("PDF exceeds the 2000-page parsing limit.")
        outlines = collections.defaultdict(list)
        for item in doc.get_toc(simple=False):
            level, title, page_no = item[:3]
            if not title.strip() or not 1 <= page_no <= doc.page_count:
                continue
            dest = item[3] if len(item) > 3 and isinstance(item[3], dict) else {}
            point = dest.get("to")
            y = float(point.y) if point is not None else 0.0
            outlines[page_no].append((y, min(max(int(level), 1), 6), title.strip()))

        # Weight by text length, not by a potentially decorative largest span.
        sizes = collections.Counter()
        for page in doc:
            for block in page.get_text("dict", flags=fitz.TEXTFLAGS_DICT & ~fitz.TEXT_PRESERVE_IMAGES).get("blocks", []):
                for line in block.get("lines", []):
                    for span in line.get("spans", []):
                        sizes[round(span["size"], 1)] += len(span["text"].strip())
        body_size = sizes.most_common(1)[0][0] if sizes else 11.0
        heading_sizes = sorted((s for s in sizes if s >= body_size * 1.18), reverse=True)[:6]

        for page_index, page in enumerate(doc):
            page_no = page_index + 1
            blocks = page.get_text("dict", sort=True, flags=fitz.TEXTFLAGS_DICT & ~fitz.TEXT_PRESERVE_IMAGES).get("blocks", [])
            has_text = any(span["text"].strip() for b in blocks for line in b.get("lines", []) for span in line.get("spans", []))
            if not has_text:
                scanned_pages.append(page_no)
            events, table_boxes = [], []
            # Table detection is deterministic and optional on older PyMuPDF.
            if has_text and hasattr(page, "find_tables"):
                try:
                    tables = page.find_tables().tables
                except (ValueError, RuntimeError):
                    tables = []
                for table in tables:
                    rows = table.extract()
                    if not rows or len(rows) < 2:
                        continue
                    width = max(len(row) for row in rows)
                    def row_md(row):
                        cells = [str(c or "").replace("|", "\\|").replace("\n", "<br>") for c in row]
                        return "| " + " | ".join(cells + [""] * (width - len(cells))) + " |"
                    text = "\n".join([row_md(rows[0]), "| " + " | ".join(["---"] * width) + " |"] + [row_md(r) for r in rows[1:]])
                    table_boxes.append(fitz.Rect(table.bbox))
                    events.append((table.bbox[1], table.bbox[0], text))

            page_outlines = outlines.get(page_no, [])
            titles = {re.sub(r"\s+", "", title).casefold() for _, _, title in page_outlines}
            for y, level, title in page_outlines:
                events.append((y - 0.01, 0, "#" * level + " " + title))
            for block in blocks:
                if block.get("type") != 0:
                    continue
                paragraphs = []
                for line in block.get("lines", []):
                    box = fitz.Rect(line["bbox"])
                    if any(t.contains(box.tl) and t.contains(box.br) for t in table_boxes):
                        continue
                    spans = line.get("spans", [])
                    text = "".join(s["text"] for s in spans).strip()
                    if not text or re.sub(r"\s+", "", text).casefold() in titles:
                        continue
                    size = max((round(s["size"], 1) for s in spans if s["text"].strip()), default=body_size)
                    if not outlines and size in heading_sizes and len(text) <= 200:
                        if paragraphs:
                            events.append((block["bbox"][1], block["bbox"][0], "\n".join(paragraphs)))
                            paragraphs = []
                        events.append((box.y0, box.x0, "#" * (heading_sizes.index(size) + 1) + " " + text))
                    else:
                        paragraphs.append(text)
                if paragraphs:
                    events.append((block["bbox"][1], block["bbox"][0], "\n".join(paragraphs)))

            if write_images:
                regions = []
                for info in page.get_image_info():
                    rect = fitz.Rect(info["bbox"]) & page.rect
                    if not rect.is_empty and rect.width >= 8 and rect.height >= 8:
                        regions.append((rect, "image"))
                if hasattr(page, "cluster_drawings"):
                    try:
                        drawings = page.cluster_drawings()
                    except (ValueError, RuntimeError):
                        drawings = []
                    for box in drawings:
                        rect = fitz.Rect(box) & page.rect
                        if rect.is_empty or rect.width < 24 or rect.height < 24:
                            continue
                        if rect.get_area() > page.rect.get_area() * 0.95 and has_text:
                            continue
                        if any(t.intersects(rect) for t in table_boxes):
                            continue
                        if not any(existing.contains(rect) for existing, _ in regions):
                            regions.append((rect, "vector"))
                # Keep an image-only page even when its PDF representation is unusual.
                if not has_text and not regions:
                    regions.append((page.rect, "page"))
                seen = set()
                for rect, kind in regions:
                    key = tuple(round(v, 1) for v in rect)
                    if key in seen:
                        continue
                    seen.add(key)
                    if len(assets) >= 5000:
                        raise ValueError("PDF exceeds the 5000-image extraction limit.")
                    # Bound allocation BEFORE rendering a large or malicious page.
                    scale = min(2.0, math.sqrt(4_000_000 / max(rect.get_area(), 1)), 4096 / max(rect.width, rect.height))
                    pixmap = page.get_pixmap(matrix=fitz.Matrix(scale, scale), clip=rect, alpha=False)
                    data = pixmap.tobytes("png")
                    image_bytes += len(data)
                    if image_bytes > 256 * 1024 * 1024:
                        raise ValueError("PDF extracted images exceed 256 MiB.")
                    relative = f"assets/page-{page_no:04d}-{len(seen):03d}.png"
                    target = output_dir / relative
                    target.parent.mkdir(exist_ok=True)
                    target.write_bytes(data)
                    assets.append({"path": relative, "page": page_no, "kind": kind, "bbox": list(rect), "sha256": hashlib.sha256(data).hexdigest()})
                    events.append((rect.y0, rect.x0, f"![Page {page_no} {kind} {len(seen)}]({relative})"))
            if not has_text:
                events.insert(0, (-1, 0, "> 本页没有可提取的文本层，已保留页面图片；文字检索需要 OCR。"))
            pages.append(f"<!-- page: {page_no} -->\n\n" + "\n\n".join(e[2] for e in sorted(events, key=lambda e: (e[0], e[1]))))

        markdown = "\n\n".join(pages).strip()
        if not markdown:
            raise ValueError("PDF contains no readable pages.")
        markdown_path, text_path = output_dir / "document.md", output_dir / "document.txt"
        markdown_path.write_text(markdown, encoding="utf-8")
        text_path.write_text(re.sub(r"!\[[^\]]*\]\([^)]+\)", "", markdown), encoding="utf-8")
        return {"ok": True, "provider": "document-parser", "action": "parse_pdf", "engine": "pymupdf-layout",
                "markdown_path": str(markdown_path), "text_path": str(text_path), "page_count": doc.page_count,
                "assets": assets, "scanned_pages": scanned_pages,
                "warnings": [f"{len(scanned_pages)} 页没有文本层，已保留图片，尚未 OCR。"] if scanned_pages else []}
