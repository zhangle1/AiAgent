# Agent Platform Runtime

- [架构规划](./agent-platform-runtime-design.md)
- [交互原型](./agent-platform-runtime-prototype.html)

本专题明确废弃的是原先高度耦合的 `AgentLoop` 实现，而不是放弃自有 .NET Agent Runtime。新平台参考 Codex 的运行时分层与协议语义，在 .NET 中重建可控的单 Agent 执行内核和多 Agent 编排中枢；Codex app-server v2 是首个外部 Runtime Adapter 与兼容性参照，不是唯一内核。设计同时覆盖 Skill、Plugin、MCP、定时任务、多 Agent 协作与无限工作画布。
