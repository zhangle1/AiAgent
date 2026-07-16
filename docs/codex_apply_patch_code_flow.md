# OpenAI Codex 修改代码流程详解：`apply_patch` 是怎么工作的

> 目标：解释 `https://github.com/openai/codex` 里，Codex 修改代码时从「模型提出修改」到「文件真正落盘」的完整代码流程。
>
> 当前仓库主线：`codex-rs` 是核心 Rust 工程，代码修改主要围绕 `apply_patch` 工具、shell 拦截、patch 解析、权限审批、沙箱执行、事件回传这几部分展开。

---

## 1. 先给结论

Codex 修改代码不是简单地让模型输出一段代码，然后直接覆盖文件。

它大致是这样做的：

```text
用户提出需求
  ↓
Codex 把仓库上下文、工具列表、规则发给模型
  ↓
模型决定要修改文件
  ↓
模型输出一个 apply_patch 补丁
  ↓
Codex 解析 patch，判断它要新增/修改/删除哪些文件
  ↓
Codex 做安全判断：是否允许写这些路径？是否要用户确认？
  ↓
通过 ToolOrchestrator 走审批 + 沙箱策略
  ↓
ApplyPatchRuntime 调用 codex_apply_patch::apply_patch 真正写文件
  ↓
返回 stdout/stderr/exit_code
  ↓
Codex 把结果重新喂给模型
  ↓
模型继续检查、跑测试、修复，直到结束
```

关键点：

```text
模型负责“提出 patch”
Codex 负责“解析、验证、审批、沙箱、执行、回传结果”
```

---

## 2. 两种修改代码入口

Codex 现在有两种常见方式进入代码修改流程。

### 2.1 入口一：模型直接调用 `apply_patch` 自定义工具

在 `codex-rs/core/src/tools/handlers/apply_patch_spec.rs` 里，Codex 注册了一个 freeform 工具：

```rust
pub fn create_apply_patch_freeform_tool(include_environment_id: bool) -> ToolSpec {
    ToolSpec::Freeform(FreeformTool {
        name: "apply_patch".to_string(),
        description: "Use the `apply_patch` tool to edit files.\nThis is a FREEFORM tool, so do not wrap the patch in JSON.".to_string(),
        format: FreeformToolFormat {
            r#type: "grammar".to_string(),
            syntax: "lark".to_string(),
            definition,
        },
    })
}
```

意思是：

```text
这个工具不是普通 JSON 参数工具
而是让模型直接输出一段 patch 文本
并且这段文本要符合 apply_patch.lark 语法
```

模型输出的大概长这样：

```text
*** Begin Patch
*** Update File: src/main.rs
@@
-println!("old");
+println!("new");
*** End Patch
```

这条路径会进入：

```text
ApplyPatchHandler::handle_call
```

对应文件：

```text
codex-rs/core/src/tools/handlers/apply_patch.rs
```

---

### 2.2 入口二：模型通过 shell 调用 `apply_patch`

有些模型会输出 shell 命令：

```bash
apply_patch <<'PATCH'
*** Begin Patch
*** Update File: README.md
@@
-old
+new
*** End Patch
PATCH
```

或者：

```bash
cd some-folder && apply_patch <<'PATCH'
*** Begin Patch
*** Add File: a.txt
+hello
*** End Patch
PATCH
```

Codex 不会直接把这个命令交给系统执行。

在 shell handler 里，有这样一步：

```rust
// Intercept apply_patch if present.
let apply_patch_cwd = PathUri::from_abs_path(&exec_params.cwd);
if let Some(output) = intercept_apply_patch(...).await? {
    return Ok(output);
}
```

对应文件：

```text
codex-rs/core/src/tools/handlers/shell.rs
```

也就是说：

```text
shell 命令执行前
Codex 先检查这个命令是不是 apply_patch
如果是，就拦截并走内部 apply_patch 逻辑
而不是让 bash/powershell/cmd 真的去找 apply_patch 命令
```

这也是为什么 `apply_patch` 可以看起来像一个命令，但本质上它是 Codex 内部虚拟工具。

---

## 3. patch 格式是什么

`apply_patch` 的语法是一个简化版 diff 格式。

整体外壳：

```text
*** Begin Patch
...
*** End Patch
```

中间可以有三类操作：

```text
*** Add File: path       新增文件
*** Update File: path    修改文件
*** Delete File: path    删除文件
```

### 3.1 新增文件

```text
*** Begin Patch
*** Add File: docs/hello.md
+# Hello
+This is a new file.
*** End Patch
```

每一行新增内容前面用 `+`。

### 3.2 修改文件

```text
*** Begin Patch
*** Update File: src/app.ts
@@
-const name = "old";
+const name = "new";
*** End Patch
```

`-` 表示旧内容，`+` 表示新内容。

### 3.3 删除文件

```text
*** Begin Patch
*** Delete File: old.txt
*** End Patch
```

### 3.4 重命名文件

```text
*** Begin Patch
*** Update File: old.txt
*** Move to: new.txt
@@
-old content
+new content
*** End Patch
```

---

## 4. 源码流程一：工具注册

工具注册核心在：

```text
codex-rs/core/src/tools/handlers/apply_patch_spec.rs
```

它把 `apply_patch` 注册成一个 freeform tool：

```text
ToolSpec::Freeform
  name = apply_patch
  format = lark grammar
```

这一步的作用是告诉模型：

```text
你可以用 apply_patch 修改文件
但是不要用 JSON 包起来
你要直接输出 patch 文本
```

这就是模型为什么会生成这种格式：

```text
*** Begin Patch
*** Update File: xxx
...
*** End Patch
```

---

## 5. 源码流程二：模型输出 tool call 后，Router 分发

模型返回 tool call 后，会经过工具路由。

关键文件：

```text
codex-rs/core/src/tools/router.rs
```

里面会把模型响应里的工具调用转成内部结构：

```rust
ResponseItem::CustomToolCall { name, namespace, input, call_id, .. }
```

转成：

```rust
ToolCall {
    tool_name,
    call_id,
    payload: ToolPayload::Custom { input },
}
```

对于 `apply_patch` 来说，`input` 就是完整 patch 文本。

之后 `ToolRouter` 调用 registry 分发：

```text
ToolRouter
  ↓
ToolRegistry
  ↓
ApplyPatchHandler
```

---

## 6. 源码流程三：`ApplyPatchHandler::handle_call`

核心文件：

```text
codex-rs/core/src/tools/handlers/apply_patch.rs
```

关键函数：

```rust
async fn handle_call(&self, invocation: ToolInvocation)
```

这一步做几件事。

### 6.1 取出 patch 文本

```rust
let ToolPayload::Custom { input: patch_input } = payload else {
    return Err(...);
};
```

也就是从模型 tool call 里拿到 patch 内容。

### 6.2 解析 patch

```rust
let args = match codex_apply_patch::parse_patch(&patch_input) {
    Ok(args) => args,
    Err(parse_error) => {
        return Err(FunctionCallError::RespondToModel(format!(
            "apply_patch verification failed: {parse_error}"
        )));
    }
};
```

如果模型 patch 语法写错，Codex 不会改文件，而是把错误返回给模型。

模型下一轮会根据错误重新生成 patch。

### 6.3 找到要操作的环境

```rust
let selected_environment_id = require_environment_id(...)?;
let Some(turn_environment) = resolve_tool_environment(...)? else {
    return Err(...);
};
```

你可以理解为：

```text
确定这个 patch 要应用到哪个工作区 / 哪个环境
```

普通本地 CLI 通常就是当前项目目录。

### 6.4 拿文件系统对象

```rust
let fs = turn_environment.environment.get_filesystem();
```

这里很关键。

Codex 不一定直接用本机 `std::fs`。

它通过抽象的 `ExecutorFileSystem` 操作文件。

这样本地环境、远程环境、多环境都可以统一处理。

### 6.5 验证 patch

```rust
codex_apply_patch::verify_apply_patch_args(
    args,
    turn_environment.cwd(),
    fs.as_ref(),
    Some(&sandbox),
).await
```

验证阶段不会立刻写文件，它主要是在判断：

```text
这个 patch 能不能被正确理解？
它会改哪些文件？
Update File 的旧内容能不能在目标文件里找到？
Delete File 的文件是否能读取？
Move to 的目标路径是什么？
```

验证成功后得到：

```rust
ApplyPatchAction
```

它里面包含：

```rust
changes: HashMap<PathUri, ApplyPatchFileChange>
patch: String
cwd: PathUri
```

也就是：

```text
这次 patch 要改哪些绝对路径
每个路径是 Add/Delete/Update
原始 patch 文本是什么
基准 cwd 是哪里
```

---

## 7. 源码流程四：验证 patch 的细节

验证逻辑在：

```text
codex-rs/apply-patch/src/invocation.rs
```

关键函数：

```rust
verify_apply_patch_args
try_verify_apply_patch_args
```

核心逻辑类似这样：

```rust
for hunk in hunks {
    let path = hunk.resolve_path(&effective_cwd)?;
    match hunk {
        Hunk::AddFile { contents, .. } => {
            changes.insert(path, ApplyPatchFileChange::Add { content: contents });
        }
        Hunk::DeleteFile { .. } => {
            let content = fs.read_file_text(&path, sandbox).await?;
            changes.insert(path, ApplyPatchFileChange::Delete { content });
        }
        Hunk::UpdateFile { move_path, chunks, .. } => {
            let ApplyPatchFileUpdate { unified_diff, content, .. } =
                unified_diff_from_chunks(&path, &chunks, fs, sandbox).await?;
            changes.insert(path, ApplyPatchFileChange::Update {
                unified_diff,
                move_path,
                new_content: content,
            });
        }
    }
}
```

### 7.1 Add File 怎么验证

新增文件比较简单：

```text
把 patch 里的新增内容记录下来
```

它会形成：

```rust
ApplyPatchFileChange::Add { content }
```

### 7.2 Delete File 怎么验证

删除文件时，Codex 会先读取被删除文件的内容：

```rust
fs.read_file_text(&path, sandbox).await
```

为什么要读？

因为它需要知道删除前的内容，用于：

```text
展示 diff
记录变更
失败恢复/审计
turn diff 追踪
```

### 7.3 Update File 怎么验证

修改文件最复杂。

它会：

```text
读取原文件
根据 patch chunk 找到要替换的旧行
计算新内容
生成 unified diff
记录 new_content
```

这里会调用：

```rust
unified_diff_from_chunks
derive_new_contents_from_chunks
compute_replacements
apply_replacements
```

核心思想：

```text
patch 不是盲写
而是先在原文件里寻找旧内容
找到后才计算替换结果
找不到就报错，让模型重试
```

比如 patch 写：

```text
-old line
+new line
```

但是目标文件里根本没有 `old line`，Codex 会返回类似：

```text
Failed to find expected lines
```

然后模型会重新读取文件，再生成新的 patch。

---

## 8. 源码流程五：安全判断 `assess_patch_safety`

验证成功后，还不能直接写文件。

`ApplyPatchHandler` 会调用：

```rust
apply_patch::apply_patch(turn.as_ref(), &file_system_sandbox_policy, changes).await
```

这个函数在：

```text
codex-rs/core/src/apply_patch.rs
```

里面会调用：

```rust
assess_patch_safety(
    &action,
    turn_context.approval_policy.value(),
    &turn_context.permission_profile(),
    file_system_sandbox_policy,
    &action.cwd,
    turn_context.windows_sandbox_level,
)
```

返回三种结果：

```rust
SafetyCheck::AutoApprove
SafetyCheck::AskUser
SafetyCheck::Reject
```

对应含义：

| 结果 | 含义 |
|---|---|
| AutoApprove | 当前权限策略允许，自动通过 |
| AskUser | 需要用户确认 |
| Reject | 直接拒绝，不能执行 |

然后转成内部调用：

```rust
InternalApplyPatchInvocation::DelegateToRuntime(...)
```

或者：

```rust
InternalApplyPatchInvocation::Output(Err(...))
```

这里有一个重要点：

```text
是否能写文件，不是模型决定的
是 Codex 根据 sandbox policy / approval policy / permission profile 决定的
```

---

## 9. 源码流程六：计算额外写权限

在真正执行前，Codex 会计算这次 patch 需要写哪些路径。

关键函数：

```rust
file_paths_for_action
write_permissions_for_paths
effective_patch_permissions
```

源码位置：

```text
codex-rs/core/src/tools/handlers/apply_patch.rs
```

大概逻辑：

```text
1. 收集 action.changes() 里的所有路径
2. 如果 Update 里有 Move to，也把目标路径算进去
3. 判断这些路径当前 sandbox 是否可写
4. 如果不可写，生成 AdditionalPermissionProfile
5. 后续交给审批和沙箱执行层处理
```

例如：

```text
当前 cwd = /project
patch 修改 /project/src/a.ts    可以写
patch 修改 /etc/hosts          不应该自动写，需要拒绝或审批
```

---

## 10. 源码流程七：ToolOrchestrator 统一处理审批和沙箱

执行前会进入：

```rust
let mut orchestrator = ToolOrchestrator::new();
let mut runtime = ApplyPatchRuntime::new();
let out = orchestrator.run(...).await
```

核心文件：

```text
codex-rs/core/src/tools/orchestrator.rs
```

这个模块开头的注释已经说明它的职责：

```text
Central place for approvals + sandbox selection + retry semantics.
```

也就是：

```text
统一处理：审批 + 沙箱选择 + 失败后重试
```

它的流程是：

```text
1. 判断这个工具调用是否需要审批
2. 如果需要，向用户或 Guardian 自动审查器请求 approval
3. 选择第一次执行用什么 sandbox
4. 运行工具 runtime
5. 如果 sandbox 拒绝，并且策略允许，尝试升级权限/无沙箱重试
6. 返回最终结果
```

对应到 `apply_patch`：

```text
ToolOrchestrator 并不关心 patch 怎么写文件
它只关心：这个工具是否允许执行？在哪种沙箱里执行？失败后是否可重试？
```

---

## 11. 源码流程八：ApplyPatchRuntime 真正执行 patch

真正写文件的是：

```text
codex-rs/core/src/tools/runtimes/apply_patch.rs
```

关键函数：

```rust
impl ToolRuntime for ApplyPatchRuntime {
    async fn run(&mut self, req: &ApplyPatchRequest, attempt: &SandboxAttempt<'_>, _ctx: &ToolCtx)
}
```

里面会做：

```rust
let fs = req.turn_environment.environment.get_filesystem();
let sandbox = Self::file_system_sandbox_context_for_attempt(req, attempt);

let result = codex_apply_patch::apply_patch(
    &req.action.patch,
    &req.action.cwd,
    &mut stdout,
    &mut stderr,
    fs.as_ref(),
    sandbox.as_ref(),
).await;
```

这一步才是真正落盘。

也就是说：

```text
core handler 负责验证和调度
runtime 负责在选定 sandbox 下真正执行
codex_apply_patch crate 负责真正的文件修改算法
```

---

## 12. 源码流程九：`codex_apply_patch::apply_patch` 真正怎么改文件

核心文件：

```text
codex-rs/apply-patch/src/lib.rs
```

入口函数：

```rust
pub async fn apply_patch(
    patch: &str,
    cwd: &PathUri,
    stdout: &mut impl std::io::Write,
    stderr: &mut impl std::io::Write,
    fs: &dyn ExecutorFileSystem,
    sandbox: Option<&FileSystemSandboxContext>,
) -> Result<AppliedPatchDelta, ApplyPatchFailure>
```

它先解析 patch：

```rust
let hunks = match parse_patch(patch) {
    Ok(source) => source.hunks,
    Err(e) => { ... return Err(...) }
};
```

然后执行：

```rust
apply_hunks(&hunks, cwd, stdout, stderr, fs, sandbox).await
```

再进入：

```rust
apply_hunks_to_files
```

---

## 13. Add File 的落盘流程

源码逻辑大概是：

```rust
Hunk::AddFile { contents, .. } => {
    let overwritten_content = read_optional_file_text_for_delta(...).await;

    write_file_with_missing_parent_retry(
        fs,
        &path_uri,
        contents.clone().into_bytes(),
        sandbox,
    ).await;

    delta.changes.push(AppliedPatchChange {
        path: path_uri.to_path_buf(),
        change: AppliedPatchFileChange::Add {
            content: contents.clone(),
            overwritten_content,
        },
    });
}
```

解释一下：

```text
1. 先看看目标文件是否已经存在
2. 如果父目录不存在，就创建父目录
3. 写入新内容
4. 记录 AppliedPatchDelta
```

注意：

```text
Add File 如果目标文件已有内容，它会记录 overwritten_content
这样 Codex 知道这次操作覆盖了什么
```

---

## 14. Delete File 的落盘流程

源码逻辑大概是：

```rust
Hunk::DeleteFile { .. } => {
    let deleted_content = fs.read_file_text(&path_uri, sandbox).await.ok();
    ensure_not_directory(&path_uri, fs, sandbox).await?;

    fs.remove(
        &path_uri,
        RemoveOptions { recursive: false, force: false },
        sandbox,
    ).await?;

    delta.changes.push(AppliedPatchChange {
        path: path_uri.to_path_buf(),
        change: AppliedPatchFileChange::Delete { content },
    });
}
```

解释：

```text
1. 先读出原文件内容
2. 确保目标不是目录
3. 非递归删除文件
4. 记录删除前内容
```

这里很谨慎：

```text
remove recursive = false
force = false
```

也就是说，它不是危险的 `rm -rf`。

---

## 15. Update File 的落盘流程

修改文件的流程：

```rust
Hunk::UpdateFile { move_path, chunks, .. } => {
    let AppliedPatch { original_contents, new_contents } =
        derive_new_contents_from_chunks(&path_uri, chunks, fs, sandbox).await?;

    if let Some(dest) = move_path {
        // 写入新路径，然后删除旧路径
    } else {
        fs.write_file(&path_uri, new_contents.clone().into_bytes(), sandbox).await?;
    }

    delta.changes.push(AppliedPatchChange::Update { ... });
}
```

也就是说：

```text
普通 Update：读取旧文件 → 计算新内容 → 覆盖写回
Move Update：读取旧文件 → 计算新内容 → 写到新路径 → 删除旧路径
```

---

## 16. `derive_new_contents_from_chunks` 如何计算新内容

关键函数：

```rust
derive_new_contents_from_chunks
compute_replacements
apply_replacements
```

流程：

```text
1. 读取原文件 original_contents
2. 按行拆分 original_lines
3. 对每个 chunk 查找旧内容所在位置
4. 生成 replacements：从第几行开始，删几行，插入哪些新行
5. 从后往前应用 replacements，避免行号偏移
6. 拼回 new_contents
```

为什么从后往前应用？

比如有两个修改：

```text
第 10 行改一次
第 30 行改一次
```

如果先改第 10 行，可能会导致第 30 行的位置变化。

所以它从后往前改：

```text
先改第 30 行
再改第 10 行
```

这样前面的行号不会影响后面的定位。

---

## 17. 失败后怎么处理

如果 patch 失败，比如：

```text
旧内容找不到
文件读不到
沙箱不允许写
写文件失败
```

Codex 不会假装成功。

它会返回：

```text
exit_code = 1
stdout/stderr = 错误信息
```

然后 `ToolEmitter.finish` 会把错误内容包装成：

```rust
FunctionCallError::RespondToModel(content)
```

意思是：

```text
把错误返回给模型
让模型下一轮根据错误修正 patch
```

这就是你看到 Codex 有时会：

```text
先尝试 patch
失败
重新读取文件
再生成新的 patch
```

本质是 Agent Loop：

```text
模型行动 → 工具执行 → 结果反馈 → 模型继续行动
```

---

## 18. 沙箱失败后的重试机制

`ToolOrchestrator` 里有一段逻辑：

```text
第一次在沙箱里执行
如果被 sandbox denied
判断是否允许升级
如果允许，重新请求审批
然后用更高权限或无沙箱重试
```

大概流程：

```text
run first attempt
  ↓
if success: return
  ↓
if sandbox denied:
  ↓
  can escalate?
      no  → 返回错误
      yes → 请求用户审批 / guardian 审批
             ↓
             retry
```

所以 Codex 的执行不是简单的一次 `write_file`。

它有完整的权限控制链路：

```text
approval policy
sandbox policy
permission profile
additional permissions
cached approval
retry without sandbox / escalated sandbox
```

---

## 19. 修改成功后怎么通知界面和模型

Codex 会发事件。

关键文件：

```text
codex-rs/core/src/tools/events.rs
```

对于 apply_patch，会有：

```text
PatchApplyBegin
PatchApplyEnd
TurnDiff
```

实际源码里通过：

```rust
ToolEmitter::apply_patch_for_environment(...)
emitter.begin(...)
emitter.finish(...)
```

开始时发：

```rust
TurnItem::FileChange {
    changes,
    status: None,
    auto_approved,
    stdout: None,
    stderr: None,
}
```

结束时发：

```rust
TurnItem::FileChange {
    changes,
    status: Some(PatchApplyStatus::Completed / Failed / Declined),
    stdout,
    stderr,
}
```

如果 patch 成功，还会更新 turn diff tracker：

```text
这一轮到底改了什么文件
最终 diff 是什么
```

所以界面上能显示：

```text
修改了哪些文件
新增/删除/修改了什么
patch 是否自动通过
执行是否成功
```

---

## 20. shell 方式为什么也能修改代码

如果模型不是直接调用 `apply_patch` 工具，而是输出 shell：

```bash
bash -lc "apply_patch <<'PATCH'
...
PATCH"
```

Codex 会在 shell handler 里调用：

```rust
intercept_apply_patch(...)
```

`intercept_apply_patch` 会调用：

```rust
codex_apply_patch::maybe_parse_apply_patch_verified(command, cwd, fs, Some(&sandbox)).await
```

这个函数在：

```text
codex-rs/apply-patch/src/invocation.rs
```

它支持几种形式：

```text
apply_patch <patch-body>
applypatch <patch-body>
bash -lc "apply_patch <<'EOF' ... EOF"
cd xxx && apply_patch <<'EOF' ... EOF
powershell -Command "apply_patch <<'EOF' ... EOF"
cmd /c "apply_patch <<'EOF' ... EOF"
```

它用 Tree-sitter Bash 去解析 heredoc。

注意它很保守，只接受特定形式。

比如这些会被拒绝或不识别：

```bash
echo hi && apply_patch <<'PATCH'
...
PATCH
```

```bash
cd foo; apply_patch <<'PATCH'
...
PATCH
```

```bash
cd foo || apply_patch <<'PATCH'
...
PATCH
```

为什么这么保守？

因为 Codex 不希望模型把复杂 shell 脚本伪装成 patch。

安全设计是：

```text
简单、明确、可解析的 apply_patch → 走内部安全 patch 流程
复杂 shell 脚本 → 按普通 shell 命令处理，走 shell 审批/沙箱流程
```

---

## 21. 整体代码调用链

### 21.1 直接 apply_patch 工具调用链

```text
模型输出 CustomToolCall(name = "apply_patch", input = patch)
  ↓
ToolRouter::build_tool_call
  ↓
ToolRegistry dispatch
  ↓
ApplyPatchHandler::handle_call
  ↓
codex_apply_patch::parse_patch
  ↓
resolve_tool_environment
  ↓
codex_apply_patch::verify_apply_patch_args
  ↓
effective_patch_permissions
  ↓
core::apply_patch::apply_patch
  ↓
safety::assess_patch_safety
  ↓
ToolEmitter::apply_patch_for_environment(...).begin
  ↓
ToolOrchestrator::run
  ↓
ApplyPatchRuntime::run
  ↓
codex_apply_patch::apply_patch
  ↓
apply_hunks_to_files
  ↓
fs.write_file / fs.remove / fs.create_directory
  ↓
ToolEmitter.finish
  ↓
返回 stdout/stderr/exit_code 给模型
```

### 21.2 shell 里 apply_patch 的调用链

```text
模型输出 FunctionCall(name = "shell", command = ["bash", "-lc", "apply_patch <<'PATCH'..."])
  ↓
ShellCommandHandler
  ↓
run_exec_like
  ↓
intercept_apply_patch
  ↓
codex_apply_patch::maybe_parse_apply_patch_verified
  ↓
如果识别为 apply_patch：走 ApplyPatchRuntime
  ↓
如果不是：走普通 ShellRuntime
```

---

## 22. 和你之前理解的 Agent Loop 对应起来

你可以把 Codex 修改代码理解成：

```text
LLM = 决策器
ToolRouter = 工具分发器
ApplyPatchHandler = patch 工具入口
verify_apply_patch_args = 预执行检查器
assess_patch_safety = 安全检查器
ToolOrchestrator = 审批 + 沙箱调度器
ApplyPatchRuntime = 工具运行时
codex_apply_patch::apply_patch = 真实文件修改器
ToolEmitter = 事件/结果回传器
```

如果用 C# 类比，可以设计成：

```csharp
class AgentLoop
class ToolRouter
class ApplyPatchToolHandler
class PatchParser
class PatchVerifier
class SafetyChecker
class ToolOrchestrator
class ApplyPatchRuntime
interface IExecutorFileSystem
class ToolEventEmitter
```

---

## 23. 简化版伪代码

```csharp
async Task HandleModelToolCall(ToolCall call)
{
    if (call.Name == "apply_patch")
    {
        var patchText = call.Input;

        var parsed = PatchParser.Parse(patchText);
        if (!parsed.Success)
            return SendErrorToModel(parsed.Error);

        var action = await PatchVerifier.VerifyAsync(
            parsed.Args,
            cwd,
            fileSystem,
            sandbox
        );

        var safety = SafetyChecker.Assess(
            action,
            approvalPolicy,
            sandboxPolicy
        );

        if (safety.Reject)
            return SendErrorToModel("patch rejected");

        var request = new ApplyPatchRequest
        {
            Action = action,
            RequiredPermissions = CalculateWritePermissions(action)
        };

        var result = await ToolOrchestrator.RunAsync(
            new ApplyPatchRuntime(fileSystem),
            request
        );

        return SendToolResultToModel(result);
    }
}
```

真实 Codex 的复杂点在于：

```text
多环境
沙箱
审批
Guardian 审查
Windows/macOS/Linux 差异
事件流
turn diff 追踪
patch streaming progress
```

但主线就是上面这个。

---

## 24. 这个设计为什么好

### 24.1 比直接覆盖文件安全

模型不能随便说：

```text
把整个文件替换成 xxx
```

它要提供结构化 patch。

Codex 可以提前知道：

```text
要改哪些文件
怎么改
是否越权
是否需要审批
```

### 24.2 失败可以反馈给模型

如果 patch 失败，错误会返回模型。

模型会自动调整：

```text
读取文件 → 重新生成 patch → 再执行
```

### 24.3 适合多文件修改

一个 patch 可以包含多个文件：

```text
Add File
Update File
Delete File
```

所以它适合：

```text
重构
批量改名
新增测试
修 bug
改文档
```

### 24.4 适合审计和 UI 展示

因为 patch 是结构化的，Codex 能展示：

```text
新增了哪些文件
修改了哪些文件
删除了哪些文件
diff 是什么
是否用户批准
是否沙箱拒绝
```

---

## 25. 你读源码建议顺序

如果你想自己把源码读懂，建议按这个顺序：

```text
1. codex-rs/core/src/tools/handlers/apply_patch_spec.rs
   看 apply_patch 工具是怎么暴露给模型的

2. codex-rs/core/src/tools/router.rs
   看模型 tool call 怎么被转成内部 ToolCall

3. codex-rs/core/src/tools/handlers/apply_patch.rs
   看 apply_patch handler 主流程

4. codex-rs/apply-patch/src/invocation.rs
   看 shell 里 apply_patch 怎么被识别、验证

5. codex-rs/apply-patch/src/lib.rs
   看 patch 真正怎么落盘

6. codex-rs/core/src/apply_patch.rs
   看 safety check 怎么决定自动通过/询问/拒绝

7. codex-rs/core/src/tools/orchestrator.rs
   看审批、沙箱、重试机制

8. codex-rs/core/src/tools/runtimes/apply_patch.rs
   看 ApplyPatchRuntime 怎么连接 orchestrator 和真实文件系统

9. codex-rs/core/src/tools/events.rs
   看修改结果怎么发给 UI 和模型

10. codex-rs/core/src/tools/handlers/shell.rs
    看 shell 方式下 apply_patch 怎么被拦截
```

---

## 26. 最核心的一句话

Codex 修改代码的本质是：

```text
模型生成结构化 patch，Codex 解析并验证 patch，
再通过审批和沙箱机制把 patch 应用到文件系统，
最后把执行结果和 diff 返回给模型继续推理。
```

也就是：

```text
LLM 负责“想怎么改”
Codex runtime 负责“能不能改、怎么安全地改、改完怎么反馈”
```

