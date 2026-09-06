using DynamicEntity.Domain.Storage;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

internal static class SqlServerTenantConnection
{
    public static SqlConnection Create(SqlServerOptions options, EntityStorageLocation storage)
    {
        if (!string.Equals(storage.ConnectionKey, options.ConnectionKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unknown tenant connection key '{storage.ConnectionKey}'.");

        if (string.IsNullOrWhiteSpace(storage.ConnectionString))
            throw new InvalidOperationException("No connection string is configured for this tenant.");
        var builder = new SqlConnectionStringBuilder(storage.ConnectionString);
        builder.InitialCatalog = storage.DatabaseName;
        return new SqlConnection(builder.ConnectionString);
    }
}
