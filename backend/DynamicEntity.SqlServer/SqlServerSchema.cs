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

    public const string TenantDatabaseV1 = """
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
                IndexName SYSNAME NOT NULL,
                Status NVARCHAR(32) NOT NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                CONSTRAINT FK_EntityIndexDefinitions_Entities FOREIGN KEY (EntityId) REFERENCES dbo.EntityDefinitions(Id),
                CONSTRAINT UQ_EntityIndexDefinitions_Name UNIQUE (EntityId, IndexName)
            );
        END;

        IF OBJECT_ID(N'dbo.EntityIndexColumns', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.EntityIndexColumns
            (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_EntityIndexColumns PRIMARY KEY,
                IndexId UNIQUEIDENTIFIER NOT NULL,
                FieldId UNIQUEIDENTIFIER NOT NULL,
                PhysicalColumnName SYSNAME NOT NULL,
                SortOrder INT NOT NULL,
                IsDescending BIT NOT NULL,
                CONSTRAINT FK_EntityIndexColumns_Indexes FOREIGN KEY (IndexId) REFERENCES dbo.EntityIndexDefinitions(Id),
                CONSTRAINT FK_EntityIndexColumns_Fields FOREIGN KEY (FieldId) REFERENCES dbo.FieldDefinitions(Id),
                CONSTRAINT UQ_EntityIndexColumns_Index_Field UNIQUE (IndexId, FieldId),
                CONSTRAINT UQ_EntityIndexColumns_Index_SortOrder UNIQUE (IndexId, SortOrder)
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

    public const string TenantDatabaseV2 = """
        IF OBJECT_ID(N'dbo.ReportDefinitions', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ReportDefinitions
            (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ReportDefinitions PRIMARY KEY,
                EntityId UNIQUEIDENTIFIER NOT NULL,
                Name NVARCHAR(200) NOT NULL,
                Description NVARCHAR(2000) NULL,
                DefinitionJson NVARCHAR(MAX) NOT NULL,
                CreatedBy UNIQUEIDENTIFIER NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                UpdatedAt DATETIMEOFFSET(7) NOT NULL,
                CONSTRAINT FK_ReportDefinitions_Entities FOREIGN KEY (EntityId) REFERENCES dbo.EntityDefinitions(Id),
                CONSTRAINT CK_ReportDefinitions_DefinitionJson CHECK (ISJSON(DefinitionJson) = 1)
            );
            CREATE INDEX IX_ReportDefinitions_EntityId ON dbo.ReportDefinitions(EntityId);
        END;

        IF OBJECT_ID(N'dbo.MetricDefinitions', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.MetricDefinitions
            (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_MetricDefinitions PRIMARY KEY,
                EntityId UNIQUEIDENTIFIER NOT NULL,
                Name NVARCHAR(200) NOT NULL,
                Description NVARCHAR(2000) NULL,
                Aggregate NVARCHAR(32) NOT NULL,
                FieldId UNIQUEIDENTIFIER NULL,
                FilterJson NVARCHAR(MAX) NULL,
                FormatJson NVARCHAR(MAX) NULL,
                CreatedBy UNIQUEIDENTIFIER NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                UpdatedAt DATETIMEOFFSET(7) NOT NULL,
                CONSTRAINT FK_MetricDefinitions_Entities FOREIGN KEY (EntityId) REFERENCES dbo.EntityDefinitions(Id),
                CONSTRAINT FK_MetricDefinitions_Fields FOREIGN KEY (FieldId) REFERENCES dbo.FieldDefinitions(Id),
                CONSTRAINT CK_MetricDefinitions_FilterJson CHECK (FilterJson IS NULL OR ISJSON(FilterJson) = 1),
                CONSTRAINT CK_MetricDefinitions_FormatJson CHECK (FormatJson IS NULL OR ISJSON(FormatJson) = 1)
            );
            CREATE INDEX IX_MetricDefinitions_EntityId ON dbo.MetricDefinitions(EntityId);
        END;

        IF OBJECT_ID(N'dbo.AlertDefinitions', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.AlertDefinitions
            (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AlertDefinitions PRIMARY KEY,
                EntityId UNIQUEIDENTIFIER NOT NULL,
                MetricId UNIQUEIDENTIFIER NOT NULL,
                Name NVARCHAR(200) NOT NULL,
                ComparisonOperator NVARCHAR(32) NOT NULL,
                ThresholdJson NVARCHAR(MAX) NOT NULL,
                [Interval] NVARCHAR(32) NOT NULL,
                Timezone NVARCHAR(100) NOT NULL,
                CooldownSeconds INT NOT NULL,
                NotifyOnRecovery BIT NOT NULL,
                IsEnabled BIT NOT NULL,
                LastState NVARCHAR(32) NULL,
                LastEvaluatedAt DATETIMEOFFSET(7) NULL,
                NextEvaluationAt DATETIMEOFFSET(7) NOT NULL,
                LeaseOwner NVARCHAR(200) NULL,
                LeaseExpiresAt DATETIMEOFFSET(7) NULL,
                CreatedBy UNIQUEIDENTIFIER NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                UpdatedAt DATETIMEOFFSET(7) NOT NULL,
                CONSTRAINT FK_AlertDefinitions_Entities FOREIGN KEY (EntityId) REFERENCES dbo.EntityDefinitions(Id),
                CONSTRAINT FK_AlertDefinitions_Metrics FOREIGN KEY (MetricId) REFERENCES dbo.MetricDefinitions(Id),
                CONSTRAINT CK_AlertDefinitions_ThresholdJson CHECK (ISJSON(CONCAT(N'[', ThresholdJson, N']')) = 1),
                CONSTRAINT CK_AlertDefinitions_Cooldown CHECK (CooldownSeconds >= 0)
            );
            CREATE INDEX IX_AlertDefinitions_EntityId ON dbo.AlertDefinitions(EntityId);
            CREATE INDEX IX_AlertDefinitions_Due ON dbo.AlertDefinitions(NextEvaluationAt) WHERE IsEnabled = 1;
        END;

        IF OBJECT_ID(N'dbo.AlertEvaluations', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.AlertEvaluations
            (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AlertEvaluations PRIMARY KEY,
                AlertId UNIQUEIDENTIFIER NOT NULL,
                ValueJson NVARCHAR(MAX) NULL,
                ThresholdJson NVARCHAR(MAX) NOT NULL,
                State NVARCHAR(32) NOT NULL,
                Error NVARCHAR(4000) NULL,
                EvaluatedAt DATETIMEOFFSET(7) NOT NULL,
                CONSTRAINT FK_AlertEvaluations_Alerts FOREIGN KEY (AlertId) REFERENCES dbo.AlertDefinitions(Id),
                CONSTRAINT CK_AlertEvaluations_ValueJson CHECK (ValueJson IS NULL OR ISJSON(CONCAT(N'[', ValueJson, N']')) = 1),
                CONSTRAINT CK_AlertEvaluations_ThresholdJson CHECK (ISJSON(CONCAT(N'[', ThresholdJson, N']')) = 1)
            );
            CREATE INDEX IX_AlertEvaluations_AlertId_EvaluatedAt ON dbo.AlertEvaluations(AlertId, EvaluatedAt DESC);
        END;

        IF OBJECT_ID(N'dbo.AlertNotifications', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.AlertNotifications
            (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AlertNotifications PRIMARY KEY,
                AlertId UNIQUEIDENTIFIER NOT NULL,
                EvaluationId UNIQUEIDENTIFIER NOT NULL,
                Channel NVARCHAR(32) NOT NULL,
                Status NVARCHAR(32) NOT NULL,
                Attempts INT NOT NULL,
                LastError NVARCHAR(4000) NULL,
                CreatedAt DATETIMEOFFSET(7) NOT NULL,
                DeliveredAt DATETIMEOFFSET(7) NULL,
                CONSTRAINT FK_AlertNotifications_Alerts FOREIGN KEY (AlertId) REFERENCES dbo.AlertDefinitions(Id),
                CONSTRAINT FK_AlertNotifications_Evaluations FOREIGN KEY (EvaluationId) REFERENCES dbo.AlertEvaluations(Id),
                CONSTRAINT CK_AlertNotifications_Attempts CHECK (Attempts >= 0),
                CONSTRAINT UQ_AlertNotifications_Evaluation_Channel UNIQUE (EvaluationId, Channel)
            );
            CREATE INDEX IX_AlertNotifications_Status_CreatedAt ON dbo.AlertNotifications(Status, CreatedAt);
        END;
        """;
}
