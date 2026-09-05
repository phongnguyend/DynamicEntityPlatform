namespace DynamicEntity.SqlServer;

public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    public required string ControlDatabaseConnectionString { get; init; }
    public required string TenantServerConnectionString { get; init; }
    public string ConnectionKey { get; init; } = "DefaultSqlServer";
}
