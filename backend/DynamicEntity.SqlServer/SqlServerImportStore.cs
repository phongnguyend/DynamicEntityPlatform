using System.Data;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Imports;
using DynamicEntity.Domain.Storage;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerImportStore(SqlServerOptions options) : IImportStore
{
    public async Task CreateAsync(Guid tenantId, ImportJob job, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.ImportJobs (Id, EntityId, FileName, Status, ColumnsJson, TotalRows, ValidRows, InvalidRows, CreatedAt, UpdatedAt)
            SELECT @id, @entityId, @fileName, @status, N'[]', 0, 0, 0, @createdAt, @updatedAt
            WHERE EXISTS (SELECT 1 FROM dbo.EntityDefinitions WHERE Id = @entityId AND TenantId = @tenantId);
            """;
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@id", job.Id);
        command.Parameters.AddWithValue("@entityId", job.EntityId);
        command.Parameters.AddWithValue("@fileName", job.FileName);
        command.Parameters.AddWithValue("@status", job.Status.ToString());
        command.Parameters.AddWithValue("@createdAt", job.CreatedAt);
        command.Parameters.AddWithValue("@updatedAt", job.UpdatedAt);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Entity ownership changed while creating import.");
    }

    public async Task SetColumnsAsync(Guid tenantId, Guid importId, IReadOnlyList<string> columns, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        await ExecuteOwnedUpdateAsync(storage, importId, tenantId,
            "UPDATE j SET ColumnsJson = @value, UpdatedAt = SYSUTCDATETIME() FROM dbo.ImportJobs j INNER JOIN dbo.EntityDefinitions e ON e.Id=j.EntityId WHERE j.Id=@id AND e.TenantId=@tenantId;",
            JsonSerializer.Serialize(columns), cancellationToken);
    }

    public async Task StageRowsAsync(Guid tenantId, Guid importId, IReadOnlyList<ImportSourceRow> rows, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;
        await using var connection = await OpenAsync(storage, cancellationToken);
        var table = new DataTable();
        table.Columns.Add("ImportId", typeof(Guid)); table.Columns.Add("SourceRowNumber", typeof(long)); table.Columns.Add("SourceJson", typeof(string));
        foreach (var row in rows) table.Rows.Add(importId, row.RowNumber, row.SourceJson);
        using var bulk = new SqlBulkCopy(connection) { DestinationTableName = "dbo.ImportRows", BatchSize = rows.Count };
        bulk.ColumnMappings.Add("ImportId", "ImportId"); bulk.ColumnMappings.Add("SourceRowNumber", "SourceRowNumber"); bulk.ColumnMappings.Add("SourceJson", "SourceJson");
        await bulk.WriteToServerAsync(table, cancellationToken);
    }

    public async Task<ImportJob?> GetAsync(Guid tenantId, Guid importId, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT j.Id,j.EntityId,j.FileName,j.Status,j.ColumnsJson,j.TotalRows,j.ValidRows,j.InvalidRows,j.CreatedAt,j.UpdatedAt
            FROM dbo.ImportJobs j INNER JOIN dbo.EntityDefinitions e ON e.Id=j.EntityId WHERE j.Id=@id AND e.TenantId=@tenantId;
            """;
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", importId); command.Parameters.AddWithValue("@tenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ReadJob(reader);
    }

    public async IAsyncEnumerable<ImportSourceRow> ReadRowsAsync(Guid tenantId, Guid importId, EntityStorageLocation storage, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.SourceRowNumber,r.SourceJson,r.ConvertedData,r.ValidationError
            FROM dbo.ImportRows r INNER JOIN dbo.ImportJobs j ON j.Id=r.ImportId INNER JOIN dbo.EntityDefinitions e ON e.Id=j.EntityId
            WHERE r.ImportId=@id AND e.TenantId=@tenantId ORDER BY r.SourceRowNumber;
            """;
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", importId); command.Parameters.AddWithValue("@tenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            yield return new(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    public async Task SaveValidationAsync(Guid tenantId, Guid importId, IReadOnlyList<ImportSourceRow> rows, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var create = new SqlCommand("CREATE TABLE #ValidatedRows(RowNumber BIGINT NOT NULL PRIMARY KEY, ConvertedData NVARCHAR(MAX) NULL, ValidationError NVARCHAR(MAX) NULL);", connection, transaction))
            await create.ExecuteNonQueryAsync(cancellationToken);
        var table = new DataTable();
        table.Columns.Add("RowNumber", typeof(long)); table.Columns.Add("ConvertedData", typeof(string)); table.Columns.Add("ValidationError", typeof(string));
        foreach (var row in rows) table.Rows.Add(row.RowNumber, (object?)row.ConvertedData ?? DBNull.Value, (object?)row.ValidationError ?? DBNull.Value);
        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction) { DestinationTableName = "#ValidatedRows" })
        {
            bulk.ColumnMappings.Add("RowNumber", "RowNumber"); bulk.ColumnMappings.Add("ConvertedData", "ConvertedData"); bulk.ColumnMappings.Add("ValidationError", "ValidationError");
            await bulk.WriteToServerAsync(table, cancellationToken);
        }
        await using (var update = new SqlCommand("UPDATE r SET ConvertedData=v.ConvertedData, ValidationError=v.ValidationError FROM dbo.ImportRows r INNER JOIN #ValidatedRows v ON v.RowNumber=r.SourceRowNumber WHERE r.ImportId=@id;", connection, transaction))
        { update.Parameters.AddWithValue("@id", importId); await update.ExecuteNonQueryAsync(cancellationToken); }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetPreviewAsync(Guid tenantId, Guid importId, int total, int valid, int invalid, ImportStatus status, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE j SET TotalRows=@total,ValidRows=@valid,InvalidRows=@invalid,Status=@status,UpdatedAt=SYSUTCDATETIME()
            FROM dbo.ImportJobs j INNER JOIN dbo.EntityDefinitions e ON e.Id=j.EntityId WHERE j.Id=@id AND e.TenantId=@tenantId;
            """;
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", importId); command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@total", total); command.Parameters.AddWithValue("@valid", valid); command.Parameters.AddWithValue("@invalid", invalid); command.Parameters.AddWithValue("@status", status.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CommitAsync(Guid tenantId, ImportJob job, EntityStorageLocation entityStorage, EntityStorageLocation tenantStorage, CancellationToken cancellationToken)
    {
        if (!string.Equals(entityStorage.DatabaseName, tenantStorage.DatabaseName, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Cross-database entity import is not implemented.");
        var table = PhysicalName.QuoteSqlIdentifier(entityStorage.TableName);
        var predicate = entityStorage.RequiresEntityPredicate ? "EntityId, " : string.Empty;
        var values = entityStorage.RequiresEntityPredicate ? "@entityId, " : string.Empty;
        var sql = $"""
            INSERT INTO dbo.{table} ({predicate}Id,Data,CreatedAt,CreatedBy,UpdatedAt,UpdatedBy)
            SELECT {values}NEWID(),ConvertedData,SYSUTCDATETIME(),NULL,SYSUTCDATETIME(),NULL
            FROM dbo.ImportRows WHERE ImportId=@id AND ConvertedData IS NOT NULL AND ValidationError IS NULL;
            DECLARE @count INT=@@ROWCOUNT;
            UPDATE dbo.ImportJobs SET Status=N'Committed',UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;
            SELECT @count;
            """;
        await using var connection = await OpenAsync(tenantStorage, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@id", job.Id); command.Parameters.AddWithValue("@entityId", job.EntityId);
        var count = (int)(await command.ExecuteScalarAsync(cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        return count;
    }

    private async Task ExecuteOwnedUpdateAsync(EntityStorageLocation storage, Guid id, Guid tenantId, string sql, object value, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(storage, cancellationToken); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@tenantId", tenantId); command.Parameters.AddWithValue("@value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private async Task<SqlConnection> OpenAsync(EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        var connection = SqlServerTenantConnection.Create(storage); await connection.OpenAsync(cancellationToken); return connection;
    }
    private static ImportJob ReadJob(SqlDataReader reader) => new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),Enum.Parse<ImportStatus>(reader.GetString(3)),
        JsonSerializer.Deserialize<string[]>(reader.GetString(4)) ?? [],reader.GetInt32(5),reader.GetInt32(6),reader.GetInt32(7),reader.GetFieldValue<DateTimeOffset>(8),reader.GetFieldValue<DateTimeOffset>(9));
}
