# 聊天交互架构图

在聊天点击「可视化」→「交互架构图」。可直接使用当前对话和已选项目资料，也可在弹窗选择代码库，逐级浏览根目录，选择目录或 `.sln/.slnx`、工程、JSON 文件。确认配置后，在聊天输入分析要求并发送；仅选择代码范围也可直接发送默认分析请求。多个解决方案时，提示词明确限定选定入口与必要依赖，不允许静默换用其他入口。

回复中的 `aiagent-architecture` JSON 自动成为交互图。支持节点详情、模型标注来源、直接关系列表、上游/下游高亮、有向最短路径追踪、缩放、滚动浏览、大图及 JSON/SVG/PNG 导出。节点可通过 Tab 聚焦、Enter/空格选择，大图支持 Escape 关闭。继续对话要求修改图时，模型返回完整新图；历史版本保留在各自的聊天消息中，重新打开会话即可再次渲染。画布当前选择和缩放不持久化。

## 实现选择

Archify 上游渲染器为 Node CLI，会在构建阶段输出完整 HTML。为避免给现有 .NET 部署增加运行时进程，正式聊天图采用独立 React/SVG 画布；并非动态调用 Archify。原来的 Archify 固定示例、许可证和隔离策略保留不变。正式图没有主题切换、任意节点拖动和 Archify 全套自动布线功能；当前按分组分列排布，复杂大图宜拆分。

生成使用既有模型与普通聊天提交链路，不修改 ChatStreamProvider、后端 API 或数据库。选择的范围和输出格式要求随用户消息保存；重试沿用请求快照。不同项目/会话会清空范围，异步目录响应在卸载/切换目录后失效。取消弹窗不提交草稿选择。

## 数据契约与边界

```json
{
  "version": 1,
  "title": "系统架构",
  "nodes": [
    {"id": "web", "label": "前端", "group": "应用层", "description": "负责交互", "source": "对话方案，尚未核实代码"},
    {"id": "api", "label": "接口", "group": "服务层", "description": "处理请求", "source": "对话方案，尚未核实代码"}
  ],
  "edges": [{"from": "web", "to": "api", "label": "请求"}]
}
```

校验在 `front/lib/chat-architecture.ts`：最多 100000 字符，1–40 个节点、100 条边，限定字段长度，拒绝重复/非法 ID 和不存在的节点引用。仅投影允许字段，文本通过 React 和 SVG 文本节点转义，不接受模型提供的坐标、脚本或 HTML。无效/尚未完整的流式 JSON 显示提示和受限源数据，允许用户继续对话修正。导出仅从当前受控 SVG 或已校验 JSON 产生 Blob；PNG 最大边不超过 4096 像素。无外部字体、图片或网络依赖。

图形权限继承聊天消息权限，不另建公开产物链接、不写入 public 或 localStorage。`source` 是模型标注的文本，UI 明示其性质，不跳转任意来源 URL，不宣称系统已核实。选定路径只是分析范围提示，读取仍使用原来的服务端授权；不具备代码读取能力的模型应说明无法读取，不能假造证据。仅分析提示不替代 Agent 工具本身的权限设置。

## 验证

```powershell
npx tsc --noEmit --incremental false
node --test tests/chat-architecture.test.mjs tests/chat-visualization.test.mjs tests/chat-packaging.test.mjs
$env:ARCHIFY_TEST_TOOLS = '<已有 playwright 和 esbuild 的工具目录>'
node tests/chat-architecture.browser.mjs
```

单元测试覆盖数据边界、引用、循环图路径、不可信字段与路径范围。浏览器测试挂载实际 MarkdownMessage、图形画布和配置弹窗，使用模拟目录 API；覆盖范围选择和取消、节点交互、三种导出、无效流式数据、移动端大图与零外部请求。没有调用真实模型或登录后端，不代表生产环境端到端模型生成验收。
