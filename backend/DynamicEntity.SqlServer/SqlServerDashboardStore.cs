using System.Data;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Dashboards;
using DynamicEntity.Domain.Storage;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerDashboardStore : IDashboardStore
{
    private const string Columns = "Id,TenantId,Name,DefinitionJson,CreatedBy,CreatedAt,UpdatedAt";

    public async Task<DashboardDefinition> CreateAsync(DashboardDefinition dashboard, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        var sql = $"INSERT dbo.DashboardDefinitions ({Columns}) VALUES (@id,@tenantId,@name,@definition,@createdBy,@createdAt,@updatedAt);";
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, dashboard);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return dashboard;
    }

    public Task<IReadOnlyList<DashboardDefinition>> ListAsync(Guid tenantId, EntityStorageLocation storage, CancellationToken cancellationToken) =>
        ReadManyAsync($"SELECT {Columns} FROM dbo.DashboardDefinitions WHERE TenantId=@tenantId ORDER BY Name,Id;", tenantId, null, storage, cancellationToken);

    public async Task<DashboardDefinition?> GetAsync(Guid tenantId, Guid dashboardId, EntityStorageLocation storage, CancellationToken cancellationToken) =>
        (await ReadManyAsync($"SELECT {Columns} FROM dbo.DashboardDefinitions WHERE TenantId=@tenantId AND Id=@id;", tenantId, dashboardId, storage, cancellationToken)).SingleOrDefault();

    public async Task<DashboardDefinition?> UpdateAsync(DashboardDefinition dashboard, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE dbo.DashboardDefinitions SET Name=@name,DefinitionJson=@definition,UpdatedAt=@updatedAt WHERE TenantId=@tenantId AND Id=@id;";
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, dashboard);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? dashboard : null;
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid dashboardId, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM dbo.DashboardDefinitions WHERE TenantId=@tenantId AND Id=@id;";
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@id", dashboardId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> NameExistsAsync(Guid tenantId, string name, Guid? excludedDashboardId, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        const string sql = "SELECT COUNT(1) FROM dbo.DashboardDefinitions WHERE TenantId=@tenantId AND Name=@name AND (@excludedId IS NULL OR Id<>@excludedId);";
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.Add("@excludedId", SqlDbType.UniqueIdentifier).Value = (object?)excludedDashboardId ?? DBNull.Value;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static async Task<IReadOnlyList<DashboardDefinition>> ReadManyAsync(
        string sql, Guid tenantId, Guid? dashboardId, EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(storage, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        if (dashboardId is not null) command.Parameters.AddWithValue("@id", dashboardId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<DashboardDefinition>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    private static async Task<SqlConnection> OpenAsync(EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        var connection = SqlServerTenantConnection.Create(storage);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void Add(SqlCommand command, DashboardDefinition dashboard)
    {
        command.Parameters.AddWithValue("@id", dashboard.Id);
        command.Parameters.AddWithValue("@tenantId", dashboard.TenantId);
        command.Parameters.AddWithValue("@name", dashboard.Name);
        command.Parameters.AddWithValue("@definition", dashboard.DefinitionJson);
        command.Parameters.AddWithValue("@createdBy", (object?)dashboard.CreatedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", dashboard.CreatedAt);
        command.Parameters.AddWithValue("@updatedAt", dashboard.UpdatedAt);
    }

    private static DashboardDefinition Read(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6));
}
