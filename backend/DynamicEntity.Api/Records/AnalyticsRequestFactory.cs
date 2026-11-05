using DynamicEntity.Contracts.Analytics;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Entities;

namespace DynamicEntity.Api.Records;

public static class AnalyticsRequestFactory
{
    public static AnalyticsQuery Create(AnalyticsPreviewRequest request, IReadOnlyList<FieldDefinition> fields, bool allowRuntimeParameters = false) => new(
        RecordQueryFactory.CreateFilter(request.Filter, fields, allowMetricParameters: allowRuntimeParameters),
        request.Dimensions ?? [], request.Measures ?? [], request.Sort ?? [], request.Limit, request.Sql);
}
