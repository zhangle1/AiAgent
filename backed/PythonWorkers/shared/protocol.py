import json
import sys


def configure_stdio():
    """确保 worker JSON 输出使用 UTF-8，避免 Windows 默认编码导致中文乱码。"""
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")


def error(provider, code, message, details=None, action=""):
    return {
        "ok": False,
        "provider": provider,
        "action": action,
        "error": {
            "code": code,
            "message": message,
            "details": details or {},
        },
    }


def write_json(payload, exit_code=0):
    configure_stdio()
    sys.stdout.write(json.dumps(payload, ensure_ascii=False))
    sys.exit(exit_code)
