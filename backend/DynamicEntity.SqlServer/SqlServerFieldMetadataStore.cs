using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerFieldMetadataStore(SqlServerOptions options) : IFieldMetadataStore
{
    public async Task<FieldDefinition> CreateAsync(
        Guid tenantId,
        FieldDefinition field,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.EntityDefinitions WHERE Id = @entityId AND TenantId = @tenantId AND Status = N'Active')
                THROW 50001, 'Entity was not found for tenant.', 1;

            INSERT INTO dbo.FieldDefinitions
                (Id, EntityId, Name, DisplayName, StorageKey, DataType, IsRequired, IsUnique,
                 IsFilterable, IsSortable, IsFacetable, IsSearchable, DefaultValueJson,
                 ConfigurationJson, SortOrder, IsActive, CreatedAt, UpdatedAt)
            VALUES
                (@id, @entityId, @name, @displayName, @storageKey, @dataType, @isRequired, @isUnique,
                 @isFilterable, @isSortable, @isFacetable, @isSearchable, @defaultValueJson,
                 @configurationJson, @sortOrder, 1, @createdAt, @updatedAt);

            UPDATE dbo.EntityDefinitions
            SET SchemaVersion = SchemaVersion + 1, UpdatedAt = @updatedAt
            WHERE Id = @entityId AND TenantId = @tenantId;
            """;

        await using var connection = await OpenTenantAsync(tenantStorage, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@tenantId", tenantId);
            command.Parameters.AddWithValue("@id", field.Id);
            command.Parameters.AddWithValue("@entityId", field.EntityId);
            command.Parameters.AddWithValue("@name", field.Name);
            command.Parameters.AddWithValue("@displayName", field.DisplayName);
            command.Parameters.AddWithValue("@storageKey", field.StorageKey);
            command.Parameters.AddWithValue("@dataType", field.DataType.ToString());
            command.Parameters.AddWithValue("@isRequired", field.IsRequired);
            command.Parameters.AddWithValue("@isUnique", field.IsUnique);
            command.Parameters.AddWithValue("@isFilterable", field.IsFilterable);
            command.Parameters.AddWithValue("@isSortable", field.IsSortable);
            command.Parameters.AddWithValue("@isFacetable", field.IsFacetable);
            command.Parameters.AddWithValue("@isSearchable", field.IsSearchable);
            command.Parameters.AddWithValue("@defaultValueJson", (object?)field.DefaultValueJson ?? DBNull.Value);
            command.Parameters.AddWithValue("@configurationJson", (object?)field.ConfigurationJson ?? DBNull.Value);
            command.Parameters.AddWithValue("@sortOrder", field.SortOrder);
            command.Parameters.AddWithValue("@createdAt", field.CreatedAt);
            command.Parameters.AddWithValue("@updatedAt", field.UpdatedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return field;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException($"A field named '{field.Name}' already exists for this entity.");
        }
    }

    public async Task<IReadOnlyList<FieldDefinition>> ListAsync(
        Guid tenantId,
        Guid entityId,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT f.Id, f.EntityId, f.Name, f.DisplayName, f.DataType, f.IsRequired, f.IsUnique,
                   f.IsFilterable, f.IsSortable, f.IsFacetable, f.IsSearchable, f.DefaultValueJson,
                   f.ConfigurationJson, f.SortOrder, f.IsActive, f.CreatedAt, f.UpdatedAt, idx.PhysicalColumnName
            FROM dbo.FieldDefinitions AS f
            INNER JOIN dbo.EntityDefinitions AS e ON e.Id = f.EntityId
            OUTER APPLY (
                SELECT TOP 1 ic.PhysicalColumnName
                FROM dbo.EntityIndexColumns AS ic
                INNER JOIN dbo.EntityIndexDefinitions AS i ON i.Id = ic.IndexId AND i.Status = N'Active'
                WHERE ic.FieldId = f.Id
            ) AS idx
            WHERE e.TenantId = @tenantId AND f.EntityId = @entityId AND f.IsActive = 1
            ORDER BY f.SortOrder, f.Id;
            """;
        await using var connection = await OpenTenantAsync(tenantStorage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@entityId", entityId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<FieldDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new FieldDefinition(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                Enum.Parse<FieldDataType>(reader.GetString(4)), reader.GetBoolean(5), reader.GetBoolean(6),
                reader.GetBoolean(7), reader.GetBoolean(8), reader.GetBoolean(9), reader.GetBoolean(10),
                reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetInt32(13), reader.GetBoolean(14), reader.GetFieldValue<DateTimeOffset>(15),
                reader.GetFieldValue<DateTimeOffset>(16))
                { IndexColumnName = reader.IsDBNull(17) ? null : reader.GetString(17) });
        }
        return result;
    }

    public async Task<FieldDefinition> UpdateAsync(
        Guid tenantId,
        FieldDefinition field,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE f SET Name = @name, DisplayName = @displayName, IsRequired = @isRequired,
                         IsUnique = @isUnique, IsFilterable = @isFilterable, IsSortable = @isSortable,
                         IsFacetable = @isFacetable, IsSearchable = @isSearchable,
                         ConfigurationJson = @configurationJson, SortOrder = @sortOrder, UpdatedAt = @updatedAt
            FROM dbo.FieldDefinitions AS f
            INNER JOIN dbo.EntityDefinitions AS e ON e.Id = f.EntityId
            WHERE f.Id = @fieldId AND f.EntityId = @entityId AND e.TenantId = @tenantId AND f.IsActive = 1;
            IF @@ROWCOUNT = 0 THROW 50002, 'Field was not found.', 1;
            UPDATE dbo.EntityDefinitions SET SchemaVersion = SchemaVersion + 1, UpdatedAt = @updatedAt
            WHERE Id = @entityId AND TenantId = @tenantId;
            """;
        await using var connection = await OpenTenantAsync(tenantStorage, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@tenantId", tenantId);
            command.Parameters.AddWithValue("@entityId", field.EntityId);
            command.Parameters.AddWithValue("@fieldId", field.Id);
            command.Parameters.AddWithValue("@name", field.Name);
            command.Parameters.AddWithValue("@displayName", field.DisplayName);
            command.Parameters.AddWithValue("@isRequired", field.IsRequired);
            command.Parameters.AddWithValue("@isUnique", field.IsUnique);
            command.Parameters.AddWithValue("@isFilterable", field.IsFilterable);
            command.Parameters.AddWithValue("@isSortable", field.IsSortable);
            command.Parameters.AddWithValue("@isFacetable", field.IsFacetable);
            command.Parameters.AddWithValue("@isSearchable", field.IsSearchable);
            command.Parameters.AddWithValue("@configurationJson", (object?)field.ConfigurationJson ?? DBNull.Value);
            command.Parameters.AddWithValue("@sortOrder", field.SortOrder);
            command.Parameters.AddWithValue("@updatedAt", field.UpdatedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return field;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException($"A field named '{field.Name}' already exists for this entity.");
        }
        catch (SqlException exception) when (exception.Number == 50002)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new NotFoundException($"Field '{field.Id}' was not found.");
        }
    }

    public async Task<bool> DeactivateAsync(
        Guid tenantId,
        Guid entityId,
        Guid fieldId,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE f SET IsActive = 0, UpdatedAt = SYSUTCDATETIME()
            FROM dbo.FieldDefinitions AS f
            INNER JOIN dbo.EntityDefinitions AS e ON e.Id = f.EntityId
            WHERE f.Id = @fieldId AND f.EntityId = @entityId AND e.TenantId = @tenantId AND f.IsActive = 1;
            SELECT @@ROWCOUNT;
            """;
        await using var connection = await OpenTenantAsync(tenantStorage, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@entityId", entityId);
        command.Parameters.AddWithValue("@fieldId", fieldId);
        var changed = (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
        if (changed)
        {
            await using var bump = new SqlCommand(
                "UPDATE dbo.EntityDefinitions SET SchemaVersion = SchemaVersion + 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @entityId AND TenantId = @tenantId;",
                connection, transaction);
            bump.Parameters.AddWithValue("@tenantId", tenantId);
            bump.Parameters.AddWithValue("@entityId", entityId);
            await bump.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    private async Task<SqlConnection> OpenTenantAsync(EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        var connection = SqlServerTenantConnection.Create(storage);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
