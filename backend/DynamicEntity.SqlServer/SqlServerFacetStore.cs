using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerFacetStore(
    IEntityStorageResolver storageResolver) : IFacetStore
{
    public async Task<IReadOnlyList<FacetValue>> GetValuesAsync(
        TenantContext tenant,
        EntityDefinition entity,
        FieldDefinition field,
        CancellationToken cancellationToken)
    {
        var storage = await storageResolver.ResolveAsync(tenant.TenantId, entity.Id, cancellationToken);
        await using var connection = SqlServerTenantConnection.Create(storage);
        await connection.OpenAsync(cancellationToken);
        var table = PhysicalName.QuoteSqlIdentifier(storage.TableName);
        var entityWhere = storage.RequiresEntityPredicate ? " AND r.EntityId = @entityId" : string.Empty;
        var sql = field.DataType == FieldDataType.MultiChoice
            ? $"""
                SELECT CONVERT(NVARCHAR(4000), j.[value]) AS [Value], COUNT_BIG(*) AS RecordCount
                FROM dbo.{table} AS r CROSS APPLY OPENJSON(r.Data, '$.{field.StorageKey}') AS j
                WHERE j.[value] IS NOT NULL{entityWhere}
                GROUP BY CONVERT(NVARCHAR(4000), j.[value]) ORDER BY [Value];
                """
            : $"""
                SELECT {(field.IndexColumnName is null ? $"JSON_VALUE(r.Data, '$.{field.StorageKey}')" : $"r.{PhysicalName.QuoteSqlIdentifier(field.IndexColumnName)}")} AS [Value], COUNT_BIG(*) AS RecordCount
                FROM dbo.{table} AS r
                WHERE {(field.IndexColumnName is null ? $"JSON_VALUE(r.Data, '$.{field.StorageKey}')" : $"r.{PhysicalName.QuoteSqlIdentifier(field.IndexColumnName)}")} IS NOT NULL{entityWhere}
                GROUP BY {(field.IndexColumnName is null ? $"JSON_VALUE(r.Data, '$.{field.StorageKey}')" : $"r.{PhysicalName.QuoteSqlIdentifier(field.IndexColumnName)}")} ORDER BY [Value];
                """;
        await using var command = new SqlCommand(sql, connection);
        if (storage.RequiresEntityPredicate) command.Parameters.AddWithValue("@entityId", entity.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<FacetValue>();
        while (await reader.ReadAsync(cancellationToken)) values.Add(new(reader.GetString(0), reader.GetInt64(1)));
        return values;
    }
}
