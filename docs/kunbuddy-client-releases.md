# 坤伴安装包发布与客户端更新

管理员进入「设置 → 管理 → 坤伴客户端发布」（`/settings/admin/client-releases`），填写版本、操作系统、架构和更新说明，上传安装包。客户端登录平台后立即检查版本，此后每 30 分钟检查一次，也可在「设置 → 关于」手动检查。

Windows 使用本项目生成的 NSIS `KunBuddy-Setup-版本.exe`。macOS 使用 DMG，或包含 `KunBuddy.app/Contents/Info.plist` 的应用 ZIP。当前发布支持 Windows/macOS × x64/arm64，包大小上限 2 GiB。服务器检查格式和 SHA-256，不执行安装包，也不推断包内版本、架构或签名；发布者须确保填写值与实际构建一致。

版本使用 `major.minor.patch`，例如 `0.20.2`。同平台、架构和版本不可覆盖，撤回后也须递增版本。客户端只提示大于当前已安装版本的更新，不自动降级。撤回会阻止后续客户端下载，打开已下载安装包前也会重新检查是否仍为当前发布版本。

## 打包与手动验证

在原生 Windows x64 构建机准备 Node >=22.19、pnpm 和 Rust/MSVC 工具链，在 `client` 目录执行：

```powershell
pnpm install --frozen-lockfile
node scripts/set-kunbuddy-version.mjs 0.20.2 --dry-run
node scripts/set-kunbuddy-version.mjs 0.20.2
pnpm --filter @kunbuddy/desktop dist:win
```

版本脚本同步工作区 package.json、Cargo 工作区/lock 和共享运行时版本，保留原文件编码、BOM、换行；发布日志仍需人工编写。产物位于 `client/apps/desktop/release/`。上传 `KunBuddy-Setup-0.20.2.exe`，不要上传便携版或 unpacked 目录中的应用 EXE。安装包内的应用版本是客户端检查版本的依据。

首次测试先手动安装包含本功能的低版本，登录测试平台；再把源码版本递增至 `0.20.3`，重新打包，在后台发布该包。低版本客户端点击「检查更新 → 下载安装包 → 打开安装包并退出」，在安装向导中替换原安装。重新启动确认版本已变为 `0.20.3`。保持应用 ID 和安装位置，可继续使用原本地数据。老客户端没有本功能时，第一次仍须手动安装。

macOS 在对应架构的 Mac 上执行相同的安装依赖和版本命令，再执行：

```sh
pnpm --filter @kunbuddy/desktop dist:mac
```

Apple Silicon 在 arm64 环境构建，Intel 在 x64 环境构建，确保 Electron 与原生 Rust host 架构一致。上传对应 DMG 或应用 ZIP；下载后点击打开，应用退出，再将 KunBuddy.app 拖入 Applications 完成替换。对外分发需配置自己的 Apple 签名和公证，Windows 可信签名也使用发布者证书。当前 Windows 环境的自测不包含 Mac 原生安装、签名或公证验收。

## 切换 URL

更新地址由主进程中已登录的平台账号确定，渲染层不能传入任意安装包下载地址。切换平台 URL 或账号时取消旧请求、清空旧版本/下载状态和安装权限，按新平台重新检查；即使两台平台版本号相同，也不会复用旧平台的已下载包。退出或登录过期同样清除权限，临时网络错误保留有效账号。

下载使用当前账号令牌、固定同源 API 和禁止重定向的请求；先校验字节数和 SHA-256，再发布可安装状态，安装前再次计算哈希。用户显式下载安装，开发模式可验证下载但不能执行安装包。主窗口才有更新 IPC 权限。此通道不读取 Pi Desktop 的更新源；主程序使用平台通道时不采用旧的 `KUNBUDDY_UPDATE_FEED_URL`。

## 服务端部署

无需新增数据库表。安装包和原子发布目录存储在后端 `DataPath/client-releases`；相对 DataPath 按后端内容根目录解析，未设置时使用 `data/client-releases`。部署时保留此目录并给服务账号读写权限，备份须包含 catalog.json 和版本子目录。单后端实例使用进程内发布锁，多实例须共用一个发布服务入口。

管理员列表/上传/撤回使用网页管理员登录；客户端最新版本及下载使用独立 `kunbuddy-client` 会话，普通网页令牌不能代替。接口为 `/api/v1/admin/client-releases/list`、`upload`、`{id}/withdraw`、`{id}/file`，客户端为 `/api/v1/kunbuddy/releases/latest` 和 `{id}/file`。

上传接口的单个文件参数直接声明 `IFormFile file`，由 ASP.NET Core 推断文件绑定；不要在该参数上添加 `[FromForm]`，否则当前 Swashbuckle 会在生成文档时抛出异常，使 Settings 和 v1 文档返回 500。其他字符串元数据仍使用 `[FromForm]`。`SwaggerDocumentTests` 用隔离后端验证两组文档返回 200，并检查 multipart 中的二进制文件和元数据字段；更新修复后需重新编译并重启正在访问的后端。

应用允许约 2 GiB 的上传体。前端安装包上传使用专用 Node route 流式转发，绕过 Next.js rewrite 默认 10 MiB 克隆上限，仍由后端鉴权及校验。反向代理也须相应配置大小及上传超时。例如 nginx 的 `client_max_body_size 2049m;`；IIS 的 requestFiltering/requestLimits `maxAllowedContentLength` 至少 2148532224。不要把真实安装包、数据目录、证书或构建输出提交 Git。
