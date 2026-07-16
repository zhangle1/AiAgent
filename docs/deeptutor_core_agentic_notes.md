# DeepTutor Core / Agentic 源码阅读笔记

> 目标：帮助理解 DeepTutor `core` 目录中 Agent Loop 相关核心文件的职责与方法作用。

本文重点分析以下 5 个文件：

```text
core/agentic/loop.py
core/agentic/labeled_step.py
core/agentic/tool_dispatch.py
core/tool_protocol.py
core/context.py
```

---

## 一、整体理解

DeepTutor 的 `core` 目录可以理解为 **Agent 引擎的公共协议层**。

它不直接负责具体业务，而是定义：

- 一次请求的上下文如何表示
- 工具如何定义
- 模型如何调用工具
- Agent 如何一轮一轮执行
- 执行过程如何流式输出给前端

整体链路如下：

```text
UnifiedContext
    ↓
Tool / Capability
    ↓
run_agentic_loop
    ↓
run_labeled_step
    ↓
模型输出 THINK / TOOL / FINISH
    ↓
如果 TOOL → dispatch_tool_calls
    ↓
工具结果追加回 messages
    ↓
继续下一轮
    ↓
FINISH 输出最终答案
```

一句话理解：

```text
context.py 是数据包
tool_protocol.py 是工具标准
labeled_step.py 是单次模型调用
tool_dispatch.py 是工具执行器
loop.py 是多轮 Agent 调度器
```

---

# 二、`core/context.py`

这个文件主要定义一次请求的上下文对象。

## 1. `Attachment`

```python
@dataclass
class Attachment:
```

表示用户上传的附件。

常见字段：

```python
type            # 附件类型，例如 image / file / pdf
url             # 附件地址
base64          # 图片或文件的 base64 内容
filename        # 文件名
mime_type       # MIME 类型
id              # 附件唯一 ID
extracted_text  # PDF / Word / PPT 等提取后的文本
```

### 作用

让 LLM 或工具知道用户这次上传了什么文件。

例如：

```text
用户上传 PDF
    ↓
系统提取文本
    ↓
保存到 Attachment.extracted_text
    ↓
后续 Agent / RAG / Tool 可以使用
```

---

## 2. `UnifiedContext`

```python
@dataclass
class UnifiedContext:
```

这是 DeepTutor 中非常重要的上下文对象。

可以理解为：

```text
UnifiedContext = 当前这轮用户请求的所有资料包
```

常见字段：

```python
session_id              # 会话 ID
user_message            # 当前用户输入
conversation_history    # 历史消息
enabled_tools           # 当前启用的工具
allowed_builtin_tools   # 允许使用的内置工具
active_capability       # 当前能力，例如 chat / solve / research
knowledge_bases         # 当前可用知识库
attachments             # 附件列表
config_overrides        # 本轮模型参数覆盖
language                # 当前语言
memory_context          # 记忆内容
persona_context         # 人设内容
skills_manifest         # 技能清单
source_manifest         # 附件来源清单
metadata                # 额外信息
```

### 作用

`UnifiedContext` 会贯穿整个 Agent 执行链路：

```text
ChatOrchestrator
    ↓
Capability
    ↓
Agent Loop
    ↓
Tool
```

这样工具执行时不用到处找参数，直接从 `UnifiedContext` 里拿上下文。

---

# 三、`core/tool_protocol.py`

这个文件定义 DeepTutor 的工具协议。

也就是说：

```text
普通 Python 工具如何变成 LLM 可调用的 Tool
```

---

## 1. `ToolParameter`

```python
@dataclass
class ToolParameter:
```

表示工具的一个参数。

例如一个搜索工具可能需要：

```python
ToolParameter(
    name="query",
    type="string",
    description="搜索关键词",
    required=True
)
```

### 方法：`to_schema`

```python
def to_schema(self) -> dict[str, Any]:
```

作用：把工具参数转成 JSON Schema。

例如：

```python
ToolParameter(name="query", type="string")
```

会变成类似：

```json
{
  "type": "string",
  "description": ""
}
```

如果参数是数组：

```python
if self.type == "array":
    schema["items"] = self.items if self.items is not None else {"type": "string"}
```

意思是：数组参数必须声明 `items`。

原因是不同模型供应商对 tool schema 的严格程度不同：

- OpenAI 可能比较宽松
- Gemini / Anthropic 可能更严格

所以 DeepTutor 会尽量生成规范 schema。

---

## 2. `ToolDefinition`

```python
@dataclass
class ToolDefinition:
```

表示一个工具的完整定义。

字段：

```python
name             # 工具名
description      # 工具描述
parameters       # 参数列表
raw_parameters   # 原始 JSON Schema
```

### 方法：`to_openai_schema`

```python
def to_openai_schema(self) -> dict[str, Any]:
```

作用：把 DeepTutor 内部的工具定义转换成 OpenAI function calling 格式。

转换后类似：

```json
{
  "type": "function",
  "function": {
    "name": "search",
    "description": "搜索资料",
    "parameters": {
      "type": "object",
      "properties": {
        "query": {
          "type": "string",
          "description": "搜索关键词"
        }
      },
      "required": ["query"]
    }
  }
}
```

这一步非常关键。

因为 LLM 并不知道 Python 类是什么，它只认识工具 schema。

---

## 3. `ToolResult`

```python
@dataclass
class ToolResult:
```

表示工具执行后的结果。

字段：

```python
content          # 返回给 LLM 看的文本
sources          # 引用来源
metadata         # 给前端或系统看的结构化数据
success          # 是否执行成功
terminate_turn   # 是否终止当前 Agent Loop
pause_for_user   # 是否暂停，等待用户输入
```

### 作用

工具不只是返回文本，还可以控制 Agent Loop 的行为。

例如：

```text
普通搜索工具
    ↓
返回 content + sources
```

```text
ask_user 工具
    ↓
返回 pause_for_user
    ↓
Agent 暂停，等用户回答
```

```text
某些终止工具
    ↓
返回 terminate_turn=True
    ↓
Agent 直接结束当前轮
```

---

## 4. `BaseTool`

```python
class BaseTool(ABC):
```

所有工具都要继承它。

### 抽象方法：`get_definition`

```python
@abstractmethod
def get_definition(self) -> ToolDefinition:
```

作用：返回工具定义。

告诉模型：

```text
这个工具叫什么？
什么时候用？
需要哪些参数？
```

---

### 抽象方法：`execute`

```python
@abstractmethod
async def execute(self, **kwargs: Any) -> ToolResult:
```

作用：真正执行工具。

例如：

```text
search 工具 → 执行搜索
read_file 工具 → 读取文件
rag_search 工具 → 检索知识库
```

---

### 方法：`get_prompt_hints`

```python
def get_prompt_hints(self, language: str = "en") -> ToolPromptHints:
```

作用：返回工具使用提示。

这些提示会被拼到系统 prompt 中，帮助模型更好地决定什么时候调用工具。

---

### 属性：`name`

```python
@property
def name(self) -> str:
```

作用：获取工具名称。

内部通常就是：

```python
return self.get_definition().name
```

---

# 四、`core/agentic/labeled_step.py`

这个文件负责 **一次 LLM 调用**。

可以理解为：

```text
run_labeled_step = 调一次模型，并判断它这一轮想干什么
```

DeepTutor 要求模型输出类似协议标签：

```text
THINK
我需要先分析一下……
```

```text
TOOL
我需要调用工具……
```

```text
FINISH
这是最终答案……
```

---

## 1. `LabeledStepResult`

```python
@dataclass(frozen=True)
class LabeledStepResult:
```

表示一次模型调用的结果。

字段：

```python
label       # THINK / TOOL / FINISH / UNKNOWN
text        # 去掉标签后的正文
tool_calls  # 模型请求调用的工具列表
```

---

## 2. `_message_content_chars`

```python
def _message_content_chars(message):
```

作用：统计一条 message 大概有多少字符。

主要用于 token 用量估算。

有些模型供应商不会返回 usage，这时 DeepTutor 会用字符数粗略估算。

---

## 3. `_error_text`

```python
def _error_text(exc):
```

作用：从异常里提取错误文本，并转换成小写。

后面会根据错误文本判断：

```text
是不是 stream_options 不支持？
是不是 tool schema 不支持？
是不是图片输入不支持？
```

---

## 4. `_is_stream_options_unsupported`

```python
def _is_stream_options_unsupported(exc):
```

作用：判断当前模型供应商是否不支持：

```python
stream_options={"include_usage": True}
```

如果不支持，DeepTutor 会去掉 `stream_options` 再重试。

---

## 5. `_is_tool_schema_unsupported`

```python
def _is_tool_schema_unsupported(exc):
```

作用：判断模型供应商是否不支持原生工具调用 schema。

如果不支持，会降级：

```text
第一次：带 tools 请求模型
    ↓
失败
    ↓
第二次：去掉 tools 再请求模型
```

---

## 6. `_is_image_input_unsupported`

```python
def _is_image_input_unsupported(exc):
```

作用：判断当前模型是否不支持图片输入。

如果不支持，会把图片内容从 messages 中剥离，降级为纯文本请求。

---

## 7. `run_labeled_step`

```python
async def run_labeled_step(...):
```

这是本文件最核心的方法。

### 主流程

```text
准备模型请求参数
    ↓
流式调用 LLM
    ↓
解析开头标签
    ↓
收集正文文本
    ↓
收集 tool_calls
    ↓
返回 LabeledStepResult
```

### 关键参数

```python
client              # OpenAI-compatible client
model               # 模型名
messages            # 当前上下文消息
completion_kwargs   # temperature 等参数
tool_schemas        # 可用工具 schema
allowed_labels      # 允许的标签
final_labels        # 哪些标签算最终回答
tool_label          # 哪个标签表示工具调用
stream              # 流式事件总线
usage               # token 统计器
```

---

### 内部方法：`_open_sub_trace`

作用：打开前端 trace 卡片。

例如前端可能显示：

```text
Reasoning...
Tool call...
Retrieve...
```

---

### 内部方法：`_emit_text`

作用：把模型输出的文本分流。

逻辑类似：

```python
if label in final_labels:
    stream.content(...)
else:
    stream.thinking(...)
```

也就是说：

```text
FINISH 内容 → 展示给用户
THINK 内容 → 展示为思考过程或 trace
```

---

### 内部方法：`_emit_prelude_content`

有些 reasoning 模型会先输出：

```xml
<think>...</think>
```

再输出正式标签。

这个方法负责把 `<think>` 中的内容送到 thinking trace。

---

### 内部方法：`_drain_prelude_or_close`

作用：处理 `<think>...</think>` 是否结束。

因为流式输出可能被切成很多片段，例如：

```text
<th
ink>
```

所以它需要保留一小段尾巴，避免误判。

---

### 内部方法：`_ingest_pre_label`

作用：解析模型开头的标签。

它会判断模型这一轮是：

```text
THINK
TOOL
FINISH
UNKNOWN
```

如果模型输出了很长内容还没有出现合法标签，就设置：

```python
label = LABEL_UNKNOWN
```

然后交给 `loop.py` 做协议修复。

---

### 内部方法：`_create_response_stream`

作用：真正请求模型。

它有三层降级策略：

```text
1. stream_options 不支持 → 去掉 stream_options 重试
2. tools schema 不支持 → 去掉 tools 重试
3. 图片不支持 → 去掉图片重试
```

---

### 主循环

核心逻辑类似：

```python
response_stream = await _create_response_stream()

async for chunk in response_stream:
    # 读取 usage
    # 读取 finish_reason
    # 读取 reasoning_content
    # 读取 delta.content
    # 读取 delta.tool_calls
```

它会同时收集：

```python
content_acc   # 文本内容
tc_acc        # 工具调用
usage_seen    # token 用量
```

最后返回：

```python
return LabeledStepResult(
    label=label,
    text=text,
    tool_calls=ordered_tool_calls
)
```

---

# 五、`core/agentic/tool_dispatch.py`

这个文件负责 **工具执行**。

可以理解为：

```text
模型说要调用工具
    ↓
tool_dispatch.py 真正执行工具
    ↓
把工具结果变成 role=tool message
    ↓
交还给下一轮 LLM
```

---

## 1. `DispatchOutcome`

```python
@dataclass(frozen=True)
class DispatchOutcome:
```

表示一批工具执行后的汇总结果。

字段：

```python
sources              # 所有工具返回的来源
tool_messages        # role=tool 消息
tool_metadata_by_id  # 每个 tool_call_id 的 metadata
terminate            # 是否终止 Agent Loop
terminate_payload    # 终止内容
pause                # 是否暂停等待用户
pause_payload        # 暂停内容
pause_tool_call_id   # 哪个工具触发暂停
```

---

## 2. `dispatch_tool_calls`

```python
async def dispatch_tool_calls(...):
```

这是本文件主函数。

### 主流程

```text
拿工具注册表
    ↓
限制最多 8 个工具调用
    ↓
解析工具参数
    ↓
检测重复工具调用
    ↓
给每个工具生成 trace metadata
    ↓
并行执行工具
    ↓
收集结果
```

### 限制并行数量

```python
if len(tool_calls) > MAX_PARALLEL_TOOL_CALLS:
    tool_calls = tool_calls[:MAX_PARALLEL_TOOL_CALLS]
```

一般是最多并行 8 个工具调用。

---

## 3. `_detect_duplicate_calls`

```python
def _detect_duplicate_calls(prepared):
```

作用：检测模型是否重复调用同一个工具。

例如：

```text
search({"query":"AI"})
search({"query":"AI"})
```

第二个会被认为是重复。

特殊情况：

```text
ask_user
```

通常只允许一次，因为 UI 只能暂停等待一次用户回复。

---

## 4. `_duplicate_stub_result`

```python
def _duplicate_stub_result(...):
```

作用：重复工具不会真的执行，而是返回一个假的工具结果。

它解决两个问题：

```text
1. 满足 OpenAI tool_call 必须对应 tool message 的协议
2. 告诉模型不要重复调用同一个工具
```

---

## 5. `_prepare_tool_args`

```python
def _prepare_tool_args(tool_calls, context, kwarg_augmenter):
```

作用：把模型给出的工具调用整理成内部可执行格式。

模型给出的格式大概是：

```json
{
  "id": "call_xxx",
  "name": "search",
  "arguments": "{\"query\":\"AI\"}"
}
```

内部会整理成：

```python
(tool_call_id, tool_name, exec_args)
```

它还会调用：

```python
kwarg_augmenter(tool_name, tool_args, context)
```

这个很重要。

意思是：

```text
模型只传公开参数
服务端可以补充隐藏上下文参数
```

例如：

```python
session_id
source_index
_sandbox_mounts
```

---

## 6. `_build_per_tool_trace_meta`

```python
def _build_per_tool_trace_meta(...):
```

作用：给每个工具调用生成前端 trace 信息。

例如前端显示：

```text
Tool call: rag_search
Tool call: read_source
Tool call: web_fetch
```

每个工具都有独立 `call_id`，方便前端展示调用链。

---

## 7. `execute_tool_call`

```python
async def execute_tool_call(...):
```

作用：真正执行一个工具。

主流程：

```text
如果是 retrieve 类型 → 先显示检索开始
    ↓
registry.execute(tool_name, **tool_args)
    ↓
拿到 ToolResult
    ↓
返回 result_text / success / sources / metadata
```

如果工具执行失败：

```python
except Exception as exc:
```

不会直接让整个 Agent 崩掉，而是返回错误结果：

```python
{
  "result_text": "Error executing xxx",
  "success": False,
  "sources": [],
  "metadata": {"error": str(exc)}
}
```

---

## 8. `_collect_outcome`

```python
async def _collect_outcome(...):
```

作用：把所有工具结果汇总成 `DispatchOutcome`。

它会生成类似：

```python
{
  "role": "tool",
  "tool_call_id": tool_call_id,
  "name": tool_name,
  "content": result_text
}
```

这个非常关键。

因为下一轮 LLM 会看到：

```text
assistant: 我调用了 search
tool: search 的结果是……
```

然后模型继续推理。

如果工具返回：

```python
terminate_turn=True
```

Agent Loop 会结束。

如果工具返回：

```python
pause_for_user={...}
```

Agent Loop 会暂停，等待用户回答。

---

# 六、`core/agentic/loop.py`

这个文件是 DeepTutor Agent 的总调度器。

可以理解为：

```text
模型思考
    ↓
判断标签
    ↓
调用工具
    ↓
把工具结果塞回上下文
    ↓
继续问模型
    ↓
直到 FINISH
```

---

## 1. `LabelProtocol`

```python
@dataclass(frozen=True)
class LabelProtocol:
```

定义一个能力允许哪些标签。

字段：

```python
allowed        # 所有允许标签
terminal       # 哪些标签会结束循环
intermediate   # 哪些标签会继续循环
final          # 哪些标签内容要展示给用户
tool_label     # 哪个标签表示工具调用
```

例如：

```python
allowed = ("THINK", "TOOL", "FINISH")
terminal = {"FINISH"}
intermediate = {"THINK"}
final = {"FINISH"}
tool_label = "TOOL"
```

---

## 2. `LoopOutcome`

```python
@dataclass(frozen=True)
class LoopOutcome:
```

表示整个 Agent Loop 的最终结果。

字段：

```python
final_label   # 最后触发结束的标签
final_text    # 最终文本
iterations    # 跑了几轮
sources       # 引用来源
messages      # 完整消息历史
completed     # 是否正常完成
```

---

## 3. `LoopHost`

```python
class LoopHost(Protocol):
```

这是一个接口协议。

为什么需要它？

因为 `loop.py` 想保持通用，不想写死 Chat、Solve、Research 的业务逻辑。

所以它把业务差异交给 Host。

Host 需要提供这些方法：

---

### `guard_context_window`

```python
guard_context_window(messages)
```

作用：控制上下文长度。

如果 messages 太长，就需要裁剪，避免超过模型上下文窗口。

---

### `build_iteration_trace_meta`

```python
build_iteration_trace_meta(...)
```

作用：给当前 iteration 生成 trace 元数据。

用于前端显示当前 Agent 执行到了哪一步。

---

### `dispatch_tools`

```python
dispatch_tools(...)
```

作用：执行工具调用。

通常会进一步调用 `tool_dispatch.py` 里的 `dispatch_tool_calls`。

---

### `resolve_pause`

```python
resolve_pause(outcome)
```

作用：处理工具要求暂停等待用户输入的情况。

---

### `emit_terminator`

```python
emit_terminator(...)
```

作用：工具要求终止时，输出最终内容。

---

### `emit_final`

```python
emit_final(...)
```

作用：把最终回答发给前端。

---

### `validate_terminal`

```python
validate_terminal(...)
```

作用：在接受终止标签前，再做一次验证。

例如：模型说 `FINISH`，但内容可能不符合当前能力要求，此时可以阻止结束。

---

### `assistant_message_with_tool_calls`

```python
assistant_message_with_tool_calls(...)
```

作用：把模型的 tool_call 包装成 assistant message。

这一步是为了符合 OpenAI 的消息协议。

---

### `protocol_retry_notice`

```python
protocol_retry_notice(...)
```

作用：协议错误时，生成给前端看的提示。

---

### `protocol_repair_message`

```python
protocol_repair_message(...)
```

作用：协议错误时，生成塞回给模型的修复提示。

---

### `force_finalize`

```python
force_finalize(...)
```

作用：达到最大轮数后强制收尾。

防止 Agent 无限循环。

---

## 4. `run_agentic_loop`

```python
async def run_agentic_loop(...):
```

这是整个 Agent Loop 的核心方法。

### 主流程

```text
for iteration in max_iterations:
    1. guard_context_window
    2. before_iteration
    3. run_labeled_step
    4. 检查协议错误
    5. 如果 FINISH → 输出最终答案，结束
    6. 如果 TOOL → 执行工具，把工具结果放回 messages
    7. 如果 THINK → 把思考内容放回 messages，继续
    8. 如果异常标签 → 修复提示，重试

如果超过最大轮数：
    force_finalize
```

---

### 第一步：上下文窗口保护

```python
await host.guard_context_window(messages)
```

防止 messages 太长，超出模型上下文。

---

### 第二步：每轮开始钩子

```python
before_iteration = getattr(host, "before_iteration", None)
```

如果 Host 实现了 `before_iteration`，就执行。

用途包括：

```text
告诉模型当前是第几轮
动态注入计划
动态加入状态
```

---

### 第三步：调用模型

```python
step = await run_labeled_step(...)
```

这一轮模型调用返回：

```python
step.label
step.text
step.tool_calls
```

---

### 第四步：检查协议错误

```python
violation = _protocol_violation(step, protocol)
```

可能的协议错误包括：

```text
没有输出标签
FINISH 里面却带 tool_calls
TOOL 标签但没有 tool_calls
输出多个阶段标签
```

如果有错误：

```python
_emit_retry_notice(...)
_append_repair_messages(...)
continue
```

也就是提醒模型重新按协议输出。

---

### 第五步：终止标签

```python
if step.label in protocol.terminal:
```

比如 `FINISH`。

然后：

```python
await host.emit_final(step.text, final_meta)
```

把最终答案发给用户。

最后：

```python
completed = True
break
```

---

### 第六步：工具标签

```python
if protocol.tool_label is not None and step.label == protocol.tool_label:
```

说明模型这轮要调用工具。

先把 assistant 工具调用消息加入上下文：

```python
messages.append(
    host.assistant_message_with_tool_calls(...)
)
```

然后执行工具：

```python
outcome = await host.dispatch_tools(...)
```

再把工具结果加入上下文：

```python
messages.extend(outcome.tool_messages)
```

如果工具要求暂停：

```python
if outcome.pause:
    resumed = await host.resolve_pause(outcome)
```

如果工具要求终止：

```python
if outcome.terminate:
    await host.emit_terminator(...)
```

否则继续下一轮。

---

### 第七步：中间标签

```python
if step.label in protocol.intermediate:
```

比如 `THINK`。

它不会结束，也不会调用工具，只是把模型这轮思考加入上下文：

```python
messages.append({"role": "assistant", "content": step.text})
```

然后继续下一轮。

---

## 5. `_protocol_violation`

```python
def _protocol_violation(step, protocol):
```

作用：检查模型有没有违反协议。

它会检查：

```python
step.label == LABEL_UNKNOWN
```

表示模型没输出合法标签。

```python
find_inline_labels(step.text)
```

表示正文里又出现了别的标签，可能模型一次输出了多个阶段。

```python
step.label == tool_label and not step.tool_calls
```

表示模型说要调用工具，但没有真正发出 tool_call。

```python
step.label != tool_label and step.tool_calls
```

表示模型不是工具标签，却带了 tool_call。

这些都会触发修复流程。

---

## 6. `_emit_retry_notice`

```python
async def _emit_retry_notice(...):
```

作用：给前端发一个 warning。

意思是：

```text
模型输出格式错了，我让它重试
```

---

## 7. `_append_repair_messages`

```python
def _append_repair_messages(...):
```

作用：把模型刚才错误输出的一部分保存下来，然后追加一条用户消息告诉模型怎么改。

逻辑类似：

```python
messages.append({"role": "assistant", "content": clipped})
messages.append({"role": "user", "content": host.protocol_repair_message(violation)})
```

这样下一轮模型就知道：

```text
你刚才格式错了，请按 THINK / TOOL / FINISH 重来
```

---

# 七、最终总结

这 5 个文件可以这样分层理解：

```text
第一层：请求上下文
    context.py

第二层：工具协议
    tool_protocol.py

第三层：单次模型调用
    labeled_step.py

第四层：工具执行
    tool_dispatch.py

第五层：多轮调度
    loop.py
```

完整执行过程：

```text
用户输入
    ↓
UnifiedContext 保存请求上下文
    ↓
Capability 构造 messages / tools / protocol
    ↓
run_agentic_loop 开始循环
    ↓
run_labeled_step 调用模型
    ↓
模型输出标签：THINK / TOOL / FINISH
    ↓
如果 THINK：保存思考，继续下一轮
    ↓
如果 TOOL：dispatch_tool_calls 执行工具
    ↓
工具结果作为 role=tool message 塞回上下文
    ↓
继续调用模型
    ↓
如果 FINISH：输出最终答案
```

---

## 八、如果你要用 C# 复刻一个简化版

最小版本可以这样设计：

```text
AgentContext
ToolDefinition
ToolResult
ITool
AgentLoop
LabeledStep
ToolDispatcher
```

C# 伪结构：

```csharp
public class AgentContext
{
    public string UserMessage { get; set; }
    public List<Message> History { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}

public interface ITool
{
    ToolDefinition GetDefinition();
    Task<ToolResult> ExecuteAsync(Dictionary<string, object> args);
}

public class AgentLoop
{
    public async Task RunAsync(AgentContext context)
    {
        while (true)
        {
            var step = await RunLabeledStepAsync(context);

            if (step.Label == "FINISH")
                break;

            if (step.Label == "TOOL")
            {
                var results = await DispatchToolsAsync(step.ToolCalls);
                context.History.AddRange(results);
            }

            if (step.Label == "THINK")
            {
                context.History.Add(new Message("assistant", step.Text));
            }
        }
    }
}
```

---

## 九、一句话记忆

```text
DeepTutor 的 core/agentic 本质就是：

让模型每轮先声明“我要思考、我要调用工具、还是我要结束”，
系统根据这个声明执行对应动作，直到得到最终答案。
```
