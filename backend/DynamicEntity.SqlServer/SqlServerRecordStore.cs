using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using Microsoft.Data.SqlClient;
using DynamicEntity.SqlServer.Queries;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerRecordStore(
    SqlServerOptions options,
    IEntityStorageResolver storageResolver) : IRecordStore
{
    public async Task<DynamicRecord?> GetAsync(
        TenantContext tenant,
        EntityDefinition entity,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        var (connection, table) = await OpenAsync(tenant, entity, cancellationToken);
        await using (connection)
        await using (var command = new SqlCommand($"SELECT Id, Data, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, Version FROM dbo.{table} WHERE Id = @id;", connection))
        {
            command.Parameters.AddWithValue("@id", recordId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
        }
    }

    public async Task<PagedResult<DynamicRecord>> QueryAsync(
        TenantContext tenant,
        EntityDefinition entity,
        RecordQuery query,
        CancellationToken cancellationToken)
    {
        var storage = await storageResolver.ResolveAsync(tenant.TenantId, entity.Id, cancellationToken);
        var connection = await OpenConnectionAsync(storage, cancellationToken);
        var plan = SqlRecordQueryBuilder.Build(entity, storage, query);
        await using (connection)
        await using (var command = new SqlCommand(plan.Sql, connection))
        {
            foreach (var parameter in plan.Parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var records = new List<DynamicRecord>();
            while (await reader.ReadAsync(cancellationToken)) records.Add(ReadRecord(reader));
            var hasMore = records.Count > query.PageSize;
            if (hasMore) records.RemoveAt(records.Count - 1);
            var cursor = hasMore
                ? SqlRecordQueryBuilder.EncodeNextCursor(records[^1], plan.Offset + query.PageSize, plan.UsesOffset)
                : null;
            return new PagedResult<DynamicRecord>(records, cursor);
        }
    }

    public async Task<DynamicRecord> InsertAsync(
        TenantContext tenant,
        EntityDefinition entity,
        Guid recordId,
        string data,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var (connection, table) = await OpenAsync(tenant, entity, cancellationToken);
        var sql = $"""
            INSERT INTO dbo.{table} (Id, Data, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
            OUTPUT inserted.Id, inserted.Data, inserted.CreatedAt, inserted.CreatedBy,
                   inserted.UpdatedAt, inserted.UpdatedBy, inserted.Version
            VALUES (@id, @data, SYSUTCDATETIME(), @userId, SYSUTCDATETIME(), @userId);
            """;
        await using (connection)
        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@id", recordId);
            command.Parameters.AddWithValue("@data", data);
            command.Parameters.AddWithValue("@userId", (object?)userId ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return ReadRecord(reader);
        }
    }

    public async Task<RecordWriteResult> UpdateAsync(
        TenantContext tenant,
        EntityDefinition entity,
        Guid recordId,
        string data,
        byte[] expectedVersion,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var (connection, table) = await OpenAsync(tenant, entity, cancellationToken);
        var sql = $"""
            UPDATE dbo.{table}
            SET Data = @data, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @userId
            OUTPUT inserted.Id, inserted.Data, inserted.CreatedAt, inserted.CreatedBy,
                   inserted.UpdatedAt, inserted.UpdatedBy, inserted.Version
            WHERE Id = @id AND Version = @expectedVersion;
            """;
        await using (connection)
        {
            await using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@id", recordId);
                command.Parameters.AddWithValue("@data", data);
                command.Parameters.AddWithValue("@userId", (object?)userId ?? DBNull.Value);
                command.Parameters.AddWithValue("@expectedVersion", expectedVersion);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                    return new RecordWriteResult(RecordWriteStatus.Success, ReadRecord(reader));
            }
            return new RecordWriteResult(await ExistsAsync(connection, table, recordId, cancellationToken)
                ? RecordWriteStatus.Conflict : RecordWriteStatus.NotFound);
        }
    }

    public async Task<RecordWriteStatus> DeleteAsync(
        TenantContext tenant,
        EntityDefinition entity,
        Guid recordId,
        byte[] expectedVersion,
        CancellationToken cancellationToken)
    {
        var (connection, table) = await OpenAsync(tenant, entity, cancellationToken);
        await using (connection)
        {
            await using (var command = new SqlCommand($"DELETE FROM dbo.{table} WHERE Id = @id AND Version = @expectedVersion;", connection))
            {
                command.Parameters.AddWithValue("@id", recordId);
                command.Parameters.AddWithValue("@expectedVersion", expectedVersion);
                if (await command.ExecuteNonQueryAsync(cancellationToken) == 1) return RecordWriteStatus.Success;
            }
            return await ExistsAsync(connection, table, recordId, cancellationToken)
                ? RecordWriteStatus.Conflict : RecordWriteStatus.NotFound;
        }
    }

    private async Task<(SqlConnection Connection, string QuotedTable)> OpenAsync(
        TenantContext tenant,
        EntityDefinition entity,
        CancellationToken cancellationToken)
    {
        var storage = await storageResolver.ResolveAsync(tenant.TenantId, entity.Id, cancellationToken);
        if (storage.Mode != EntityStorageMode.DedicatedTable || storage.RequiresEntityPredicate)
            throw new NotSupportedException("The configured storage mode is not implemented by this record store yet.");
        var connection = await OpenConnectionAsync(storage, cancellationToken);
        return (connection, PhysicalName.QuoteSqlIdentifier(storage.TableName));
    }

    private async Task<SqlConnection> OpenConnectionAsync(EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        var connection = SqlServerTenantConnection.Create(storage);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> ExistsAsync(SqlConnection connection, string table, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand($"SELECT COUNT_BIG(1) FROM dbo.{table} WHERE Id = @id;", connection);
        command.Parameters.AddWithValue("@id", id);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
    }

    private static DynamicRecord ReadRecord(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), AsUtc(reader.GetDateTime(2)),
        reader.IsDBNull(3) ? null : reader.GetGuid(3), AsUtc(reader.GetDateTime(4)),
        reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.GetFieldValue<byte[]>(6));

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
