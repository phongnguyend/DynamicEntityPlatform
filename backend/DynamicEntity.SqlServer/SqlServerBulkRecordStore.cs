using System.Data;
using System.Text.Json;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerBulkRecordStore(SqlServerOptions options, IEntityStorageResolver resolver) : IBulkRecordStore
{
    public async Task<int> PatchAsync(TenantContext tenant, EntityDefinition entity, IReadOnlyList<Guid> recordIds,
        string normalizedPatch, Guid? userId, CancellationToken cancellationToken)
    {
        var (connection, transaction, table) = await OpenAsync(tenant, entity, cancellationToken);
        await using (connection) await using (transaction)
        {
            await StageIdsAsync(connection, transaction, recordIds, cancellationToken);
            using var patch = JsonDocument.Parse(normalizedPatch);
            var expression = "r.Data";
            var command = new SqlCommand { Connection = connection, Transaction = transaction };
            var index = 0;
            foreach (var property in patch.RootElement.EnumerateObject())
            {
                var parameter = $"@value{index++}";
                var path = $"$.{property.Name}";
                if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                {
                    expression = $"JSON_MODIFY({expression}, '{path}', JSON_QUERY({parameter}))";
                    command.Parameters.AddWithValue(parameter, property.Value.GetRawText());
                }
                else
                {
                    expression = $"JSON_MODIFY({expression}, '{path}', {parameter})";
                    command.Parameters.AddWithValue(parameter, Scalar(property.Value));
                }
            }
            command.CommandText = $"UPDATE r SET Data={expression},UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@userId FROM dbo.{table} r INNER JOIN #Ids i ON i.Id=r.Id;";
            command.Parameters.AddWithValue("@userId", (object?)userId ?? DBNull.Value);
            var changed = await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return changed;
        }
    }

    public async Task<int> DeleteAsync(TenantContext tenant, EntityDefinition entity, IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken)
    {
        var (connection, transaction, table) = await OpenAsync(tenant, entity, cancellationToken);
        await using (connection) await using (transaction)
        {
            await StageIdsAsync(connection, transaction, recordIds, cancellationToken);
            await using var command = new SqlCommand($"DELETE r FROM dbo.{table} r INNER JOIN #Ids i ON i.Id=r.Id;", connection, transaction);
            var changed = await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return changed;
        }
    }

    private async Task<(SqlConnection, SqlTransaction, string)> OpenAsync(TenantContext tenant, EntityDefinition entity, CancellationToken cancellationToken)
    {
        var storage = await resolver.ResolveAsync(tenant.TenantId, entity.Id, cancellationToken);
        if (storage.Mode != EntityStorageMode.DedicatedTable) throw new NotSupportedException("Bulk operations currently require dedicated storage.");
        var connection = SqlServerTenantConnection.Create(storage); await connection.OpenAsync(cancellationToken);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        return (connection, transaction, PhysicalName.QuoteSqlIdentifier(storage.TableName));
    }

    private static async Task StageIdsAsync(SqlConnection connection, SqlTransaction transaction, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        await using (var create = new SqlCommand("CREATE TABLE #Ids(Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);", connection, transaction)) await create.ExecuteNonQueryAsync(cancellationToken);
        var table = new DataTable(); table.Columns.Add("Id", typeof(Guid)); foreach (var id in ids) table.Rows.Add(id);
        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction) { DestinationTableName = "#Ids" };
        bulk.ColumnMappings.Add("Id", "Id"); await bulk.WriteToServerAsync(table, cancellationToken);
    }

    private static object Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!, JsonValueKind.Number when value.TryGetInt64(out var number) => number,
        JsonValueKind.Number => value.GetDecimal(), JsonValueKind.True => true, JsonValueKind.False => false,
        JsonValueKind.Null => DBNull.Value, _ => value.GetRawText()
    };
}
