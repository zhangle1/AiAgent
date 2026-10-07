# Archify 本地渲染验证

## 体验入口

在聊天输入框点击「可视化」→「体验 Archify 交互示例」。也可直接访问 `/archify/aiagent-demo.html` 查看独立画布。

固定的 AI 工作台概念架构包含 7 个节点、6 条连线和 1 个分组；支持节点关系探索、上下游高亮、有向路径追踪、缩放、主题切换、SVG/PNG 导出及 JSON 源文件下载。返回图形选择后保留原选择，不发送消息。

这是固定的渲染器可行性验证，示例本身未接入 AI 生成。正式聊天现在可选择独立的「交互架构图」，接入结构化生成、代码范围选择、对话修改与消息历史；使用 React/SVG 画布，并非动态调用此 Archify CLI。详见 [聊天交互架构图](chat-interactive-architecture.md)。其他图形继续使用 Mermaid。

## 来源与构建

复用 [Archify](https://github.com/tt-a1i/archify) 的 architecture 渲染器，固定提交 `73aaa0696e8f72c232ea710e6fa94fd953f3e773`，MIT 许可证。许可证、第三方声明和字体 OFL 随产物保存在 `front/public/archify/`。

渲染器是 Node CLI。本次用它将固定 JSON 编译为自包含 HTML，再由 React 弹窗嵌入。运行无需 Node 渲染进程、在线服务或 CDN；上游源码仅在重新生成示例时需要。

在 `front/` 执行，上游 checkout 必须干净且位于上述提交：

```powershell
node scripts/build-archify-demo.mjs <archify-checkout>
```

源文件为 `front/public/archify/aiagent-demo.architecture.json`。生成脚本附加 CSP 和版权声明，不修改上游渲染器。上游更新不会自动进入本项目。

## 隔离边界

iframe 只允许脚本和下载，不允许同源访问、弹窗或顶层导航。HTML 的 CSP 禁止联网和表单提交；字体、脚本、样式均内嵌。公开目录仅保存固定概念数据，不能存放用户资料或带权限的产物。Office、项目文档和原型 iframe 策略保持不变。

## 验证方式

```powershell
npx tsc --noEmit
node --test tests/chat-visualization.test.mjs
# 独立工具目录需要 playwright、esbuild 及 Playwright Chromium
$env:ARCHIFY_TEST_TOOLS = '<tools-dir>'
node tests/archify-demo.browser.mjs
```

浏览器验证挂载实际 VisualizationDialog，在独立本地服务中检查交互、SVG/PNG 下载、返回后选择保留、移动端弹窗边界，以及无外部请求和浏览器错误。无需登录或读取用户数据；不等同于完整登录后的应用端到端验证。
