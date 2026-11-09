using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Views;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlGlobalViewStore : IGlobalViewStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Columns = "Id,Name,Description,DefinitionJson,CreatedBy,CreatedAt,UpdatedAt";

    public async Task<GlobalViewDefinition> CreateAsync(Guid tenantId, GlobalViewDefinition view, EntityStorageLocation storage, CancellationToken token)
    {
        await using var connection = await OpenAsync(storage, token);
        await using var command = new SqlCommand("INSERT dbo.GlobalViewDefinitions(TenantId,Id,Name,Description,DefinitionJson,CreatedBy,CreatedAt,UpdatedAt) VALUES(@tenantId,@id,@name,@description,@definition,@createdBy,@createdAt,@updatedAt);", connection);
        Add(command, tenantId, view);
        await command.ExecuteNonQueryAsync(token);
        return view;
    }

    public Task<IReadOnlyList<GlobalViewDefinition>> ListAsync(Guid tenantId, EntityStorageLocation storage, CancellationToken token) =>
        ReadAsync(tenantId, null, storage, token);

    public async Task<GlobalViewDefinition?> GetAsync(Guid tenantId, Guid viewId, EntityStorageLocation storage, CancellationToken token) =>
        (await ReadAsync(tenantId, viewId, storage, token)).SingleOrDefault();

    public async Task<GlobalViewDefinition?> UpdateAsync(Guid tenantId, GlobalViewDefinition view, EntityStorageLocation storage, CancellationToken token)
    {
        await using var connection = await OpenAsync(storage, token);
        await using var command = new SqlCommand("UPDATE dbo.GlobalViewDefinitions SET Name=@name,Description=@description,DefinitionJson=@definition,UpdatedAt=@updatedAt WHERE TenantId=@tenantId AND Id=@id;", connection);
        Add(command, tenantId, view);
        return await command.ExecuteNonQueryAsync(token) == 1 ? view : null;
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid viewId, EntityStorageLocation storage, CancellationToken token)
    {
        await using var connection = await OpenAsync(storage, token);
        await using var command = new SqlCommand("DELETE dbo.GlobalViewDefinitions WHERE TenantId=@tenantId AND Id=@id;", connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@id", viewId);
        return await command.ExecuteNonQueryAsync(token) == 1;
    }

    private static async Task<IReadOnlyList<GlobalViewDefinition>> ReadAsync(Guid tenantId, Guid? id, EntityStorageLocation storage, CancellationToken token)
    {
        await using var connection = await OpenAsync(storage, token);
        await using var command = new SqlCommand($"SELECT {Columns} FROM dbo.GlobalViewDefinitions WHERE TenantId=@tenantId AND (@id IS NULL OR Id=@id) ORDER BY Name,Id;", connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.Add("@id", System.Data.SqlDbType.UniqueIdentifier).Value = (object?)id ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new List<GlobalViewDefinition>();
        while (await reader.ReadAsync(token))
        {
            result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                JsonSerializer.Deserialize<GlobalViewSpecification>(reader.GetString(3), JsonOptions)!,
                reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6)));
        }
        return result;
    }

    private static void Add(SqlCommand command, Guid tenantId, GlobalViewDefinition view)
    {
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@id", view.Id);
        command.Parameters.AddWithValue("@name", view.Name);
        command.Parameters.AddWithValue("@description", (object?)view.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@definition", JsonSerializer.Serialize(view.Definition, JsonOptions));
        command.Parameters.AddWithValue("@createdBy", (object?)view.CreatedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", view.CreatedAt);
        command.Parameters.AddWithValue("@updatedAt", view.UpdatedAt);
    }

    private static async Task<SqlConnection> OpenAsync(EntityStorageLocation storage, CancellationToken token)
    {
        var connection = SqlServerTenantConnection.Create(storage);
        await connection.OpenAsync(token);
        return connection;
    }
}
