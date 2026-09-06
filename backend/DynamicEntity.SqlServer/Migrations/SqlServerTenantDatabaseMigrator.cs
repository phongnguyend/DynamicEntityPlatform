using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Storage;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer.Migrations;

public sealed class SqlServerTenantDatabaseMigrator(SqlServerOptions options) : ITenantDatabaseMigrator
{
    private static readonly (int Version, string Name, string Sql)[] Migrations =
    [
        (1, "Initial schema", SqlServerSchema.TenantDatabaseV1),
        (2, "Analytics definitions", SqlServerSchema.TenantDatabaseV2),
        (3, "Webhook subscriptions", SqlServerSchema.TenantDatabaseV3),
        (4, "Alert actions", SqlServerSchema.TenantDatabaseV4),
        (5, "Multiple alert webhook URLs", SqlServerSchema.TenantDatabaseV5)
    ];

    public async Task MigrateAsync(EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        await using var connection = SqlServerTenantConnection.Create(options, storage);
        await connection.OpenAsync(cancellationToken);

        const string ensureTrackingSql = """
            IF OBJECT_ID(N'dbo.SchemaMigrations', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SchemaMigrations
                (
                    Version INT NOT NULL CONSTRAINT PK_SchemaMigrations PRIMARY KEY,
                    Name NVARCHAR(200) NOT NULL,
                    AppliedAt DATETIMEOFFSET(7) NOT NULL
                );
            END;
            """;
        await using (var ensure = new SqlCommand(ensureTrackingSql, connection))
        {
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var migration in Migrations)
        {
            await ApplyAsync(connection, migration, cancellationToken);
        }
    }

    private static async Task ApplyAsync(
        SqlConnection connection,
        (int Version, string Name, string Sql) migration,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string lockSql = """
                DECLARE @result INT;
                EXEC @result = sys.sp_getapplock
                    @Resource = N'DynamicEntityTenantSchemaMigration',
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Transaction',
                    @LockTimeout = 30000;
                IF @result < 0 THROW 51000, 'Could not acquire the tenant schema migration lock.', 1;
                """;
            await using (var acquireLock = new SqlCommand(lockSql, connection, transaction))
            {
                await acquireLock.ExecuteNonQueryAsync(cancellationToken);
            }

            const string existsSql = "SELECT COUNT(1) FROM dbo.SchemaMigrations WHERE Version = @version;";
            await using var exists = new SqlCommand(existsSql, connection, transaction);
            exists.Parameters.AddWithValue("@version", migration.Version);
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken)) != 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            await using (var apply = new SqlCommand(migration.Sql, connection, transaction))
            {
                await apply.ExecuteNonQueryAsync(cancellationToken);
            }

            const string recordSql = """
                INSERT INTO dbo.SchemaMigrations (Version, Name, AppliedAt)
                VALUES (@version, @name, SYSUTCDATETIME());
                """;
            await using (var record = new SqlCommand(recordSql, connection, transaction))
            {
                record.Parameters.AddWithValue("@version", migration.Version);
                record.Parameters.AddWithValue("@name", migration.Name);
                await record.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
