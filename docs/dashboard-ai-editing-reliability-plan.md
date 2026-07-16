# 看板 AI 精准改码解决方案

## 1. 结论

目前看板 AI “找不到文件、改错文件、说已修改但页面没有对应变化”的根因，不是模型不会写 React 或 ECharts，而是系统没有把**当前看板工作区的结构、入口文件和可验证的修改目标**作为一等上下文提供给 AI。

现有能力只有“按模型给出的相对路径读一个文件 / 写一个完整文件”。AI 在收到“加一个柱状图”后，需要自行猜测项目入口是 `index.html`、`src/main.jsx`、`src/App.tsx` 还是其他文件，也无法知道图表数据与样式分别在哪个文件；猜错后，后端仍允许向新路径写文件，结果就是“工具成功，但用户页面没有变化”。

目标应调整为：**AI 先理解唯一的当前工作区，再定位真实渲染入口和关联文件，执行带版本保护的最小修改，最后给出机器可验证的修改证据并刷新预览。**

---

## 2. 当前实现与证据

### 2.1 当前运行模型

```text
应用列表创建
  └─ 复制模板到 <代码库>/.aiagent-dashboard/<appId>
       或直接把已选代码库根目录作为工作区

看板工作台
  ├─ 左侧：GET /tree 读取文件树
  ├─ 中间：GET /file、PUT /file 编辑文件
  └─ 右侧：向通用 Agent 发送 dashboard_application_id + 当前文件文本提示

通用 Agent
  ├─ read_dashboard_file(path)
  └─ write_dashboard_file(path, 完整内容)
```

当前模板工作区实际是 Vite/React 结构：`index.html` → `src/main.jsx` → `src/styles.css`，但这一事实没有以结构化形式传给 Agent。

### 2.2 已确认的根因

| 编号 | 现状证据 | 导致的行为 | 严重度 |
| --- | --- | --- | --- |
| R1 | AI 工具只有按路径读取/写入，没有“列目录 / 搜索当前工作区 / 识别入口”的工具。见 `ToolDispatch.cs:119-184`。 | AI 必须猜 `src/main.jsx`、`App.tsx` 或 `index.html`，所以会报找不到文件或读错文件。 | P0 |
| R2 | 看板请求在带有 `dashboard_application_id` 时，前端仍传递 `code_repository_names: [app.repository_name]`。见 `DashboardStudio.tsx:414-415`。 | 通用 `code_search` 可面向关联代码库，而真正工作区常在 `.aiagent-dashboard/<id>`；模型同时看到两套范围，容易定位到错误目录。 | P0 |
| R3 | `write_dashboard_file` 可以创建任意允许扩展名的新路径；并不要求该路径已由当前轮读取。见 `DashboardApplicationAppService.cs:202-216,270-288`。 | 例如 AI 猜测 `src/App.jsx`，会成功创建新文件，但 Vite 入口仍使用 `src/main.jsx`，页面自然不变。 | P0 |
| R4 | Prompt 仅用自然语言要求“先读后写”。见 `ChatPromptBuilder.cs:79-81`。 | 这是软约束；模型可跳过读取、读错文件或因为工具轮数耗尽而直接回答。 | P0 |
| R5 | Agent 最多 5 轮工具循环。见 `AgentLoop.cs:65`。 | 对“定位入口 → 查图表 → 查样式 → 改 JS → 改 CSS → 验证”的常见任务不够，且没有针对看板的固定流程。 | P1 |
| R6 | 文件读取返回完整 JSON 字符串，写入结果只返回 `dashboard_file_written:<path>`。见 `ToolDispatch.cs:147,184`。 | UI 无法展示结构化的“修改前后哈希、Diff 摘要、关联入口、验证结果”；用户只能相信一句成功提示。 | P1 |
| R7 | 前端仅根据 `dashboard_file_written:` 正则刷新树和当前文件。见 `DashboardStudio.tsx:422-426`。 | 不能可靠刷新多个关联文件，不能处理版本冲突，也没有强制刷新 iframe/预览资源。 | P1 |
| R8 | 运行时已能动态挑选端口，但只接受预置模板中完全等于 `vite` 的 dev 脚本。见 `DashboardRuntimeService.cs:32-38,95-102`。 | 对关联的真实项目，改码与预览之间可能无法形成一致闭环。 | P2 |

### 2.3 现有设计中已经可复用的部分

- 工作区路径隔离、目录穿越保护、忽略 `node_modules/.git/dist` 等目录已经存在；应继续复用。
- 文件写入使用临时文件后原子替换，方向正确。
- 编辑器已能在收到写入标记后刷新树和当前打开文件。
- 运行时已具备动态端口分配与日志采集，适合扩展为“修改后预览刷新”。

---

## 3. 目标设计

### 3.1 不变原则

1. **唯一工作区**：看板 AI 只能操作当前 `dashboard_application_id` 指向的目录；关联 Git 仓库仅用于 Git 拉取、提交和推送，不再作为同一轮 AI 检索/写入的第二作用域。
2. **先发现、后读取、再修改、最后验证**：这是服务端执行规则，不依赖模型自觉。
3. **默认不创建文件**：用户说“加柱状图”时，只允许修改已识别的入口或关联文件；新文件必须由 AI 显式说明原因并单独确认。
4. **每次写入必须可证明**：返回变更文件、前后版本、Diff 摘要、是否被入口引用、预览刷新结果。
5. **以左侧资源管理器为真相源**：右侧 AI 不出现额外的“写入目标代码库”选择框；当前工作区与当前文件由左侧选择决定。

### 3.2 新的工作区快照（Workspace Snapshot）

打开看板或文件树变化时，后端生成并缓存一个结构化快照：

```json
{
  "application_id": "...",
  "root": ".../.aiagent-dashboard/...",
  "revision": "tree-sha256",
  "framework": "vite-react",
  "package_manager": "npm",
  "entrypoints": ["index.html", "src/main.jsx"],
  "style_files": ["src/styles.css"],
  "source_files": ["src/main.jsx"],
  "imports": {
    "src/main.jsx": ["src/styles.css"]
  },
  "visual_targets": [
    { "file": "src/main.jsx", "symbol": "App", "role": "dashboard-page" },
    { "file": "src/main.jsx", "symbol": "EChart", "role": "chart-wrapper" }
  ]
}
```

初版不需要引入复杂编译器：可由 `package.json`、`index.html` 的 script、JS/TS import、`createRoot`/`ReactDOM`、Vue/Svelte 常见入口做静态识别。识别失败时明确返回“未知框架”，要求 AI 先检索，而不是猜路径。

### 3.3 面向看板的专用工具

替代“仅有读/写文件”的工具集合：

| 工具 | 作用 | 服务端约束 |
| --- | --- | --- |
| `inspect_dashboard_workspace` | 返回快照、文件树摘要、入口和版本号。 | 每次新会话和快照过期时强制先调用。 |
| `search_dashboard_code` | 仅在当前工作区搜索文本/符号，返回文件、行号与小片段。 | 不使用关联代码库索引；忽略生成目录。 |
| `read_dashboard_file` | 读取指定文件，可按行区间读取，返回内容、编码、SHA-256。 | 路径必须来自快照或搜索结果。 |
| `apply_dashboard_patch` | 对已读文件应用统一 diff 或结构化替换。 | 必须携带 `expected_sha256`；默认只允许已存在文件。 |
| `validate_dashboard_change` | 检查入口可达、import 未断、目标标记存在、文件版本匹配。 | 返回明确 pass/fail 与原因；不做 npm build。 |
| `refresh_dashboard_preview` | 通知前端 reload iframe，或返回运行时需重启的原因。 | 与实际运行时状态关联。 |

`write_dashboard_file` 可以保留为内部兼容接口，但不再直接暴露给看板 Agent。新工具仍复用现有路径隔离和原子写入逻辑。

### 3.4 受控的 Agent 流程

以“给当前看板加一个柱状图”为例：

```text
1. inspect_dashboard_workspace
   → 确认 Vite + React，入口 src/main.jsx，样式 src/styles.css
2. search_dashboard_code(query: "EChart|series|option")
   → 定位 EChart 组件与现有折线 series
3. read_dashboard_file(src/main.jsx, expected revision)
   → 读取完整的真实页面文件
4. apply_dashboard_patch(src/main.jsx, expected_sha256)
   → 在同一 option 的 series 内追加 bar series
5. validate_dashboard_change(changed_files: [src/main.jsx])
   → 确认入口仍 import 该文件、series 中存在 type: bar
6. refresh_dashboard_preview
   → iframe 重新加载；右侧显示 Diff 和验证通过
```

当任务涉及视觉样式时，流程自动补充读取/修改 `src/styles.css`。当模型尝试写入 `src/App.jsx` 而该文件不在快照中时，服务端拒绝并返回“该文件不是已识别的现有目标；请先搜索或显式请求创建文件”。

---

## 4. 前后端改造清单

### 阶段 A：消除作用域混乱（P0）

1. `DashboardStudio.tsx` 在看板聊天请求中不再传 `code_repository_names`。
2. `AgentContext` 增加 `DashboardWorkspaceSnapshot` 或 `DashboardRevision`，不再把“当前文件”拼到用户自然语言中。
3. `ChatPromptBuilder` 在检测到 `dashboard_application_id` 时采用独立的 Dashboard tool policy：第一轮只能 `inspect_dashboard_workspace`；禁止 `code_search` 指向关联代码库。
4. 在 UI 顶部固定显示：`当前工作区 / 当前文件 / 工作区版本`，所有 Agent 事件都显示这些字段。

**验收**：关联仓库根目录与 `.aiagent-dashboard/<id>` 同时存在时，AI 所有搜索结果与写入结果仍只属于当前应用工作区。

### 阶段 B：建立可靠定位（P0）

1. 在 `DashboardApplicationWorkspace` 中新增快照构建器，输出目录、入口、import 图、可编辑文件、文件 SHA-256。
2. 新增 `search_dashboard_code`，在当前工作区直接搜索；不得依赖“代码库索引已建立”。
3. 增加模板 manifest：`.aiagent-template.json` 声明 `entrypoints`、`styles`、`framework`、`preview_command`；静态分析结果作为兜底。
4. 新建应用时写入应用级 `.aiagent-workspace.json`，记录模板版本、入口、最近一次快照版本；绑定 Git 后随工作区一起迁移。

**验收**：对现有 Vite 模板，AI 在第一次工具调用就能得到 `src/main.jsx`、`src/styles.css`；对未知项目，AI 能得到可搜索文件树且不会猜测入口。

### 阶段 C：把“整文件覆盖”改成“版本化补丁”（P0）

1. 新增 `apply_dashboard_patch(path, expected_sha256, patch)`；服务端在写入前验证 SHA-256。
2. 默认拒绝创建新文件；新增 `create_dashboard_file`，仅在用户明确提出“新增文件/组件”且父目录已存在时使用。
3. 补丁应用前后都生成摘要：新增/删除行数、关键 symbol、文件版本。
4. 写入后自动重建受影响的快照，并以结构化 `dashboard_change_applied` 事件返回，而不是单一字符串标记。

**验收**：编辑器或其他 AI 在读取后修改了同一文件时，旧版本补丁被拒绝，用户看到“文件已变化，请重新读取”，不会静默覆盖。

### 阶段 D：验证、预览和可见性闭环（P1）

1. `validate_dashboard_change` 至少检查：入口链路、import 路径、JSON/JSX 基础语法、任务关键词/结构断言。
2. 针对 ECharts 建立轻量断言：如“柱状图”要求实际 `series` 包含 `type: "bar"`；不是只检查回答文字。
3. 前端以结构化事件刷新所有已改文件、失效 iframe 并展示可折叠 Diff。
4. 预览运行中时自动 reload；若依赖/入口变化需要重启，提示用户并给出明确按钮。
5. 右侧工具日志区分“定位、读取、补丁、验证、预览”，隐藏完整原始文件 JSON，只保留可展开的安全片段。

**验收**：右侧显示“`src/main.jsx` 已修改 → 校验通过 → 预览已刷新”；点击变更可打开精确文件与行号。

### 阶段 E：运行时适配（P2）

1. 保留动态端口扫描；端口、进程 PID、启动命令均在工作区状态中记录。
2. 将当前“仅允许 `scripts.dev === vite`”改为模板 manifest 白名单命令；非模板项目显示可配置的受限预览策略，不执行模型提供的任意 shell。
3. 依赖安装失败时返回 Node/npm 绝对路径、工作目录和可复制命令；禁止把安装失败伪装成代码修改失败。

---

## 5. 交互方案

### 5.1 右侧对话区

不再显示独立的“写入目标代码库”下拉框。改为只读上下文条：

```text
工作区：test 看板
入口：src/main.jsx
当前文件：src/main.jsx  (SHA 8af3…)
预览：运行中 · 4310
```

每条 AI 操作以卡片呈现：

```text
✓ 已定位：src/main.jsx / App / EChart
✓ 已修改：src/main.jsx（+12 -1）
✓ 已验证：series 中存在柱状图 type: "bar"
✓ 已刷新：预览 4310
```

### 5.2 失败处理

| 失败 | 用户看到的结果 | 系统下一步 |
| --- | --- | --- |
| 路径不存在 | “`src/App.jsx` 不在当前工作区；已返回入口列表。” | 自动回到搜索工具，不写新文件。 |
| 文件版本冲突 | “文件在读取后已被修改，未覆盖。” | 重新读取并重新规划。 |
| 找不到图表 | “在 `src/main.jsx` 中找到 ECharts wrapper，但没有 series；需要确认是新增独立图表还是扩展现有图表。” | 请求澄清，不假装成功。 |
| 静态验证失败 | “补丁已回滚，import/JSX 校验失败。” | 不刷新预览，保留错误和候选 diff。 |
| 预览失败 | “代码写入且静态校验通过；预览启动失败，原因是 npm/端口。” | 独立显示运行时诊断。 |

---

## 6. 验收用例

| 用例 | 操作 | 通过标准 |
| --- | --- | --- |
| U1 | “把当日计划从 800 改为 1260” | 只修改真实指标所在文件；重新读取后数值为 1260；预览同步。 |
| U2 | “在现有趋势图增加柱状图” | AI 定位实际 ECharts `series`；写入 `type: "bar"`；验证工具返回通过。 |
| U3 | “把图表间距调大” | AI 同时识别组件文件和 CSS 文件；只修改必要文件；Diff 可见。 |
| U4 | 关联代码库根目录下也存在另一个 `src/main.jsx` | AI 绝不搜索或写入根目录错误文件，只操作 `.aiagent-dashboard/<id>`。 |
| U5 | 打开文件后人工修改，再让 AI 写 | 因 SHA 不匹配而拒绝旧补丁，不覆盖人工内容。 |
| U6 | AI 猜测不存在的 `src/App.jsx` | 服务端拒绝，提示先搜索；不产生幽灵文件。 |
| U7 | 添加新组件 | AI 先解释新增文件计划；确认后创建文件、更新入口 import，并验证引用链。 |
| U8 | 预览端口已占用 | 运行时使用其他可用端口；代码修改结果与运行时状态分开显示。 |

---

## 7. 建议实施顺序

1. **先做 A + B**：清除双作用域，补齐工作区快照与搜索。这一步已经能显著减少“找不到/找错文件”。
2. **再做 C**：引入 SHA 与补丁写入，解决“写成功但写错位置/覆盖旧内容”。
3. **随后做 D**：让“加柱状图”以真实结构断言验收，而不是依赖 AI 口头说明。
4. **最后做 E**：扩大到非预置模板的受控预览。

建议先以当前 `react-echarts-operations` 模板实现 U1/U2/U3/U5/U6 的端到端验收，再扩展到其他框架。这样可以先把“改当前看板一定改到真实渲染文件”做成可靠能力，而不是先追求支持所有项目。

## 8. 本次审查范围

本方案基于当前源码的只读审查，重点包括：

- `Services/DashboardApp/DashboardApplicationAppService.cs`
- `Services/Chat/Agentic/ToolDispatch.cs`
- `Services/Chat/Agentic/AgentLoop.cs`
- `Services/Chat/Prompting/ChatPromptBuilder.cs`
- `Services/DashboardApp/DashboardRuntimeService.cs`
- `front/components/dashboard-applications/DashboardStudio.tsx`
- 当前已登记看板工作区及 `react-echarts-operations` 模板

本次未修改任何业务实现、未启动服务、未执行编译或构建。
