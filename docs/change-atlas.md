# 研发工作台：变化地图

## 使用

从「研发工作台 → 变化地图」进入，也可访问 `/change-atlas?project=<项目ID>`。项目列表沿用已有项目授权。选择项目、全部或单个仓库、近 3/7/14/30 天，以及当前分支或可读取引用。

页面按需生成真实 Git 快照，刷新按钮重新采集。默认读取本地已提交版本；勾选「同步远端后读取」后，在独立 bare 缓存中读取 origin。使用当前登录用户自己的 Git 账号；凭据未配置时沿用 Git 可用的认证环境。远端读取失败时显式提示并展示本地版本，不声称已经同步。

本版没有定时采集、Webhook 或推送通知联动。切换项目、仓库、分支或范围会生成新快照；模块、人员和日内筛选在同一快照上联动。

| 视角 | 本版展示 |
| --- | --- |
| 业务地图 | 按目录自动分组的模块、提交频次、改动文件与署名数；AI 可提出业务名称候选 |
| 代码结构 | 固定 head SHA 的目录包含关系、文件，以及当前过滤范围的活动高亮 |
| 变化回放 | 按 Git committed_at 的 UTC 日期统计活动，支持累计范围与单日 |
| 协作交集 | Git 署名 × 模块的去重提交数，以及非合并提交中的共同修改统计 |

结构覆盖为 `directory`，不包含 Roslyn/TypeScript 语义调用图。目录包含、历史共改和 AI 推测不能显示成已经解析或运行验证的依赖。交付视角切换到业务地图，展示模块目录树及历史同改；开发视角切换到代码结构，展示可折叠文件树。两者均可继续访问四个视图，并影响 AI 的表达粒度。

页面固定使用白色主题，不随系统深色模式切换。目录按实际相对路径逐级分组，重复文件名保留各自父目录。文件树最多展示 500 个匹配文件，优先选入改动文件，搜索可定位其余文件。独立改动关系画布展示最近 8 次提交与最多 16 个所选模块文件，可缩放、滚动，并通过文件或连线打开指定差异；完整已载入证据仍在列表中。蓝线表示 Git 改动，红色虚线表示 head 中已不存在的文件，不表示运行依赖。

交互设计参考开源 [React Arborist](https://github.com/jameskerr/react-arborist) 的目录展开与选择方式，以及 [React Flow](https://github.com/xyflow/xyflow) 的节点连线与画布缩放方式。当前用原生 details、React 与受控 SVG 实现，无新增第三方运行依赖。

## 事实口径

- 读取已提交 Git 对象，排除工作目录未提交内容，不 checkout、pull、reset、commit 或 push。
- 同一提交可以涉及多个模块。模块次数不可相加当作项目总提交数；文件和人员分别去重。
- 提交活动包含合并提交，其文件差异相对第一父提交；共同修改统计排除合并提交。
- 重命名暂按删除与新增展示。二进制文件没有文本增删行数。
- 提交活动不同于最终净变化。修改再回滚仍保留活动记录。
- 日期/人员过滤只影响活动高亮，结构仍是快照 head。日期滑块不表示已还原过去当天的远端状态。
- Git 署名未绑定平台身份，不据此推断岗位、AI 生成归属或操作人。
- 推送、合入、部署和测试结果分别判断。本版没有关联构建/测试执行记录。

## 服务与存储

后端位于 `backed/Services/ChangeAtlas/`，前端位于 `front/components/change-atlas/`，HTTP 与类型集中在 `front/lib/change-atlas-{api,types}.ts`。`front/lib/change-atlas.ts` 负责确定性的过滤与聚合。

`ChangeAtlasGitReader` 按仓库缓存路径串行采集，在私有 bare 缓存 fetch 本地对象及可选远端 refs。每个快照通过 `refs/atlas/snapshots/<id>` 固定 head，后续强制推送仍能读取已保存对象。来源目录必须等于 Git 根目录，避免登记子目录时读取整个上级仓库。

Git 通过 ArgumentList 传参，读取进程有超时、取消和输出预算。stdout/stderr 并发排空，异常不向浏览器输出远端地址、凭据或服务器目录。证据路径来自快照成员，采用 literal pathspec；浏览器不能指定任意 SHA、服务器路径或 Git 命令。

快照和解读存于服务端配置的 DataPath 下 `change-atlas/owners/<用户哈希>/`，JSON 使用临时文件和原子替换；不新增数据库表。仓库缓存、分析目录和结果按用户隔离，快照正文额外记录 OwnerId。项目、仓库归属在快照读取、差异读取及 AI 发布时重新校验。

快照访问有效期为 24 小时；它不表示底层 bare 缓存和历史文件已自动回收。本版暂未实现自动清理，需要部署方把该目录纳入容量管理。仓库移除或改归其他项目后，旧快照停止提供访问。

读取预算：最多 12 个仓库，每库最近 120 次提交、每次最多 500 个改动文件、目录初始 5000 个文件，改动且仍存在于 head 的文件额外补入。项目累计最多 20000 个唯一文件、30000 个文件变更条目；过预算的仓库跳过并提示单独选择。单个快照 JSON 上限 32 MB。截断和失败均提供可见提示，计数只覆盖已载入部分。

## AI

用户显式点击「解读当前模块」才调用模型。模型和推理等级读取既有 Codex 模型策略，由 `ICodexChatService` 最终解析与执行。模型不可用或返回失败时显示错误，地图保持可用。

每次分析使用独立会话 ID、当前登录用户 RuntimeUserId、独立分析工作目录和 `read-only` 模式，不恢复或修改用户现有聊天。只读描述和提示词不构成额外的操作系统读取隔离；执行能力沿用现有 Codex 通道的沙箱和账号配置。

同一用户与快照复用运行连接标识，分析会话 ID 每次独立，避免每次点击新建运行租约累积达到会话上限。回复兼容聊天服务的 Content／Answer 字段，仍严格校验 JSON 和证据引用。页面通过 SSE 接收固定阶段提示、10 秒保活及最终结果，保留聊天服务已脱敏的诊断错误；取消请求会传递到模型，过滤或视角切换后丢弃旧事件。阶段提示不等于进度百分比，模型中间文本不会提前发布。

宿主准备最多 12 条提交元数据、6 份差异；每份最多 7000 字符，携带截断标记、过滤条件和明确的未知信息。文件配置、环境文件及密钥类文件正文被阻止读取。原始差异预览最多 24000 字符；没有可用正文证据时不会调用模型。

模型返回结构化 JSON：summary、module_label、claims、checks、unknowns。claim 的 kind 限 code_observation/inference/suggestion，每条必须引用宿主提供的 evidence ID。结构或引用校验失败时不发布、不缓存；正文由 React 文本节点展示，代码差异使用 pre，不执行模型 HTML。

同一用户、快照和完整请求参数缓存成功解读；失败不缓存。业务名称仅作为当前快照的 AI 候选，不自动成为人工确认的长期模块映射。模型输出的代码解释仍需结合引用核对，不标成静态解析器证明的业务正确性。

前端在项目、范围、模块、人员、模型或阅读视角改变时终止旧请求，并校验作用域与请求序号，避免旧差异/回答覆盖新选择。

## HTTP

所有路由先登录，再校验项目访问；快照和文件仅以不透明 ID 引用。

```text
POST /api/v1/projects/{projectId}/change-atlas/snapshots
GET  /api/v1/projects/{projectId}/change-atlas/snapshots/{snapshotId}
GET  /api/v1/projects/{projectId}/change-atlas/snapshots/{snapshotId}/evidence?commit_id=&file_id=
POST /api/v1/projects/{projectId}/change-atlas/snapshots/{snapshotId}/analysis
POST /api/v1/projects/{projectId}/change-atlas/snapshots/{snapshotId}/analysis/stream
```

创建请求：`{ repository_id?, days, ref: "HEAD" | 服务端引用列表中的完整ref, refresh_remote }`。分析请求：`{ module_id, author_id?, until?: "yyyy-MM-dd", daily, perspective: "delivery" | "developer", question, model_id?, reasoning_effort? }`。

流式分析沿用相同请求体，身份与快照访问错误在开流前返回 HTTP 状态。开流后使用 `status`（message）、`heartbeat`、`result`（AtlasAnalysis）、`error`（message）事件。仅 `result` 代表解读成功；断流、取消、错误或引用校验失败不得视为完成，旧 JSON 接口继续保留。

未登录 401，无权访问 403，参数无效 400，快照不存在或过期 404，采集/模型/缓存状态错误 409。取消沿用 HTTP 请求取消令牌。

## 验证与后续

2026-11-03 界面与解读链路优化：后端变化地图专项 10 项、前端统计／树／事件流 5 项、TypeScript 检查及浏览器交互检查通过。后端使用合成 Git 仓库、隔离 SQLite 和模型替身，新增事件流成功／引用失败／401、运行连接复用与 Answer 兼容检查；浏览器覆盖白色主题、目录折叠、视角联动、关系图差异定位、旧响应隔离及 320–1440px 布局。未调用真实模型，未验证用户原始失败请求或生产代理环境。评审截图为合成数据，见 [页面截图](change-atlas-review.png)。

`backed.tests/ChangeAtlasTests.cs` 使用私有临时仓库、合成远端、隔离 SQLite 与模型替身，覆盖脏工作区保留、版本固定、外部推送和强制推送、身份/项目/仓库校验、敏感正文阻止、模型请求、缓存、引用拒绝及权限撤销。没有连接客户真实仓库或消耗真实模型调用。

`front/tests/change-atlas.test.mjs` 验证统计去重、日期/人员过滤与非合并共同修改。`front/tests/change-atlas.browser.mjs` 在真实前端页面拦截合成 API，验证工作台入口、四视图、证据转义、模型请求、迟到响应隔离及桌面/窄屏布局。

复现前端浏览器检查：先启动已构建前端，设置 `ATLAS_TEST_URL`；Playwright 在外部工具目录时设置 `AIAGENT_BROWSER_TOOLS` 指向包含其 package.json 的目录；可选 `ATLAS_TEST_BROWSER_CHANNEL=chrome` 和 `ATLAS_SCREENSHOT`。

2026-11-02 本轮验证：前后端编译、TypeScript 检查、新增后端 8 项、前端聚合 3 项与浏览器交互检查通过。全量后端 296 项中 286 项通过、10 项失败，集中于现有 UsageStatisticsServiceTests 的 SQLite 夹具以及 CodexDiagnosticsTests / ChatRuntimeTests 的 Windows CLI/进程执行；这些文件本轮未修改。排除上述三个测试类后，其余 259 项全部通过，不能据此声称全量测试通过。

后续阶段可增加有授权的后台采集与清理、稳定业务映射编辑、语义结构和双版本净变化、平台操作身份关联，以及持续工具式 AI 追问。当前功能不执行代码维护或 Git 交付。
