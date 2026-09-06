namespace DynamicEntity.SqlServer;

public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    public required string ControlDatabaseConnectionString { get; init; }
    public int AnalyticsCommandTimeoutSeconds { get; init; } = 30;
}
