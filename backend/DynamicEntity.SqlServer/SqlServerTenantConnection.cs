using DynamicEntity.Domain.Storage;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

internal static class SqlServerTenantConnection
{
    public static SqlConnection Create(EntityStorageLocation storage)
    {
        if (string.IsNullOrWhiteSpace(storage.ConnectionString))
            throw new InvalidOperationException("No connection string is configured for this tenant.");
        var builder = new SqlConnectionStringBuilder(storage.ConnectionString);
        builder.InitialCatalog = storage.DatabaseName;
        return new SqlConnection(builder.ConnectionString);
    }
}
