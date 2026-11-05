# 团队知识 Git（首轮）

入口：知识工作区右上角「Git 同步与设置」，跳转「设置 → 知识工作区 → 团队知识 Git」。管理员保存团队唯一的 HTTPS 远程与分支，凭据复用本人已激活的 Git 账号，不存入远程地址或 appsettings。

## 配置与目录

服务器 appsettings 或环境变量 `KnowledgeStorage__RootPath` 可指定固定绝对路径：

```json
{ "KnowledgeStorage": { "RootPath": "D:\\KnowledgeCenter" } }
```

- `storage/knowledge_bases/`：原始知识持久化存储。
- `workspaces/team-knowledge/`：团队唯一 Git 工作区。
- `team-git.json`：远程和分支配置，不含凭据，不进入团队仓库。

空值保留旧 `DataPath/knowledge_bases` 存储行为，团队工作区放在 `DataPath/knowledge-workspaces/workspaces/team-knowledge`。页面只读展示实际路径，修改 appsettings 后重启。

切换目录不自动迁移数据。应先停写、备份并复制原存储，核对数据库记录中的绝对 `StoragePath`、解析正文路径等映射以及数据保护密钥，确认原件和解析内容可读取后再切换。只保留知识文件目录不能代替数据库备份。

## 使用

1. 在 Git 账号设置中激活有目标仓库权限的账号。
2. 保存 HTTPS 远程地址和分支（默认 main）。首次使用空远程仓库；不自动合并既有历史。
3. 点击「刷新资料并检查远端」。先完整生成暂存副本，再替换受管目录；普通异常会回滚替换。刷新不会触发解析或模型调用。
4. 检查文件列表、输入提交说明，确认提交并推送。服务端复核文件内容快照与远端祖先关系。

仓库保留 `resources/` 与 `projects/<项目>/<目录>/` 的知识资源结构，项目归属沿用现有知识目录／organization.project；并未新增代码项目授权绑定。未归属项目的既有知识继续在资料中。源 URI、导出路径、SHA256 保存在 `manifest.json`；合法中文目录直接保留，保留名称编码，大小写冲突报错。

仅导出共享资料与项目树中的原件、已发布解析章节和可读取的公共产物；不会导出人员树、数据库、凭据、向量索引或缓存。空目录可在服务器副本中查看，Git 本身不保存空目录。已有公共产物过期／缺失会使本次刷新失败，保留上一副本。

远端领先、历史分叉、凭据错误都会阻止推送；不做自动拉取、合并、强推或重置。推送失败时保留本地提交，刷新后可再次推送，不重复生成相同提交。首轮不在聊天中挂载该工作区或新增 KnowAgent 模式切换。

## HTTP 契约

全部为管理员接口，由服务层验证身份；无客户端任意磁盘路径参数。

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| GET | `/api/v1/knowledge/team-git` | 实际存储、工作区路径、配置与 Git 状态 |
| PUT | `/api/v1/knowledge/team-git` | `{remote_url, branch}` 保存／初始化 |
| POST | `/api/v1/knowledge/team-git/refresh` | 生成团队副本并返回文件列表、检查项与 snapshot_sha256 |
| POST | `/api/v1/knowledge/team-git/push` | `{message, snapshot}` 复核后提交推送 |

工作区采用单实例操作锁。应用进程异常退出期间可能留下临时目录；多后端进程共享同一目录前需要增加跨进程锁和崩溃恢复。服务器工作区由应用维护，避免由外部进程同时修改。

## 验证

- 后端编译通过。使用本机 .NET 10 SDK 编译 net9.0，测试运行通过 `DOTNET_ROLL_FORWARD=Major` 使用 .NET 10 运行时。
- 5 项新增测试通过：固定根目录和旧配置兼容、导出路径和个人范围、远程配置保护、临时 bare 仓库首次推送／重试／远端领先、实际资源导出排除人员数据及失败保留旧副本。现有 GitHistoryTests 回归通过。
- `npx tsc --noEmit` 通过。
- `front/tests/knowledge-team-git.browser.mjs` 用模拟 API 验证配置、文件确认、推送与 390px 窄屏布局；不连接真实知识远端。

浏览器脚本可用 `KNOWLEDGE_TEST_URL` 指定开发地址、`KNOWLEDGE_TEST_TOOLS` 指定已安装 Playwright 的目录、`KNOWLEDGE_TEST_BROWSER` 指定 Chromium 可执行文件。
