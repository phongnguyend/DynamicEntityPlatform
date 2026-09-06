using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Storage;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer.Analytics;

public sealed class SqlWebhookSubscriptionStore(SqlServerOptions options) : IWebhookSubscriptionStore
{
    private const string Columns = "w.Id,w.EntityId,w.Name,w.EndpointUrl,w.EventsJson,w.IsEnabled,w.CreatedBy,w.CreatedAt,w.UpdatedAt";
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    public async Task<WebhookSubscription> CreateAsync(Guid tenantId, WebhookSubscription value, EntityStorageLocation storage, CancellationToken token)
    {
        const string sql = "INSERT dbo.WebhookSubscriptions(Id,EntityId,Name,EndpointUrl,EventsJson,IsEnabled,CreatedBy,CreatedAt,UpdatedAt) SELECT @id,@entityId,@name,@endpoint,@events,@enabled,@createdBy,@createdAt,@updatedAt WHERE EXISTS(SELECT 1 FROM dbo.EntityDefinitions WHERE Id=@entityId AND TenantId=@tenantId);";
        await using var connection = await OpenAsync(storage, token); await using var command = new SqlCommand(sql, connection); Add(command, tenantId, value);
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Entity ownership changed while creating webhook subscription.");
        return value;
    }

    public Task<IReadOnlyList<WebhookSubscription>> ListAsync(Guid tenantId, Guid entityId, EntityStorageLocation storage, CancellationToken token) =>
        ReadManyAsync($"SELECT {Columns} FROM dbo.WebhookSubscriptions w JOIN dbo.EntityDefinitions e ON e.Id=w.EntityId WHERE e.TenantId=@tenantId AND w.EntityId=@entityId ORDER BY w.Name,w.Id;", tenantId, entityId, null, storage, token);

    public async Task<WebhookSubscription?> GetAsync(Guid tenantId, Guid entityId, Guid subscriptionId, EntityStorageLocation storage, CancellationToken token) =>
        (await ReadManyAsync($"SELECT {Columns} FROM dbo.WebhookSubscriptions w JOIN dbo.EntityDefinitions e ON e.Id=w.EntityId WHERE e.TenantId=@tenantId AND w.EntityId=@entityId AND w.Id=@id;", tenantId, entityId, subscriptionId, storage, token)).SingleOrDefault();

    public async Task<WebhookSubscription?> UpdateAsync(Guid tenantId, WebhookSubscription value, EntityStorageLocation storage, CancellationToken token)
    {
        var sql = $"UPDATE w SET Name=@name,EndpointUrl=@endpoint,EventsJson=@events,IsEnabled=@enabled,UpdatedAt=@updatedAt OUTPUT {string.Join(',', Columns.Split(',').Select(column => "inserted." + column[(column.IndexOf('.') + 1)..]))} FROM dbo.WebhookSubscriptions w JOIN dbo.EntityDefinitions e ON e.Id=w.EntityId WHERE w.Id=@id AND w.EntityId=@entityId AND e.TenantId=@tenantId;";
        await using var connection = await OpenAsync(storage, token); await using var command = new SqlCommand(sql, connection); Add(command, tenantId, value);
        await using var reader = await command.ExecuteReaderAsync(token); return await reader.ReadAsync(token) ? Read(reader) : null;
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid entityId, Guid subscriptionId, EntityStorageLocation storage, CancellationToken token)
    {
        const string sql = "DELETE w FROM dbo.WebhookSubscriptions w JOIN dbo.EntityDefinitions e ON e.Id=w.EntityId WHERE w.Id=@id AND w.EntityId=@entityId AND e.TenantId=@tenantId;";
        await using var connection = await OpenAsync(storage, token); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId); command.Parameters.AddWithValue("@entityId", entityId); command.Parameters.AddWithValue("@id", subscriptionId);
        return await command.ExecuteNonQueryAsync(token) == 1;
    }

    private async Task<IReadOnlyList<WebhookSubscription>> ReadManyAsync(string sql, Guid tenantId, Guid entityId, Guid? id, EntityStorageLocation storage, CancellationToken token)
    {
        await using var connection = await OpenAsync(storage, token); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId); command.Parameters.AddWithValue("@entityId", entityId); if (id is not null) command.Parameters.AddWithValue("@id", id.Value);
        await using var reader = await command.ExecuteReaderAsync(token); var result = new List<WebhookSubscription>(); while (await reader.ReadAsync(token)) result.Add(Read(reader)); return result;
    }

    private async Task<SqlConnection> OpenAsync(EntityStorageLocation storage, CancellationToken token)
    {
        if (!string.Equals(storage.ConnectionKey, options.ConnectionKey, StringComparison.Ordinal)) throw new InvalidOperationException("Unknown tenant connection key.");
        var builder = new SqlConnectionStringBuilder(options.TenantServerConnectionString) { InitialCatalog = storage.DatabaseName };
        var connection = new SqlConnection(builder.ConnectionString); await connection.OpenAsync(token); return connection;
    }

    private static void Add(SqlCommand command, Guid tenantId, WebhookSubscription value)
    {
        command.Parameters.AddWithValue("@tenantId", tenantId); command.Parameters.AddWithValue("@id", value.Id); command.Parameters.AddWithValue("@entityId", value.EntityId);
        command.Parameters.AddWithValue("@name", value.Name); command.Parameters.AddWithValue("@endpoint", value.Endpoint.AbsoluteUri);
        command.Parameters.AddWithValue("@events", JsonSerializer.Serialize(value.Events, JsonOptions)); command.Parameters.AddWithValue("@enabled", value.IsEnabled);
        command.Parameters.AddWithValue("@createdBy", (object?)value.CreatedBy ?? DBNull.Value); command.Parameters.AddWithValue("@createdAt", value.CreatedAt); command.Parameters.AddWithValue("@updatedAt", value.UpdatedAt);
    }

    private static WebhookSubscription Read(SqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
        new Uri(reader.GetString(3), UriKind.Absolute), JsonSerializer.Deserialize<WebhookEvent[]>(reader.GetString(4), JsonOptions) ?? [],
        reader.GetBoolean(5), reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(8));
}
