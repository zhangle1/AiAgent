# Agent Work Canvas 设计稿

本目录是 AiAgent「多会话工作画布」的设计产物，不包含生产代码或接口变更。

- `agent-work-canvas.html`：可直接在浏览器打开的交互式视觉原型。
- `agent-work-canvas-design.md`：产品、交互、架构与分阶段实施说明。

原型采用虚拟数据，展示多会话 Codex 任务在无限画布上的运行、依赖、告警与汇总方式。实际实施必须继续复用现有聊天流、会话权限和 Codex 运行时租约，不允许浏览器直接控制 CLI。
