using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Storage;
using DynamicEntity.SqlServer.Migrations;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerTenantDatabaseProvisioner(SqlServerOptions options) : ITenantDatabaseProvisioner
{
    public async Task<EntityStorageLocation> ProvisionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var databaseName = PhysicalName.ForTenantDatabase(tenantId);
        var builder = new SqlConnectionStringBuilder(options.TenantServerConnectionString);

        await using (var provisioningConnection = new SqlConnection(builder.ConnectionString))
        {
            await provisioningConnection.OpenAsync(cancellationToken);
            const string createSql = """
                IF DB_ID(@databaseName) IS NULL
                BEGIN
                    DECLARE @createDatabaseSql NVARCHAR(MAX) = N'CREATE DATABASE ' + QUOTENAME(@databaseName) + N';';
                    EXEC sys.sp_executesql @createDatabaseSql;
                END;
                """;
            await using var command = new SqlCommand(createSql, provisioningConnection);
            command.Parameters.AddWithValue("@databaseName", databaseName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var location = new EntityStorageLocation(
            options.ConnectionKey,
            databaseName,
            string.Empty,
            EntityStorageMode.DedicatedTable,
            false);
        await new SqlServerTenantDatabaseMigrator(options).MigrateAsync(location, cancellationToken);
        return location;
    }
}
