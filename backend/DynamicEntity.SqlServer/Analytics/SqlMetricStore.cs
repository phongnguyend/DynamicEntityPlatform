using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Storage;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer.Analytics;

public sealed class SqlMetricStore : IMetricStore
{
    public async Task<MetricDefinition> CreateAsync(Guid tenantId, MetricDefinition metric, EntityStorageLocation storage, CancellationToken token)
    {
        const string sql = "INSERT INTO dbo.MetricDefinitions (TenantId,Id,EntityId,Name,Description,Aggregate,FieldId,FilterJson,FormatJson,CreatedBy,CreatedAt,UpdatedAt,SqlJson) SELECT @tenantId,@id,@entityId,@name,@description,@aggregate,@fieldId,@filter,@format,@createdBy,@createdAt,@updatedAt,@sql WHERE @entityId IS NULL OR EXISTS (SELECT 1 FROM dbo.EntityDefinitions WHERE Id=@entityId AND TenantId=@tenantId);";
        await using var c = await OpenAsync(storage, token);
        await using var cmd = new SqlCommand(sql, c);
        Add(cmd, tenantId, metric);
        if (await cmd.ExecuteNonQueryAsync(token) != 1)
        {
            throw new InvalidOperationException("Entity ownership changed while creating metric.");
        }

        return metric;
    }
    public Task<IReadOnlyList<MetricDefinition>> ListAsync(Guid tenantId, Guid? entityId, EntityStorageLocation storage, CancellationToken token) => ReadManyAsync("SELECT m.Id,m.EntityId,m.Name,m.Description,m.Aggregate,m.FieldId,m.FilterJson,m.FormatJson,m.CreatedBy,m.CreatedAt,m.UpdatedAt,m.SqlJson FROM dbo.MetricDefinitions m LEFT JOIN dbo.EntityDefinitions e ON e.Id=m.EntityId WHERE m.TenantId=@tenantId AND (m.EntityId=@entityId OR (m.EntityId IS NULL AND @entityId IS NULL)) ORDER BY m.Name,m.Id;", tenantId, entityId, null, storage, token);
    public async Task<MetricDefinition?> GetAsync(Guid tenantId, Guid? entityId, Guid metricId, EntityStorageLocation storage, CancellationToken token) => (await ReadManyAsync("SELECT m.Id,m.EntityId,m.Name,m.Description,m.Aggregate,m.FieldId,m.FilterJson,m.FormatJson,m.CreatedBy,m.CreatedAt,m.UpdatedAt,m.SqlJson FROM dbo.MetricDefinitions m LEFT JOIN dbo.EntityDefinitions e ON e.Id=m.EntityId WHERE m.TenantId=@tenantId AND (m.EntityId=@entityId OR (m.EntityId IS NULL AND @entityId IS NULL)) AND m.Id=@id;", tenantId, entityId, metricId, storage, token)).SingleOrDefault();
    public async Task<MetricDefinition?> GetByIdAsync(Guid tenantId, Guid metricId, EntityStorageLocation storage, CancellationToken token) => (await ReadManyAsync("SELECT m.Id,m.EntityId,m.Name,m.Description,m.Aggregate,m.FieldId,m.FilterJson,m.FormatJson,m.CreatedBy,m.CreatedAt,m.UpdatedAt,m.SqlJson FROM dbo.MetricDefinitions m WHERE m.TenantId=@tenantId AND m.Id=@id;", tenantId, null, metricId, storage, token)).SingleOrDefault();
    public async Task<MetricDefinition?> UpdateAsync(Guid tenantId, MetricDefinition metric, EntityStorageLocation storage, CancellationToken token)
    {
        const string sql = "UPDATE m SET Name=@name,Description=@description,Aggregate=@aggregate,FieldId=@fieldId,FilterJson=@filter,FormatJson=@format,UpdatedAt=@updatedAt,SqlJson=@sql OUTPUT inserted.Id,inserted.EntityId,inserted.Name,inserted.Description,inserted.Aggregate,inserted.FieldId,inserted.FilterJson,inserted.FormatJson,inserted.CreatedBy,inserted.CreatedAt,inserted.UpdatedAt,inserted.SqlJson FROM dbo.MetricDefinitions m LEFT JOIN dbo.EntityDefinitions e ON e.Id=m.EntityId WHERE m.Id=@id AND (m.EntityId=@entityId OR (m.EntityId IS NULL AND @entityId IS NULL)) AND m.TenantId=@tenantId;";
        await using var c = await OpenAsync(storage, token);
        await using var cmd = new SqlCommand(sql, c);
        Add(cmd, tenantId, metric);
        await using var r = await cmd.ExecuteReaderAsync(token);
        return await r.ReadAsync(token) ? Read(r) : null;
    }
    public async Task<bool> DeleteAsync(Guid tenantId, Guid? entityId, Guid metricId, EntityStorageLocation storage, CancellationToken token)
    {
        const string sql = "DELETE m FROM dbo.MetricDefinitions m LEFT JOIN dbo.EntityDefinitions e ON e.Id=m.EntityId WHERE m.Id=@id AND (m.EntityId=@entityId OR (m.EntityId IS NULL AND @entityId IS NULL)) AND m.TenantId=@tenantId AND NOT EXISTS(SELECT 1 FROM dbo.AlertDefinitions a WHERE a.MetricId=m.Id);";
        await using var c = await OpenAsync(storage, token);
        await using var cmd = new SqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@id", metricId);
        cmd.Parameters.Add("@entityId", System.Data.SqlDbType.UniqueIdentifier).Value = (object?)entityId ?? DBNull.Value;
        cmd.Parameters.AddWithValue("@tenantId", tenantId);
        return await cmd.ExecuteNonQueryAsync(token) == 1;
    }
    private async Task<IReadOnlyList<MetricDefinition>> ReadManyAsync(string sql, Guid tenantId, Guid? entityId, Guid? id, EntityStorageLocation storage, CancellationToken token)
    {
        await using var c = await OpenAsync(storage, token);
        await using var cmd = new SqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);
        cmd.Parameters.Add("@entityId", System.Data.SqlDbType.UniqueIdentifier).Value = (object?)entityId ?? DBNull.Value;
        if (id is not null)
        {
            cmd.Parameters.AddWithValue("@id", id.Value);
        }

        await using var r = await cmd.ExecuteReaderAsync(token);
        var x = new List<MetricDefinition>();
        while (await r.ReadAsync(token))
        {
            x.Add(Read(r));
        }

        return x;
    }
    private async Task<SqlConnection> OpenAsync(EntityStorageLocation storage, CancellationToken token)
    {
        var c = SqlServerTenantConnection.Create(storage);
        await c.OpenAsync(token);
        return c;
    }
    private static void Add(SqlCommand c, Guid tenantId, MetricDefinition m)
    {
        c.Parameters.AddWithValue("@sql", m.Sql is null ? DBNull.Value : JsonSerializer.Serialize(m.Sql));
        c.Parameters.AddWithValue("@tenantId", tenantId);
        c.Parameters.AddWithValue("@id", m.Id);
        c.Parameters.Add("@entityId", System.Data.SqlDbType.UniqueIdentifier).Value = (object?)m.EntityId ?? DBNull.Value;
        c.Parameters.AddWithValue("@name", m.Name);
        c.Parameters.AddWithValue("@description", (object?)m.Description ?? DBNull.Value);
        c.Parameters.AddWithValue("@aggregate", m.Aggregate.ToString());
        c.Parameters.AddWithValue("@fieldId", (object?)m.FieldId ?? DBNull.Value);
        c.Parameters.AddWithValue("@filter", (object?)m.FilterJson ?? DBNull.Value);
        c.Parameters.AddWithValue("@format", (object?)m.FormatJson ?? DBNull.Value);
        c.Parameters.AddWithValue("@createdBy", (object?)m.CreatedBy ?? DBNull.Value);
        c.Parameters.AddWithValue("@createdAt", m.CreatedAt);
        c.Parameters.AddWithValue("@updatedAt", m.UpdatedAt);
    }
    private static MetricDefinition Read(SqlDataReader r) => new(r.GetGuid(0), r.IsDBNull(1) ? null : r.GetGuid(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), Enum.Parse<AggregateFunction>(r.GetString(4)), r.IsDBNull(5) ? null : r.GetGuid(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetGuid(8), r.GetFieldValue<DateTimeOffset>(9), r.GetFieldValue<DateTimeOffset>(10), r.IsDBNull(11) ? null : JsonSerializer.Deserialize<VirtualSqlDefinition>(r.GetString(11)));
}
