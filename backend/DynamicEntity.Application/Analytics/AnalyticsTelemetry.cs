using System.Diagnostics;

namespace DynamicEntity.Application.Analytics;

public static class AnalyticsTelemetry
{
    public const string SourceName = "DynamicEntity.Analytics";
    public static readonly ActivitySource Source = new(SourceName);
}
