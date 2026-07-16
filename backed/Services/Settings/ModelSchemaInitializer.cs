using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Entities.Settings;
using SqlSugar;

namespace AiAgent.Backend.Services.Settings;

/// <summary>
/// 模型、设置和知识库表结构初始化实现。
/// </summary>
public sealed class ModelSchemaInitializer : IModelSchemaInitializer
{
    private readonly ISqlSugarClient _db;
    private readonly ILogger<ModelSchemaInitializer> _logger;

    /// <summary>
    /// 初始化模型设置表结构初始化器，用于启动时补齐数据库表和默认供应商数据。
    /// </summary>
    public ModelSchemaInitializer(ISqlSugarClient db, ILogger<ModelSchemaInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 初始化表结构、补充索引并写入 provider 种子数据。
    /// </summary>
    public void Initialize()
    {
        EnsureChatSessionColumns();
        EnsureChatProjectPreferenceTable();
        _db.CodeFirst.InitTables(
            typeof(AiModelProvider),
            typeof(AiModelProfile),
            typeof(AiModel),
            typeof(AiModelDiagnosticRun),
            typeof(AiSettingSnapshot),
            typeof(AiKnowledgeBase),
            typeof(AiKnowledgeDocument),
            typeof(AiKnowledgeIndexVersion),
            typeof(AiKnowledgeChunk),
            typeof(AiKnowledgeJob),
            typeof(AiCodeProject),
            typeof(AiCodeRepository),
            typeof(AiCodeRepositoryFile),
            typeof(AiUser),
            typeof(AiUserSession),
            typeof(AiChatSession),
            typeof(AiChatProjectPreference),
            typeof(AiChatMessage),
            typeof(AiGitAccount));

        EnsureColumns();
        EnsureIndexes();
        SeedProviders();
    }

    private void EnsureColumns()
    {
        ExecuteIndexSql("""
IF COL_LENGTH(N'dbo.ai_model', N'SupportedDimensions') IS NULL
    ALTER TABLE dbo.ai_model ADD SupportedDimensions NVARCHAR(256) NULL;

IF COL_LENGTH(N'dbo.ai_code_repository', N'ProjectId') IS NULL
    ALTER TABLE dbo.ai_code_repository ADD ProjectId BIGINT NULL;

""");
    }

    private void EnsureChatSessionColumns()
    {
        ExecuteIndexSql("""
IF OBJECT_ID(N'dbo.ai_chat_session', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.ai_chat_session', N'CodeProjectId') IS NULL
        ALTER TABLE dbo.ai_chat_session ADD CodeProjectId BIGINT NULL;
    ELSE IF COLUMNPROPERTY(OBJECT_ID(N'dbo.ai_chat_session'), N'CodeProjectId', 'AllowsNull') = 0
        ALTER TABLE dbo.ai_chat_session ALTER COLUMN CodeProjectId BIGINT NULL;

    IF COL_LENGTH(N'dbo.ai_chat_session', N'SortOrder') IS NULL
        ALTER TABLE dbo.ai_chat_session ADD SortOrder INT NULL;
    ELSE IF COLUMNPROPERTY(OBJECT_ID(N'dbo.ai_chat_session'), N'SortOrder', 'AllowsNull') = 0
        ALTER TABLE dbo.ai_chat_session ALTER COLUMN SortOrder INT NULL;

    IF COL_LENGTH(N'dbo.ai_chat_session', N'Priority') IS NULL
        ALTER TABLE dbo.ai_chat_session ADD Priority NVARCHAR(16) NULL;
    ELSE IF COLUMNPROPERTY(OBJECT_ID(N'dbo.ai_chat_session'), N'Priority', 'AllowsNull') = 0
        ALTER TABLE dbo.ai_chat_session ALTER COLUMN Priority NVARCHAR(16) NULL;

    IF COL_LENGTH(N'dbo.ai_chat_session', N'IsPinned') IS NULL
        ALTER TABLE dbo.ai_chat_session ADD IsPinned BIT NULL;
    ELSE IF COLUMNPROPERTY(OBJECT_ID(N'dbo.ai_chat_session'), N'IsPinned', 'AllowsNull') = 0
        ALTER TABLE dbo.ai_chat_session ALTER COLUMN IsPinned BIT NULL;
END
""");
    }

    private void EnsureChatProjectPreferenceTable()
    {
        ExecuteIndexSql("""
IF OBJECT_ID(N'dbo.ai_chat_proj_pref', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_chat_proj_pref
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ai_chat_proj_pref PRIMARY KEY,
        UserId NVARCHAR(64) NOT NULL,
        CodeProjectId BIGINT NOT NULL,
        IsPinned BIT NOT NULL CONSTRAINT DF_ai_chat_proj_pref_IsPinned DEFAULT 0,
        SortMode NVARCHAR(16) NOT NULL CONSTRAINT DF_ai_chat_proj_pref_SortMode DEFAULT N'updated',
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_ai_chat_proj_pref_UpdatedAt DEFAULT SYSUTCDATETIME()
    );
END
""");
    }

    private void EnsureIndexes()
    {
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_model_provider_Service_Code' AND object_id = OBJECT_ID(N'dbo.ai_model_provider'))
    CREATE UNIQUE INDEX UX_ai_model_provider_Service_Code ON dbo.ai_model_provider(ServiceType, ProviderCode) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_model_profile_Service_Active' AND object_id = OBJECT_ID(N'dbo.ai_model_profile'))
    CREATE INDEX IX_ai_model_profile_Service_Active ON dbo.ai_model_profile(ServiceType, IsActive) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_model_profile_OneActive' AND object_id = OBJECT_ID(N'dbo.ai_model_profile'))
    CREATE UNIQUE INDEX UX_ai_model_profile_OneActive ON dbo.ai_model_profile(ServiceType) WHERE IsDeleted = 0 AND IsActive = 1;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_model_Profile' AND object_id = OBJECT_ID(N'dbo.ai_model'))
    CREATE INDEX IX_ai_model_Profile ON dbo.ai_model(ProfileId, SortOrder) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_model_Profile_ModelId' AND object_id = OBJECT_ID(N'dbo.ai_model'))
    CREATE UNIQUE INDEX UX_ai_model_Profile_ModelId ON dbo.ai_model(ProfileId, ModelId) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_model_diagnostic_run_Service_Time' AND object_id = OBJECT_ID(N'dbo.ai_model_diagnostic_run'))
    CREATE INDEX IX_ai_model_diagnostic_run_Service_Time ON dbo.ai_model_diagnostic_run(ServiceType, StartedAt DESC);
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_setting_snapshot_Key_Time' AND object_id = OBJECT_ID(N'dbo.ai_setting_snapshot'))
    CREATE INDEX IX_ai_setting_snapshot_Key_Time ON dbo.ai_setting_snapshot(SettingKey, AppliedAt DESC);
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_knowledge_base_Name' AND object_id = OBJECT_ID(N'dbo.ai_knowledge_base'))
    CREATE UNIQUE INDEX UX_ai_knowledge_base_Name ON dbo.ai_knowledge_base(Name) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_knowledge_document_Kb_Status' AND object_id = OBJECT_ID(N'dbo.ai_knowledge_document'))
    CREATE INDEX IX_ai_knowledge_document_Kb_Status ON dbo.ai_knowledge_document(KnowledgeBaseId, Status) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_knowledge_index_version_Kb_Status' AND object_id = OBJECT_ID(N'dbo.ai_knowledge_index_version'))
    CREATE INDEX IX_ai_knowledge_index_version_Kb_Status ON dbo.ai_knowledge_index_version(KnowledgeBaseId, Status, VersionNo DESC);
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_knowledge_chunk_Version' AND object_id = OBJECT_ID(N'dbo.ai_knowledge_chunk'))
    CREATE INDEX IX_ai_knowledge_chunk_Version ON dbo.ai_knowledge_chunk(IndexVersionId, DocumentId, ChunkNo);
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_knowledge_job_Kb_Status' AND object_id = OBJECT_ID(N'dbo.ai_knowledge_job'))
    CREATE INDEX IX_ai_knowledge_job_Kb_Status ON dbo.ai_knowledge_job(KnowledgeBaseId, Status, CreatedAt DESC);
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_code_repository_Name' AND object_id = OBJECT_ID(N'dbo.ai_code_repository'))
    CREATE UNIQUE INDEX UX_ai_code_repository_Name ON dbo.ai_code_repository(Name) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_code_repository_RootPath' AND object_id = OBJECT_ID(N'dbo.ai_code_repository'))
    CREATE UNIQUE INDEX UX_ai_code_repository_RootPath ON dbo.ai_code_repository(RootPath) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_code_project_Name' AND object_id = OBJECT_ID(N'dbo.ai_code_project'))
    CREATE UNIQUE INDEX UX_ai_code_project_Name ON dbo.ai_code_project(Name) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_code_project_RootPath' AND object_id = OBJECT_ID(N'dbo.ai_code_project'))
    CREATE UNIQUE INDEX UX_ai_code_project_RootPath ON dbo.ai_code_project(RootPath) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_code_repository_Project' AND object_id = OBJECT_ID(N'dbo.ai_code_repository'))
    CREATE INDEX IX_ai_code_repository_Project ON dbo.ai_code_repository(ProjectId, UpdatedAt DESC) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_user_Username' AND object_id = OBJECT_ID(N'dbo.ai_user'))
    CREATE UNIQUE INDEX UX_ai_user_Username ON dbo.ai_user(Username);
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_user_session_TokenHash' AND object_id = OBJECT_ID(N'dbo.ai_user_session'))
    CREATE UNIQUE INDEX UX_ai_user_session_TokenHash ON dbo.ai_user_session(TokenHash);
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_chat_session_User_Updated' AND object_id = OBJECT_ID(N'dbo.ai_chat_session'))
    CREATE INDEX IX_ai_chat_session_User_Updated ON dbo.ai_chat_session(UserId, UpdatedAt DESC) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_chat_session_User_Project_Sort' AND object_id = OBJECT_ID(N'dbo.ai_chat_session'))
    CREATE INDEX IX_ai_chat_session_User_Project_Sort ON dbo.ai_chat_session(UserId, CodeProjectId, SortOrder DESC, UpdatedAt DESC) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_chat_session_User_Pinned_Priority' AND object_id = OBJECT_ID(N'dbo.ai_chat_session'))
    CREATE INDEX IX_ai_chat_session_User_Pinned_Priority ON dbo.ai_chat_session(UserId, IsPinned DESC, Priority, UpdatedAt DESC) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_chat_proj_pref_User_Project' AND object_id = OBJECT_ID(N'dbo.ai_chat_proj_pref'))
    CREATE UNIQUE INDEX UX_ai_chat_proj_pref_User_Project ON dbo.ai_chat_proj_pref(UserId, CodeProjectId);
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_chat_message_Session_Id' AND object_id = OBJECT_ID(N'dbo.ai_chat_message'))
    CREATE INDEX IX_ai_chat_message_Session_Id ON dbo.ai_chat_message(SessionId, Id);
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_git_account_User_Provider_Username' AND object_id = OBJECT_ID(N'dbo.ai_git_account'))
    CREATE UNIQUE INDEX UX_ai_git_account_User_Provider_Username ON dbo.ai_git_account(UserId, Provider, Username) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_git_account_User_Active' AND object_id = OBJECT_ID(N'dbo.ai_git_account'))
    CREATE INDEX IX_ai_git_account_User_Active ON dbo.ai_git_account(UserId, IsActive) WHERE IsDeleted = 0;
""");
        ExecuteIndexSql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_git_account_OneActive' AND object_id = OBJECT_ID(N'dbo.ai_git_account'))
    CREATE UNIQUE INDEX UX_ai_git_account_OneActive ON dbo.ai_git_account(UserId) WHERE IsDeleted = 0 AND IsActive = 1;
""");
    }

    private void SeedProviders()
    {
        var seedRows = ModelProviderSeedData.Providers;
        var serviceTypes = seedRows.Select(x => x.ServiceType).Distinct().ToList();
        var existingRows = _db.Queryable<AiModelProvider>()
            .Where(x => serviceTypes.Contains(x.ServiceType) && !x.IsDeleted)
            .ToList();

        var existingMap = existingRows.ToDictionary(x => $"{x.ServiceType}:{x.ProviderCode}", StringComparer.OrdinalIgnoreCase);
        var inserts = new List<AiModelProvider>();
        var updates = new List<AiModelProvider>();
        var now = DateTime.UtcNow;

        foreach (var seed in seedRows)
        {
            if (!existingMap.TryGetValue($"{seed.ServiceType}:{seed.ProviderCode}", out var existing))
            {
                seed.CreatedAt = now;
                inserts.Add(seed);
                continue;
            }

            existing.ProviderName = seed.ProviderName;
            existing.BindingType = seed.BindingType;
            existing.BaseUrl = seed.BaseUrl;
            existing.AuthType = seed.AuthType;
            existing.ApiVersion = seed.ApiVersion;
            existing.IconKey = seed.IconKey;
            existing.DefaultModel = seed.DefaultModel;
            existing.DefaultDimension = seed.DefaultDimension;
            existing.DefaultVoice = seed.DefaultVoice;
            existing.CapabilitiesJson = seed.CapabilitiesJson;
            existing.SortOrder = seed.SortOrder;
            existing.IsEnabled = true;
            existing.UpdatedAt = now;
            updates.Add(existing);
        }

        if (inserts.Count > 0)
        {
            _db.Insertable(inserts).ExecuteCommand();
        }

        if (updates.Count > 0)
        {
            _db.Updateable(updates)
                .UpdateColumns(x => new
                {
                    x.ProviderName,
                    x.BindingType,
                    x.BaseUrl,
                    x.AuthType,
                    x.ApiVersion,
                    x.IconKey,
                    x.DefaultModel,
                    x.DefaultDimension,
                    x.DefaultVoice,
                    x.CapabilitiesJson,
                    x.SortOrder,
                    x.IsEnabled,
                    x.UpdatedAt
                })
                .ExecuteCommand();
        }

        _logger.LogInformation("Model provider CodeFirst seed completed. Inserted {InsertCount}, updated {UpdateCount}.", inserts.Count, updates.Count);
    }

    private void ExecuteIndexSql(string sql)
    {
        _db.Ado.ExecuteCommand(sql);
    }
}
