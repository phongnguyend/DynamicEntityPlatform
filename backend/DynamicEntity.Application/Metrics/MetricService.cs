using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Analytics;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Application.Metrics;

public sealed class MetricService(IControlPlaneStore controlPlane, IEntityMetadataStore entities, IFieldMetadataStore fields, IMetricStore metrics, AnalyticsService analytics, AnalyticsQueryValidator validator, TimeProvider timeProvider, IVirtualSqlService? virtualSql = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };
    public async Task<MetricDefinition> CreateAsync(Guid tenantId, Guid? entityId, string name, string? description, AggregateFunction aggregate, Guid? fieldId, Domain.Queries.FilterGroup? filter, string? formatJson, Guid? userId, CancellationToken token, VirtualSqlDefinition? sql = null)
    {
        RequireSqlForGlobal(entityId, sql);
        ValidateName(name);
        var s = await RequireEntityAsync(tenantId, entityId, token);
        ValidateFormat(formatJson);
        var q = Query(aggregate, fieldId, filter);
        if (sql?.Enabled == true)
        {
            await virtualSql!.PreviewAsync(tenantId, entityId, sql, null, token);
        }
        else
        {
            ValidateQuery(entityId!.Value, q, await fields.ListAsync(tenantId, entityId.Value, s, token));
        }
        var now = timeProvider.GetUtcNow();
        var m = new MetricDefinition(Guid.NewGuid(), entityId, name.Trim(), description?.Trim(), aggregate, fieldId, filter is null ? null : JsonSerializer.Serialize(filter, JsonOptions), formatJson, userId, now, now, sql);
        return await metrics.CreateAsync(tenantId, m, s, token);
    }
    public async Task<IReadOnlyList<MetricDefinition>> ListAsync(Guid tenantId, Guid? entityId, CancellationToken token)
    {
        var s = await RequireEntityAsync(tenantId, entityId, token);
        return await metrics.ListAsync(tenantId, entityId, s, token);
    }
    public async Task<MetricDefinition> GetAsync(Guid tenantId, Guid? entityId, Guid id, CancellationToken token)
    {
        var s = await RequireEntityAsync(tenantId, entityId, token);
        return await metrics.GetAsync(tenantId, entityId, id, s, token) ?? throw new NotFoundException($"Metric '{id}' was not found.");
    }
    public async Task<MetricDefinition> UpdateAsync(Guid tenantId, Guid? entityId, Guid id, string name, string? description, AggregateFunction aggregate, Guid? fieldId, Domain.Queries.FilterGroup? filter, string? formatJson, CancellationToken token, VirtualSqlDefinition? sql = null)
    {
        RequireSqlForGlobal(entityId, sql);
        ValidateName(name);
        ValidateFormat(formatJson);
        var s = await RequireEntityAsync(tenantId, entityId, token);
        if (sql?.Enabled == true)
        {
            await virtualSql!.PreviewAsync(tenantId, entityId, sql, null, token);
        }
        else
        {
            ValidateQuery(entityId!.Value, Query(aggregate, fieldId, filter), await fields.ListAsync(tenantId, entityId.Value, s, token));
        }
        var current = await metrics.GetAsync(tenantId, entityId, id, s, token) ?? throw new NotFoundException($"Metric '{id}' was not found.");
        var updated = current with
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            Aggregate = aggregate,
            FieldId = fieldId,
            FilterJson = filter is null ? null : JsonSerializer.Serialize(filter, JsonOptions),
            FormatJson = formatJson,
            Sql = sql,
            UpdatedAt = timeProvider.GetUtcNow()
        };
        return await metrics.UpdateAsync(tenantId, updated, s, token) ?? throw new NotFoundException($"Metric '{id}' was not found.");
    }
    public async Task DeleteAsync(Guid tenantId, Guid? entityId, Guid id, CancellationToken token)
    {
        var s = await RequireEntityAsync(tenantId, entityId, token);
        if (!await metrics.DeleteAsync(tenantId, entityId, id, s, token))
        {
            throw new ConflictException($"Metric '{id}' was not found or is referenced by an alert.");
        }
    }
    public async Task<(object? Value, DateTimeOffset EvaluatedAt)> EvaluateAsync(Guid tenantId, Guid? entityId, Guid id,
        CancellationToken token, IReadOnlyDictionary<string, JsonElement>? parameters = null)
    {
        using var activity = AnalyticsTelemetry.Source.StartActivity("metric.evaluate");
        activity?.SetTag("tenant.id", tenantId);
        activity?.SetTag("entity.id", entityId);
        activity?.SetTag("metric.id", id);
        var metric = await GetAsync(tenantId, entityId, id, token);
        var result = await PreviewAsync(tenantId, entityId, metric.Aggregate, metric.FieldId, DeserializeFilter(metric.FilterJson), token, parameters, metric.Sql);
        return (result.Rows.SingleOrDefault()?.GetValueOrDefault("value"), result.GeneratedAt);
    }
    public async Task<(object? Value, DateTimeOffset EvaluatedAt)> EvaluateForAlertAsync(Guid tenantId, Guid? alertEntityId, Guid metricId,
        CancellationToken token, IReadOnlyDictionary<string, JsonElement>? parameters = null)
    {
        var storage = await RequireEntityAsync(tenantId, alertEntityId, token);
        var metric = await metrics.GetByIdAsync(tenantId, metricId, storage, token)
            ?? throw new NotFoundException($"Metric '{metricId}' was not found.");
        if (alertEntityId is not null && metric.EntityId != alertEntityId)
        {
            throw new NotFoundException($"Metric '{metricId}' was not found.");
        }
        return await EvaluateAsync(tenantId, metric.EntityId, metricId, token, parameters);
    }
    public async Task<AnalyticsResult> PreviewAsync(Guid tenantId, Guid? entityId, AggregateFunction aggregate, Guid? fieldId,
        Domain.Queries.FilterGroup? filter, CancellationToken token, IReadOnlyDictionary<string, JsonElement>? parameters = null, VirtualSqlDefinition? sql = null)
    {
        RequireSqlForGlobal(entityId, sql);
        if (sql?.Enabled == true)
        {
            return await virtualSql!.ExecuteAsync(tenantId, entityId, sql, parameters, 2, true, token);
        }
        var storage = await RequireEntityAsync(tenantId, entityId, token);
        var definitions = await fields.ListAsync(tenantId, entityId!.Value, storage, token);
        var resolved = MetricFilterParameters.Resolve(filter, definitions, parameters, timeProvider.GetUtcNow());
        return await analytics.ExecuteAsync(tenantId, entityId!.Value, Query(aggregate, fieldId, resolved), token);
    }
    public async Task<(Domain.Entities.EntityDefinition Entity, AnalyticsQuery Query)> PrepareQueryAsync(
        Guid tenantId, Guid entityId, Guid id, CancellationToken token,
        IReadOnlyDictionary<string, JsonElement>? parameters = null)
    {
        var metric = await GetAsync(tenantId, entityId, id, token);
        return await PrepareQueryAsync(tenantId, entityId, metric.Aggregate, metric.FieldId,
            DeserializeFilter(metric.FilterJson), token, parameters);
    }
    public async Task<(Domain.Entities.EntityDefinition Entity, AnalyticsQuery Query)> PrepareQueryAsync(
        Guid tenantId, Guid entityId, AggregateFunction aggregate, Guid? fieldId,
        Domain.Queries.FilterGroup? filter, CancellationToken token,
        IReadOnlyDictionary<string, JsonElement>? parameters = null)
    {
        var storage = await RequireEntityAsync(tenantId, entityId, token);
        var entity = await entities.GetAsync(tenantId, entityId, storage, token)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found.");
        var definitions = await fields.ListAsync(tenantId, entityId, storage, token);
        var resolved = MetricFilterParameters.Resolve(filter, definitions, parameters, timeProvider.GetUtcNow(), allowUnbound: true);
        var query = Query(aggregate, fieldId, resolved);
        validator.Validate(entityId, query, definitions);
        return (entity with
        {
            Fields = definitions
        }, query);
    }
    public static Domain.Queries.FilterGroup? DeserializeFilter(string? json) => json is null ? null : JsonSerializer.Deserialize<Domain.Queries.FilterGroup>(json, JsonOptions);
    private void ValidateQuery(Guid entityId, AnalyticsQuery query, IReadOnlyList<Domain.Entities.FieldDefinition> definitions)
    {
        validator.Validate(entityId, query, definitions);
        MetricFilterParameters.Resolve(query.Filter, definitions, null, timeProvider.GetUtcNow(), validateOnly: true);
    }
    private static AnalyticsQuery Query(AggregateFunction aggregate, Guid? fieldId, Domain.Queries.FilterGroup? filter) => new(filter, [], [new AnalyticsMeasure(aggregate, fieldId, "value")], [], 1);
    private async Task<Domain.Storage.EntityStorageLocation> RequireEntityAsync(Guid tenantId, Guid? entityId, CancellationToken token)
    {
        var s = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, token);
        if (entityId is not null)
        {
            _ = await entities.GetAsync(tenantId, entityId.Value, s, token) ?? throw new NotFoundException($"Entity '{entityId}' was not found.");
        }
        return s;
    }
    private static void RequireSqlForGlobal(Guid? entityId, VirtualSqlDefinition? sql)
    {
        if (entityId is null && sql?.Enabled != true)
        {
            throw new ValidationException("Global definitions require SQL mode.");
        }
    }
    private static void ValidateName(string n)
    {
        if (n.Trim().Length is < 1 or > 200)
        {
            throw new ValidationException("Metric name must contain between 1 and 200 characters.");
        }
    }
    private static void ValidateFormat(string? json)
    {
        if (json is null)
        {
            return;
        }

        try
        {
            using var d = JsonDocument.Parse(json);
            if (d.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ValidationException("Metric format must be a JSON object.");
            }
        }
        catch (JsonException) { throw new ValidationException("Metric format must be valid JSON."); }
    }
}
