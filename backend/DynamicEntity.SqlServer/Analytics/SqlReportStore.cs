using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Storage;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer.Analytics;

public sealed class SqlReportStore(SqlServerOptions options) : IReportStore
{
    public async Task<ReportDefinition> CreateAsync(Guid tenantId, ReportDefinition report, EntityStorageLocation storage, CancellationToken token)
    {
        const string sql = """
            INSERT INTO dbo.ReportDefinitions (Id, EntityId, Name, Description, DefinitionJson, CreatedBy, CreatedAt, UpdatedAt)
            SELECT @id, @entityId, @name, @description, @json, @createdBy, @createdAt, @updatedAt
            WHERE EXISTS (SELECT 1 FROM dbo.EntityDefinitions WHERE Id=@entityId AND TenantId=@tenantId);
            """;
        await using var connection = await OpenAsync(storage, token);
        await using var command = new SqlCommand(sql, connection); Add(command, tenantId, report);
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Entity ownership changed while creating report.");
        return report;
    }

    public async Task<IReadOnlyList<ReportDefinition>> ListAsync(Guid tenantId, Guid entityId, EntityStorageLocation storage, CancellationToken token)
    {
        const string sql = """
            SELECT r.Id,r.EntityId,r.Name,r.Description,r.DefinitionJson,r.CreatedBy,r.CreatedAt,r.UpdatedAt
            FROM dbo.ReportDefinitions r JOIN dbo.EntityDefinitions e ON e.Id=r.EntityId
            WHERE e.TenantId=@tenantId AND r.EntityId=@entityId ORDER BY r.Name,r.Id;
            """;
        return await ReadManyAsync(sql, tenantId, entityId, null, storage, token);
    }

    public async Task<ReportDefinition?> GetAsync(Guid tenantId, Guid entityId, Guid reportId, EntityStorageLocation storage, CancellationToken token) =>
        (await ReadManyAsync("""
            SELECT r.Id,r.EntityId,r.Name,r.Description,r.DefinitionJson,r.CreatedBy,r.CreatedAt,r.UpdatedAt
            FROM dbo.ReportDefinitions r JOIN dbo.EntityDefinitions e ON e.Id=r.EntityId
            WHERE e.TenantId=@tenantId AND r.EntityId=@entityId AND r.Id=@id;
            """, tenantId, entityId, reportId, storage, token)).SingleOrDefault();

    public async Task<ReportDefinition?> UpdateAsync(Guid tenantId, ReportDefinition report, EntityStorageLocation storage, CancellationToken token)
    {
        const string sql = """
            UPDATE r SET Name=@name,Description=@description,DefinitionJson=@json,UpdatedAt=@updatedAt
            OUTPUT inserted.Id,inserted.EntityId,inserted.Name,inserted.Description,inserted.DefinitionJson,inserted.CreatedBy,inserted.CreatedAt,inserted.UpdatedAt
            FROM dbo.ReportDefinitions r JOIN dbo.EntityDefinitions e ON e.Id=r.EntityId
            WHERE r.Id=@id AND r.EntityId=@entityId AND e.TenantId=@tenantId;
            """;
        await using var connection = await OpenAsync(storage, token);
        await using var command = new SqlCommand(sql, connection); Add(command, tenantId, report);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? Read(reader) : null;
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid entityId, Guid reportId, EntityStorageLocation storage, CancellationToken token)
    {
        const string sql = "DELETE r FROM dbo.ReportDefinitions r JOIN dbo.EntityDefinitions e ON e.Id=r.EntityId WHERE r.Id=@id AND r.EntityId=@entityId AND e.TenantId=@tenantId;";
        await using var connection = await OpenAsync(storage, token); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", reportId); command.Parameters.AddWithValue("@entityId", entityId); command.Parameters.AddWithValue("@tenantId", tenantId);
        return await command.ExecuteNonQueryAsync(token) == 1;
    }

    private async Task<IReadOnlyList<ReportDefinition>> ReadManyAsync(string sql, Guid tenantId, Guid entityId, Guid? id, EntityStorageLocation storage, CancellationToken token)
    {
        await using var connection = await OpenAsync(storage, token); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId); command.Parameters.AddWithValue("@entityId", entityId);
        if (id is not null) command.Parameters.AddWithValue("@id", id.Value);
        await using var reader = await command.ExecuteReaderAsync(token); var result = new List<ReportDefinition>();
        while (await reader.ReadAsync(token)) result.Add(Read(reader)); return result;
    }

    private async Task<SqlConnection> OpenAsync(EntityStorageLocation storage, CancellationToken token)
    {
        if (!string.Equals(storage.ConnectionKey, options.ConnectionKey, StringComparison.Ordinal)) throw new InvalidOperationException("Unknown tenant connection key.");
        var builder = new SqlConnectionStringBuilder(options.TenantServerConnectionString) { InitialCatalog=storage.DatabaseName };
        var connection = new SqlConnection(builder.ConnectionString); await connection.OpenAsync(token); return connection;
    }
    private static void Add(SqlCommand command, Guid tenantId, ReportDefinition report)
    {
        command.Parameters.AddWithValue("@tenantId",tenantId); command.Parameters.AddWithValue("@id",report.Id); command.Parameters.AddWithValue("@entityId",report.EntityId);
        command.Parameters.AddWithValue("@name",report.Name); command.Parameters.AddWithValue("@description",(object?)report.Description??DBNull.Value);
        command.Parameters.AddWithValue("@json",report.DefinitionJson); command.Parameters.AddWithValue("@createdBy",(object?)report.CreatedBy??DBNull.Value);
        command.Parameters.AddWithValue("@createdAt",report.CreatedAt); command.Parameters.AddWithValue("@updatedAt",report.UpdatedAt);
    }
    private static ReportDefinition Read(SqlDataReader r) => new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetGuid(5),r.GetFieldValue<DateTimeOffset>(6),r.GetFieldValue<DateTimeOffset>(7));
}
