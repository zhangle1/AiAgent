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
  }
}
```

`Rag` 负责索引与检索，独立的 `Ocr` worker 仍按需使用 Python。PDF 主解析已改为 C# PdfPig + PDFium，由 `Services/Parsing/DocumentParsingService` 执行；不调用本目录的旧 `parsing/` 脚本，也不自动回退到 Python。旧脚本仅保留为历史参考，发布时排除，无需安装其 requirements。

PDF 使用 `DocumentParsing:TimeoutSeconds`（默认 300 秒）和可选 `DocumentParsing:AllowedRoots`；未配置允许根时兼容旧 `PythonWorkers:AllowedRoots`，知识库实际存储根始终单独纳入受控范围。解释器、worker 路径及 PythonWorkers 超时不再作用于 PDF。部署与限制见 `docs/knowledge-document-parsing.md`。
