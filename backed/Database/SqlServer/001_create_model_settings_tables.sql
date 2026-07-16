SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.ai_model_provider', N'U') IS NULL
BEGIN
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
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_model_provider_Service_Code' AND object_id = OBJECT_ID(N'dbo.ai_model_provider'))
BEGIN
    CREATE UNIQUE INDEX UX_ai_model_provider_Service_Code
    ON dbo.ai_model_provider(ServiceType, ProviderCode)
    WHERE IsDeleted = 0;
END;

IF OBJECT_ID(N'dbo.ai_model_profile', N'U') IS NULL
BEGIN
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
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ai_model_profile_Provider')
BEGIN
    ALTER TABLE dbo.ai_model_profile
    ADD CONSTRAINT FK_ai_model_profile_Provider
    FOREIGN KEY (ProviderId) REFERENCES dbo.ai_model_provider(Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_model_profile_Service_Active' AND object_id = OBJECT_ID(N'dbo.ai_model_profile'))
BEGIN
    CREATE INDEX IX_ai_model_profile_Service_Active
    ON dbo.ai_model_profile(ServiceType, IsActive)
    WHERE IsDeleted = 0;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_model_profile_OneActive' AND object_id = OBJECT_ID(N'dbo.ai_model_profile'))
BEGIN
    CREATE UNIQUE INDEX UX_ai_model_profile_OneActive
    ON dbo.ai_model_profile(ServiceType)
    WHERE IsDeleted = 0 AND IsActive = 1;
END;

IF OBJECT_ID(N'dbo.ai_model', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_model (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ai_model PRIMARY KEY,
        ProfileId BIGINT NOT NULL,
        ServiceType NVARCHAR(32) NOT NULL,
        ModelName NVARCHAR(128) NOT NULL,
        ModelId NVARCHAR(256) NOT NULL,
        ContextWindow INT NULL,
        Dimension INT NULL,
        SendDimensions BIT NULL,
        SupportedDimensions NVARCHAR(256) NULL,
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
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ai_model_Profile')
BEGIN
    ALTER TABLE dbo.ai_model
    ADD CONSTRAINT FK_ai_model_Profile
    FOREIGN KEY (ProfileId) REFERENCES dbo.ai_model_profile(Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_model_Profile' AND object_id = OBJECT_ID(N'dbo.ai_model'))
BEGIN
    CREATE INDEX IX_ai_model_Profile
    ON dbo.ai_model(ProfileId, SortOrder)
    WHERE IsDeleted = 0;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ai_model_Profile_ModelId' AND object_id = OBJECT_ID(N'dbo.ai_model'))
BEGIN
    CREATE UNIQUE INDEX UX_ai_model_Profile_ModelId
    ON dbo.ai_model(ProfileId, ModelId)
    WHERE IsDeleted = 0;
END;

IF OBJECT_ID(N'dbo.ai_model_diagnostic_run', N'U') IS NULL
BEGIN
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
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_model_diagnostic_run_Service_Time' AND object_id = OBJECT_ID(N'dbo.ai_model_diagnostic_run'))
BEGIN
    CREATE INDEX IX_ai_model_diagnostic_run_Service_Time
    ON dbo.ai_model_diagnostic_run(ServiceType, StartedAt DESC);
END;

IF OBJECT_ID(N'dbo.ai_setting_snapshot', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_setting_snapshot (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ai_setting_snapshot PRIMARY KEY,
        SettingKey NVARCHAR(128) NOT NULL,
        PayloadJson NVARCHAR(MAX) NOT NULL,
        VersionNo INT NOT NULL CONSTRAINT DF_ai_setting_snapshot_VersionNo DEFAULT (1),
        AppliedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ai_setting_snapshot_AppliedAt DEFAULT (SYSUTCDATETIME()),
        AppliedBy NVARCHAR(64) NULL,
        Remark NVARCHAR(512) NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ai_setting_snapshot_Key_Time' AND object_id = OBJECT_ID(N'dbo.ai_setting_snapshot'))
BEGIN
    CREATE INDEX IX_ai_setting_snapshot_Key_Time
    ON dbo.ai_setting_snapshot(SettingKey, AppliedAt DESC);
END;
