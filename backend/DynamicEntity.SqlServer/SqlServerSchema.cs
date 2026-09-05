namespace DynamicEntity.SqlServer;

internal static class SqlServerSchema
{
    public const string ControlPlane = """
        IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Tenants
            (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
                Name NVARCHAR(200) NOT NULL,
                Status NVARCHAR(32) NOT NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                UpdatedAt DATETIMEOFFSET(7) NOT NULL
            );
        END;

        IF OBJECT_ID(N'dbo.TenantStorage', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.TenantStorage
            (
                TenantId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantStorage PRIMARY KEY,
                ConnectionKey NVARCHAR(100) NOT NULL,
                DatabaseName SYSNAME NOT NULL,
                Status NVARCHAR(32) NOT NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                UpdatedAt DATETIMEOFFSET(7) NOT NULL,
                CONSTRAINT FK_TenantStorage_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
            );
        END;
        """;

    public const string TenantDatabase = """
        IF OBJECT_ID(N'dbo.EntityDefinitions', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.EntityDefinitions
            (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_EntityDefinitions PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL,
                Name NVARCHAR(100) NOT NULL,
                DisplayName NVARCHAR(200) NOT NULL,
                Description NVARCHAR(2000) NULL,
                SchemaVersion INT NOT NULL,
                Status NVARCHAR(32) NOT NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                UpdatedAt DATETIMEOFFSET(7) NOT NULL,
                CONSTRAINT UQ_EntityDefinitions_Tenant_Name UNIQUE (TenantId, Name)
            );
        END;

        IF OBJECT_ID(N'dbo.FieldDefinitions', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.FieldDefinitions
            (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_FieldDefinitions PRIMARY KEY,
                EntityId UNIQUEIDENTIFIER NOT NULL,
                Name NVARCHAR(100) NOT NULL,
                DisplayName NVARCHAR(200) NOT NULL,
                StorageKey VARCHAR(34) NOT NULL,
                DataType NVARCHAR(32) NOT NULL,
                IsRequired BIT NOT NULL,
                IsUnique BIT NOT NULL,
                IsFilterable BIT NOT NULL,
                IsSortable BIT NOT NULL,
                IsFacetable BIT NOT NULL,
                IsSearchable BIT NOT NULL,
                DefaultValueJson NVARCHAR(MAX) NULL,
                ConfigurationJson NVARCHAR(MAX) NULL,
                SortOrder INT NOT NULL,
                IsActive BIT NOT NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                UpdatedAt DATETIMEOFFSET(7) NOT NULL,
                CONSTRAINT FK_FieldDefinitions_Entities FOREIGN KEY (EntityId) REFERENCES dbo.EntityDefinitions(Id),
                CONSTRAINT UQ_FieldDefinitions_Entity_Name UNIQUE (EntityId, Name),
                CONSTRAINT UQ_FieldDefinitions_Entity_StorageKey UNIQUE (EntityId, StorageKey),
                CONSTRAINT CK_FieldDefinitions_DefaultValueJson CHECK (DefaultValueJson IS NULL OR ISJSON(DefaultValueJson) = 1),
                CONSTRAINT CK_FieldDefinitions_ConfigurationJson CHECK (ConfigurationJson IS NULL OR ISJSON(ConfigurationJson) = 1)
            );
        END;

        IF OBJECT_ID(N'dbo.EntityStorageMappings', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.EntityStorageMappings
            (
                EntityId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_EntityStorageMappings PRIMARY KEY,
                TableName SYSNAME NOT NULL,
                StorageMode NVARCHAR(32) NOT NULL,
                RequiresEntityPredicate BIT NOT NULL,
                Status NVARCHAR(32) NOT NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                UpdatedAt DATETIMEOFFSET(7) NOT NULL,
                CONSTRAINT FK_EntityStorageMappings_Entities FOREIGN KEY (EntityId) REFERENCES dbo.EntityDefinitions(Id)
            );
        END;

        IF OBJECT_ID(N'dbo.ViewDefinitions', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ViewDefinitions
            (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ViewDefinitions PRIMARY KEY,
                EntityId UNIQUEIDENTIFIER NOT NULL,
                Name NVARCHAR(200) NOT NULL,
                DefinitionJson NVARCHAR(MAX) NOT NULL,
                CreatedBy UNIQUEIDENTIFIER NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                UpdatedAt DATETIMEOFFSET(7) NOT NULL,
                CONSTRAINT FK_ViewDefinitions_Entities FOREIGN KEY (EntityId) REFERENCES dbo.EntityDefinitions(Id),
                CONSTRAINT CK_ViewDefinitions_DefinitionJson CHECK (ISJSON(DefinitionJson) = 1)
            );
        END;

        IF OBJECT_ID(N'dbo.EntityIndexDefinitions', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.EntityIndexDefinitions
            (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_EntityIndexDefinitions PRIMARY KEY,
                EntityId UNIQUEIDENTIFIER NOT NULL,
                FieldId UNIQUEIDENTIFIER NOT NULL,
                PhysicalColumnName SYSNAME NOT NULL,
                IndexName SYSNAME NOT NULL,
                Status NVARCHAR(32) NOT NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                CONSTRAINT FK_EntityIndexDefinitions_Entities FOREIGN KEY (EntityId) REFERENCES dbo.EntityDefinitions(Id),
                CONSTRAINT FK_EntityIndexDefinitions_Fields FOREIGN KEY (FieldId) REFERENCES dbo.FieldDefinitions(Id),
                CONSTRAINT UQ_EntityIndexDefinitions_Field UNIQUE (EntityId, FieldId)
            );
        END;

        IF OBJECT_ID(N'dbo.ImportJobs', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ImportJobs
            (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ImportJobs PRIMARY KEY,
                EntityId UNIQUEIDENTIFIER NOT NULL,
                FileName NVARCHAR(260) NOT NULL,
                Status NVARCHAR(32) NOT NULL,
                ColumnsJson NVARCHAR(MAX) NOT NULL,
                TotalRows INT NOT NULL,
                ValidRows INT NOT NULL,
                InvalidRows INT NOT NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                UpdatedAt DATETIMEOFFSET(7) NOT NULL,
                CONSTRAINT FK_ImportJobs_Entities FOREIGN KEY (EntityId) REFERENCES dbo.EntityDefinitions(Id),
                CONSTRAINT CK_ImportJobs_ColumnsJson CHECK (ISJSON(ColumnsJson) = 1)
            );
        END;

        IF OBJECT_ID(N'dbo.ImportRows', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ImportRows
            (
                ImportId UNIQUEIDENTIFIER NOT NULL,
                SourceRowNumber BIGINT NOT NULL,
                SourceJson NVARCHAR(MAX) NOT NULL,
                ConvertedData NVARCHAR(MAX) NULL,
                ValidationError NVARCHAR(MAX) NULL,
                CONSTRAINT PK_ImportRows PRIMARY KEY (ImportId, SourceRowNumber),
                CONSTRAINT FK_ImportRows_Jobs FOREIGN KEY (ImportId) REFERENCES dbo.ImportJobs(Id),
                CONSTRAINT CK_ImportRows_SourceJson CHECK (ISJSON(SourceJson) = 1),
                CONSTRAINT CK_ImportRows_ConvertedData CHECK (ConvertedData IS NULL OR ISJSON(ConvertedData) = 1)
            );
        END;
        """;
}
