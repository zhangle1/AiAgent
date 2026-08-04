import json
import os
import sys
import time
import traceback
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "shared"))
from protocol import configure_stdio, error, write_json


PROVIDER = "paddleocr"
SUPPORTED_LANGUAGES = {"ch", "en", "japan", "korean"}


def as_json_value(value):
    return value.tolist() if hasattr(value, "tolist") else value


def result_json(result):
    value = getattr(result, "json", result)
    if callable(value):
        value = value()
    value = as_json_value(value)
    if not isinstance(value, dict):
        raise TypeError(f"Unexpected PaddleOCR result type: {type(value).__name__}")
    nested = value.get("res")
    return nested if isinstance(nested, dict) else value


def progress(stage, percent, message):
    configure_stdio()
    sys.stdout.write(json.dumps({"type": "progress", "stage": stage, "progress": percent, "message": message}, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def main():
    configure_stdio()
    try:
        # .NET StreamWriter may prefix its first UTF-8 write with a BOM.
        # Accept it so Windows host input remains valid JSON.
        payload = json.loads((sys.stdin.read() or "{}").lstrip("\ufeff"))
        if payload.get("command") == "health":
            write_json(health(), 0)
        if payload.get("command") != "ocr_image":
            write_json(error(PROVIDER, "unknown_command", "Only ocr_image is supported."), 2)
        result = ocr_image(payload)
        write_json(result, 0 if result.get("ok") else 2)
    except Exception as exc:
        write_json(error(PROVIDER, "worker_exception", str(exc), {"traceback": traceback.format_exc(limit=8)}), 1)


def ocr_image(payload):
    image_path = Path(str(payload.get("image_path") or "")).resolve()
    if not image_path.is_file():
        return error(PROVIDER, "image_not_found", "The requested image does not exist.")
    language = str(payload.get("language") or "ch").strip().lower()
    if language not in SUPPORTED_LANGUAGES:
        return error(PROVIDER, "unsupported_language", f"Unsupported OCR language: {language}.")

    progress("loading", 10, "正在加载本地 OCR 引擎。")
    started = time.monotonic()
    # PaddleX reads this source-selection flag during package initialization.
    os.environ.setdefault("PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK", "True")
    try:
        from paddleocr import PaddleOCR
    except ImportError:
        return error(PROVIDER, "missing_dependencies", "PaddleOCR is not installed. Run PythonWorkers/ocr/install.ps1 first.")

    progress("recognizing", 35, "正在识别图片中的文字。")
    options = {
        "use_doc_orientation_classify": False,
        "use_doc_unwarping": False,
        "use_textline_orientation": False,
        "device": "cpu",
        # Avoid the known Windows CPU oneDNN/PIR failure path.
        "enable_mkldnn": False,
    }
    if language == "ch":
        options["text_detection_model_name"] = "PP-OCRv5_mobile_det"
        options["text_recognition_model_name"] = "PP-OCRv5_mobile_rec"
    else:
        options["lang"] = language
    ocr = PaddleOCR(**options)
    lines = []
    for page in ocr.predict(str(image_path)):
        page_json = result_json(page)
        texts = page_json.get("rec_texts") or []
        scores = page_json.get("rec_scores") or []
        for index, value in enumerate(texts):
            text = str(value or "").strip()
            if text:
                confidence = float(scores[index]) if index < len(scores) and scores[index] is not None else None
                lines.append({"text": text, "confidence": confidence})

    text = "\n".join(item["text"] for item in lines)
    confidence_values = [item["confidence"] for item in lines if item["confidence"] is not None]
    progress("completed", 100, "图片文字识别完成。")
    return {
        "ok": True,
        "provider": PROVIDER,
        "action": "ocr_image",
        "engine": PROVIDER,
        "model": "PP-OCRv5_mobile",
        "text": text,
        "lines": lines,
        "confidence": round(sum(confidence_values) / len(confidence_values), 4) if confidence_values else None,
        "elapsed_ms": int((time.monotonic() - started) * 1000),
    }


def health():
    try:
        os.environ.setdefault("PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK", "True")
        import paddle
        import paddleocr
        return {"ok": True, "provider": PROVIDER, "paddle_version": paddle.__version__, "paddleocr_version": getattr(paddleocr, "__version__", "unknown")}
    except Exception as exc:
        return error(PROVIDER, "missing_dependencies", str(exc))


if __name__ == "__main__":
    main()
