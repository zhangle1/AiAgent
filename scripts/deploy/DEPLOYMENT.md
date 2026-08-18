# AiAgent Windows Server 部署说明

部署只保留两个入口：源码机器使用一个**打包脚本**，服务器使用一个**运行脚本**。前端和后端会被一起生成到同一个 ZIP 包中。

## 一、在源码机器生成单个部署包

在 `AiAgent` 源码目录执行。构建机器需要 .NET 9 SDK、Node.js 和 npm：

```powershell
./scripts/deploy/Build-ServerPackage.ps1 `
  -BackendApiUrl "http://127.0.0.1:5000" `
  -FrontendPort 3782
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
  -BackendApiUrl "http://127.0.0.1:5000" `
  -FrontendPort 3782 `
  -IncludePythonWorkers
```

未携带 Python Worker 的部署包仍可正常使用不依赖 Python 的功能；启用知识库 Python/RAG 或 OCR 前，请改用带 `-IncludePythonWorkers` 的完整包，或在服务器上自行部署并配置对应 Worker。

## 二、服务器配置与运行

1. 解压 `AiAgent-server.zip` 到固定目录，例如 `D:\AiAgent`。
2. 将 `backend\appsettings.Production.json.example` 复制为 `backend\appsettings.Production.json`，再填写 SQL Server、`Cors:Origins`、代码库根目录、模型和 Python/RAG 配置。首次部署还必须通过受控配置设置 `Authentication:InitialAdministratorUsername` 和 `Authentication:InitialAdministratorPassword`；若用环境变量，对应名称为 `Authentication__InitialAdministratorUsername` 和 `Authentication__InitialAdministratorPassword`。初始化口令至少 16 个字符，不要把真实口令写入脚本、命令行、日志或文档。
3. 首次启动成功并确认可以登录后，立即从配置文件和环境变量/Secret Store 中移除 `Authentication:InitialAdministratorPassword`。已有管理员或用户的密码不会在升级、重启时被重置；如果没有可用管理员且初始化配置缺失，后端会以启动失败方式保护部署。
4. 除非使用 `-SelfContained` 打包，否则服务器还需要安装 .NET 9 Runtime。新生成的部署包会自带前端运行所需的 `front\node.exe`，服务器无需另行安装 Node.js。

在解压目录根部执行唯一运行脚本：

```powershell
cd D:\AiAgent
./Run-AiAgent.ps1 -Action Start -BackendPort 5000 -FrontendPort 3782
```

前端 API 默认自动转发到同机的 `http://127.0.0.1:<BackendPort>`。跨机器部署时，编辑 `front\api-proxy.json`，填写后端完整地址，再重启服务：

```json
{
  "backendApiUrl": "http://192.168.1.20:5000"
}
```

留空则继续自动跟随 `-BackendPort`。也可以不改文件，启动时临时指定：

```powershell
./Run-AiAgent.ps1 -Action Restart -BackendPort 5000 -FrontendPort 3782 -BackendApiUrl "http://192.168.1.20:5000"
```

修改前端端口时无需重打包：

```powershell
./Run-AiAgent.ps1 -Action Restart -BackendPort 5000 -FrontendPort 3782
```

停止服务：

```powershell
./Run-AiAgent.ps1 -Action Stop
```

前后端监听全部 IPv4 网卡（`0.0.0.0`）；日志与 PID 文件保存在 `runtime\`。

## 三、防火墙与公网访问

按需开放前端、后端端口。AiAgent 代码运行功能会动态使用前端 `4300-4399`、后端 `5100-5199`；只有远程用户必须直连这些临时服务时才开放对应端口段。公网 IP 或域名还需设置防火墙和 NAT / 反向代理映射。

```powershell
New-NetFirewallRule -DisplayName "AiAgent 前端" -Direction Inbound -Protocol TCP -LocalPort 3782 -Action Allow
New-NetFirewallRule -DisplayName "AiAgent 后端" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
```

不要提交或传播 `appsettings.Production.json`，它可能包含数据库密码、Token 或 API Key。

## 四、项目 Markdown 上传目录的最小 NTFS 权限

项目根目录必须位于 `CodeRepository:AllowedRoots` 的某一个根目录中。AiAgent 不会接受浏览器传来的服务器路径。默认 Markdown 上传和项目 Agent 索引使用每个项目根目录下固定的：

```text
uploads\aiagent-documents\
```

首次部署时，由管理员在每个已注册项目中预先创建这个目录，然后仅给运行 AiAgent 后端服务的 Windows 账户授予该目录及其子项的“修改 (Modify)”权限。不要把“完全控制”或项目根目录的写权限授予服务账户。

```powershell
$projectRoot = "E:\Projects\ExampleProject"
$serviceAccount = "DOMAIN\AiAgentSvc" # 或 NT SERVICE\AiAgentBackend
$uploadDirectory = Join-Path $projectRoot "uploads\aiagent-documents"
New-Item -ItemType Directory -Force -Path $uploadDirectory
$grant = "{0}:(OI)(CI)M" -f $serviceAccount
icacls $uploadDirectory /grant $grant
```

服务账户仍需对项目根目录和已注册仓库拥有读取/列出权限，以便生成索引和读取代码；默认情况下只有上述专用目录需要写入权限。项目文档页面也允许把**当前项目已注册仓库内已选中的目录**作为交付上传或新建文件夹目标：若启用此流程，管理员必须仅对需要交付的仓库目录（建议单独的 `docs\delivery` 子目录）及其子项授予服务账户“修改 (Modify)”权限，不能向整个 `AllowedRoots`、任意项目根目录或未注册仓库授予写权限。所有目标仍会规范化为仓库相对路径，拒绝越界、符号链接/重解析点及 `.git`、`node_modules`、构建目录。

验证步骤：以服务账户启动后端，在有权限的账号下打开“项目文档”，选中 `uploads/aiagent-documents` 或已注册仓库中的交付目录，上传一个 UTF-8 `.md` 文件并新建一个子目录，然后下载该文档、引用到聊天，最后点击“刷新索引”。上传、下载、引用和索引均应成功。若返回写入失败，请先核对项目根目录/仓库根目录位于 `CodeRepository:AllowedRoots`，再运行 `icacls` 确认服务账户在**所选目标目录**拥有 `(M)`，并检查该目录及其父目录没有重解析点。
