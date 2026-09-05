using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Views;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerViewStore(SqlServerOptions options) : IViewStore
{
    public async Task<ViewDefinition> CreateAsync(Guid tenantId, ViewDefinition view, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.ViewDefinitions (Id, EntityId, Name, DefinitionJson, CreatedBy, CreatedAt, UpdatedAt)
            SELECT @id, @entityId, @name, @definition, @createdBy, @createdAt, @updatedAt
            WHERE EXISTS (SELECT 1 FROM dbo.EntityDefinitions WHERE Id = @entityId AND TenantId = @tenantId);
            """;
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, tenantId, view);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Entity ownership changed while creating view.");
        return view;
    }

    public async Task<IReadOnlyList<ViewDefinition>> ListAsync(Guid tenantId, Guid entityId, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT v.Id, v.EntityId, v.Name, v.DefinitionJson, v.CreatedBy, v.CreatedAt, v.UpdatedAt
            FROM dbo.ViewDefinitions v INNER JOIN dbo.EntityDefinitions e ON e.Id = v.EntityId
            WHERE e.TenantId = @tenantId AND v.EntityId = @entityId ORDER BY v.Name, v.Id;
            """;
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@entityId", entityId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ViewDefinition>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    public async Task<ViewDefinition?> UpdateAsync(Guid tenantId, ViewDefinition view, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE v SET Name = @name, DefinitionJson = @definition, UpdatedAt = @updatedAt
            FROM dbo.ViewDefinitions v INNER JOIN dbo.EntityDefinitions e ON e.Id = v.EntityId
            WHERE v.Id = @id AND v.EntityId = @entityId AND e.TenantId = @tenantId;
            """;
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, tenantId, view);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? view : null;
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid viewId, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE v FROM dbo.ViewDefinitions v INNER JOIN dbo.EntityDefinitions e ON e.Id = v.EntityId
            WHERE v.Id = @id AND e.TenantId = @tenantId;
            """;
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", viewId);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<SqlConnection> OpenAsync(EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        if (!string.Equals(storage.ConnectionKey, options.ConnectionKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unknown tenant connection key '{storage.ConnectionKey}'.");
        var builder = new SqlConnectionStringBuilder(options.TenantServerConnectionString) { InitialCatalog = storage.DatabaseName };
        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void Add(SqlCommand command, Guid tenantId, ViewDefinition view)
    {
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@id", view.Id);
        command.Parameters.AddWithValue("@entityId", view.EntityId);
        command.Parameters.AddWithValue("@name", view.Name);
        command.Parameters.AddWithValue("@definition", view.DefinitionJson);
        command.Parameters.AddWithValue("@createdBy", (object?)view.CreatedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", view.CreatedAt);
        command.Parameters.AddWithValue("@updatedAt", view.UpdatedAt);
    }

    private static ViewDefinition Read(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6));
}
