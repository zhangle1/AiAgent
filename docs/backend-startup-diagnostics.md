# 后端启动耗时定位

## 启动日志

启动保留 `Database:CodeFirst` 原有行为和数据库失败即终止的约定。日志仅记录阶段名称和毫秒数，不记录连接串、账号、令牌和 SQL 参数。

| 日志阶段 | 检查内容 |
| --- | --- |
| Backend startup services built | 配置、服务注册和容器构建累计时间 |
| chat-compatibility | 聊天历史字段与偏好表的兼容迁移 |
| entity-tables | 主要实体的 CodeFirst 表结构检查 |
| canvas-tables | 工作画布实体表结构检查 |
| additive-columns | 既有表的显式补列迁移 |
| indexes | 索引存在性检查和必要的创建 |
| provider-seed | 默认模型供应商数据同步 |
| Backend startup administrator check | 初始管理员可用性检查 |
| Backend startup ready | Hosted Service 启动完成、HTTP 服务就绪时的累计时间 |

`Database startup stage … started` 后迟迟没有对应完成日志，可定位当前数据库阶段。管理员检查完成到 `ready` 之间的延迟属于后续宿主启动阶段，应再核对后台服务的首次查询、网络连接和调试器状态。编译耗时与上述运行时耗时分开判断。

## 索引检查优化

原 `EnsureIndexes` 为 60 个索引定义串行发送 46 段 SQL，即使索引已经存在也有 46 次往返。现将原 SQL 按原顺序拼为一个批次执行，减少网络延迟和反复连接／命令开销。CodeFirst 的 51 个实体、原索引名称、列、过滤条件、唯一性和存在性判断保持不变；补列迁移仍在索引前完成。

没有关闭 CodeFirst、跳过 schema 校验或并行使用同一数据库连接。任何索引语句失败仍向启动入口传播，不会宣布服务已就绪。

## 验证和边界

`backed.tests/StartupSchemaTests.cs` 直接执行实际索引初始化方法，用隔离的 SqlSugar 接口探针记录调用。在每次命令注入 5ms 模拟往返延迟时，修复前记录 46 次调用、约 725ms，修复后 1 次调用、约 7ms。这是合成测试结果，不是生产数据库启动时长；实际结果还取决于连接延迟、表结构变更和索引是否需要创建。

46 段原始索引 SQL 及 51 个 CodeFirst 实体已与 Git 基线逐项核对内容和顺序。相关聊天技能目录、清理 HTTP 和启动测试共 13 项通过，编译无新增错误。

本轮未读取或更改真实连接配置，未对真实数据库执行初始化。工具自动审批拒绝了后台启动后端进程的操作，返回 `blocked by policy`，因此尚未测得实际后端启动时间，不能据此确认整套服务的慢启动原因已完全消除。需在 Visual Studio 重新启动已编译的后端，检查上表阶段日志以及实际监听端口。
