using DynamicEntity.Application.Analytics;
using DynamicEntity.Application.Common;
using DynamicEntity.Application.Metrics;
using DynamicEntity.Application.Reports;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.UnitTests;

public sealed class GlobalAnalyticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GlobalMetricsRejectMissingOrDisabledSqlBeforeAccessingStorage(bool includeDisabledSql)
    {
        var service = new MetricService(null!, null!, null!, null!, null!, new AnalyticsQueryValidator(), TimeProvider.System);
        var tenant = Guid.NewGuid();
        var sql = includeDisabledSql ? new VirtualSqlDefinition(false, "SELECT 1 AS value", []) : null;
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(tenant, null, "Global", null,
            AggregateFunction.Count, null, null, null, null, CancellationToken.None, sql));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(tenant, null, Guid.NewGuid(), "Global", null,
            AggregateFunction.Count, null, null, null, CancellationToken.None, sql));
        await Assert.ThrowsAsync<ValidationException>(() => service.PreviewAsync(tenant, null,
            AggregateFunction.Count, null, null, CancellationToken.None, sql: sql));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GlobalReportsRejectMissingOrDisabledSqlBeforeAccessingStorage(bool includeDisabledSql)
    {
        var service = new ReportService(null!, null!, null!, null!, null!, new AnalyticsQueryValidator(), TimeProvider.System);
        var tenant = Guid.NewGuid();
        var sql = includeDisabledSql ? new VirtualSqlDefinition(false, "SELECT 1 AS value", []) : null;
        var query = new AnalyticsQuery(null, [], [new(AggregateFunction.Count, null, "count")], [], 100, sql);
        var specification = new ReportSpecification(query, VisualizationType.Table);
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(tenant, null, "Global", null,
            specification, null, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(tenant, null, Guid.NewGuid(), "Global", null,
            specification, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.PreviewAsync(tenant, null, query, CancellationToken.None));
    }
}
