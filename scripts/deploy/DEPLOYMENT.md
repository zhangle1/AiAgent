# AiAgent Windows Server 部署说明

部署只保留两个入口：源码机器使用一个**打包脚本**，服务器使用一个**运行脚本**。前端和后端会被一起生成到同一个 ZIP 包中。

## 一、在源码机器生成单个部署包

在 `AiAgent` 源码目录执行。构建机器需要 .NET 9 SDK、Node.js 和 npm：

```powershell
./scripts/deploy/Build-ServerPackage.ps1 `
  -BackendApiUrl "http://127.0.0.1:8081" `
  -FrontendPort 8080
```

输出文件为：`artifacts/server-package/AiAgent-server.zip`。ZIP 内包含：

```text
AiAgent-server/
├─ backend/                 # 已发布的 .NET 后端
├─ front/                   # 已构建的 Next.js standalone 前端
├─ Run-AiAgent.ps1          # 服务器唯一运行/停止/重启脚本
└─ DEPLOYMENT.md
```

`BackendApiUrl` 是构建时的默认前端 API 转发目标。部署包启动后会由 `front\api-proxy.json` 覆盖，因此更改服务端口不再需要重新打包。若目标服务器没有 .NET 9 Runtime，请在构建命令后追加 `-SelfContained`。

默认部署包不包含 Python Worker、RAG 脚本及本地 Python 虚拟环境，以避免 PaddleOCR 等依赖显著增大 ZIP 体积。需要在目标服务器使用知识库 Python/RAG 或第三方 Profile 图片 OCR 时，显式追加 `-IncludePythonWorkers`：

```powershell
./scripts/deploy/Build-ServerPackage.ps1 `
  -BackendApiUrl "http://127.0.0.1:8081" `
  -FrontendPort 8080 `
  -IncludePythonWorkers
```

未携带 Python Worker 的部署包仍可正常使用不依赖 Python 的功能；启用知识库 Python/RAG 或 OCR 前，请改用带 `-IncludePythonWorkers` 的完整包，或在服务器上自行部署并配置对应 Worker。

## 二、服务器配置与运行

1. 解压 `AiAgent-server.zip` 到固定目录，例如 `D:\AiAgent`。
2. 将 `backend\appsettings.Production.json.example` 复制为 `backend\appsettings.Production.json`，再填写 SQL Server、`Cors:Origins`、代码库根目录、模型和 Python/RAG 配置。
3. 除非使用 `-SelfContained` 打包，否则服务器还需要安装 .NET 9 Runtime。新生成的部署包会自带前端运行所需的 `front\node.exe`，服务器无需另行安装 Node.js。

在解压目录根部执行唯一运行脚本：

```powershell
cd D:\AiAgent
./Run-AiAgent.ps1 -Action Start -BackendPort 8081 -FrontendPort 8080
```

前端 API 默认自动转发到同机的 `http://127.0.0.1:<BackendPort>`。跨机器部署时，编辑 `front\api-proxy.json`，填写后端完整地址，再重启服务：

```json
{
  "backendApiUrl": "http://192.168.1.20:8081"
}
```

留空则继续自动跟随 `-BackendPort`。也可以不改文件，启动时临时指定：

```powershell
./Run-AiAgent.ps1 -Action Restart -BackendPort 8081 -FrontendPort 8080 -BackendApiUrl "http://192.168.1.20:8081"
```

修改前端端口时无需重打包：

```powershell
./Run-AiAgent.ps1 -Action Restart -BackendPort 8081 -FrontendPort 8080
```

停止服务：

```powershell
./Run-AiAgent.ps1 -Action Stop
```

前后端监听全部 IPv4 网卡（`0.0.0.0`）；日志与 PID 文件保存在 `runtime\`。

## 三、防火墙与公网访问

按需开放前端、后端端口。AiAgent 代码运行功能会动态使用前端 `4300-4399`、后端 `5100-5199`；只有远程用户必须直连这些临时服务时才开放对应端口段。公网 IP 或域名还需设置防火墙和 NAT / 反向代理映射。

```powershell
New-NetFirewallRule -DisplayName "AiAgent 前端" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow
New-NetFirewallRule -DisplayName "AiAgent 后端" -Direction Inbound -Protocol TCP -LocalPort 8081 -Action Allow
```

不要提交或传播 `appsettings.Production.json`，它可能包含数据库密码、Token 或 API Key。
