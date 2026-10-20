# KunBuddy 桌面客户端

完整移植到 AiAgent/client 的 Electron + React + Rust 客户端，保留 Agent 运行时、插件 SDK、开发工具、示例和文档。

PowerShell 启动：

```powershell
cd E:\项目\know-why\AiAgent\client
pnpm install --frozen-lockfile
.\Run-KunBuddy.ps1
```

使用独立应用标识 com.knowwhy.kunbuddy、开发端口 4382、.kunbuddy/.kunbuddy-dev 数据目录和 KUNBUDDY_* 环境变量，避免与 Pi Desktop 互相接管或共写数据库。不会自动迁入原应用的用户配置。

自定义插件从 examples/、packages/plugin-sdk 和 packages/plugin-devkit 开始，使用 @kunbuddy/* 工作区依赖及 kunbuddy-plugin 命令。原有插件格式与权限边界保留。此次仅完成客户端移植与隔离，尚未接入 AiAgent 服务端登录。

未配置 KunBuddy 专属 HTTPS 更新源前自动更新关闭，不会安装上游 Pi Desktop 更新包。完整构建命令见 README.md，原项目及 LGPL 许可证说明见 UPSTREAM.md。
