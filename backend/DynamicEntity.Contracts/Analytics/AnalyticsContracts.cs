using System.Text.Json;
using DynamicEntity.Contracts.Records;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Contracts.Analytics;

public sealed record AnalyticsPreviewRequest(
    FilterNodeRequest? Filter,
    IReadOnlyList<AnalyticsDimension>? Dimensions = null,
    IReadOnlyList<AnalyticsMeasure>? Measures = null,
    IReadOnlyList<AnalyticsSort>? Sort = null,
    int Limit = 100);

public sealed record AnalyticsColumnResponse(string Key, string Label, string DataType, AnalyticsColumnRole Role);

public sealed record AnalyticsResultResponse(
    IReadOnlyList<AnalyticsColumnResponse> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Rows,
    DateTimeOffset GeneratedAt,
    bool Truncated);
