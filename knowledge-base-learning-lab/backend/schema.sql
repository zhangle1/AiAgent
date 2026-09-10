IF OBJECT_ID(N'dbo.KnowledgeLabResource', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.KnowledgeLabResource (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Title NVARCHAR(120) NOT NULL,
        Source NVARCHAR(120) NOT NULL,
        Content NVARCHAR(MAX) NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        NodeCount INT NOT NULL CONSTRAINT DF_KnowledgeLabResource_NodeCount DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL,
        UpdatedAt DATETIME2 NOT NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.KnowledgeLabIndexJob', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.KnowledgeLabIndexJob (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        ResourceId UNIQUEIDENTIFIER NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        Stage NVARCHAR(40) NOT NULL,
        Progress INT NOT NULL,
        Message NVARCHAR(1000) NOT NULL,
        CreatedAt DATETIME2 NOT NULL,
        UpdatedAt DATETIME2 NOT NULL,
        CONSTRAINT FK_KnowledgeLabIndexJob_Resource FOREIGN KEY (ResourceId) REFERENCES dbo.KnowledgeLabResource(Id)
    );
    CREATE INDEX IX_KnowledgeLabIndexJob_ResourceId_UpdatedAt ON dbo.KnowledgeLabIndexJob(ResourceId, UpdatedAt DESC);
END;
GO

IF OBJECT_ID(N'dbo.KnowledgeLabContextNode', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.KnowledgeLabContextNode (
        Id NVARCHAR(160) NOT NULL PRIMARY KEY,
        ResourceId UNIQUEIDENTIFIER NOT NULL,
        [Level] TINYINT NOT NULL,
        Title NVARCHAR(160) NOT NULL,
        Content NVARCHAR(MAX) NOT NULL,
        ChunkNo INT NULL,
        Generator NVARCHAR(20) NOT NULL,
        CreatedAt DATETIME2 NOT NULL,
        CONSTRAINT FK_KnowledgeLabContextNode_Resource FOREIGN KEY (ResourceId) REFERENCES dbo.KnowledgeLabResource(Id)
    );
    CREATE INDEX IX_KnowledgeLabContextNode_ResourceId_Level ON dbo.KnowledgeLabContextNode(ResourceId, [Level]);
END;
GO

IF OBJECT_ID(N'dbo.KnowledgeLabLlmProvider', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.KnowledgeLabLlmProvider (
        Provider NVARCHAR(40) NOT NULL PRIMARY KEY,
        IsEnabled BIT NOT NULL,
        Model NVARCHAR(120) NOT NULL,
        ApiBase NVARCHAR(500) NOT NULL,
        ApiKeyEncrypted NVARCHAR(MAX) NULL,
        TimeoutSeconds INT NOT NULL,
        MaxRetries INT NOT NULL,
        UpdatedAt DATETIME2 NOT NULL
    );
END;
GO
