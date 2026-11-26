# 聊天初始化请求取消

## 故障与复现

用户报告登录成功后 Visual Studio 在 `ChatAppService.RequireUser` 停下，异常为 `TaskCanceledException: A task was canceled`。聊天页挂载会请求技能目录；该请求在卸载时通过 AbortController 取消。额度弹窗也会在关闭时取消读取。

在真实 Action 上用合成鉴权依赖复现：请求已取消，或取消恰好发生在鉴权调用期间时，`Skills` 与 `GetCodexQuota` 原实现均从 `RequireUser` 向外传播 TaskCanceledException。外层 HTTP 取消中间件虽可兜底，Action 本身仍缺少对预期取消的就地处理。截图没有请求路径，因此不声称验证了用户当时的完整调用栈。

## 修复范围

两个只读 Action 共用 `ReadBootstrapAsync`：

1. 开始前检查本轮 CancellationToken 和 HttpContext.RequestAborted，已取消则直接返回空正文 499，不做鉴权。
2. 鉴权返回后再次检查，避免取消被误报为未登录，也不继续读取资料或额度。
3. 只捕获当前请求确实取消时的 OperationCanceledException；独立超时、数据库故障等继续传播。
4. 成功响应仍为原始技能数组或额度 DTO；未登录继续拒绝。

不修改 AuthService 的取消契约，不关闭调试器异常设置，不改聊天完成、WebSocket、目标模式、Codex CLI 参数、工具或代码执行流程。此修复不表示所有任意来源的取消异常都应被忽略。

## 验证与生效

`ChatBootstrapCancellationTests` 先复现同类异常，再验证修复；包含已取消、鉴权期间取消、鉴权返回后取消、独立超时、真实故障、未登录与技能数组兼容。并运行原 AuthCancellation、RequestCancellationMiddleware、ChatCommand、CodexQuota 和 SwaggerDocument 回归，共 44 项通过。

测试使用合成身份和隔离输出，不访问真实用户数据库或 Codex 登录状态。更新代码后需要重新编译并重启后端；已经加载旧程序集的 Visual Studio 调试会话不会自动获得修复。
