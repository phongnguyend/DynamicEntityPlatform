using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerEntityIndexManager(
    SqlServerOptions options,
    IEntityStorageResolver storageResolver) : IEntityIndexManager
{
    public async Task<EntityIndexDefinition> CreateAsync(
        TenantContext tenant,
        EntityDefinition entity,
        FieldDefinition field,
        CancellationToken cancellationToken)
    {
        var storage = await storageResolver.ResolveAsync(tenant.TenantId, entity.Id, cancellationToken);
        if (storage.Mode != EntityStorageMode.DedicatedTable || storage.RequiresEntityPredicate)
            throw new NotSupportedException("Computed indexes currently require dedicated table storage.");
        if (!string.Equals(storage.ConnectionKey, options.ConnectionKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unknown tenant connection key '{storage.ConnectionKey}'.");

        var columnName = $"IDX_{field.Id:N}";
        var indexName = $"IX_{field.Id:N}";
        var table = PhysicalName.QuoteSqlIdentifier(storage.TableName);
        var column = PhysicalName.QuoteSqlIdentifier(columnName);
        var index = PhysicalName.QuoteSqlIdentifier(indexName);
        var expression = ComputedExpression(field);
        var definition = new EntityIndexDefinition(Guid.NewGuid(), entity.Id, field.Id, columnName, indexName, "Active", DateTimeOffset.UtcNow);
        var sql = $"""
            IF COL_LENGTH(N'dbo.{storage.TableName}', N'{columnName}') IS NULL
                ALTER TABLE dbo.{table} ADD {column} AS ({expression}) PERSISTED;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.{storage.TableName}') AND name = N'{indexName}')
                CREATE INDEX {index} ON dbo.{table} ({column});
            IF NOT EXISTS (SELECT 1 FROM dbo.EntityIndexDefinitions WHERE EntityId = @entityId AND FieldId = @fieldId)
                INSERT INTO dbo.EntityIndexDefinitions
                    (Id, EntityId, FieldId, PhysicalColumnName, IndexName, Status, CreatedAt)
                VALUES (@id, @entityId, @fieldId, @columnName, @indexName, N'Active', @createdAt);
            """;
        var builder = new SqlConnectionStringBuilder(options.TenantServerConnectionString) { InitialCatalog = storage.DatabaseName };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
            command.Parameters.AddWithValue("@id", definition.Id);
            command.Parameters.AddWithValue("@entityId", entity.Id);
            command.Parameters.AddWithValue("@fieldId", field.Id);
            command.Parameters.AddWithValue("@columnName", columnName);
            command.Parameters.AddWithValue("@indexName", indexName);
            command.Parameters.AddWithValue("@createdAt", definition.CreatedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return definition;
        }
        catch (SqlException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException($"Index creation failed for field '{field.DisplayName}': {exception.Message}");
        }
    }

    public async Task<bool> DeleteAsync(
        TenantContext tenant,
        EntityDefinition entity,
        FieldDefinition field,
        CancellationToken cancellationToken)
    {
        var storage = await storageResolver.ResolveAsync(tenant.TenantId, entity.Id, cancellationToken);
        if (storage.Mode != EntityStorageMode.DedicatedTable || storage.RequiresEntityPredicate)
            throw new NotSupportedException("Computed indexes currently require dedicated table storage.");
        if (!string.Equals(storage.ConnectionKey, options.ConnectionKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unknown tenant connection key '{storage.ConnectionKey}'.");

        var columnName = $"IDX_{field.Id:N}";
        var indexName = $"IX_{field.Id:N}";
        var table = PhysicalName.QuoteSqlIdentifier(storage.TableName);
        var column = PhysicalName.QuoteSqlIdentifier(columnName);
        var index = PhysicalName.QuoteSqlIdentifier(indexName);
        var sql = $"""
            IF NOT EXISTS (SELECT 1 FROM dbo.EntityIndexDefinitions WHERE EntityId = @entityId AND FieldId = @fieldId AND Status = N'Active')
                SELECT CAST(0 AS INT);
            ELSE
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.{storage.TableName}') AND name = N'{indexName}')
                    DROP INDEX {index} ON dbo.{table};
                IF COL_LENGTH(N'dbo.{storage.TableName}', N'{columnName}') IS NOT NULL
                    ALTER TABLE dbo.{table} DROP COLUMN {column};
                DELETE FROM dbo.EntityIndexDefinitions WHERE EntityId = @entityId AND FieldId = @fieldId;
                SELECT CAST(1 AS INT);
            END;
            """;
        var builder = new SqlConnectionStringBuilder(options.TenantServerConnectionString) { InitialCatalog = storage.DatabaseName };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
            command.Parameters.AddWithValue("@entityId", entity.Id);
            command.Parameters.AddWithValue("@fieldId", field.Id);
            var deleted = (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
            await transaction.CommitAsync(cancellationToken);
            return deleted;
        }
        catch (SqlException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException($"Index removal failed for field '{field.DisplayName}': {exception.Message}");
        }
    }

    private static string ComputedExpression(FieldDefinition field)
    {
        var json = $"JSON_VALUE(Data, '$.{field.StorageKey}')";
        return field.DataType switch
        {
            FieldDataType.Integer => $"TRY_CONVERT(BIGINT, {json})",
            FieldDataType.Decimal => $"TRY_CONVERT(DECIMAL(28, 8), {json})",
            FieldDataType.Boolean => $"TRY_CONVERT(BIT, {json})",
            FieldDataType.Date => $"TRY_CONVERT(DATE, {json}, 23)",
            FieldDataType.DateTime => $"TRY_CONVERT(DATETIME2(7), {json}, 127)",
            FieldDataType.Lookup => $"TRY_CONVERT(UNIQUEIDENTIFIER, {json})",
            _ => $"CONVERT(NVARCHAR(450), {json})"
        };
    }
}
