using System.Text.Json;
using DynamicEntity.Contracts.Records;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Contracts.Metrics;

public sealed record SaveMetricRequest(
    string Name, string? Description, AggregateFunction Aggregate, Guid? FieldId,
    FilterNodeRequest? Filter = null, JsonElement? Format = null,
    IReadOnlyDictionary<string, JsonElement>? Parameters = null, VirtualSqlDefinition? Sql = null);

public sealed record EvaluateMetricRequest(IReadOnlyDictionary<string, JsonElement>? Parameters = null);

public sealed record MetricResponse(
    Guid Id, Guid? EntityId, string Name, string? Description, AggregateFunction Aggregate,
    Guid? FieldId, FilterNodeRequest? Filter, JsonElement? Format, Guid? CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, VirtualSqlDefinition? Sql = null);

public sealed record MetricEvaluationResponse(object? Value, DateTimeOffset EvaluatedAt);
