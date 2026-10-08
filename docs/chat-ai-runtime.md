# 聊天 AI 多工程运行

## 使用流程

1. 在聊天选择项目，打开“项目程序运行 → 配置 AI 运行”。
2. 勾选多个代码库，可在各目录多选 `.csproj`、解决方案、JSON/YAML/config 文件作为 AI 分析入口。不选文件时探测已选代码库。
3. 填写期望页面路径、前后端联调要求以及 5–240 分钟空闲回收时间，填入聊天并发送。
4. AI 检查实际文件、依赖、入口、脚本、API 地址、代理与 CORS，明确各端口，原子写入清单。
5. 后端每 3 秒检查已准备请求的清单。先按顺序启动后端并等待 HTTP 就绪，再启动前端；失败回收已经启动的同组服务，写入真实结果供 AI 读取。
6. 从聊天卡片或右下角浮窗打开应用实际端口，优先使用已就绪前端及配置的页面路径。历史 `/runtime-test` 链接在就绪后自动跳转；等待、失败、停止或找不到请求时显示状态，不根据清单猜测运行端口。
7. 浮窗合并当前项目 AI 运行请求和运行管理器的进程，按 run_id 去重，展示 PID、端口、状态。AI 分组提供关闭整组，其余托管进程提供“结束对应进程”，沿用后端进程树终止及项目授权校验。任一查询失败显示错误，不伪装成空列表。
8. 需要切换服务、页面路径或续期时，打开“管理 / 续期”（`/runtime-test?…&mode=manage`）。直接端口访问不会续期，仍受配置的空闲超时约束。

## 协议与边界

接口基路径：`/api/v1/code-runtime/projects/{projectId}/chat-runs`。

停止确认：Windows 使用每次运行独立的 Job Object，依赖安装及实际启动进程均纳入托管。启动器退出后继续保留子进程控制，宿主释放 Job 时回收其中的子进程。仅当托管进程退出且配置端口不再监听，才标记 stopped。停止未完成时保留 stopping 运行记录、整组显示关闭失败，单进程及整组停止接口返回 409；浮窗与管理页允许重试。端口被其他进程复用时只报告未释放，不终止无归属证据的进程。非 Windows 保留原进程树终止方式并增加端口确认，本次真实进程回归在 Windows 执行。

升级边界：需要更新前后端并重启后端，新运行才能使用新的托管方式。旧后端已丢失的进程记录不能通过重启恢复；旧残留进程需要在服务所在机器核实 PID 和项目归属后处理。

| 操作 | 方法与后缀 | 内容 |
| --- | --- | --- |
| 准备请求 | POST | `selections: [{repository_name, entry_paths}]`, `idle_minutes` |
| 状态列表 | GET | 请求状态、清单位置、进程、页面配置 |
| 测试窗口续期 | POST `/{requestId}/visit` | 当前项目整组续期 |
| 关闭整组 | POST `/{requestId}/stop` | 取消启动并关闭本组进程树 |

全部接口验证登录和项目访问权；原有运行状态、日志及停止接口同步增加访问过滤。每个项目最多八组等待/启动/运行请求，每份清单最多 12 个目标、64 KiB。等待清单一小时超时，终态记录创建超过 24 小时后清理。运行状态驻留内存，正常后端关闭清理本组进程，重启不会重放磁盘清单；强制杀死宿主或系统崩溃后的孤儿进程恢复不在当前实现内。

清单文件为首个所选仓库内 `artifacts/aiagent-runs/<requestId>.json`，结果文件为同目录 `<requestId>.result.json`。AI 必须先写 `.tmp` 再重命名，且将目录加入 Git 忽略。清单示例：

```json
{
  "targets": [
    {"repository_name":"api","entry_path":"src/Api.csproj","role":"backend","preferred_port":5101,"health_path":"/health","page_path":"/swagger","environment":{}},
    {"repository_name":"web","entry_path":"package.json","role":"frontend","run_script":"dev","preferred_port":4301,"health_path":"/","page_path":"/login","environment":{"NEXT_PUBLIC_API_BASE_URL":"http://example.test:5101"}}
  ]
}
```

角色定义启动次序，入口定义执行器：`package.json` 使用现有 npm script（也支持 Node 后端），可运行的 `.csproj` 使用 `dotnet run`。显式端口必填且范围为 1024–65535，占用即失败，不静默调整联调配置。Next 使用 `--hostname`，Vite/Vue/Angular 使用 `--host`，通用 npm 应用通过 `PORT`/`HOST` 接收端口和监听地址；忽略这些变量的自定义脚本需要 AI 根据项目实际配置调整。不存在的显式 npm script 不回退到其他脚本。

环境覆盖仅允许 `NEXT_PUBLIC_*`、`VITE_*`、`REACT_APP_*`、`API_BASE_URL`、`ASPNETCORE_ENVIRONMENT`、`DOTNET_ENVIRONMENT`；禁止在清单中存储密钥。其他页面配置由 AI 在现有授权范围内处理。路径校验拒绝绝对路径、穿越、符号链接及 junction。清单不能接管现有 PID，也不通过全系统扫描认领其他服务。

每个目标最多等待三分钟，整组启动最多十分钟。指定健康路径要求 2xx；默认 `/` 仅要求小于 500 的 HTTP 响应，表示 HTTP 进程可响应，不表示业务功能验收成功。运行中的某一项进程退出会关闭同组其他项。

## 外网与空闲回收

浏览器使用当前 AiAgent 地址的主机名/IP，加实际运行端口；不采用服务端内网 IP，也不写死部署地址。开发服务使用 HTTP，HTTPS 主站显示直接打开入口，避免宣称存在并未配置的 TLS。防火墙、NAT 端口映射和公网连通需要部署环境验证。

测试管理窗口（`mode=manage`）可见时每十秒续期整组服务；浮窗列表查询不续期。最后续期超过设定时间后关闭整组，前端访问可同时维持后端。关闭或隐藏测试窗口即停止续期。直接访问开发端口的流量不经过 AiAgent，无法统计其真实访问量；当前按测试窗口租约回收，不声称已监测所有 HTTP 请求。多个可见测试窗口任意一个续期即可保留整组。

## 验证

- `npx tsc --noEmit`：前端契约及组件类型检查。
- `node --test tests/chat-runtime.test.mjs tests/chat-packaging.test.mjs`：运行 URL、跨项目链接限制、多工程提示词及打包回归。
- `dotnet test backed.tests/AiAgent.Backend.Tests.csproj --filter FullyQualifiedName~ChatRuntimeTests`：临时 .NET API 和 Node 前端真实启动、顺序、端口冲突、停止、空闲回收、路径和环境限制、不选仓库拒绝。
- `RUNTIME_TEST_TOOLS=<临时工具目录> node tests/chat-runtime.browser.mjs`：真实 React 组件与模拟接口，验证多选、页面配置、浮窗、新窗口、续期、关闭、移动端及项目切换。

验证未调用真实模型生成清单，未验证部署服务器的公网端口；不把模拟接口浏览器测试当作这两项的验收。
