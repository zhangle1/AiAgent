# 原型设计工作台实现说明

## 目标

在 AiAgent 首页工作区增加对话驱动的原型设计入口。用户选择项目与代码库后，可以通过自然语言创建或迭代 HTML 原型；产物直接保存在代码库的 `design/` 目录，因此可继续通过 Git 评审、回滚和交付。

## 页面结构

- 左侧：代码库原型文件树，只显示 HTML、Markdown、CSS、JS、SVG。
- 中间：HTML 安全预览与源码视图，支持桌面、平板、手机宽度切换。
- 右侧：项目级 Agent 对话。系统约束 Agent 仅修改 `design/`，完成后刷新目录和当前预览。
- 顶部：项目/代码库选择、分享入口。

## 文件过滤与预览安全

文件树隐藏 `node_modules`、`.git`、`.next`、`bin`、`obj`、`dist`、`build`、`coverage`、`.cache`、`data` 等依赖、构建和运行数据目录。后端仍负责代码库根路径边界检查。

HTML 通过 `iframe srcDoc` 展示，并启用 sandbox。注入的内容安全策略禁止网络连接、外部 frame 和默认资源加载，只允许内联样式/脚本以及 data/blob 图片和媒体。预览不会获得主站同源权限。

## 分享语义

第一阶段分享链接携带项目、代码库和文件的非敏感标识，供已登录团队成员打开代码库当前版本；同时支持下载独立 HTML。链接不包含服务器绝对路径、Token 或源代码内容。公开匿名发布、不可变版本快照和撤销令牌属于后续服务端发布能力，不在本阶段伪装为已完成。

## 参考取舍

- Claude Artifacts：采用“对话产生独立产物、专用预览、可继续迭代与分享”的交互心智。
- Bolt.new：采用 Agent 可操作受控文件系统、预览与聊天同步的工作区方式。
- OpenPage：借鉴人和 AI 围绕同一份可交付页面源文件协作的思想，但本期不引入其 JSON 页面模型。

## 代码位置

- 页面路由：`front/app/prototype-studio/page.tsx`
- 工作台组件：`front/components/prototype-studio/PrototypeStudio.tsx`
- 可点击静态原型：`design/prototype-studio/prototype-studio.html`
- 视觉与产品说明：`design/prototype-studio/prototype-studio-design.md`
