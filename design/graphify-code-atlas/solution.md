# Graphify × AiAgent 代码地图融合方案

日期：2026-11-10 · 状态：设计提案，尚未接入产品。

配套：[交互 HTML 原型](prototype.html)。原型全部为合成数据，可离线打开，不请求服务、不安装 Graphify。文件名称取自项目，示例连线、计数、版本、代码与结论均不代表已验证的项目事实。

## 1. 建议与价值

建议将 Graphify 作为代码结构的可替换解析适配器，接到现有变化地图的 Git 快照之下。界面保留业务地图、代码结构、变化回放、协作交集，增加依赖探索与影响分析。第一阶段先做单仓库、固定提交、文件级的局部关系查询。

当前地图回答“最近哪里在改”；融合后可以回答“这个改动周围有哪些结构关系，哪些地方值得检查”。例如选中聊天相关文件，先查看它的导入、引用和可解析调用，再反向追查使用方，最后结合本轮 Git 改动形成核查清单。这里只给出候选影响范围，不宣称运行一定受影响。

不建议把 Graphify 自带 graph.html 嵌进产品：需要保留 AiAgent 的项目授权、快照、证据、过滤与 AI 生命周期；外部生成页面也不应获得工作台同源权限。应读取经过校验的图数据，以现有 React/SVG 体系展示。

## 2. 当前代码核查

本次核查对象：仓库中的以下源码与文档，未运行完整项目、未构建真实依赖图。

| 位置 | 已有能力 | 融合点 |
| --- | --- | --- |
| `front/components/change-atlas/ChangeAtlasPage.tsx` | 四视图、模块/人员/时间筛选、模型选择、异步取消与作用域序号 | 新增视图和关系查询状态，保留旧交互 |
| `front/components/change-atlas/AtlasChangeGraph.tsx` | 提交与文件的改动画布 | 保持 Git 改动语义；另建依赖画布 |
| `front/lib/change-atlas-types.ts` | `AtlasSnapshot.coverage` 固定为 `directory`，含仓库 head、文件、提交和证据 | 增加独立 graph manifest，不把旧字段直接改为“语义完整” |
| `backed/Services/ChangeAtlas/ChangeAtlasService.cs` | 项目访问校验、快照组装、仓库归属复查、差异证据 | 图任务入口沿用同一授权链 |
| `backed/Services/ChangeAtlas/ChangeAtlasGitReader.cs` | bare 缓存及固定 Git 对象读取 | 从固定 SHA 导出受限源码快照 |
| `backed/Services/ChangeAtlas/ChangeAtlasStore.cs` | 按用户隔离快照 | 保存不可变图版本与任务状态 |
| `backed/Services/ChangeAtlas/ChangeAtlasAnalysisService.cs` | 显式 AI 解读、独立会话、证据校验 | 新的图证据类型与局部查询上下文 |
| `docs/change-atlas.md` | 目录覆盖、24 小时快照访问期、读取预算及事实边界 | 必须继续遵守，并在实现时更新 |

现有共同修改统计属于历史相关性；现有提交→文件连线属于 Git 改动。两者均不能升级为调用依赖。日期/人员过滤只改变活动高亮，结构仍属于固定 head。

## 3. Graphify 能用什么，需要验证什么

官方 README 描述了本地 AST 解析、可查询图、关系来源标签和图格式版本元数据；文档语义处理涉及模型。这里将这些作为上游声明，不视为 AiAgent 已完成验证。第一阶段仅使用本地代码解析；具体 headless 构建命令、C#/TSX 支持深度和输出 schema，必须先用锁定版本源码及合成夹具核实。

上游资料（查阅于 2026-11-10）：

- [官方 README 与功能边界](https://github.com/Graphify-Labs/graphify#readme)
- [上游架构源码文档](https://github.com/Graphify-Labs/graphify/blob/v8/ARCHITECTURE.md)
- [官方 CLI 文档入口](https://docs.graphify.com/cli)
- [许可证文件](https://github.com/Graphify-Labs/graphify/blob/v8/LICENSE) 与 [MIT 许可文件](https://github.com/Graphify-Labs/graphify/blob/v8/LICENSE-MIT)：试点时核实所锁定发行版及传递依赖的许可证和分发要求。

不采用上游营销指标作为本项目性能承诺。Graphify 的 `INFERRED` 可能来自静态名称解析，不能统一等同于 LLM 猜测；`EXTRACTED` 也不意味着编译器绑定或运行验证。

## 4. 用户场景与界面

### 4.1 依赖探索

入口沿用 `/change-atlas?project=…`，上方显示项目、仓库、固定 SHA、图状态及覆盖情况。默认文件级、1 跳、仅静态提取；点击文件或边，在右侧显示关系方向、类型、提取器、文件行范围与证据。支持名称/相对路径搜索和展开 2 跳；范围超过预算明确截断，不自动倾倒全库关系。

左侧保留真实目录树，聚类仅作为可选着色，不替代真实路径，也不自动当作业务边界。中间展示局部子图，右侧展示证据与未知项。使用静态提取、解析推断、AI 候选三类标签；Git 改动用节点活动标记表达，避免一根线表达两件事。

### 4.2 影响分析

从选中文件或当前筛选后的改动集合出发，沿“调用者→被调用者”“导入方→被导入方”的反方向查使用方；继承、文档关联分别定义传播规则，不能无差别遍历所有边。展示直接关联、间接关联、未覆盖区域与待验证清单。路径含推断边时整条路径降级标识，不显示伪精确的影响概率。

删除文件无法在 head 图中分析：第一阶段提示“当前版本不存在，需基线图”；后续用 base/head 双图处理删除、重命名和关系消失。人员/日期筛选只选择种子和高亮，不改结构版本。未修改的调用者也可成为关联结果。

### 4.3 AI 解读

显式点击后，将有限子图、固定 SHA 的源码片段、Git 差异、过滤快照及缺口交给既有模型通道。输出分别包含代码观察、推断、检查建议，并引用宿主签发的 evidence ID。校验版本、节点、边与证据引用后才发布；模型失败地图仍可用。自然语言问题的图检索先用确定性路径/符号检索，LLM 查询规划放到后续阶段。

## 5. 服务架构与数据流

```text
既有 ChangeAtlasSnapshot（每仓库 head SHA）
  → GraphBuildService：授权复核、任务去重、预算
  → SnapshotExporter：从 Git 对象导出允许的源码文件
  → ICodeGraphExtractor / GraphifyAdapter：锁定的本地解析器
  → GraphNormalizer：路径、schema、ID、证据和边类型校验
  → GraphStore：不可变图版本 + 原子 manifest
  → GraphQueryService：子图、路径、反向候选影响
  → React/SVG 局部画布 + 证据侧栏
  → 既有 AnalysisService（显式、带证据的 AI 解读）
```

新增服务建议放在 `backed/Services/ChangeAtlas/CodeGraph/`。Controller 仅处理协议。CLI 用 `ProcessStartInfo.ArgumentList`，固定可执行文件及参数白名单，设置取消、超时、输出和内存预算，并回收进程树；不接受浏览器命令或路径。不要执行 `graphify install`，不要注册 hook 或修改 Agent 指令。

导出器从允许的源码 blob 构建隔离目录，拒绝符号链接、子模块与路径越界，忽略配置密钥、二进制、构建产物和生成代码。拒绝超大 blob，禁止执行仓库脚本、构建步骤及插件。只读提示不等于系统隔离：解析进程还需独立低权限账号/容器和禁止外连的运行环境。解析完成再检查访问权，未通过则不发布。

## 6. 建议数据契约

以下为新增设计字段，不是当前 API。

```ts
type GraphManifest = {
  id: string; snapshot_id: string; repository_id: number; head_sha: string;
  schema_version: 'aiagent-codegraph-1'; extractor_version: string;
  upstream_schema_version: string; configuration_hash: string;
  status: 'queued' | 'building' | 'ready' | 'partial' | 'failed' | 'cancelled';
  coverage: { included_files: number; parsed_files: number;
    skipped_files: number; unsupported_files: number; failed_files: number };
  warnings: string[];
};
type GraphNode = {
  id: string; file_id: string; kind: 'file' | 'symbol' | 'document';
  name: string; language?: string; start_line?: number; end_line?: number;
};
type GraphEdge = {
  id: string; source: string; target: string;
  kind: 'imports' | 'calls' | 'inherits' | 'references' | 'documents';
  provenance: 'static_extracted' | 'static_resolved' | 'llm_inferred';
  upstream_tag?: string; extractor: string; evidence_ids: string[];
};
```

证据记录保存 `graph_id/repository_id/head_sha/blob_sha/path/line range/content_hash`，浏览器仅以不透明 ID 获取经授权片段；路径必须是固定 Git tree 的成员。节点按仓库+版本+路径+符号签名区分，同名文件/符号不得合并；跨版本身份另行映射。原始图只作受控内部产物，拒绝未知 schema 和无效端点，不透传任意 JSON/HTML。

图缓存键包含 owner、project、repository、head、解析器版本、配置哈希、schema；AI 缓存另加 graph ID、种子、关系过滤、方向、跳数、模型与问题。文件图聚合保留支持该边的符号边引用，不将多条调用压成无法解释的一条线。任务配置冻结，重试不悄悄升级解析器。

## 7. HTTP 草案与兼容

统一前缀：`/api/v1/projects/{projectId}/change-atlas/snapshots/{snapshotId}`。

| 方法与路径 | 用途 |
| --- | --- |
| `POST /graphs` | `{repository_id}` 创建/复用固定 head 图，返回 202 与 job ID |
| `GET /graph-jobs/{jobId}` | 读取真实任务阶段、覆盖缺口和错误 |
| `POST /graph-jobs/{jobId}/cancel` | 幂等取消；终态后不重复启动 |
| `GET /graphs/{graphId}` | 图 manifest |
| `POST /graphs/{graphId}/query` | `{seed_ids, direction, kinds, provenance, depth, limit}` |
| `POST /graphs/{graphId}/impact` | 显式种子 + 当前活动筛选，输出候选路径 |
| `GET /graphs/{graphId}/evidence/{evidenceId}` | 固定版本源码片段 |

现有 analysis 接口保持原契约；新图分析建议独立 `POST /graphs/{graphId}/analysis/stream`，复用原 SSE 投影与校验组件。每次接口重查身份、快照、图与仓库归属。401/403/404 复用原语义；图版本不匹配返回 409；过大查询返回可理解的 400。图失败保留原四视图与 Git 证据。

快照 24 小时过期后，图和证据也不再通过该快照访问；后台缓存可按配额保留，不能绕过过期判断。切换项目/仓库/快照/视图终止旧查询和 SSE，比较请求序号与 scope key，禁止迟到结果覆盖。

前端新增 `AtlasDependencyView`、`AtlasImpactView`、`AtlasGraphEvidence` 与 `useAtlasGraph`；HTTP/类型继续集中到 lib 的领域文件。旧 `coverage: directory` 保持原义，新图覆盖独立显示。

## 8. 第一阶段范围与验证门槛

| 阶段 | 交付 | 进入下一阶段的条件 |
| --- | --- | --- |
| P0 适配试验 | 锁定版本、schema 夹具、C#/TS/TSX 合成仓库、许可核查；与 Roslyn/TS Compiler API 小样对照 | 明确导入/调用/动态引用的覆盖与误连情况；确认可无模型无网络构建，否则替换适配器 |
| P1 文件级 MVP | 单仓库、1–2 跳、静态关系、反向候选影响、版本证据、任务状态与取消 | 授权、版本固定、取消和原地图回归通过 |
| P2 精细化 | 符号展开、基线/目标双图、删除/重命名分析、增量缓存 | 增量结果与同版本全量结果一致；过期边可清除 |
| P3 可选语义 | 显式选择文档语义、有限图问答、业务聚类候选 | 服务端模型配置与预算、证据和隔离验收完成 |

建议初始预算（设计值，待测）：每任务 5,000 个允许文件、单文件 1 MiB、总源码 100 MiB、5 分钟超时；每次画布 60 节点/120 边、最多 2 跳，超限提供继续缩小范围的入口。边数限制要在遍历期间生效，不先全图展开再截屏。显示“已解析/允许文件数”及不支持/跳过/失败清单，不能只给一个容易误导的百分比。

验收夹具包括：C# 接口/依赖注入/重载、TS 别名/动态 import、TSX、同名符号、循环依赖、删除文件、不支持语言、软链接、超大图、恶意 schema、解析超时、双用户与项目撤权、切换后的迟到响应。动态路由、依赖注入和反射无法确认时必须保留未知；跨前后端 HTTP 路由匹配作为后续专门适配器，不因 URL 同名就连线。

性能记录冷构建耗时、峰值内存、缓存命中耗时、子图查询 p95、证据可定位率与人工标注边的准确率/召回率；先记录基线再制定门槛，不预先承诺提速比例。

## 9. 原型评审路径

打开 `prototype.html`，依次尝试：依赖探索/影响分析切换 → 左侧选择文件 → 静态提取/解析推断过滤 → 1/2 跳切换 → 点击连线查看证据 → 输入文件名搜索 → 模拟图不可用并恢复 → 生成示例解读。

原型目的：验证“从改动出发看局部关系，再按证据核查”的产品流程。不会读取真实仓库、触发模型或生成真实影响结论。本次交付仅设计文件，不修改现有业务实现，不安装依赖，不提交或推送。
