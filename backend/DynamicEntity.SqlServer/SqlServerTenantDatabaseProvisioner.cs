using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Storage;
using DynamicEntity.SqlServer.Migrations;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerTenantDatabaseProvisioner : ITenantDatabaseProvisioner
{
    public async Task<EntityStorageLocation> ProvisionAsync(Guid tenantId, string connectionString, CancellationToken cancellationToken)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new ValidationException($"The tenant connection string is invalid: {exception.Message}");
        }
        var databaseName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ValidationException("The tenant connection string must specify Initial Catalog (Database).");
        if (builder.DataSource.Contains("\\\\", StringComparison.Ordinal) || databaseName.Contains('\\'))
            throw new ValidationException("The tenant connection string contains escaped backslashes. Enter it as plain text, for example Server=(localdb)\\MSSQLLocalDB;Database=Tenant_name.");

        var location = new EntityStorageLocation(
            databaseName,
            string.Empty,
            EntityStorageMode.DedicatedTable,
            false,
            builder.ConnectionString);
        await new SqlServerTenantDatabaseMigrator().MigrateAsync(location, cancellationToken);
        return location;
    }
}
