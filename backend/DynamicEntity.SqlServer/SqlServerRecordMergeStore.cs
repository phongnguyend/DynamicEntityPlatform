using System.Data;
using System.Text.Json;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerRecordMergeStore(SqlServerOptions options, IEntityStorageResolver resolver) : IRecordMergeStore
{
    public async Task<(int Inserted, int Updated)> MergeAsync(TenantContext tenant, EntityDefinition entity,
        FieldDefinition matchField, IReadOnlyList<string> normalizedRecords, Guid? userId, CancellationToken cancellationToken)
    {
        var storage = await resolver.ResolveAsync(tenant.TenantId, entity.Id, cancellationToken);
        if (storage.Mode != EntityStorageMode.DedicatedTable) throw new NotSupportedException("Merge currently requires dedicated storage.");
        await using var connection = SqlServerTenantConnection.Create(options, storage); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var create = new SqlCommand("CREATE TABLE #MergeRows(SourceRow INT NOT NULL PRIMARY KEY, ExternalKey NVARCHAR(4000) NOT NULL, NewData NVARCHAR(MAX) NOT NULL);", connection, transaction))
            await create.ExecuteNonQueryAsync(cancellationToken);
        var rows = new DataTable(); rows.Columns.Add("SourceRow", typeof(int)); rows.Columns.Add("ExternalKey", typeof(string)); rows.Columns.Add("NewData", typeof(string));
        for (var index = 0; index < normalizedRecords.Count; index++)
        {
            using var json = JsonDocument.Parse(normalizedRecords[index]);
            rows.Rows.Add(index + 1, Scalar(json.RootElement.GetProperty(matchField.StorageKey)), normalizedRecords[index]);
        }
        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction) { DestinationTableName = "#MergeRows" })
        {
            bulk.ColumnMappings.Add("SourceRow", "SourceRow"); bulk.ColumnMappings.Add("ExternalKey", "ExternalKey"); bulk.ColumnMappings.Add("NewData", "NewData");
            await bulk.WriteToServerAsync(rows, cancellationToken);
        }
        var table = PhysicalName.QuoteSqlIdentifier(storage.TableName);
        var path = $"$.{matchField.StorageKey}";
        var sql = $"""
            UPDATE target SET Data=source.NewData,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@userId
            FROM dbo.{table} target INNER JOIN #MergeRows source ON JSON_VALUE(target.Data, '{path}')=source.ExternalKey;
            DECLARE @updated INT=@@ROWCOUNT;
            INSERT INTO dbo.{table}(Id,Data,CreatedAt,CreatedBy,UpdatedAt,UpdatedBy)
            SELECT NEWID(),source.NewData,SYSUTCDATETIME(),@userId,SYSUTCDATETIME(),@userId
            FROM #MergeRows source WHERE NOT EXISTS
                (SELECT 1 FROM dbo.{table} target WHERE JSON_VALUE(target.Data, '{path}')=source.ExternalKey);
            SELECT @@ROWCOUNT AS Inserted,@updated AS Updated;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@userId", (object?)userId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken);
        var result = (reader.GetInt32(0), reader.GetInt32(1)); await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken); return result;
    }
    private static string Scalar(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
}
