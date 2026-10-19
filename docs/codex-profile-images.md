# Codex Profile 原图输入

管理员可在「设置 → 管理配置 → 第三方代理」上方的「Codex 模型与图片上传」区域，直接在对应模型卡片勾选「支持图片上传（原图）」，开启同一区域「允许聊天上传图片」，点击「保存模型与图片配置」。新增 Profile 时也可预先勾选，保存仍复用同一流程；模型本身需在允许使用列表中。图片总开关只提交 `native_image_input_enabled`，不覆盖隐藏的历史配置。保存回读检查能力与路由，成功显示提示；后端未识别新字段、或两步保存部分失败时显示具体原因。

此开关声明该 Profile 的模型和供应商支持视觉输入。2026-10-19 对本机 deepseek Profile / deepseek-v4-flash 的独立探测使用 Codex CLI 0.159.2 直接传入合成图片，正确识别随机数字与三个彩色形状；未使用 OCR。其他 Profile 的实际支持情况取决于其模型与服务商，不自动开启。

## 配置与兼容

`CodexProfileModelDto` 和前端 `CodexProfileModel` 新增布尔字段 `supports_image_input`，默认 false。该值与其他 Profile 字段一起保存到现有 `codex_model_policy` 设置快照 JSON；无需新表或字段迁移。历史快照缺少该值时继续保持原行为。

模型目录 `models[].image_input` 的投影与附件校验共用以下路由：

| 模型配置 | 路由 | 全局开关 | 发送内容 |
| --- | --- | --- | --- |
| 内置 Codex 或 Profile 的 `supports_image_input=true` | native | `native_image_input_enabled` | 原图 |
| 未开启原图，且 `supports_image_ocr=true` | ocr | OCR `enabled` | 原有本机 OCR 文本 |
| 两种能力均关闭 | none | 无 | 拒绝图片附件 |

原图能力优先；即使旧 OCR 标志仍为 true，也跳过 OCR。原图总开关关闭时拒绝图片，不静默切换 OCR。上表的 OCR 路由仅保留后端旧客户端兼容；当前聊天界面只开放原图上传，历史 OCR 标志不能开启原图入口。新建 Profile 默认两种能力均关闭。

界面不再展示 OCR 策略、逐 Profile OCR 降级和诊断入口；管理首页移除对应卡片，旧 `/settings/admin/image-ocr` 页面跳转到模型配置。未删除历史设置及后端服务。

聊天在恢复窗口焦点、页面可见或收到策略保存事件时重新读取模型与图片策略，保留仍被允许的当前模型。请求代次与卸载标记阻止旧响应覆盖新配置。图片被拦截时提示具体模型和需要开启的开关，提供新标签页设置链接与「刷新图片配置」按钮；能力生效后清除对应旧提示。无需重新创建会话。

## 后端边界

HTTP/SSE 和 WebSocket 在解析附件 ID 前，调用同一 `CodexResolvedModel.EnsureImageInputEnabled` 策略。浏览器只提供不透明附件 ID；沿用现有图片签名、大小、数量、用户归属、会话持久化与历史访问检查。`LocalImagePaths` 保持 `[JsonIgnore]`，不接受客户端本地路径。

Profile 的新会话与 `exec resume` 均通过 `ProcessStartInfo.ArgumentList` 逐张传入 `--image <受控路径>`，去除空值并去重，不使用 shell 拼接。未开启原图的 Profile 不接收图片参数。内置模型继续使用 app-server `localImage`，已有沙箱、模型与推理参数不变。

## 验证

- `backed.tests/CodexProfileImageTests.cs`：策略持久化、管理员权限、历史 JSON 默认值、三路能力及全局开关、新会话/resume 参数、原图跳过 OCR。
- `backed.tests/CodexModelPolicyTests.cs`：已有内置模型升级和推理策略回归。
- `front/tests/codex-profile-images.browser.mjs`：模型卡片上的复选框、统一保存与回读、已有/新增 Profile、OCR 界面隐藏及旧 URL 跳转、移动端宽度、焦点及手动刷新、聊天原图上传、历史 OCR 标志不开放上传、无能力模型及总开关拦截、旧后端忽略字段时禁止显示保存成功。连接已有前端，所有 API 使用合成响应，无真实模型或用户数据访问。

首轮原图实现的 19 项专项后端测试已通过，使用临时输出目录与内存 SQLite。后续入口与刷新优化只修改前端，未重复后端编译；浏览器回归先复现“模型卡片没有复选框”，修改后通过。测试不会启动真实 CLI 模型、改用户配置、重启服务或部署。编译前须按当前 CodeLimit 约定取得用户明确授权。
