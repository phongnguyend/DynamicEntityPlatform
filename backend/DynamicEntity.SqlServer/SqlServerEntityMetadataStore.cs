using System.Text;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerEntityMetadataStore : IEntityMetadataStore
{
    private const string ListSql = """
        SELECT Id, TenantId, Name, DisplayName, Description, SchemaVersion, Status, CreatedAt, UpdatedAt, Icon, PinnedOrder
        FROM dbo.EntityDefinitions WHERE TenantId = @tenantId AND Status = N'Active' ORDER BY DisplayName, Id;
        """;


    public async Task<EntityDefinition> CreateAsync(
        EntityDefinition entity,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken)
    {
        var tableName = PhysicalName.ForEntityTable(entity.Id);
        var quotedTable = PhysicalName.QuoteSqlIdentifier(tableName);
        var sql = $$"""
            INSERT INTO dbo.EntityDefinitions
                (Id, TenantId, Name, DisplayName, Description, Icon, SchemaVersion, Status, CreatedAt, UpdatedAt)
            VALUES
                (@id, @tenantId, @name, @displayName, @description, @icon, @schemaVersion, @status, @createdAt, @updatedAt);

            CREATE TABLE dbo.{{quotedTable}}
            (
                Id UNIQUEIDENTIFIER NOT NULL,
                Data NVARCHAR(MAX) NOT NULL,
                CreatedAt DATETIME2(7) NOT NULL,
                CreatedBy UNIQUEIDENTIFIER NULL,
                UpdatedAt DATETIME2(7) NOT NULL,
                UpdatedBy UNIQUEIDENTIFIER NULL,
                Version ROWVERSION NOT NULL,
                CONSTRAINT {{PhysicalName.QuoteSqlIdentifier($"PK_{tableName}")}} PRIMARY KEY (Id),
                CONSTRAINT {{PhysicalName.QuoteSqlIdentifier($"CK_{tableName}_Data_IsJson")}} CHECK (ISJSON(Data) = 1)
            );

            INSERT INTO dbo.EntityStorageMappings
                (EntityId, TableName, StorageMode, RequiresEntityPredicate, Status, CreatedAt, UpdatedAt)
            VALUES
                (@id, @tableName, @storageMode, 0, N'Active', @createdAt, @updatedAt);
            """;

        await using var connection = await OpenTenantAsync(tenantStorage, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            AddEntityParameters(command, entity);
            command.Parameters.AddWithValue("@tableName", tableName);
            command.Parameters.AddWithValue("@storageMode", EntityStorageMode.DedicatedTable.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return entity;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException($"An entity named '{entity.Name}' already exists.");
        }
    }

    public async Task<EntityDefinition?> GetAsync(
        Guid tenantId,
        Guid entityId,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, TenantId, Name, DisplayName, Description, SchemaVersion, Status, CreatedAt, UpdatedAt, Icon, PinnedOrder
            FROM dbo.EntityDefinitions WHERE TenantId = @tenantId AND Id = @entityId;
            """;
        await using var connection = await OpenTenantAsync(tenantStorage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@entityId", entityId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntity(reader) : null;
    }

    public async Task<IReadOnlyList<EntityDefinition>> ListAsync(
        Guid tenantId,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenTenantAsync(tenantStorage, cancellationToken);
        return await ListAsync(connection, transaction: null, tenantId, cancellationToken);
    }

    public async Task<EntityStorageLocation?> GetStorageAsync(
        Guid tenantId,
        Guid entityId,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT m.TableName, m.StorageMode, m.RequiresEntityPredicate
            FROM dbo.EntityStorageMappings AS m
            INNER JOIN dbo.EntityDefinitions AS e ON e.Id = m.EntityId
            WHERE e.TenantId = @tenantId AND e.Id = @entityId AND m.Status = N'Active';
            """;
        await using var connection = await OpenTenantAsync(tenantStorage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@entityId", entityId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return tenantStorage with
        {
            TableName = reader.GetString(0),
            Mode = Enum.Parse<EntityStorageMode>(reader.GetString(1)),
            RequiresEntityPredicate = reader.GetBoolean(2)
        };
    }

    public async Task<EntityDefinition?> UpdateAsync(Guid tenantId, EntityDefinition entity,
        EntityStorageLocation tenantStorage, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.EntityDefinitions SET Name=@name,DisplayName=@displayName,Description=@description,Icon=@icon,UpdatedAt=@updatedAt
            WHERE Id=@id AND TenantId=@tenantId AND Status=N'Active';
            """;
        await using var connection = await OpenTenantAsync(tenantStorage, cancellationToken);
        await using var command = new SqlCommand(sql, connection); AddEntityParameters(command, entity);
        try { return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? entity : null; }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        { throw new ConflictException($"An entity named '{entity.Name}' already exists."); }
    }

    public async Task<bool> ArchiveAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken)
    {
        const string sql = "UPDATE dbo.EntityDefinitions SET Status=N'Archived',UpdatedAt=SYSUTCDATETIME() WHERE Id=@id AND TenantId=@tenantId AND Status=N'Active';";
        await using var connection = await OpenTenantAsync(tenantStorage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", entityId); command.Parameters.AddWithValue("@tenantId", tenantId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<EntityDefinition>> SetPinnedOrderAsync(Guid tenantId,
        IReadOnlyList<Guid> entityIds, EntityStorageLocation tenantStorage, CancellationToken cancellationToken)
    {
        // Clearing and re-numbering in one transaction keeps the pinned positions contiguous and unique
        // even when two clients reorder at the same time — the loser simply overwrites the whole set.
        var sql = new StringBuilder("""
            UPDATE dbo.EntityDefinitions SET PinnedOrder = NULL
            WHERE TenantId = @tenantId AND PinnedOrder IS NOT NULL;
            """);
        for (var index = 0; index < entityIds.Count; index++)
        {
            sql.AppendLine().Append($"""
                UPDATE dbo.EntityDefinitions SET PinnedOrder = @order{index}
                WHERE TenantId = @tenantId AND Id = @id{index} AND Status = N'Active';
                """);
        }

        await using var connection = await OpenTenantAsync(tenantStorage, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var command = new SqlCommand(sql.ToString(), connection, transaction))
            {
                command.Parameters.AddWithValue("@tenantId", tenantId);
                for (var index = 0; index < entityIds.Count; index++)
                {
                    command.Parameters.AddWithValue($"@order{index}", index);
                    command.Parameters.AddWithValue($"@id{index}", entityIds[index]);
                }

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var entities = await ListAsync(connection, transaction, tenantId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return entities;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<IReadOnlyList<EntityDefinition>> ListAsync(SqlConnection connection,
        SqlTransaction? transaction, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(ListSql, connection, transaction);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entities = new List<EntityDefinition>();
        while (await reader.ReadAsync(cancellationToken)) entities.Add(ReadEntity(reader));
        return entities;
    }

    private async Task<SqlConnection> OpenTenantAsync(
        EntityStorageLocation storage,
        CancellationToken cancellationToken)
    {
        var connection = SqlServerTenantConnection.Create(storage);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddEntityParameters(SqlCommand command, EntityDefinition entity)
    {
        command.Parameters.AddWithValue("@id", entity.Id);
        command.Parameters.AddWithValue("@tenantId", entity.TenantId);
        command.Parameters.AddWithValue("@name", entity.Name);
        command.Parameters.AddWithValue("@displayName", entity.DisplayName);
        command.Parameters.AddWithValue("@description", (object?)entity.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@icon", (object?)entity.Icon ?? DBNull.Value);
        command.Parameters.AddWithValue("@schemaVersion", entity.SchemaVersion);
        command.Parameters.AddWithValue("@status", entity.Status.ToString());
        command.Parameters.AddWithValue("@createdAt", entity.CreatedAt);
        command.Parameters.AddWithValue("@updatedAt", entity.UpdatedAt);
    }

    private static EntityDefinition ReadEntity(SqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetInt32(5),
        Enum.Parse<EntityStatus>(reader.GetString(6)),
        reader.GetFieldValue<DateTimeOffset>(7),
        reader.GetFieldValue<DateTimeOffset>(8))
    {
        Icon = reader.IsDBNull(9) ? null : reader.GetString(9),
        PinnedOrder = reader.IsDBNull(10) ? null : reader.GetInt32(10)
    };
}
