# PythonWorkers

这个目录统一放置后端调用的 Python worker。

当前采用轻量隔离方案：

- C# 后端每次通过独立 Python 进程调用 worker。
- stdin 输入 JSON，stdout 输出 JSON。
- 每类 worker 使用独立虚拟环境路径，避免依赖互相污染。
- C# 侧负责超时和进程树清理。

推荐配置：

```json
"PythonWorkers": {
  "BasePath": "PythonWorkers",
  "TimeoutSeconds": 120,
  "AllowedRoots": ["data"],
  "Rag": {
    "PythonPath": "PythonWorkers/sandboxes/rag/.venv/Scripts/python.exe",
    "WorkerPath": "PythonWorkers/rag/llamaindex_worker.py"
  },
  "Parsing": {
    "PythonPath": "PythonWorkers/sandboxes/parsing/.venv/Scripts/python.exe",
    "WorkerPath": "PythonWorkers/parsing/document_parser_worker.py"
  }
}
```

`Rag` 负责索引与检索，`Parsing` 负责 PDF/文档解析。后续接 MinerU、OCR、GraphRAG 时，优先新增 worker 子目录和独立 sandbox。
