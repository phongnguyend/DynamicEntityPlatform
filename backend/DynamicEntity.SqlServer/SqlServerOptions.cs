namespace DynamicEntity.SqlServer;

public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    public required string ControlDatabaseConnectionString { get; init; }
    public string ConnectionKey { get; init; } = "DefaultSqlServer";
    public int AnalyticsCommandTimeoutSeconds { get; init; } = 30;
}
