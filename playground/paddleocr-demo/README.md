# PaddleOCR Playground

## NVIDIA GPU mode (RTX 4060 verified)

This project supports either CPU or NVIDIA GPU inference. The GPU environment
uses PaddlePaddle 3.2.2 with CUDA 12.6 runtime; an NVIDIA driver supporting
CUDA 12.6 or later is required.

To switch an existing CPU virtual environment to GPU, run:

```powershell
.\.venv\Scripts\python.exe -m pip uninstall -y paddlepaddle
.\.venv\Scripts\python.exe -m pip install paddlepaddle-gpu==3.2.2 -i https://www.paddlepaddle.org.cn/packages/stable/cu126/
```

Then enable GPU explicitly when recognizing an image:

```powershell
.\.venv\Scripts\python.exe .\main.py ".\test\image.png" --device gpu --output .\result-gpu.json
```

Use `--device cpu` (the default) for CPU mode. Do not install both
`paddlepaddle` and `paddlepaddle-gpu` into the same virtual environment.

这是一个独立的 Python OCR 演示，用来验证无显卡 Windows 环境下的 PaddleOCR 接入方式。它不改动 AiAgent 后端、不上传图片，也不启动 HTTP 服务。

## 1. 创建虚拟环境并安装

在此目录执行：

```powershell
py -3.12 -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
```

首次执行识别时，PaddleOCR 会下载模型到当前用户的 Paddle 缓存目录；模型不应放在未来 AiAgent 的打包产物目录中。

## 2. 识别图片

在 VS Code 中打开此目录后，打开 `main.py`，并在集成终端执行：

```powershell
.\.venv\Scripts\python.exe .\main.py "C:\\path\\to\\screenshot.png"
```

结果会同时输出到终端，并自动保存到**当前终端目录**下的 `logs` 文件夹，例如 `logs\ocr_20260803_153045_123456.json`。

可指定语言、最低置信度和结果文件：

```powershell
.\.venv\Scripts\python.exe .\main.py "C:\\path\\to\\screenshot.png" --language ch --min-confidence 0.6
```

可通过 `--output` 指定精确文件，或通过 `--log-dir` 修改默认日志目录：

```powershell
.\.venv\Scripts\python.exe .\main.py "C:\\path\\to\\screenshot.png" --output .\result.json
.\.venv\Scripts\python.exe .\main.py "C:\\path\\to\\screenshot.png" --log-dir .\ocr-logs
```

默认使用 CPU 更快的 `PP-OCRv5_mobile` 模型；需要更高精度且接受明显更慢速度时才使用：

```powershell
.\.venv\Scripts\python.exe .\main.py "C:\\path\\to\\screenshot.png" --model-size server
```

程序会把 JSON 输出到标准输出，并保存一份到日志目录。返回结构包含整段 `text`，以及带坐标和置信度的 `lines`：

```json
{
  "engine": "paddleocr",
  "language": "ch",
  "text": "图片中识别到的文字",
  "lines": [
    { "text": "图片中识别到的文字", "confidence": 0.98, "box": [0, 0, 100, 20] }
  ],
  "elapsedMs": 1234
}
```

## 与 AiAgent 后端的后续契约

后续 OCR Worker 只应接收后端已校验、按用户隔离的附件本地路径或附件 ID；浏览器不能传入任意服务器路径。第三方 `codex --profile` 模型使用 `text` 作为带明确“不可信附件内容”边界的 Prompt 上下文；原生 Codex app-server 仍传入原图。

完整设计见 [OCR 研究与接入建议](../../docs/third-party-model-image-ocr-research.md)。
