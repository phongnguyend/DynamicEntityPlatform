using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer.Analytics;

public sealed class SqlAnalyticsStore(
    SqlServerOptions options,
    IEntityStorageResolver storageResolver) : IAnalyticsStore
{
    public async Task<AnalyticsStoreResult> ExecuteAsync(
        TenantContext tenant, EntityDefinition entity, AnalyticsQuery query,
        CancellationToken cancellationToken)
    {
        var storage = await storageResolver.ResolveAsync(tenant.TenantId, entity.Id, cancellationToken);
        if (!string.Equals(storage.ConnectionKey, options.ConnectionKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unknown tenant connection key '{storage.ConnectionKey}'.");
        var builder = new SqlConnectionStringBuilder(options.TenantServerConnectionString)
        {
            InitialCatalog = storage.DatabaseName
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var plan = SqlAnalyticsQueryBuilder.Build(entity, storage, query);
        await using var command = new SqlCommand(plan.Sql, connection) { CommandTimeout = options.AnalyticsCommandTimeoutSeconds };
        foreach (var parameter in plan.Parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                row[reader.GetName(ordinal)] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
            rows.Add(row);
        }
        var truncated = rows.Count > query.Limit;
        if (truncated) rows.RemoveAt(rows.Count - 1);
        return new(rows, truncated);
    }
}
