"""Run PaddleOCR on one image and print a backend-friendly JSON result.

This is an isolated OCR demo for the future AiAgent OCR worker.  It does not
upload images or contact external services; PaddleOCR may download its model
files into the active user's Paddle cache on its first run.
"""

from __future__ import annotations

import argparse
from datetime import datetime
import json
import os
import sys
import time
from pathlib import Path
from typing import Any


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Recognize text in one image with PaddleOCR.")
    parser.add_argument("image", type=Path, help="Path to a PNG, JPG, WebP, or GIF image")
    parser.add_argument("--language", default="ch", help="PaddleOCR language code, default: ch")
    parser.add_argument(
        "--model-size",
        choices=("mobile", "server"),
        help="OCR model size; defaults to mobile on CPU and server on GPU",
    )
    parser.add_argument("--device", choices=("cpu", "gpu"), default="cpu", help="Inference device, default: cpu; GPU requires paddlepaddle-gpu")
    parser.add_argument("--min-confidence", type=float, default=0.0, help="Drop lines below this confidence (0 to 1)")
    parser.add_argument("--output", type=Path, help="Optional JSON output file. Defaults to ./logs/ocr_<timestamp>.json")
    parser.add_argument("--log-dir", type=Path, default=Path("logs"), help="Directory for the default JSON result, relative to the current terminal directory")
    return parser.parse_args()


def require_image(path: Path) -> Path:
    resolved = path.expanduser().resolve()
    if not resolved.is_file():
        raise ValueError(f"Image does not exist: {resolved}")
    if resolved.suffix.lower() not in {".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"}:
        raise ValueError("Supported files: PNG, JPG, JPEG, WebP, GIF, BMP")
    return resolved


def as_json_value(value: Any) -> Any:
    """Convert numpy-like values from PaddleOCR result objects into JSON values."""
    if hasattr(value, "tolist"):
        return value.tolist()
    return value


def result_json(result: Any) -> dict[str, Any]:
    """Read PaddleOCR 3.x OCRResult JSON while tolerating small API variations."""
    value = getattr(result, "json", result)
    if callable(value):
        value = value()
    value = as_json_value(value)
    if not isinstance(value, dict):
        raise TypeError(f"Unexpected PaddleOCR result type: {type(value).__name__}")
    # PaddleOCR/PaddleX 3.x wraps the pipeline payload as {"res": {...}}.
    # Older result objects expose the payload directly, so support both shapes.
    nested = value.get("res")
    return nested if isinstance(nested, dict) else value


def recognize(image: Path, language: str, min_confidence: float, model_size: str, device: str) -> dict[str, Any]:
    # The model source has already been selected by PaddleX; skip its slow host probe.
    os.environ.setdefault("PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK", "True")
    try:
        from paddleocr import PaddleOCR
        import paddle
    except ModuleNotFoundError as error:
        raise RuntimeError("PaddleOCR is not installed. Run: python -m pip install -r requirements.txt") from error

    if device == "gpu":
        if not paddle.is_compiled_with_cuda():
            raise RuntimeError(
                "GPU mode needs the Paddle GPU runtime. Install paddlepaddle-gpu, then run with --device gpu."
            )
        if paddle.device.cuda.device_count() < 1:
            raise RuntimeError("GPU mode was requested but no CUDA GPU is available to PaddlePaddle.")

    started = time.perf_counter()
    model_prefix = f"PP-OCRv5_{model_size}" if language == "ch" else None
    options: dict[str, Any] = {
        "use_doc_orientation_classify": False,
        "use_doc_unwarping": False,
        "use_textline_orientation": False,
        "device": "gpu:0" if device == "gpu" else "cpu",
    }
    if device == "cpu":
        # PaddlePaddle 3.3.x can fail in the Windows CPU oneDNN/PIR path.
        # Use standard CPU kernels for this portable demo.
        options["enable_mkldnn"] = False
    if model_prefix:
        options["text_detection_model_name"] = f"{model_prefix}_det"
        options["text_recognition_model_name"] = f"{model_prefix}_rec"
    else:
        options["lang"] = language
    ocr = PaddleOCR(**options)

    lines: list[dict[str, Any]] = []
    for page in ocr.predict(str(image)):
        page_json = result_json(page)
        texts = page_json.get("rec_texts") or []
        scores = page_json.get("rec_scores") or []
        boxes = page_json.get("rec_boxes") or page_json.get("dt_polys") or []
        for index, text in enumerate(texts):
            confidence = float(scores[index]) if index < len(scores) and scores[index] is not None else None
            if confidence is not None and confidence < min_confidence:
                continue
            lines.append(
                {
                    "text": str(text),
                    "confidence": confidence,
                    "box": as_json_value(boxes[index]) if index < len(boxes) else None,
                }
            )

    return {
        "engine": "paddleocr",
        "device": device,
        "language": language,
        "modelSize": model_size if model_prefix else "language-default",
        "detectionModel": f"{model_prefix}_det" if model_prefix else None,
        "recognitionModel": f"{model_prefix}_rec" if model_prefix else None,
        "imagePath": str(image),
        "text": "\n".join(line["text"] for line in lines),
        "lines": lines,
        "elapsedMs": round((time.perf_counter() - started) * 1000),
    }


def resolve_output_path(args: argparse.Namespace) -> Path:
    if args.output:
        return args.output.expanduser().resolve()
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S_%f")
    return (Path.cwd() / args.log_dir / f"ocr_{timestamp}.json").resolve()


def main() -> int:
    args = parse_args()
    if not 0 <= args.min_confidence <= 1:
        raise ValueError("--min-confidence must be between 0 and 1")

    model_size = args.model_size or ("server" if args.device == "gpu" else "mobile")
    payload = recognize(require_image(args.image), args.language, args.min_confidence, model_size, args.device)
    output = json.dumps(payload, ensure_ascii=False, indent=2)
    output_path = resolve_output_path(args)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(output + "\n", encoding="utf-8")
    print(output)
    print(f"\nJSON saved to: {output_path}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, ValueError, TypeError) as error:
        print(json.dumps({"error": str(error)}, ensure_ascii=False), file=sys.stderr)
        raise SystemExit(2)
