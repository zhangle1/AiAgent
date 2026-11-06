# 知识库页面进入时的请求取消异常

## 触发与定位

浏览器切换页面、刷新或组件卸载时可以取消尚未完成的请求。知识 Agent 面板在卸载时会取消模型目录请求，这属于正常生命周期。前端开发环境的重复挂载也可能增加此类取消。

原鉴权方法 `AuthService.TryGetUserAsync` 在入口直接执行 `ThrowIfCancellationRequested()`。当 HTTP 请求已被取消时，它同步抛出 `OperationCanceledException`；请求管线没有专门处理客户端取消的边界，Visual Studio 会显示用户未处理异常。该现象发生在查询数据库和知识目录之前，不能据此判定知识存储路径配置错误。

清理弹窗还存在一个异步传播点：`KnowledgeResourceCleanupService.RequireUser` 等待已取消的鉴权任务时产生 `TaskCanceledException`。外层中间件能够终止请求，但清理方法内部缺少取消处理，仍可能触发调试器的“用户未处理”中断。通过真实清理控制器和服务调用复现到同一位置；修复前新增的 7 个用例中 4 个失败。

## 修复边界

- `RequestCancellationMiddleware` 注册在 `UseInject` 及鉴权之前：已取消请求直接结束，不进入鉴权和业务；处理中仅对 `RequestAborted` 已取消的 `OperationCanceledException` 作正常结束处理。
- 响应尚未开始时标记 HTTP 499，不向已断开的客户端写错误正文；已开始的响应保持原状态和已发送内容。
- 鉴权发现传入令牌已取消时返回携带原令牌的取消任务，不再在查找方法入口同步抛出，也不将取消伪装成空用户或 HTTP 401。
- 清理服务在鉴权前检查客户端取消，不再等待已取消的鉴权任务；鉴权等待中及排队／扫描／写入中的取消在服务内部处理。仅在传入令牌和捕获的 `HttpContext.RequestAborted` 均已取消时返回取消结果，由控制器输出无正文的 HTTP 499。正常请求仍返回原有 JSON。
- 清理写入在提交前再次检查取消，取消时回滚整个事务；即使请求上下文访问器已被清空，也使用入口捕获的上下文判断断开。
- 客户端仍连接时的内部超时、真实业务／数据库异常继续传播；原有登录及网页／插件／坤伴会话范围不变。
- 不修改前端正常卸载取消行为，不关闭调试器异常设置，不更改知识目录、真实配置或用户数据。

## 回归验证

新增测试先在旧行为上失败，再验证修复：

- 网页、插件、坤伴三个鉴权入口返回取消任务且不访问数据库。
- 知识目录和模型目录的已取消请求不再进入业务。
- 请求进入之后、鉴权之前取消，以及异步处理期间取消，不再越过请求边界。
- 流式响应已开始后取消，保留已有状态与正文。
- 连续 20 次取消后重新请求，当前请求仍正常返回。
- 内部超时与真实异常仍能上报，成功及未授权响应保持不变。
- 现有坤伴登录会话范围、过期和撤销测试继续通过。
- 清理预览与执行的已取消请求返回 499，不访问数据库且不产生 `TaskCanceledException`；覆盖鉴权开始后取消、上下文清空及等待清理锁时取消。
- 清理写入后的断开回滚软删除；仅业务令牌取消、未登录和真实鉴权错误继续传播。

在 PowerShell 中运行，输出独立放到临时目录，避免覆盖正在调试的后端程序：

```powershell
$testOutput = Join-Path $env:TEMP 'AiAgent-knowledge-cancellation-tests'
dotnet test backed.tests/AiAgent.Backend.Tests.csproj --no-restore --filter 'FullyQualifiedName~RequestCancellationMiddlewareTests|FullyQualifiedName~AuthCancellationTests|FullyQualifiedName~KnowledgeCleanupCancellationTests|FullyQualifiedName~CleanupDisconnectAfterWritingRollsBackTheWholeBatch|FullyQualifiedName~KunBuddySessionsAreScopedHashedRevocableAndExpire' -p:OutputPath="$testOutput\" -p:UseAppHost=false --logger 'console;verbosity=minimal'
```

测试仅使用合成 HTTP 请求及隔离测试数据库，不启动正式应用，不连接真实账号、资料和模型。实际浏览器与调试器交互仍需用重新编译的后端复核；未自动重启或替换用户正在运行的服务。
