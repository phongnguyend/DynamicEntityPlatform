using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Application.Analytics;

public sealed class AnalyticsService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore entities,
    IFieldMetadataStore fields,
    IAnalyticsStore analytics,
    AnalyticsQueryValidator validator,
    TimeProvider timeProvider)
{
    public async Task<AnalyticsResult> ExecuteAsync(Guid tenantId, Guid entityId, AnalyticsQuery query,
        CancellationToken cancellationToken)
    {
        using var activity = AnalyticsTelemetry.Source.StartActivity("analytics.execute");
        activity?.SetTag("tenant.id", tenantId);
        activity?.SetTag("entity.id", entityId);
        activity?.SetTag("analytics.dimensions", query.Dimensions.Count);
        activity?.SetTag("analytics.measures", query.Measures.Count);
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var entity = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found.");
        var definitions = await fields.ListAsync(tenantId, entityId, storage, cancellationToken);
        validator.Validate(entityId, query, definitions);
        entity = entity with { Fields = definitions };
        var data = await analytics.ExecuteAsync(new TenantContext(tenantId), entity, query, cancellationToken);
        var byId = definitions.ToDictionary(field => field.Id);
        var columns = query.Dimensions.Select(dimension => new AnalyticsColumn(
                dimension.Alias, byId[dimension.FieldId].DisplayName,
                dimension.DateBucket == DateBucket.None ? byId[dimension.FieldId].DataType.ToString() : "Date",
                AnalyticsColumnRole.Dimension))
            .Concat(query.Measures.Select(measure => new AnalyticsColumn(
                measure.Alias,
                measure.FieldId is Guid id ? $"{measure.Aggregate} of {byId[id].DisplayName}" : "Count",
                measure.Aggregate is AggregateFunction.Count or AggregateFunction.CountDistinct ? "Integer" :
                    measure.FieldId is Guid fieldId ? byId[fieldId].DataType.ToString() : "Decimal",
                AnalyticsColumnRole.Measure))).ToArray();
        activity?.SetTag("analytics.rows", data.Rows.Count);
        activity?.SetTag("analytics.truncated", data.Truncated);
        return new(columns, data.Rows, timeProvider.GetUtcNow(), data.Truncated);
    }
}
