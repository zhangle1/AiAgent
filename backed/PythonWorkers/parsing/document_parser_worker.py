import importlib.util
import contextlib
import json
import sys
import traceback
from pathlib import Path


PROVIDER = "document-parser"


def main():
    for stream in (sys.stdin, sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    try:
        payload = json.loads(sys.stdin.read() or "{}")
        command = payload.get("command")
        if command == "preflight":
            write_json(preflight(), 0)
        if command == "parse_pdf":
            # Third-party layout notices must never contaminate the JSON protocol.
            with contextlib.redirect_stdout(sys.stderr):
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

    if file_path.suffix.lower() != ".pdf":
        return error("unsupported_file", "Only PDF parsing is supported by parse_pdf.")

    from pdf_layout import convert_pdf
    return convert_pdf(file_path, output_dir, bool(options.get("write_images", True)))


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
