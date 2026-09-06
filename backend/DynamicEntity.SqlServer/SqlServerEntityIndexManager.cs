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
        IReadOnlyList<(FieldDefinition Field, bool Descending)> columns,
        CancellationToken cancellationToken)
    {
        var storage = await storageResolver.ResolveAsync(tenant.TenantId, entity.Id, cancellationToken);
        if (storage.Mode != EntityStorageMode.DedicatedTable || storage.RequiresEntityPredicate)
            throw new NotSupportedException("Computed indexes currently require dedicated table storage.");
        var indexId = Guid.NewGuid();
        var indexName = $"IX_{indexId:N}";
        var createdAt = DateTimeOffset.UtcNow;
        var table = PhysicalName.QuoteSqlIdentifier(storage.TableName);
        var index = PhysicalName.QuoteSqlIdentifier(indexName);
        var indexColumns = columns.Select((entry, position) => new EntityIndexColumn(
            entry.Field.Id, $"F_{entry.Field.Id:N}", position, entry.Descending)).ToArray();

        await using var connection = SqlServerTenantConnection.Create(storage);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            for (var i = 0; i < indexColumns.Length; i++)
            {
                var column = indexColumns[i];
                var field = columns[i].Field;
                var quotedColumn = PhysicalName.QuoteSqlIdentifier(column.PhysicalColumnName);
                var expression = ComputedExpression(field);
                var addColumnSql = $"""
                    IF COL_LENGTH(N'dbo.{storage.TableName}', N'{column.PhysicalColumnName}') IS NULL
                        ALTER TABLE dbo.{table} ADD {quotedColumn} AS ({expression}) PERSISTED;
                    """;
                await using var addColumnCommand = new SqlCommand(addColumnSql, connection, transaction) { CommandTimeout = 120 };
                await addColumnCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var columnList = string.Join(", ", indexColumns.Select(column =>
                $"{PhysicalName.QuoteSqlIdentifier(column.PhysicalColumnName)} {(column.IsDescending ? "DESC" : "ASC")}"));
            var createIndexSql = $"CREATE INDEX {index} ON dbo.{table} ({columnList});";
            await using (var createIndexCommand = new SqlCommand(createIndexSql, connection, transaction) { CommandTimeout = 120 })
                await createIndexCommand.ExecuteNonQueryAsync(cancellationToken);

            const string insertIndexSql = """
                INSERT INTO dbo.EntityIndexDefinitions (Id, EntityId, IndexName, Status, CreatedAt)
                VALUES (@id, @entityId, @indexName, N'Active', @createdAt);
                """;
            await using (var insertIndexCommand = new SqlCommand(insertIndexSql, connection, transaction))
            {
                insertIndexCommand.Parameters.AddWithValue("@id", indexId);
                insertIndexCommand.Parameters.AddWithValue("@entityId", entity.Id);
                insertIndexCommand.Parameters.AddWithValue("@indexName", indexName);
                insertIndexCommand.Parameters.AddWithValue("@createdAt", createdAt);
                await insertIndexCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            const string insertColumnSql = """
                INSERT INTO dbo.EntityIndexColumns (Id, IndexId, FieldId, PhysicalColumnName, SortOrder, IsDescending)
                VALUES (@id, @indexId, @fieldId, @columnName, @sortOrder, @isDescending);
                """;
            foreach (var column in indexColumns)
            {
                await using var insertColumnCommand = new SqlCommand(insertColumnSql, connection, transaction);
                insertColumnCommand.Parameters.AddWithValue("@id", Guid.NewGuid());
                insertColumnCommand.Parameters.AddWithValue("@indexId", indexId);
                insertColumnCommand.Parameters.AddWithValue("@fieldId", column.FieldId);
                insertColumnCommand.Parameters.AddWithValue("@columnName", column.PhysicalColumnName);
                insertColumnCommand.Parameters.AddWithValue("@sortOrder", column.SortOrder);
                insertColumnCommand.Parameters.AddWithValue("@isDescending", column.IsDescending);
                await insertColumnCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new EntityIndexDefinition(indexId, entity.Id, indexName, "Active", createdAt, indexColumns);
        }
        catch (SqlException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException($"Index creation failed for entity '{entity.DisplayName}': {exception.Message}");
        }
    }

    public async Task<IReadOnlyList<EntityIndexDefinition>> ListAsync(
        TenantContext tenant,
        EntityDefinition entity,
        CancellationToken cancellationToken)
    {
        var storage = await storageResolver.ResolveAsync(tenant.TenantId, entity.Id, cancellationToken);
        const string sql = """
            SELECT i.Id, i.IndexName, i.Status, i.CreatedAt, c.FieldId, c.PhysicalColumnName, c.SortOrder, c.IsDescending
            FROM dbo.EntityIndexDefinitions AS i
            INNER JOIN dbo.EntityIndexColumns AS c ON c.IndexId = i.Id
            WHERE i.EntityId = @entityId AND i.Status = N'Active'
            ORDER BY i.CreatedAt, c.SortOrder;
            """;
        await using var connection = SqlServerTenantConnection.Create(storage);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@entityId", entity.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var indexes = new List<EntityIndexDefinition>();
        var columnsByIndex = new Dictionary<Guid, List<EntityIndexColumn>>();
        var order = new List<Guid>();
        var metaById = new Dictionary<Guid, (string IndexName, string Status, DateTimeOffset CreatedAt)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var indexId = reader.GetGuid(0);
            if (!metaById.ContainsKey(indexId))
            {
                metaById[indexId] = (reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3));
                columnsByIndex[indexId] = [];
                order.Add(indexId);
            }
            columnsByIndex[indexId].Add(new EntityIndexColumn(
                reader.GetGuid(4), reader.GetString(5), reader.GetInt32(6), reader.GetBoolean(7)));
        }
        foreach (var indexId in order)
        {
            var meta = metaById[indexId];
            indexes.Add(new EntityIndexDefinition(indexId, entity.Id, meta.IndexName, meta.Status, meta.CreatedAt, columnsByIndex[indexId]));
        }
        return indexes;
    }

    public async Task<bool> DeleteAsync(
        TenantContext tenant,
        EntityDefinition entity,
        Guid indexId,
        CancellationToken cancellationToken)
    {
        var storage = await storageResolver.ResolveAsync(tenant.TenantId, entity.Id, cancellationToken);
        if (storage.Mode != EntityStorageMode.DedicatedTable || storage.RequiresEntityPredicate)
            throw new NotSupportedException("Computed indexes currently require dedicated table storage.");
        var table = PhysicalName.QuoteSqlIdentifier(storage.TableName);
        await using var connection = SqlServerTenantConnection.Create(storage);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            string indexName;
            var columns = new List<(Guid FieldId, string PhysicalColumnName)>();
            const string lookupSql = """
                SELECT i.IndexName, c.FieldId, c.PhysicalColumnName
                FROM dbo.EntityIndexDefinitions AS i
                INNER JOIN dbo.EntityIndexColumns AS c ON c.IndexId = i.Id
                WHERE i.Id = @indexId AND i.EntityId = @entityId AND i.Status = N'Active';
                """;
            await using (var lookupCommand = new SqlCommand(lookupSql, connection, transaction))
            {
                lookupCommand.Parameters.AddWithValue("@indexId", indexId);
                lookupCommand.Parameters.AddWithValue("@entityId", entity.Id);
                await using var reader = await lookupCommand.ExecuteReaderAsync(cancellationToken);
                indexName = string.Empty;
                while (await reader.ReadAsync(cancellationToken))
                {
                    indexName = reader.GetString(0);
                    columns.Add((reader.GetGuid(1), reader.GetString(2)));
                }
            }
            if (columns.Count == 0)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return false;
            }

            var quotedIndex = PhysicalName.QuoteSqlIdentifier(indexName);
            var dropIndexSql = $"""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.{storage.TableName}') AND name = N'{indexName}')
                    DROP INDEX {quotedIndex} ON dbo.{table};
                """;
            await using (var dropIndexCommand = new SqlCommand(dropIndexSql, connection, transaction) { CommandTimeout = 120 })
                await dropIndexCommand.ExecuteNonQueryAsync(cancellationToken);

            const string deleteColumnsSql = "DELETE FROM dbo.EntityIndexColumns WHERE IndexId = @indexId;";
            await using (var deleteColumnsCommand = new SqlCommand(deleteColumnsSql, connection, transaction))
            {
                deleteColumnsCommand.Parameters.AddWithValue("@indexId", indexId);
                await deleteColumnsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            const string deleteIndexSql = "DELETE FROM dbo.EntityIndexDefinitions WHERE Id = @indexId;";
            await using (var deleteIndexCommand = new SqlCommand(deleteIndexSql, connection, transaction))
            {
                deleteIndexCommand.Parameters.AddWithValue("@indexId", indexId);
                await deleteIndexCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            const string columnUsageSql = """
                SELECT COUNT(1) FROM dbo.EntityIndexColumns AS c
                INNER JOIN dbo.EntityIndexDefinitions AS i ON i.Id = c.IndexId
                WHERE c.FieldId = @fieldId AND i.Status = N'Active';
                """;
            foreach (var (fieldId, physicalColumnName) in columns)
            {
                int usageCount;
                await using (var usageCommand = new SqlCommand(columnUsageSql, connection, transaction))
                {
                    usageCommand.Parameters.AddWithValue("@fieldId", fieldId);
                    usageCount = (int)(await usageCommand.ExecuteScalarAsync(cancellationToken))!;
                }
                if (usageCount == 0)
                {
                    var quotedColumn = PhysicalName.QuoteSqlIdentifier(physicalColumnName);
                    var dropColumnSql = $"""
                        IF COL_LENGTH(N'dbo.{storage.TableName}', N'{physicalColumnName}') IS NOT NULL
                            ALTER TABLE dbo.{table} DROP COLUMN {quotedColumn};
                        """;
                    await using var dropColumnCommand = new SqlCommand(dropColumnSql, connection, transaction) { CommandTimeout = 120 };
                    await dropColumnCommand.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (SqlException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException($"Index removal failed for entity '{entity.DisplayName}': {exception.Message}");
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
