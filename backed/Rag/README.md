# LlamaIndex Worker

这个目录是后端内置的 LlamaIndex 本地 RAG worker。

## 文件说明

- `llamaindex_worker.py`：后端通过标准输入/输出调用的 Python worker。
- `requirements.txt`：worker 需要的 Python 依赖。
- `install.ps1`：在本目录创建 `.venv` 并安装依赖。

## 安装环境

在 PowerShell 中进入 `AiAgent/backed/Rag`，执行：

```powershell
.\install.ps1
```

安装完成后可以把后端 `appsettings.json` 改成使用虚拟环境：

```json
"Rag": {
  "PythonPath": "Rag/.venv/Scripts/python.exe",
  "LlamaIndexWorkerPath": "Rag/llamaindex_worker.py"
}
```

如果 `PythonPath` 保持为 `python`，则会使用系统 Python。
