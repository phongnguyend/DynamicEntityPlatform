using System.Text.Json;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Queries;
using DynamicEntity.Domain.Storage;
using DynamicEntity.SqlServer.Analytics;

namespace DynamicEntity.UnitTests;

public sealed class SqlAnalyticsQueryBuilderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VirtualNamesUseLogicalFieldsAcrossGroupingMeasuresAndFilters(bool indexed)
    {
        var (entity, field, storage) = Fixture(FieldDataType.Decimal);
        field = field with
        {
            Name = "amount]total",
            IndexColumnName = indexed ? "IX_Amount" : null
        };
        entity = entity with
        {
            Name = "orders]archive",
            Fields = [field]
        };
        storage = storage with
        {
            RequiresEntityPredicate = true
        };
        var filter = new FilterGroup(FilterLogic.And, [new FilterCondition(field.Id, FilterOperator.GreaterThan, JsonSerializer.SerializeToElement(42m))]);
        var query = new AnalyticsQuery(filter, [new(field.Id, DateBucket.None, "amount")], [new(AggregateFunction.Sum, field.Id, "total")], [new("total", SortDirection.Desc)], 10);
        var logical = SqlAnalyticsQueryBuilder.Build(entity, storage, query, virtualNames: true);
        var physical = SqlAnalyticsQueryBuilder.Build(entity, storage, query);
        Assert.Contains("FROM [orders]]archive]", logical.Sql);
        Assert.Contains("SUM([amount]]total])", logical.Sql);
        Assert.Contains("GROUP BY [amount]]total]", logical.Sql);
        Assert.Contains("ORDER BY [total] DESC", logical.Sql);
        Assert.DoesNotContain("JSON_VALUE", logical.Sql);
        Assert.DoesNotContain("IX_Amount", logical.Sql);
        Assert.DoesNotContain(logical.Parameters, p => p.Name == "@entityId");
        Assert.Contains(physical.Parameters, p => p.Name == "@entityId");
        Assert.Contains("dbo.[Records_Orders]", physical.Sql);
        Assert.Contains(indexed ? "[IX_Amount]" : field.StorageKey, physical.Sql);
        Assert.Contains(logical.Parameters, p => Equals(p.Value, 42m));
    }

    [Theory]
    [InlineData(FieldDataType.Integer, FilterOperator.Equal)]
    [InlineData(FieldDataType.Integer, FilterOperator.Between)]
    [InlineData(FieldDataType.Integer, FilterOperator.In)]
    [InlineData(FieldDataType.Text, FilterOperator.Contains)]
    [InlineData(FieldDataType.Text, FilterOperator.StartsWith)]
    [InlineData(FieldDataType.Text, FilterOperator.EndsWith)]
    [InlineData(FieldDataType.MultiChoice, FilterOperator.Contains)]
    public void SqlPreviewAllowsMissingInputsWithoutChangingQueryShape(FieldDataType type, FilterOperator operation)
    {
        var (entity, field, storage) = Fixture(type);
        var reference = JsonSerializer.SerializeToElement(new
        {
            parameter = "input"
        });
        var value = operation is FilterOperator.Between or FilterOperator.In
            ? JsonSerializer.SerializeToElement(new[] { reference, reference }) : reference;
        var filter = new FilterGroup(FilterLogic.And, [new FilterCondition(field.Id, operation, value)]);
        var now = DateTimeOffset.UtcNow;
        var unresolved = DynamicEntity.Application.Metrics.MetricFilterParameters.Resolve(filter, entity.Fields, null, now, allowUnbound: true);
        var resolved = DynamicEntity.Application.Metrics.MetricFilterParameters.Resolve(filter, entity.Fields,
            new Dictionary<string, JsonElement> { ["input"] = JsonSerializer.SerializeToElement("42") }, now);
        var preview = SqlAnalyticsQueryBuilder.Build(entity, storage,
            new(unresolved, [], [new(AggregateFunction.Count, null, "value")], [], 1), allowUnbound: true);
        var execution = SqlAnalyticsQueryBuilder.Build(entity, storage,
            new(resolved, [], [new(AggregateFunction.Count, null, "value")], [], 1));
        Assert.Equal(execution.Sql, preview.Sql);
        Assert.Contains(preview.Parameters, parameter => JsonSerializer.Serialize(parameter.Value).Contains("\"unbound\":true"));
        Assert.Throws<DynamicEntity.Application.Common.ValidationException>(() =>
            DynamicEntity.Application.Metrics.MetricFilterParameters.Resolve(filter, entity.Fields, null, now));
    }

    [Fact]
    public void Build_MetricRuntimeInputRemainsABoundParameter()
    {
        var (entity, field, storage) = Fixture(FieldDataType.Text);
        var input = "O'Brien'; DROP TABLE records; --";
        var filter = new FilterGroup(FilterLogic.And, [new FilterCondition(field.Id, FilterOperator.Equal,
            JsonSerializer.SerializeToElement(new { parameter = "name" }))]);
        var resolved = DynamicEntity.Application.Metrics.MetricFilterParameters.Resolve(filter, entity.Fields,
            new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement(input) }, DateTimeOffset.UtcNow);
        var plan = SqlAnalyticsQueryBuilder.Build(entity, storage,
            new(resolved, [], [new(AggregateFunction.Count, null, "value")], [], 1));
        Assert.DoesNotContain(input, plan.Sql);
        Assert.Contains(plan.Parameters, parameter => Equals(parameter.Value, input));
        Assert.Contains("COUNT_BIG(*)", plan.Sql);
    }

    [Theory]
    [InlineData(DateBucket.Day, "CONVERT(DATE")]
    [InlineData(DateBucket.Week, "DATEDIFF(DAY")]
    [InlineData(DateBucket.Month, "DATEFROMPARTS")]
    [InlineData(DateBucket.Quarter, "DATEPART(QUARTER")]
    [InlineData(DateBucket.Year, "YEAR(")]
    public void Build_GeneratesDateBuckets(DateBucket bucket, string expected)
    {
        var (entity, field, storage) = Fixture(FieldDataType.DateTime);
        var query = new AnalyticsQuery(null, [new(field.Id, bucket, "period")], [new(AggregateFunction.Count, null, "count")], [], 25);
        var plan = SqlAnalyticsQueryBuilder.Build(entity, storage, query);
        Assert.Contains(expected, plan.Sql);
        Assert.Contains("GROUP BY", plan.Sql);
        Assert.Contains(plan.Parameters, p => p.Name == "@take" && (int)p.Value == 26);
    }

    [Theory]
    [InlineData(AggregateFunction.Count, "COUNT_BIG(*)")]
    [InlineData(AggregateFunction.CountDistinct, "COUNT_BIG(DISTINCT")]
    [InlineData(AggregateFunction.Sum, "SUM(")]
    [InlineData(AggregateFunction.Average, "AVG(")]
    [InlineData(AggregateFunction.Min, "MIN(")]
    [InlineData(AggregateFunction.Max, "MAX(")]
    public void Build_GeneratesAggregates(AggregateFunction aggregate, string expected)
    {
        var (entity, field, storage) = Fixture(FieldDataType.Decimal);
        var query = new AnalyticsQuery(null, [], [new(aggregate, aggregate == AggregateFunction.Count ? null : field.Id, "value")], [], 1);
        Assert.Contains(expected, SqlAnalyticsQueryBuilder.Build(entity, storage, query).Sql);
    }

    [Fact]
    public void Build_ParameterizesFilterValues()
    {
        var (entity, field, storage) = Fixture(FieldDataType.Decimal);
        using var json = JsonDocument.Parse("42.5");
        var filter = new FilterGroup(FilterLogic.And, [new FilterCondition(field.Id, FilterOperator.GreaterThan, json.RootElement.Clone())]);
        var plan = SqlAnalyticsQueryBuilder.Build(entity, storage, new(filter, [], [new(AggregateFunction.Count, null, "count")], [], 10));
        Assert.DoesNotContain("42.5", plan.Sql);
        Assert.Contains(plan.Parameters, p => p.Value is decimal d && d == 42.5m);
    }

    [Fact]
    public void Build_ReusesIndexedComputedColumn()
    {
        var (entity, field, storage) = Fixture(FieldDataType.Decimal);
        field = field with
        {
            IndexColumnName = "IX_Amount"
        };
        entity = entity with
        {
            Fields = [field]
        };
        var plan = SqlAnalyticsQueryBuilder.Build(entity, storage, new AnalyticsQuery(null, [], [new(AggregateFunction.Sum, field.Id, "total")], [], 10));
        Assert.Contains("[IX_Amount]", plan.Sql);
        Assert.DoesNotContain("JSON_VALUE", plan.Sql);
    }

    private static (EntityDefinition, FieldDefinition, EntityStorageLocation) Fixture(FieldDataType type)
    {
        var now = DateTimeOffset.UtcNow;
        var entityId = Guid.NewGuid();
        var field = new FieldDefinition(Guid.NewGuid(), entityId, "amount", "Amount", type, false, false, true, true, false, false, null, null, 0, true, now, now);
        var entity = new EntityDefinition(entityId, Guid.NewGuid(), "orders", "Orders", null, 1, EntityStatus.Active, now, now) { Fields = [field] };
        return (entity, field, new("tenant", "Records_Orders", EntityStorageMode.DedicatedTable, false));
    }
}
