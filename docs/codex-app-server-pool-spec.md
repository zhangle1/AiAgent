# Codex app-server 浏览器租约规格

## 问题

每次聊天结束后立即终止 `codex app-server --stdio` 会让短问题也承担完整 CLI 与 MCP 冷启动。常驻单位不再是项目目录，而是经过认证的用户与浏览器页签运行时 ID 的组合。

## 目标

- 同一“用户 + 浏览器页签”只保留一个已初始化 CLI，页面心跳持续时不重启它。
- 每个 CLI 同时只处理一个 turn；stdout 只由该 CLI 的单一读取器消费，避免 JSONL 协议串台。
- 同一用户最多 3 个活动 Codex 会话/浏览器租约；第 4 个请求在前端和后端都会被拒绝。
- 保持当前 `approvalPolicy=never`、`dangerFullAccess`、MCP 配置与每轮新建 thread 的行为。

## 生命周期

```text
用户 ID + 页签运行时 ID -> RuntimeLease(max=1 CLI)
CLI: 进程启动 -> initialize(仅一次) -> idle
页面心跳(25 秒) -> 续租；超过 90 秒无心跳且无运行回合 -> 回收 CLI
请求: rent CLI -> thread/start -> turn/start -> turn/completed -> return CLI
异常/取消/进程退出: discard CLI -> 同一页签下次心跳或请求重建
```

- 第一次使用页签仍需冷启动；页面选中 Codex 和项目后即通过心跳预热，通常发生在发送消息之前。
- `Codex:RuntimeLeaseSeconds` 默认 `90`，取值限制为 30 到 600 秒；`Codex:MaxSessionsPerUser` 最大固定为 3。
- 运行时 ID 是 `sessionStorage` 中的随机不透明 ID，不读取设备硬件、Canvas 或浏览器隐私指纹。
- 进程不并行读取 stdout；用户需要并行运行时使用不同页签租约，受 3 个活动会话上限约束。

## 终态与页面

`done` 表示 Agent 回答生成完成；`completed` 表示会话和使用量已落盘。当前页收到 `completed` 后重读该会话历史并清除对应终态内存流，因此只保留一条已落盘回答，不显示重复的“未修改文件”卡片。
