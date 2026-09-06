using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerControlPlaneStore(SqlServerOptions options)
    : IControlPlaneStore, IControlPlaneInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var configured = new SqlConnectionStringBuilder(options.ControlDatabaseConnectionString);
        var databaseName = configured.InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("The control database connection string must specify Initial Catalog.");
        }

        configured.InitialCatalog = "tempdb";
        await using (var bootstrapConnection = new SqlConnection(configured.ConnectionString))
        {
            await bootstrapConnection.OpenAsync(cancellationToken);
            const string createSql = """
                IF DB_ID(@databaseName) IS NULL
                BEGIN
                    DECLARE @createDatabaseSql NVARCHAR(MAX) = N'CREATE DATABASE ' + QUOTENAME(@databaseName) + N';';
                    EXEC sys.sp_executesql @createDatabaseSql;
                END;
                """;
            await using var create = new SqlCommand(createSql, bootstrapConnection);
            create.Parameters.AddWithValue("@databaseName", databaseName);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var connection = new SqlConnection(options.ControlDatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(SqlServerSchema.ControlPlane, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreateTenantAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.Tenants (Id, Name, Status, CreatedAt, UpdatedAt)
            VALUES (@id, @name, @status, @createdAt, @updatedAt);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", tenant.Id);
        command.Parameters.AddWithValue("@name", tenant.Name);
        command.Parameters.AddWithValue("@status", tenant.Status.ToString());
        command.Parameters.AddWithValue("@createdAt", tenant.CreatedAt);
        command.Parameters.AddWithValue("@updatedAt", tenant.UpdatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetTenantStatusAsync(Guid tenantId, TenantStatus status, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE dbo.Tenants SET Status = @status, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id;";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", tenantId);
        command.Parameters.AddWithValue("@status", status.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateTenantNameAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE dbo.Tenants SET Name = @name, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id;";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", tenantId);
        command.Parameters.AddWithValue("@name", name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Id, Name, Status, CreatedAt, UpdatedAt FROM dbo.Tenants WHERE Id = @id;";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new Tenant(
            reader.GetGuid(0), reader.GetString(1), Enum.Parse<TenantStatus>(reader.GetString(2)),
            reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4));
    }

    public async Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Name, Status, CreatedAt, UpdatedAt
            FROM dbo.Tenants
            WHERE Status <> N'Archived'
            ORDER BY Name, Id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tenants = new List<Tenant>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tenants.Add(new Tenant(
                reader.GetGuid(0), reader.GetString(1), Enum.Parse<TenantStatus>(reader.GetString(2)),
                reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4)));
        }
        return tenants;
    }

    public async Task SaveTenantStorageAsync(
        Guid tenantId,
        EntityStorageLocation location,
        CancellationToken cancellationToken)
    {
        const string sql = """
            MERGE dbo.TenantStorage AS target
            USING (SELECT @tenantId AS TenantId) AS source ON target.TenantId = source.TenantId
            WHEN MATCHED THEN UPDATE SET DatabaseName=@databaseName,
                ConnectionString=@connectionString, Status=N'Active', UpdatedAt=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (TenantId, DatabaseName, ConnectionString, Status, CreatedAt, UpdatedAt)
                VALUES (@tenantId, @databaseName, @connectionString, N'Active', SYSUTCDATETIME(), SYSUTCDATETIME());
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@databaseName", location.DatabaseName);
        command.Parameters.AddWithValue("@connectionString", (object?)location.ConnectionString ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<EntityStorageLocation?> GetTenantStorageAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT DatabaseName, ConnectionString FROM dbo.TenantStorage WHERE TenantId = @tenantId AND Status = N'Active';";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new EntityStorageLocation(reader.GetString(0), string.Empty,
            EntityStorageMode.DedicatedTable, false, reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(options.ControlDatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
