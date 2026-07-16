# Settings Model Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the AiAgent settings center with a left sidebar, a settings hub, a Models sub-hub, and per-service model configuration pages backed by SQL Server.

**Architecture:** Keep the frontend information architecture close to DeepTutor: `Settings -> Models -> LLM/Embedding/Search/TTS/STT/Image/Video`. Keep the backend API stable around settings DTOs, but move persistence from local JSON toward normalized SqlSugar entities in SQL Server. Tables are created by SqlSugar CodeFirst from entities; SQL scripts are kept only as DBA/reference material. Provider support is data-driven so new vendors can be seeded without rewriting page logic.

**Tech Stack:** Next.js frontend, C# .NET 9 backend, Furion dynamic API, SqlSugar ORM, SQL Server.

---

## 1. DeepTutor Reference Points

Use these files as the main behavioral and UI reference:

- `E:/项目/know-why/DeepTutor/web/components/sidebar/UtilitySidebar.tsx`: left utility sidebar with top navigation, recent items, and bottom settings entry.
- `E:/项目/know-why/DeepTutor/web/app/(utility)/settings/layout.tsx`: settings area layout.
- `E:/项目/know-why/DeepTutor/web/lib/settings-nav.ts`: single source of truth for settings categories, model leaves, breadcrumbs, storage labels, and route rules.
- `E:/项目/know-why/DeepTutor/web/components/settings/SettingsHub.tsx`: settings first-level hub.
- `E:/项目/know-why/DeepTutor/web/app/(utility)/settings/models/page.tsx`: Models second-level page.
- `E:/项目/know-why/DeepTutor/web/app/(utility)/settings/llm/page.tsx`: LLM third-level page.
- `E:/项目/know-why/DeepTutor/web/components/settings/ServiceConfigEditor.tsx`: profile list, provider connection card, model list, diagnostics, Save Draft, Apply.
- `E:/项目/know-why/DeepTutor/deeptutor/services/provider_registry.py`: LLM provider registry, including AiHubMix, Anthropic, Azure OpenAI, BytePlus, DashScope, DeepSeek, Gemini, GitHub Copilot, Groq, LM Studio, MiniMax, Mistral, Moonshot, Ollama, OpenRouter, SiliconFlow, Zhipu AI.
- `E:/项目/know-why/DeepTutor/deeptutor/services/config/model_catalog.py`: model catalog persistence shape.
- `E:/项目/know-why/DeepTutor/deeptutor/services/config/provider_runtime.py`: provider runtime options for embedding, voice, image, and video.

AiAgent does not need to copy DeepTutor code one-to-one. It should copy the information architecture and important interaction behavior.

## 2. Target Page Structure

### 2.1 Global Shell

AiAgent should have a permanent left sidebar on desktop.

Sidebar sections:

- Brand area: `AiAgent` logo text and collapse button.
- Main navigation: Home, Partners, My Agents, Co-Writer, Book, Learning Space.
- Recents block: recent conversations or recent workspace entries. If data is not ready, show empty state space without fake records.
- Bottom navigation: Memory, Knowledge Center, Settings.
- Active state: Settings is active on `/settings` and all nested routes.

Layout rules:

- Sidebar width: `220px` desktop, collapsible later.
- Main area: independent scroll, white background, content max width around `960px` to match screenshots.
- Do not put the whole page in a card. Cards are only for repeated setting tiles and editor panels.

### 2.2 First-Level Page: Settings Hub

Route: `/settings`

Purpose: overview of settings modules.

Required modules:

- Appearance
- Network
- Models
- Knowledge Base
- Chat
- Partners & Agents
- Memory

Status strip:

- Backend: Online / Checking / Offline
- LLM: active model name or Not set
- Embedding: active model name or Not set
- Search: active provider or Not set

Click behavior:

- Models card navigates to `/settings/models`.
- Single-setting cards can navigate directly to their leaf page later.

### 2.3 Second-Level Page: Models

Route: `/settings/models`

Purpose: list all model-related service categories.

Cards:

- LLM: Language model providers and active profile.
- Embedding: Embedding model providers and dimensions.
- Search: Web search providers.
- Text-to-Speech: Text-to-speech for reading replies aloud.
- Speech-to-Text: Speech-to-text for microphone input.
- Image Generation: Text-to-image model for chat image generation.
- Video Generation: Text-to-video model for chat video generation.

Each card shows:

- Icon tile.
- Title.
- Status chip: `Configured` or `Not set`.
- Short description.
- External arrow icon.

Click behavior:

- Use `/settings/models/llm`, `/settings/models/embedding`, `/settings/models/search`, `/settings/models/tts`, `/settings/models/stt`, `/settings/models/image`, `/settings/models/video`.
- Add optional redirects from DeepTutor-style legacy routes like `/settings/llm` to `/settings/models/llm` only if needed.

### 2.4 Third-Level Pages: Service Editor

Routes:

- `/settings/models/llm`
- `/settings/models/embedding`
- `/settings/models/search`
- `/settings/models/tts`
- `/settings/models/stt`
- `/settings/models/image`
- `/settings/models/video`

Shared layout:

- Top line: `Saved to database: ai_model_profile / ai_model`.
- Breadcrumb: `Settings > Models > LLM`.
- Title and description.
- Top-right actions: Tour, Save Draft, Apply.
- Left panel: Profiles.
- Main panel 1: Provider connection.
- Main panel 2: Models.
- Main panel 3: Diagnostics.

Profile behavior:

- A service can have multiple profiles.
- Only one active profile per service.
- Profile delete should be soft delete.
- API keys are never displayed in plain text after save. Return `********` to the frontend.
- If frontend submits `********`, backend keeps the old encrypted secret.

Provider connection behavior:

- Provider dropdown is filtered by service type.
- Base URL is prefilled from provider seed data but editable.
- API key supports show/hide locally.
- Extra fields are collapsible and stored as JSON.

Model behavior:

- One profile can contain multiple models.
- LLM models include `modelId` and `contextWindow`.
- Embedding models include `modelId`, `dimension`, and `sendDimensions`.
- TTS models include `modelId`, `voice`, `responseFormat`.
- STT models include `modelId`, `language`, `responseFormat`.
- Image models include `modelId`, `size`, `quality`, `style`.
- Video models include `modelId`, `aspectRatio`, `duration`, `resolution`.

Diagnostics behavior:

- `Run test` calls the backend test endpoint for the selected service/profile/model.
- Store diagnostic history in SQL Server.
- Show latest state: `Not run`, `Running`, `Success`, `Failed`.

## 3. Backend API Design

Current backend entry should remain Furion dynamic API, not `ControllerBase`.

Current service boundary:

- `E:/项目/know-why/AiAgent/backed/Services/Settings/SettingsAppService.cs`

Recommended endpoints:

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/v1/settings` | Settings hub status summary. |
| `GET` | `/api/v1/settings/catalog` | Full model catalog for all services. |
| `PUT` | `/api/v1/settings/catalog` | Save draft catalog changes. |
| `POST` | `/api/v1/settings/apply` | Apply current or submitted catalog. |
| `GET` | `/api/v1/settings/model-services` | Return service cards and configured state. |
| `GET` | `/api/v1/settings/providers?service=llm` | Return provider choices for one service. |
| `POST` | `/api/v1/settings/tests/{service}/start` | Run a diagnostic test. |
| `GET` | `/api/v1/settings/tests/{service}/latest` | Return latest diagnostic result. |

DTO direction:

- Keep frontend-facing DTOs close to the existing `CatalogPayload`, `ServiceCatalogDto`, `ModelProfileDto`, and `ModelEntryDto`.
- Add database IDs as optional fields: `profileId`, `modelId`, `providerId`.
- Keep `service` string values stable: `llm`, `embedding`, `search`, `tts`, `stt`, `imagegen`, `videogen`.

Backend business rules:

- One active profile per service.
- One active model per profile is allowed; multiple configured models are allowed.
- Provider definitions are seed data and should not contain user API keys.
- User API keys live only on profiles and must be encrypted before saving.
- All queries must filter `IsDeleted = 0`.
- Updates that change profile, models, and active state should run in a transaction.

## 4. SQL Server Tables

Use SQL Server as requested by the existing connection string:

```json
"Default": "server=124.70.221.213,1666;uid=jinmacps;pwd=1;database=AGENT;MultipleActiveResultSets=true;TrustServerCertificate=true"
```

### 4.1 `ai_model_provider`

Provider registry seed table. It answers: which provider can be selected for which service?

```sql
CREATE TABLE dbo.ai_model_provider (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ai_model_provider PRIMARY KEY,
    ServiceType NVARCHAR(32) NOT NULL,
    ProviderCode NVARCHAR(64) NOT NULL,
    ProviderName NVARCHAR(128) NOT NULL,
    BindingType NVARCHAR(64) NOT NULL,
    BaseUrl NVARCHAR(512) NULL,
    AuthType NVARCHAR(32) NOT NULL CONSTRAINT DF_ai_model_provider_AuthType DEFAULT ('bearer'),
    ApiVersion NVARCHAR(64) NULL,
    IconKey NVARCHAR(64) NULL,
    DefaultModel NVARCHAR(128) NULL,
    DefaultDimension INT NULL,
    DefaultVoice NVARCHAR(64) NULL,
    CapabilitiesJson NVARCHAR(MAX) NULL,
    SortOrder INT NOT NULL CONSTRAINT DF_ai_model_provider_SortOrder DEFAULT (100),
    IsEnabled BIT NOT NULL CONSTRAINT DF_ai_model_provider_IsEnabled DEFAULT (1),
    IsDeleted BIT NOT NULL CONSTRAINT DF_ai_model_provider_IsDeleted DEFAULT (0),
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ai_model_provider_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt DATETIME2(3) NULL,
    Remark NVARCHAR(512) NULL
);

CREATE UNIQUE INDEX UX_ai_model_provider_Service_Code
ON dbo.ai_model_provider(ServiceType, ProviderCode)
WHERE IsDeleted = 0;
```

### 4.2 `ai_model_profile`

User-editable provider profile table. It answers: how does AiAgent connect to this provider?

```sql
CREATE TABLE dbo.ai_model_profile (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ai_model_profile PRIMARY KEY,
    ServiceType NVARCHAR(32) NOT NULL,
    ProfileName NVARCHAR(128) NOT NULL,
    ProviderId BIGINT NULL,
    ProviderCode NVARCHAR(64) NOT NULL,
    ProviderName NVARCHAR(128) NOT NULL,
    BindingType NVARCHAR(64) NOT NULL,
    BaseUrl NVARCHAR(512) NULL,
    ApiKeyCipher NVARCHAR(MAX) NULL,
    ApiVersion NVARCHAR(64) NULL,
    AuthType NVARCHAR(32) NOT NULL CONSTRAINT DF_ai_model_profile_AuthType DEFAULT ('bearer'),
    ExtraHeadersJson NVARCHAR(MAX) NULL,
    ExtraOptionsJson NVARCHAR(MAX) NULL,
    ProxyUrl NVARCHAR(512) NULL,
    MaxResults INT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_ai_model_profile_IsActive DEFAULT (0),
    SortOrder INT NOT NULL CONSTRAINT DF_ai_model_profile_SortOrder DEFAULT (100),
    IsDeleted BIT NOT NULL CONSTRAINT DF_ai_model_profile_IsDeleted DEFAULT (0),
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ai_model_profile_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt DATETIME2(3) NULL,
    Remark NVARCHAR(512) NULL
);

ALTER TABLE dbo.ai_model_profile
ADD CONSTRAINT FK_ai_model_profile_Provider
FOREIGN KEY (ProviderId) REFERENCES dbo.ai_model_provider(Id);

CREATE INDEX IX_ai_model_profile_Service_Active
ON dbo.ai_model_profile(ServiceType, IsActive)
WHERE IsDeleted = 0;

CREATE UNIQUE INDEX UX_ai_model_profile_OneActive
ON dbo.ai_model_profile(ServiceType)
WHERE IsDeleted = 0 AND IsActive = 1;
```

### 4.3 `ai_model`

Model list under each profile. It answers: which concrete model IDs can this profile use?

```sql
CREATE TABLE dbo.ai_model (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ai_model PRIMARY KEY,
    ProfileId BIGINT NOT NULL,
    ServiceType NVARCHAR(32) NOT NULL,
    ModelName NVARCHAR(128) NOT NULL,
    ModelId NVARCHAR(256) NOT NULL,
    ContextWindow INT NULL,
    Dimension INT NULL,
    SendDimensions BIT NULL,
    Voice NVARCHAR(64) NULL,
    ResponseFormat NVARCHAR(64) NULL,
    Language NVARCHAR(32) NULL,
    Size NVARCHAR(64) NULL,
    Quality NVARCHAR(64) NULL,
    Style NVARCHAR(64) NULL,
    AspectRatio NVARCHAR(64) NULL,
    DurationSeconds INT NULL,
    Resolution NVARCHAR(64) NULL,
    ExtraOptionsJson NVARCHAR(MAX) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_ai_model_IsActive DEFAULT (0),
    SortOrder INT NOT NULL CONSTRAINT DF_ai_model_SortOrder DEFAULT (100),
    IsDeleted BIT NOT NULL CONSTRAINT DF_ai_model_IsDeleted DEFAULT (0),
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ai_model_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt DATETIME2(3) NULL,
    Remark NVARCHAR(512) NULL
);

ALTER TABLE dbo.ai_model
ADD CONSTRAINT FK_ai_model_Profile
FOREIGN KEY (ProfileId) REFERENCES dbo.ai_model_profile(Id);

CREATE INDEX IX_ai_model_Profile
ON dbo.ai_model(ProfileId, SortOrder)
WHERE IsDeleted = 0;

CREATE UNIQUE INDEX UX_ai_model_Profile_ModelId
ON dbo.ai_model(ProfileId, ModelId)
WHERE IsDeleted = 0;
```

### 4.4 `ai_model_diagnostic_run`

Diagnostic execution history.

```sql
CREATE TABLE dbo.ai_model_diagnostic_run (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ai_model_diagnostic_run PRIMARY KEY,
    ServiceType NVARCHAR(32) NOT NULL,
    ProfileId BIGINT NULL,
    ModelId BIGINT NULL,
    ProviderCode NVARCHAR(64) NULL,
    ModelCode NVARCHAR(256) NULL,
    State NVARCHAR(32) NOT NULL,
    Message NVARCHAR(1000) NULL,
    RequestJson NVARCHAR(MAX) NULL,
    ResponseJson NVARCHAR(MAX) NULL,
    StartedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ai_model_diagnostic_run_StartedAt DEFAULT (SYSUTCDATETIME()),
    FinishedAt DATETIME2(3) NULL,
    CreatedBy NVARCHAR(64) NULL
);

CREATE INDEX IX_ai_model_diagnostic_run_Service_Time
ON dbo.ai_model_diagnostic_run(ServiceType, StartedAt DESC);
```

### 4.5 `ai_setting_snapshot`

Optional snapshot table for Apply history. It is useful when debugging config changes.

```sql
CREATE TABLE dbo.ai_setting_snapshot (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ai_setting_snapshot PRIMARY KEY,
    SettingKey NVARCHAR(128) NOT NULL,
    PayloadJson NVARCHAR(MAX) NOT NULL,
    VersionNo INT NOT NULL CONSTRAINT DF_ai_setting_snapshot_VersionNo DEFAULT (1),
    AppliedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ai_setting_snapshot_AppliedAt DEFAULT (SYSUTCDATETIME()),
    AppliedBy NVARCHAR(64) NULL,
    Remark NVARCHAR(512) NULL
);

CREATE INDEX IX_ai_setting_snapshot_Key_Time
ON dbo.ai_setting_snapshot(SettingKey, AppliedAt DESC);
```

## 5. Provider Seed Scope

Initial provider data should cover the screenshot list and common OpenAI-compatible vendors.

LLM providers:

- `aihubmix`: AiHubMix
- `anthropic`: Anthropic
- `azure_openai`: Azure OpenAI
- `byteplus`: BytePlus
- `byteplus_coding`: BytePlus Coding Plan
- `custom_anthropic`: Custom (Anthropic API)
- `custom_openai`: Custom (OpenAI API)
- `dashscope`: DashScope
- `deepseek`: DeepSeek
- `gemini`: Gemini
- `github_copilot`: GitHub Copilot
- `groq`: Groq
- `lemonade`: Lemonade
- `llama_cpp`: llama.cpp
- `lm_studio`: LM Studio
- `minimax`: MiniMax
- `minimax_anthropic`: MiniMax (Anthropic)
- `mistral`: Mistral
- `moonshot`: Moonshot
- `ollama`: Ollama
- `openai`: OpenAI
- `openrouter`: OpenRouter
- `siliconflow`: SiliconFlow
- `zhipu`: Zhipu AI

Embedding providers:

- `openai`
- `openai_compatible`
- `dashscope`
- `jina`
- `ollama`
- `cohere`
- `siliconflow`
- `gemini`
- `openrouter`
- `lm_studio`

Search providers:

- `tavily`
- `brave`
- `serper`
- `searxng`
- `jina`
- `duckduckgo`
- `exa`
- `perplexity`
- `openrouter`
- `baidu`

Voice and generation providers:

- TTS/STT: `openai`, `azure_openai`, `groq`, `openrouter`, `siliconflow`, `deepgram`, `elevenlabs`, `gemini`.
- Image: `openai`, `azure_openai`, `openrouter`, `siliconflow`, `gemini`, `dashscope`.
- Video: `dashscope`, `minimax`, `kling`, `runway`, `openai`, `custom_openai`.

## 6. Backend Implementation Tasks

### Task 1: Add SqlSugar Entities

**Files:**

- Create: `E:/项目/know-why/AiAgent/backed/Entities/Settings/AiModelProvider.cs`
- Create: `E:/项目/know-why/AiAgent/backed/Entities/Settings/AiModelProfile.cs`
- Create: `E:/项目/know-why/AiAgent/backed/Entities/Settings/AiModel.cs`
- Create: `E:/项目/know-why/AiAgent/backed/Entities/Settings/AiModelDiagnosticRun.cs`
- Create: `E:/项目/know-why/AiAgent/backed/Entities/Settings/AiSettingSnapshot.cs`

- [x] Define SqlSugar attributes with table names from section 4.
- [x] Keep C# property names aligned with SQL column names.
- [x] Use `long` for identity IDs, `DateTime` for timestamps, nullable types for optional service-specific fields.

### Task 2: Add CodeFirst Schema Initialization

**Files:**

- Create: `E:/项目/know-why/AiAgent/backed/Services/Settings/IModelSchemaInitializer.cs`
- Create: `E:/项目/know-why/AiAgent/backed/Services/Settings/ModelSchemaInitializer.cs`
- Create: `E:/项目/know-why/AiAgent/backed/Services/Settings/ModelProviderSeedData.cs`
- Modify: `E:/项目/know-why/AiAgent/backed/Program.cs`
- Modify: `E:/项目/know-why/AiAgent/backed/appsettings.json`
- Create: `E:/项目/know-why/AiAgent/backed/Database/SqlServer/001_create_model_settings_tables.sql`
- Create: `E:/项目/know-why/AiAgent/backed/Database/SqlServer/002_seed_model_providers.sql`

- [x] Add `Db.CodeFirst.InitTables(...)` for model setting entities.
- [x] Add startup switch `Database:CodeFirst`.
- [x] Add provider seed data in C# and write it through SqlSugar.
- [x] Keep SQL scripts as idempotent reference scripts.
- [x] Do not include real API keys in seed data.

### Task 3: Replace JSON Persistence With DB-Backed Catalog Service

**Files:**

- Modify: `E:/项目/know-why/AiAgent/backed/Services/Settings/ModelCatalogService.cs`
- Modify: `E:/项目/know-why/AiAgent/backed/Services/Settings/IModelCatalogService.cs`
- Modify: `E:/项目/know-why/AiAgent/backed/Services/Settings/ModelProviderOptionsService.cs`

- [ ] Read catalog from `ai_model_profile` and `ai_model`.
- [x] Read provider dropdown choices from `ai_model_provider`.
- [ ] Save profiles and model rows in one transaction.
- [ ] Preserve existing encrypted key when frontend sends `********`.
- [ ] Soft delete removed profiles and models instead of hard deleting.
- [ ] Write one row to `ai_setting_snapshot` when Apply succeeds.

### Task 4: Add Provider Seed Service

**Files:**

- Create: `E:/项目/know-why/AiAgent/backed/Services/Settings/IModelProviderSeedService.cs`
- Create: `E:/项目/know-why/AiAgent/backed/Services/Settings/ModelProviderSeedService.cs`
- Modify: `E:/项目/know-why/AiAgent/backed/Program.cs`

- [ ] Seed providers on startup only if table is empty, or expose a safe admin endpoint later.
- [ ] Match by `(ServiceType, ProviderCode)`.
- [ ] Update display metadata only, never overwrite user profile data.

### Task 5: Add Diagnostic Persistence

**Files:**

- Modify: `E:/项目/know-why/AiAgent/backed/Services/Settings/SettingsAppService.cs`
- Create: `E:/项目/know-why/AiAgent/backed/Services/Settings/IModelDiagnosticService.cs`
- Create: `E:/项目/know-why/AiAgent/backed/Services/Settings/ModelDiagnosticService.cs`

- [ ] Insert `Running` diagnostic row before testing.
- [ ] Update row to `Success` or `Failed`.
- [ ] Return latest diagnostic state to the frontend.
- [ ] Keep request/response JSON redacted.

## 7. Frontend Implementation Tasks

### Task 6: Add Global Sidebar

**Files:**

- Create: `E:/项目/know-why/AiAgent/front/components/layout/AppSidebar.tsx`
- Modify: `E:/项目/know-why/AiAgent/front/app/layout.tsx`
- Modify: `E:/项目/know-why/AiAgent/front/app/globals.css`

- [ ] Add fixed desktop sidebar matching the first screenshot.
- [ ] Highlight Settings for `/settings` nested routes.
- [ ] Keep page content scroll independent from sidebar.

### Task 7: Normalize Settings Navigation

**Files:**

- Create: `E:/项目/know-why/AiAgent/front/lib/settings-nav.ts`
- Modify: `E:/项目/know-why/AiAgent/front/components/settings/SettingsHub.tsx`
- Modify: `E:/项目/know-why/AiAgent/front/components/settings/models/ModelsSettingsPage.tsx`

- [ ] Make `settings-nav.ts` the frontend source for categories and model leaves.
- [ ] Keep service keys aligned with backend service strings.
- [ ] Use this file for breadcrumbs and card metadata.

### Task 8: Split Third-Level Service Routes

**Files:**

- Create: `E:/项目/know-why/AiAgent/front/app/settings/models/llm/page.tsx`
- Create: `E:/项目/know-why/AiAgent/front/app/settings/models/embedding/page.tsx`
- Create: `E:/项目/know-why/AiAgent/front/app/settings/models/search/page.tsx`
- Create: `E:/项目/know-why/AiAgent/front/app/settings/models/tts/page.tsx`
- Create: `E:/项目/know-why/AiAgent/front/app/settings/models/stt/page.tsx`
- Create: `E:/项目/know-why/AiAgent/front/app/settings/models/image/page.tsx`
- Create: `E:/项目/know-why/AiAgent/front/app/settings/models/video/page.tsx`

- [ ] Each page renders the shared editor with a fixed service key.
- [ ] Breadcrumb should read `Settings > Models > service`.
- [ ] The editor should not render all services on one page.

### Task 9: Update Service Editor UX

**Files:**

- Modify: `E:/项目/know-why/AiAgent/front/components/settings/models/ModelServiceEditor.tsx`
- Modify: `E:/项目/know-why/AiAgent/front/components/settings/models/model-service-config.ts`
- Modify: `E:/项目/know-why/AiAgent/front/lib/settings-types.ts`
- Modify: `E:/项目/know-why/AiAgent/front/lib/api.ts`

- [ ] Match the third-level screenshot: profile list left, provider card, models card, diagnostics card.
- [ ] Provider dropdown should show all seeded providers for the selected service.
- [ ] Keep Save Draft and Apply behavior separated.
- [ ] Show clearer backend/proxy errors when response is non-JSON.

## 8. Verification Checklist

Backend:

- [ ] Swagger opens at `http://localhost:5000/swagger`.
- [ ] Swagger contains the `Settings` group.
- [ ] `GET http://localhost:5000/api/v1/settings` returns JSON.
- [ ] `GET http://localhost:5000/api/v1/settings/catalog` returns all seven services.
- [ ] SQL Server tables exist in database `AGENT`.
- [ ] API keys are not returned in plain text.

Frontend:

- [ ] Frontend opens at `http://localhost:3782/settings`.
- [ ] Left sidebar is visible.
- [ ] `/settings` matches first screenshot structure.
- [ ] `/settings/models` matches second screenshot structure.
- [ ] `/settings/models/llm` matches third screenshot structure.
- [ ] Provider dropdown includes DeepTutor-like providers.
- [ ] Frontend and backend run as two services: frontend `3782`, backend `5000`.

## 9. Execution Order

1. Create and review this plan.
2. Add SQL scripts and SqlSugar entities.
3. Seed provider data.
4. Move backend catalog persistence from JSON to SQL Server.
5. Add diagnostic persistence.
6. Add frontend sidebar.
7. Split second-level and third-level settings routes.
8. Verify with backend Swagger and frontend browser.
