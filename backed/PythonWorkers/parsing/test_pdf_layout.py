import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import fitz

from pdf_layout import convert_pdf


class PdfLayoutTests(unittest.TestCase):
    def test_outline_text_images_vectors_and_relative_paths(self):
        with tempfile.TemporaryDirectory() as root:
            root = Path(root)
            source = root / "book.pdf"
            doc = fitz.open()
            page = doc.new_page()
            page.insert_text((40, 45), "Chapter One", fontsize=22)
            page.insert_text((40, 85), "Body text remains searchable.", fontsize=11)
            image = fitz.Pixmap(fitz.csRGB, fitz.IRect(0, 0, 40, 30), False)
            image.clear_with(100)
            page.insert_image(fitz.Rect(40, 120, 200, 240), stream=image.tobytes("png"))
            page.draw_rect(fitz.Rect(260, 120, 400, 240), color=(0, 0, 1))
            page.insert_text((270, 180), "Vector figure")
            page2 = doc.new_page()
            page2.insert_text((40, 45), "Chapter Two", fontsize=22)
            page2.insert_text((40, 85), "END_OF_BOOK", fontsize=11)
            doc.set_toc([[1, "Chapter One", 1], [1, "Chapter Two", 2]])
            doc.save(source)
            doc.close()
            original = source.read_bytes()
            result = convert_pdf(source, root / "output")
            markdown = Path(result["markdown_path"]).read_text(encoding="utf-8")
            self.assertIn("# Chapter One", markdown)
            self.assertIn("# Chapter Two", markdown)
            self.assertIn("END_OF_BOOK", markdown)
            self.assertEqual(2, result["page_count"])
            self.assertEqual([], result["scanned_pages"])
            self.assertTrue(any(a["kind"] == "image" for a in result["assets"]))
            self.assertTrue(any(a["kind"] == "vector" for a in result["assets"]))
            self.assertNotIn(str(root), markdown)
            for asset in result["assets"]:
                self.assertIn(asset["path"], markdown)
                self.assertTrue((root / "output" / asset["path"]).is_file())
                self.assertLessEqual(fitz.Pixmap(str(root / "output" / asset["path"])).width, 4096)
            self.assertEqual(original, source.read_bytes())

    def test_font_heading_and_scan_warning_without_model_or_optional_converter(self):
        with tempfile.TemporaryDirectory() as root:
            root = Path(root)
            source = root / "font-and-scan.pdf"
            doc = fitz.open()
            page = doc.new_page()
            page.insert_text((40, 45), "Font Heading", fontsize=24)
            page.insert_text((40, 85), "Body paragraph repeated for body font detection. " * 3, fontsize=10)
            doc.new_page().draw_rect(fitz.Rect(20, 20, 350, 400), fill=(0.5, 0.5, 0.5))
            doc.save(source)
            doc.close()
            result = convert_pdf(source, root / "output")
            markdown = Path(result["markdown_path"]).read_text(encoding="utf-8")
            self.assertIn("# Font Heading", markdown)
            self.assertEqual([2], result["scanned_pages"])
            self.assertTrue(result["warnings"])
            self.assertTrue(any(a["page"] == 2 for a in result["assets"]))
            # Exercise the actual stdin/stdout worker contract as well.
            process = subprocess.run([sys.executable, str(Path(__file__).with_name("document_parser_worker.py"))],
                input=json.dumps({"command": "parse_pdf", "file_path": str(source), "output_dir": str(root / "worker"), "options": {"write_images": True}}),
                text=True, capture_output=True, encoding="utf-8", check=True)
            response = json.loads(process.stdout)
            self.assertEqual("pymupdf-layout", response["engine"])
            self.assertTrue(response["assets"])


if __name__ == "__main__":
    unittest.main()
