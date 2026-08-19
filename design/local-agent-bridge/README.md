# Local Agent Bridge 设计产物

本目录描述“服务器侧消费 Codex CLI，开发者本机执行工作区操作”的产品原型与技术方案。

- `local-agent-bridge-prototype.html`：可直接用浏览器打开的交互式单页原型，内置模拟数据，不连接真实设备或 Codex。
- `local-agent-bridge-design.md`：产品边界、架构、协议、安全模型、核心数据结构和分阶段实施方案。

原型中的绑定、授权、审批、断开等操作只改变浏览器内的演示状态。生产实现必须由本地 Agent 主动建立出站加密连接，并在本机再次校验工作区边界和操作权限。
