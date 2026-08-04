```powershell
# 在 paddleocr-demo 目录执行；无需 Activate.ps1，避免误用全局 Python。
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install --upgrade pip
.\.venv\Scripts\python.exe -m pip install --force-reinstall -r .\requirements.txt

# 识别图片并写入当前目录的 result.json。
.\.venv\Scripts\python.exe .\main.py "E:\项目\know-why\AiAgent\playground\paddleocr-demo\test\image.png" --output .\result.json

# 默认使用 mobile CPU 模型；只有精度优先时才使用更慢的 server 模型。
.\.venv\Scripts\python.exe .\main.py "E:\项目\know-why\AiAgent\playground\paddleocr-demo\test\image.png" --model-size server --output .\result-server.json
```

## GPU mode (RTX 4060)

The current `.venv` has already been switched to the NVIDIA GPU runtime.
Run the following command to recognize an image with GPU 0 and write JSON:

```powershell
.\.venv\Scripts\python.exe .\main.py "E:\项目\know-why\AiAgent\playground\paddleocr-demo\test\image.png" --device gpu --output .\result-gpu.json
```

For a new CPU virtual environment, switch to the official CUDA 12.6 Paddle
runtime before using `--device gpu`:

```powershell
.\.venv\Scripts\python.exe -m pip uninstall -y paddlepaddle
.\.venv\Scripts\python.exe -m pip install paddlepaddle-gpu==3.2.2 -i https://www.paddlepaddle.org.cn/packages/stable/cu126/
```

Check that Paddle sees the GPU:

```powershell
.\.venv\Scripts\python.exe -c "import paddle; print(paddle.is_compiled_with_cuda(), paddle.device.cuda.device_count())"
```

.\.venv\Scripts\python.exe .\main.py "E:\项目\know-why\AiAgent\playground\paddleocr-demo\test\image.png" --output .\result.json

不传 `--output` 时，JSON 会自动保存到当前目录的 `logs` 文件夹。
