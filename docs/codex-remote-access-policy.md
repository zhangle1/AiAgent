# Codex CLI 远程访问与部署策略

本策略用于用户明确要求访问远程机器、部署或探测时。Markdown 规定执行步骤；实际访问依赖执行机上的 SSH、网络、认证和命令执行权限。本次仅增加文档，未实现客户端桥接，也未验证任何真实服务器。

## 1. 最小连接方式

```text
AiAgent 聊天 → 实际运行 Codex CLI 的机器 → ssh / scp → 目标 Linux 或 Windows
```

先确定 Codex 在哪里运行：平台运行的 Codex 使用平台运行账号的网络和 SSH 配置；坤伴本机运行的 Codex 使用本机运行账号的配置。坤伴能连目标不代表平台上的 Codex 能连，已有人工 SSH 终端也不代表模型拥有该终端工具。

优先让执行机直接访问目标。目标位于内网时，可由管理员配置 VPN 或 SSH 跳板；若只有客户端能访问目标，则需在客户端运行 Codex，或另外实现受认证的命令桥接。Markdown 本身不能打通网络或赋予权限。

## 2. 首次准备（由环境维护者完成）

- 在真正的执行机及运行账号下安装 OpenSSH 客户端。
- 为目标配置部署账号、密钥或 SSH Agent，并从可信渠道核对服务器指纹、加入该账号的 known_hosts。不把密码、私钥或令牌写入 Markdown、聊天或仓库。
- 将连接参数配置到该账号的 SSH config，使用稳定别名；管理员实际替换以下示例值，禁止直接将占位符当目标执行。

```sshconfig
Host aiagent-test
    HostName 192.0.2.10
    User deploy
    Port 22
    IdentityFile ~/.ssh/aiagent_deploy
    IdentitiesOnly yes
    BatchMode yes
    StrictHostKeyChecking yes
    ConnectTimeout 10
    ServerAliveInterval 15
    ServerAliveCountMax 2
```

SSH config 在 Windows 通常位于运行账号的 `%USERPROFILE%\.ssh\config`，Linux 位于 `~/.ssh/config`。私钥仅由 SSH 使用，Agent 不读取或打印内容。加密密钥需预先通过 SSH Agent 解锁。权限限制应由目标账号、操作系统或受控执行器落实，不能只依赖本文。

## 3. 每次任务输入

远程访问只需要 SSH 别名、目标系统及访问目的。部署还需要：本地项目入口、目标绝对目录、构建命令、服务启动/重启方式、端口、健康检查地址，以及允许操作的范围。缺少部署信息时先完成已授权的只读连通检查，再询问缺失项，不猜测目录或覆盖现有服务。

可以直接发送：

> 请读取 docs/codex-remote-access-policy.md，通过 SSH 别名 aiagent-test 检查 Linux 主机是否可访问，只检查身份、系统及运行环境，返回真实结果，暂不部署。

部署请求模板（替换尖括号字段后使用）：

> 按 docs/codex-remote-access-policy.md，将 <项目入口> 部署到 <SSH别名> 的 <绝对目录>；使用 <构建命令> 和 <服务管理方式>，端口 <端口>，健康检查 <URL>。限本应用范围，完成上传、启动和探测后返回证据及回滚位置。

## 4. Codex 执行顺序

1. 核对当前任务授权、执行位置及目标别名。检查本机 ssh/scp 是否存在，不输出完整 SSH 配置或凭据。不得通过修改沙箱配置或关闭审批来规避运行限制。
2. 执行只读 SSH 检查。Linux 示例：

   ```sh
   ssh -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=10 aiagent-test 'id; uname -s'
   ```

   Windows 目标须先确认 SSH 默认 shell；PowerShell 可用时使用：

   ```sh
   ssh -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=10 aiagent-test 'powershell.exe -NoProfile -NonInteractive -Command "whoami; [Environment]::OSVersion.VersionString"'
   ```

   保存退出码及必要输出。认证失败、超时或指纹异常时停止后续写入，报告失败层级；不得自动接受未知指纹、关闭校验或循环尝试密码。
3. 若用户只要求访问，到此返回结果。部署任务继续检查目标目录权限、磁盘、运行时、端口占用和现有服务状态；仅 SSH 成功不能认定部署成功。
4. 根据仓库实际构建方式生成产物，排除认证文件和本地数据。使用唯一发布目录上传，比较本地与远端 SHA-256，再安装或启动。命令参数按本地与远端 shell 分别正确引用，不把聊天内容直接拼成 shell。
5. 保留旧版本和恢复方法，使用适合目标系统的服务管理器运行服务。不可将临时 SSH 会话内的前台进程视为持久部署；不得盲杀占用端口的其他进程。
6. 分别检查服务状态、目标机器本机 HTTP 和执行机到访问地址的 HTTP；每项明确探测位置。设置合理超时和有限重试，校验 HTTP 状态及预期页面/接口内容。内网服务仅能从目标本机访问时，明确外部可达性尚未通过。Next.js 项目应核对 SSR、API、rewrite 与运行时需求，不能默认当静态站上传。
7. 失败时保留脱敏日志，按本次授权范围恢复上一版本；恢复后再次探测。SSH 中断导致结果不明时，先查询实际状态，不直接重复安装、切换版本或重启。

部署授权仅覆盖本任务指定应用。删除其他目录、修改机器全局安全配置或扩大目标范围需要单独的用户指令；已授权范围内的常规步骤连续完成，不逐条请求确认。

## 5. 返回结果

报告目标别名、版本/产物哈希、各阶段退出码、服务状态、HTTP 状态与内容检查、探测时间和位置、访问地址、日志摘要及回滚位置。状态使用“连接成功”“部署并探测成功”“部署失败”“结果待核实”中的准确表述，不以 scp 成功或启动命令返回 0 代替上线验证。输出中隐藏凭据及敏感业务数据。

## 6. 策略如何加载

本仓库 AGENTS.md 提供按任务读取本文的入口；独立 docs 文件不会自动成为所有 Codex 会话的指令。也可在聊天中显式要求读取本文。AiAgent 若在隔离工作区运行 Codex，需要确保该工作区能读取本文，或在已授权任务中显式传入策略正文。本文不修改平台工具权限或原生协议。

官方指令加载说明：[Custom instructions with AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md)。
