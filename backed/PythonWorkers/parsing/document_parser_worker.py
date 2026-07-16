import importlib.util
import json
import sys
import traceback
from pathlib import Path


PROVIDER = "document-parser"


def main():
    try:
        payload = json.loads(sys.stdin.read() or "{}")
        command = payload.get("command")
        if command == "preflight":
            write_json(preflight(), 0)
        if command == "parse_pdf":
            result = parse_pdf(payload)
            write_json(result, 0 if result.get("ok") else 2)

        write_json(error("unknown_command", f"Unknown command: {command}"), 2)
    except Exception as exc:
        write_json(
            error(
                "worker_exception",
                str(exc),
                {"traceback": traceback.format_exc(limit=8)},
            ),
            1,
        )


def preflight():
    dependencies = {
        "fitz": module_available("fitz"),
        "pymupdf4llm": module_available("pymupdf4llm"),
    }
    ok = dependencies["fitz"]
    result = {
        "ok": dependencies["fitz"],
        "provider": PROVIDER,
        "action": "preflight",
        "dependencies": dependencies,
        "python": sys.version.split()[0],
    }
    if not ok:
        missing = [name for name, ready in dependencies.items() if not ready]
        result["error"] = {
            "code": "missing_dependencies",
            "message": "Missing Python dependencies: " + ", ".join(missing),
            "details": {"missing": missing},
        }
    return result


def parse_pdf(payload):
    status = preflight()
    if not status["ok"]:
        missing = [name for name, ok in status["dependencies"].items() if not ok]
        return error("missing_dependencies", "Missing Python dependencies: " + ", ".join(missing))

    file_path = require_existing_file(payload, "file_path")
    output_dir = require_output_dir(payload, "output_dir")
    options = payload.get("options") or {}
    engine = normalize_engine(options.get("engine"))

    if file_path.suffix.lower() != ".pdf":
        return error("unsupported_file", "Only PDF parsing is supported by parse_pdf.")

    markdown_path = output_dir / f"{file_path.stem}.md"
    text_path = output_dir / f"{file_path.stem}.txt"

    if engine == "pymupdf4llm" and status["dependencies"].get("pymupdf4llm"):
        markdown = parse_with_pymupdf4llm(file_path, options)
        markdown_path.write_text(markdown, encoding="utf-8")
        text_path.write_text(markdown_to_text(markdown), encoding="utf-8")
        used_engine = "pymupdf4llm"
    else:
        text = parse_with_pymupdf(file_path)
        text_path.write_text(text, encoding="utf-8")
        markdown_path.write_text(text, encoding="utf-8")
        used_engine = "pymupdf"

    return {
        "ok": True,
        "provider": PROVIDER,
        "action": "parse_pdf",
        "engine": used_engine,
        "markdown_path": str(markdown_path),
        "text_path": str(text_path),
        "page_count": count_pdf_pages(file_path),
    }


def parse_with_pymupdf4llm(file_path, options):
    import pymupdf4llm

    kwargs = {}
    if bool(options.get("write_images")):
        image_dir = Path(options.get("image_dir") or file_path.parent / f"{file_path.stem}_images")
        image_dir.mkdir(parents=True, exist_ok=True)
        kwargs["write_images"] = True
        kwargs["image_path"] = str(image_dir)

    return pymupdf4llm.to_markdown(str(file_path), **kwargs)


def parse_with_pymupdf(file_path):
    import fitz

    parts = []
    with fitz.open(str(file_path)) as document:
        for index, page in enumerate(document, start=1):
            text = page.get_text("text").strip()
            if text:
                parts.append(f"# Page {index}\n\n{text}")
    return "\n\n".join(parts)


def count_pdf_pages(file_path):
    import fitz

    with fitz.open(str(file_path)) as document:
        return int(document.page_count)


def markdown_to_text(markdown):
    lines = []
    for line in str(markdown or "").splitlines():
        stripped = line.strip()
        if not stripped:
            lines.append("")
            continue
        lines.append(stripped.lstrip("#").strip())
    return "\n".join(lines).strip()


def normalize_engine(value):
    engine = str(value or "pymupdf4llm").strip().lower()
    return engine if engine in ("pymupdf4llm", "pymupdf") else "pymupdf4llm"


def require_existing_file(payload, key):
    value = payload.get(key)
    if not value:
        raise RuntimeError(f"{key} is required.")
    path = Path(value).resolve()
    if not path.exists() or not path.is_file():
        raise RuntimeError(f"{key} does not exist: {path}")
    return path


def require_output_dir(payload, key):
    value = payload.get(key)
    if not value:
        raise RuntimeError(f"{key} is required.")
    path = Path(value).resolve()
    path.mkdir(parents=True, exist_ok=True)
    return path


def module_available(name):
    try:
        return importlib.util.find_spec(name) is not None
    except ModuleNotFoundError:
        return False


def error(code, message, details=None):
    return {
        "ok": False,
        "provider": PROVIDER,
        "action": "parse_pdf",
        "error": {
            "code": code,
            "message": message,
            "details": details or {},
        },
    }


def write_json(payload, exit_code):
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")

    sys.stdout.write(json.dumps(payload, ensure_ascii=False))
    sys.exit(exit_code)


if __name__ == "__main__":
    main()
