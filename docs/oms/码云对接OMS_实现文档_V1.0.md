# 码云(Gitee) 对接 OMS 实现文档

> 版本：V1.0 | 日期：2026-08-18
> 来源：基于 `D:\oms-platform\gitee_import.py`（V1.0）源码整理，凭证信息已脱敏
> 适用读者：OMS 平台开发/维护人员

---

## 一、对接概述

**目标**：将码云（Gitee）企业版项目任务数据，按周自动导入 OMS 系统的"开发工时"流程表单（宜搭），触发审批流，用于开发人员工时核算、项目成本统计与能效分析。

**数据流**：

```
码云企业版 API
  → 全量扫描任务（按 updated_at 降序翻页）
  → 筛选：finished_at ∈ 目标周 + 状态 ∈ {已完成/待测试/待提单人自测}
  → 程序映射：program_id → OMS 项目配置（对照表）
  → 人员匹配：assignee.remark → 人天单价表（钉钉ID/单价/单号/实例ID）
  → 部门过滤：仅保留 5 个目标部门
  → 写入 OMS 开发工时表单（宜搭 OpenAPI，触发审批流）
  → 落库导入批次/明细（SQLite）+ 自动刷新门户数据
```

**角色**：
| 角色 | 说明 |
|------|------|
| 码云 | 任务数据源（企业ID `7013105`，浙江国坤智能科技有限公司） |
| OMS 平台 | FastAPI + SQLite 本地服务，负责拉取、匹配、写入 |
| 宜搭/钉钉 | 开发工时表单载体，提供 OpenAPI 写入与审批流 |

---

## 二、凭证清单（已脱敏）

> ⚠️ 以下凭证均硬编码于 `gitee_import.py` 顶部常量区，**文档中已全部脱敏**。实际部署时应改为环境变量或独立配置文件。

| 凭证 | 用途 | 脱敏展示 |
|------|------|---------|
| Gitee 企业 access_token | 码云企业 API 认证 | `a84c****`（尾 32 位省略） |
| 钉钉 AppKey | 获取钉钉 OpenAPI token | `dingaijrk****` |
| 钉钉 AppSecret | 同上（配合 AppKey） | `****`（完整打码） |
| 宜搭 systemToken | 宜搭 OpenAPI 写入鉴权 | `LXE66V91****` |
| 宜搭 APP_TYPE | 应用标识（非密钥） | `APP_RNX9TTEA2YZAMHTWO53G` |
| 宜搭表单 FORM_UUID | 开发工时表单（非密钥） | `FORM-E68721DA238B4F00BB4E5E85F1A133A0FX3J` |
| 审批流 PROCESS_CODE | 开发工时审批流（非密钥） | `TPROC--NMB66R81****` |
| 系统账号 USER_ID | 写入操作用户（非密钥） | `0415681538826060` |

**安全要求**：
1. 禁止将 `gitee_import.py` 原始凭证复制到任何对外文档/聊天记录
2. 文档、报表、演示中引用凭证时一律脱敏（保留前 6-10 位便于识别）
3. 建议后续迁移至环境变量（`.env` / 系统环境变量），并纳入凭证轮换机制

---

## 三、代码与文件结构

```
D:\oms-platform\
├── gitee_import.py              # 主导入脚本（拉取/匹配/写入/导出）
├── generate_gitee_preview.py   # 导入前预览生成
├── puller.py                   # pull_all_data() 宜搭全量拉取（导入后刷新门户）
├── pusher.py                   # 钉钉推送工具（通知管理员）
├── db.py                       # gitee_import_logs / gitee_import_items 表操作
├── main.py                     # FastAPI 调度任务 + /api/gitee-import/* REST 接口
├── reports/                    # 导入明细 Excel 预览存放目录
└── data/oms_platform.db        # SQLite 数据库（WAL 模式）
```

---

## 四、核心实现详解

### 4.1 Gitee 企业 API 调用封装

```python
def gitee_api_get(path):
    """GET 请求码云企业 API，15s 超时，非 2xx 抛 ConnectionError。"""
    conn = http.client.HTTPSConnection('api.gitee.com', timeout=15)
    conn.request('GET', path)
    r = conn.getresponse()
    body = r.read().decode('utf-8')
    if r.status < 200 or r.status >= 300:
        raise ConnectionError(f'Gitee API HTTP {r.status}: {body[:200]}')
    return json.loads(body)
```

- 端点：`/enterprises/{企业ID}/issues`
- 分页：`per_page=100`，最多翻 500 页
- 排序：`sort=updated_at&direction=desc`（按更新时间降序）
- 翻页终止条件：连续出现 `updated_at < (本周一 - 30 天)` 且本周无完成任务时终止（避免任务跨多页分布导致的漏拉）

### 4.2 时间窗口与批次号

| 场景 | 时间范围 | 批次号格式 |
|------|---------|-----------|
| 定时（默认） | 上周一 00:00 ~ 上周日 23:59 | `GW{周数}_{YYYYMMDD}` |
| 手动测试 | 本周一 ~ 今天 | `GT{周数}_{YYYYMMDD}` |
| 指定周 | `--week 2026-W26` 参数 | 同上 `GW` 规则 |

筛选字段为任务的**实际完成时间 `finished_at`**。

### 4.3 本地数据加载（3 张映射表）

导入前从本地宜搭缓存表加载三份数据：

| 数据 | 来源表 | 用途 |
|------|--------|------|
| 码云项目映射 | 码云项目映射表（≤500 行） | `program_id → OMS 配置`（编码/名称/实例ID/客户等） |
| 人天标准单价 | 人天单价表（≤500 行） | 人员 → 钉钉ID/单价/系数/单号/实例ID/部门 |
| 项目立项 | 项目立项表（≤500 行） | OMS 编码/客户名 → 产品类型/合同编码/项目经理/合同名称 |

**程序映射兜底**：无 OMS 编码的项目保留在映射中（`has_oms_code=False`），写入时以项目名兜底关联立项。

### 4.4 任务拉取与状态筛选

```python
# 状态白名单（二选一命中即保留）
state == 'closed'                      # 已完成
issue_state.title ∈ {待测试, 待提单人自测, 已完成}
```

- 只保留 `finished_at` 落在目标周内的任务
- 只保留 `program_id` 存在于映射表的任务
- 按 `ident`（工作项ID）去重
- 缺陷识别：`issue_type.title` 含"缺陷"或"bug"（不区分大小写）

### 4.5 人员匹配与部门过滤

**人员匹配**（`assignee.remark` 企业昵称 → 人天单价表中文名）：
1. 精确匹配全名
2. 去掉后缀（`-MES` / `-APS` / `-IOT` 等）后的基础名匹配
3. 前缀互匹配（`remark` 开头 ⊂ 中文名，或中文名开头 ⊂ `remark`）

匹配成功后提取：钉钉用户ID、人天单价、人天单价单号、人天单价表单实例ID、部门。

**部门过滤**：仅保留 5 个目标部门（`TARGET_DEPTS`）：

| 部门 | 宜搭部门ID |
|------|-----------|
| 二开组 | 913694866 |
| 性能优化及服务组 | 1000126791 |
| 开发产品组 | 914021283 |
| 产品巴 | 935522956 |
| 研发巴 | （待补充） |

> 非目标部门 → 记录为 `skipped`（失败原因"部门过滤"），不写入 OMS。

### 4.6 OMS 开发工时表单字段映射

写入时构建 `formDataJson`（约 31 个字段），完整映射见《码云工时导入OMS_字段映射对照表_V2.1》，核心字段：

| 分类 | 字段ID | 数据来源 |
|------|--------|---------|
| 项目 | `textField_mgopeecs` / `textField_mi5opzbj` | 对照表 OMS 编码/名称 |
| 人员 | `employeeField_mgopeecq` / `employeeField_mhoi6ti6` | 人天单价表钉钉ID |
| 工时 | `numberField_mgaaz64w` | 码云 `estimated_duration`（小时） |
| 成本 | `numberField_mg65dina` / `numberField_mgoybuyz` | 工时÷8 天、天×单价金额 |
| 时间 | `dateField_m1elynje` / `dateField_mj8cs5fb` | `finished_at` / `created_at`（13位时间戳） |
| 溯源 | `textField_mgh9gvqy` | 码云 `ident`（**去重关键字段**） |
| 模块 | `textField_mi5lh23e` | 码云 issue_extra 字段 7290 选项 |
| BUG责任人 | `textField_mmd6wscy` | issue_extra 字段 5572（仅缺陷） |
| 成本状态 | `selectField_mi5pi8ze` | 缺陷且 0 工时 → "不影响成本"，否则"影响成本" |
| 部门 | `departmentSelectField_mhy5tt2l` | 部门名+ID 结构化值 |
| 关联 | `associationFormField_mgopeecn` / `associationFormField_mi30itj1` | 人天单价表/对照表实例ID |

**功能模块选项映射**（issue_extra 字段 7290）：1=APS, 2=WMS, 3=生产管理, 4=质量管理, 5=设备管理, 6=模具管理, 7=供应商打标, 8=安灯管理, 9=数据同步, 10=系统管理, 11=数据报表, 12=刀具管理, 13=数据采集, 14=AGV, 15=UI设计。

### 4.7 写入宜搭（钉钉 OpenAPI）

```python
# 1. OAuth2 获取钉钉 access_token
POST https://api.dingtalk.com/v1.0/oauth2/accessToken
    body: {appKey, appSecret}
    → {accessToken}

# 2. 启动流程实例（触发审批流）
POST https://api.dingtalk.com/v1.0/yida/processes/instances/start
    headers: {x-acs-dingtalk-access-token: <token>}
    body: {
      appType, formUuid, processCode,
      formDataJson: <字段映射JSON>,
      systemToken, userId
    }
    → result = OMS 实例ID（后续溯源用）
```

- 15s 超时；HTTP 200 视为成功，返回的 `result` 即宜搭表单实例ID
- 非 200 且含"已被导入" → 判定重复，跳过

### 4.8 去重机制（双层）

| 层级 | 机制 |
|------|------|
| 内存去重 | 单次运行内按 `ident` 去重（`seen_idents`） |
| 服务端去重 | 重复写入时宜搭 API 返回"已被导入" → 记录 `ALREADY_IMPORTED` 跳过 |

跨批次去重依赖 OMS 侧 `textField_mgh9gvqy`（工作项ID）字段，导入前无需查询历史。

### 4.9 失败分类与重试

| 失败原因码 | 含义 | 处理 |
|-----------|------|------|
| `部门过滤` | 人员不在 5 个目标部门 | 记录 skipped，不写入 |
| `PERSON_NOT_FOUND` | 码云昵称匹配不到人天单价表 | 记录 failed，可重试 |
| `OMS_API_ERROR` | 宜搭 API 返回错误（非重复） | 记录 failed，可重试 |
| `NETWORK_ERROR` | 网络/超时异常 | 记录 failed，可重试 |
| `ALREADY_IMPORTED` | 服务端检测重复 | 记录 skipped，无需处理 |

**批次状态**：`success`（全成功）/ `partial`（部分失败）/ `failed`（全失败）/ `running`。

**重试机制**：Web 界面支持单条重试（重新从码云拉取该任务最新数据再写入）、按失败原因批量重试、整批失败重试；失败原因统计按条数降序汇总在批次记录中。

---

## 五、数据库设计

**`gitee_import_logs`（批次表）**：

| 字段 | 说明 |
|------|------|
| id / batch_id | 主键 / 批次号（GWxx_日期） |
| week_start / week_end | 目标周起止 |
| total_gitee / total_mapped | 拉取总数 / 映射数 |
| total_imported / total_failed / total_skipped | 成功/失败/跳过 |
| status | running/success/partial/failed |
| error_summary | 失败原因汇总 |
| started_at / finished_at | 起止时间 |

**`gitee_import_items`（明细表）**：

| 字段 | 说明 |
|------|------|
| id / batch_id / work_item_id | 主键 / 批次 / 码云任务ID（ident） |
| task_title / gitee_project / oms_project | 任务标题、码云/OMS 项目 |
| person_name / hours | 人员、工时 |
| status / fail_reason | 状态、失败原因码 |
| oms_instance_id | 写入成功的宜搭实例ID（溯源） |
| gitee_state / finished_at | 码云状态、完成日期 |

---

## 六、调度与接口层

### 6.1 定时任务（APScheduler cron）

| 任务 | report_type | 调度 | 说明 |
|------|------------|------|------|
| 周一码云周导入OMS | `gitee_weekly_import` | 周一 10:00（`0 10 * * 1`） | 拉取**上周**任务，实际写入并触发审批流，成功后自动 `pull_all_data()` 刷新门户 |

> 定时任务默认**禁用**，需在系统"定时任务"页手动启用。

### 6.2 REST API（/api/gitee-import/*，需登录且 admin/manage）

| 方法 | 路径 | 功能 |
|------|------|------|
| GET | `/logs` | 批次列表（分页/状态筛选） |
| GET | `/logs/{log_id}` | 批次详情 + 状态计数 + 失败原因汇总 |
| GET | `/items?batch_id=` | 批次明细（分页/状态/关键字/人员筛选） |
| POST | `/trigger` | 手动触发导入（**dry_run 模式**，仅记录） |
| POST | `/trigger-this-week` | 手动拉取本周数据（测试用，仅记录） |
| POST | `/retry/{item_id}` | 单条重试（重新拉码云数据并写入） |
| POST | `/retry-batch` | 按失败原因批量重试 |
| POST | `/retry-all/{batch_id}` | 整批失败项全部重试 |
| POST | `/push/{item_id}` | 手动推送单条到 OMS |
| POST | `/push-batch` | 批量推送 |
| GET | `/export/{batch_id}` | 导出批次明细 Excel |

---

## 七、运行方式

### 7.1 命令行

```bash
# 预览（默认 dry_run，只拉取并记录日志，不写入 OMS）
D:\oms-platform\python\python.exe gitee_import.py

# 指定周预览
D:\oms-platform\python\python.exe gitee_import.py --week 2026-W26

# 实际写入 OMS（触发审批流）
D:\oms-platform\python\python.exe gitee_import.py --no-dry-run

# 查看最近 10 个批次汇总
D:\oms-platform\python\python.exe gitee_import.py --summary
```

> ⚠️ `--no-dry-run` 会真实写入宜搭并触发审批流，执行前请确认。

### 7.2 Web 界面

登录 OMS 平台（admin/管理者）→ 数据管理 → 码云导入页：查看批次、明细、失败原因，支持单条/批量重试与 Excel 导出。

### 7.3 Excel 导出

`export_import_items_xlsx()` 使用 openpyxl 生成带格式的明细表：
- 表头深蓝底白字，按状态着色（成功=绿 / 失败=红 / 跳过=黄）
- 包含：工作项ID、任务标题、OMS/码云项目、人员、工时、状态、失败原因、完成日期、OMS实例ID 等 13 列

---

## 八、安全注意事项（硬性要求）

1. **凭证不落文档**：本文件及所有衍生资料中的令牌一律脱敏；原始凭证仅存在于服务器代码/配置
2. **最小权限**：Gitee token 建议使用仅含 `enterprises/issues` 只读权限的专用令牌
3. **定期轮换**：钉钉 AppSecret、宜搭 systemToken 建议按安全策略定期更换；token 失效时导入任务会返回"Gitee Token 失效"，系统会记录失败并通过钉钉通知管理员
4. **dry_run 兜底**：所有自动化入口默认 dry_run，防止误写入

---

## 九、已知限制与演进建议

| 限制 | 说明 | 建议 |
|------|------|------|
| API 翻页边界 | 约 1% 任务因 `finished_at` 与 `updated_at` 不同步而遗漏 | 引入按 program_id 分项目拉取，或使用 Gitee 时间过滤参数 |
| 部门 ID 映射 | 部门名 → 宜搭部门ID 为手工维护（`DEPT_NAME_TO_ID`），"研发巴"待补充 | 改为从宜搭部门表动态拉取 |
| 凭证硬编码 | 凭证在脚本顶部常量区 | 迁移至环境变量/配置文件 |
| 字段强耦合 | 表单字段 ID（如 `textField_*`）硬编码 | 构建字段字典配置化，降低表单改动风险 |
| 缺省单价 | 人员匹配不到单价时默认 `886.01` | 校验人天单价表完整性，缺省值可配置 |

---

*文档生成自 `gitee_import.py` 源码，与《码云导入OMS_最终业务逻辑_V1.md》《码云工时导入OMS_字段映射对照表_V2.1.md》配套使用。*
