using System.Data;
using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Storage;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer.Analytics;

public sealed class VirtualSqlService(IControlPlaneStore controlPlane, IEntityMetadataStore entities,
    IFieldMetadataStore fields, IEntityStorageResolver storageResolver, SqlServerOptions options,
    TimeProvider timeProvider) : IVirtualSqlService
{
    private async Task<(VirtualSqlPreview Plan, EntityStorageLocation Storage)> PrepareAsync(Guid tenantId, Guid? entityId,
        VirtualSqlDefinition definition, IReadOnlyDictionary<string, JsonElement>? parameters, bool allowUnbound, CancellationToken token)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, token);
        if (entityId is not null)
        {
            _ = await entities.GetAsync(tenantId, entityId.Value, storage, token) ?? throw new NotFoundException("Entity not found.");
        }
        var tables = new List<VirtualSqlTable>();
        foreach (var entity in await entities.ListAsync(tenantId, storage, token))
        {
            var location = await storageResolver.ResolveAsync(tenantId, entity.Id, token);
            if (location.DatabaseName != storage.DatabaseName)
            {
                throw new ValidationException("Virtual SQL requires entities in the same tenant database.");
            }
            tables.Add(new(entity with
            {
                Fields = await fields.ListAsync(tenantId, entity.Id, storage, token)
            }, location));
        }
        return (VirtualSqlCompiler.Compile(definition, tables, parameters, allowUnbound), storage);
    }

    public async Task<VirtualSqlPreview> PreviewAsync(Guid tenantId, Guid? entityId, VirtualSqlDefinition definition,
        IReadOnlyDictionary<string, JsonElement>? parameters, CancellationToken token)
    {
        var (plan, storage) = await PrepareAsync(tenantId, entityId, definition, parameters, true, token);
        await ReadAsync(plan, storage, definition, 1, true, token);
        return plan;
    }

    public async Task<AnalyticsResult> ExecuteAsync(Guid tenantId, Guid? entityId, VirtualSqlDefinition definition,
        IReadOnlyDictionary<string, JsonElement>? parameters, int limit, bool metric, CancellationToken token)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ValidationException("SQL result limit must be between 1 and 1000.");
        }
        var (plan, storage) = await PrepareAsync(tenantId, entityId, definition, parameters, false, token);
        var result = await ReadAsync(plan, storage, definition, metric ? 2 : limit, false, token);
        if (metric)
        {
            if (result.Rows.Count != 1 || result.Columns.Count != 1)
            {
                throw new ValidationException("A SQL metric must return exactly one row and one column.");
            }
            return result with
            {
                Columns = [result.Columns[0] with { Key = "value", Role = AnalyticsColumnRole.Measure }],
                Rows = [new Dictionary<string, object?> { ["value"] = result.Rows[0].Values.Single() }]
            };
        }
        return result;
    }

    private async Task<AnalyticsResult> ReadAsync(VirtualSqlPreview plan, EntityStorageLocation storage,
        VirtualSqlDefinition definition, int limit, bool schemaOnly, CancellationToken token)
    {
        await using var connection = SqlServerTenantConnection.Create(storage);
        await connection.OpenAsync(token);
        // The generated query runs as a login-less, SELECT-only database principal.
        await using var restrict = new SqlCommand($"EXECUTE AS USER = 'VirtualSqlReader'; SET ROWCOUNT {limit + 1};", connection);
        await restrict.ExecuteNonQueryAsync(token);
        try
        {
            await using var command = new SqlCommand(plan.Sql, connection) { CommandTimeout = options.AnalyticsCommandTimeoutSeconds };
            foreach (var value in plan.Parameters)
            {
                var definitionParameter = definition.Parameters.Single(parameter => ("@" + parameter.Parameter).Equals(value.Name, StringComparison.OrdinalIgnoreCase));
                var parameter = command.Parameters.Add(value.Name, definitionParameter.Type switch
                {
                    "Integer" => SqlDbType.BigInt,
                    "Decimal" => SqlDbType.Decimal,
                    "Boolean" => SqlDbType.Bit,
                    "Date" => SqlDbType.Date,
                    "DateTime" => SqlDbType.DateTime2,
                    "Guid" => SqlDbType.UniqueIdentifier,
                    _ => SqlDbType.NVarChar
                });
                parameter.Size = -1;
                parameter.Precision = 38;
                parameter.Scale = 10;
                parameter.Value = value.Value ?? DBNull.Value;
            }
            await using var reader = await command.ExecuteReaderAsync(schemaOnly ? CommandBehavior.SchemaOnly : CommandBehavior.Default, token);
            var columns = new List<AnalyticsColumn>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var name = reader.GetName(index);
                if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
                {
                    throw new ValidationException("Every SQL output column must have a unique, non-empty alias.");
                }
                var type = reader.GetFieldType(index);
                var numeric = type == typeof(int) || type == typeof(long) || type == typeof(decimal) || type == typeof(double) || type == typeof(float) || type == typeof(short);
                var role = definition.MeasureColumns is not null
                    ? (definition.MeasureColumns.Contains(name, StringComparer.OrdinalIgnoreCase) ? AnalyticsColumnRole.Measure : AnalyticsColumnRole.Dimension)
                    : (numeric ? AnalyticsColumnRole.Measure : AnalyticsColumnRole.Dimension);
                columns.Add(new(name, name, numeric ? "Decimal" : type == typeof(DateTime) ? "DateTime" : "Text", role));
            }
            if (definition.MeasureColumns?.Any(name => !names.Contains(name)) == true)
            {
                throw new ValidationException("A configured measure column is missing from the SQL output.");
            }
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            while (!schemaOnly && rows.Count <= limit && await reader.ReadAsync(token))
            {
                rows.Add(columns.Select((column, index) => (column.Key, Value: reader.IsDBNull(index) ? null : reader.GetValue(index)))
                    .ToDictionary(value => value.Key, value => value.Value));
            }
            var truncated = rows.Count > limit;
            if (truncated)
            {
                rows.RemoveAt(rows.Count - 1);
            }
            return new(columns, rows, timeProvider.GetUtcNow(), truncated);
        }
        catch (SqlException exception)
        {
            throw new ValidationException($"SQL validation/execution failed: {exception.Message}");
        }
        finally
        {
            await using var revert = new SqlCommand("SET ROWCOUNT 0; REVERT;", connection);
            await revert.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
