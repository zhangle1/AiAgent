# KunBuddy 桌面客户端

完整移植到 AiAgent/client 的 Electron + React + Rust 客户端，保留 Agent 运行时、插件 SDK、开发工具、示例和文档。

PowerShell 启动：

```powershell
cd E:\项目\know-why\AiAgent\client
pnpm install --frozen-lockfile
.\Run-KunBuddy.ps1
```

使用独立应用标识 com.knowwhy.kunbuddy、开发端口 4382、.kunbuddy/.kunbuddy-dev 数据目录和 KUNBUDDY_* 环境变量，避免与 Pi Desktop 互相接管或共写数据库。不会自动迁入原应用的用户配置。

自定义插件从 examples/、packages/plugin-sdk 和 packages/plugin-devkit 开始，使用 @kunbuddy/* 工作区依赖及 kunbuddy-plugin 命令。原有插件格式与权限边界保留。

未登录时默认显示登录首页，填写平台 URL、用户名和密码即可登录并自动同步“坤伴订阅”模型；有效登录在重启后恢复，检测到过期会返回首页重新登录，保留本地会话。设置 → 模型 → 坤伴订阅与首页共用登录链路。平台密钥留在服务端，本机保存加密的短期凭据及不含密码的地址／账号提示。该模式不包含支付。详见 [登录首页](../docs/kunbuddy-account-home.md)。

坤伴订阅聊天会自动将原始记录同步到平台“人员 → 我的工作区 → 会话”，一会话一份 Markdown，后续消息追加；启动及重新登录时补传已有订阅会话。上传不调用模型、不自动解析或编译，也不上传附件文件。订阅编辑弹窗可查看待上传数量并重试。普通 API Key 模式不自动上传。详见 [会话同步](../docs/kunbuddy-session-sync.md)。

管理员可在 AiAgent 后台上传 Windows EXE、Mac DMG 或应用 ZIP。客户端检查当前登录平台的新版本，用户下载校验后打开安装包；切换 URL 即替换来源。打包、发布与双版本测试见 [客户端发布](../docs/kunbuddy-client-releases.md)。不会安装上游 Pi Desktop 更新包。完整构建命令见 README.md，原项目及 LGPL 许可证说明见 UPSTREAM.md。
