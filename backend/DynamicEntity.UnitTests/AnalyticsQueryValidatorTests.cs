using System.Text.Json;
using DynamicEntity.Application.Analytics;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Queries;

namespace DynamicEntity.UnitTests;

public sealed class AnalyticsQueryValidatorTests
{
    private readonly Guid _entityId = Guid.NewGuid();

    [Theory]
    [InlineData(AggregateFunction.Sum, FieldDataType.Integer)]
    [InlineData(AggregateFunction.Sum, FieldDataType.Decimal)]
    [InlineData(AggregateFunction.Average, FieldDataType.Integer)]
    [InlineData(AggregateFunction.Average, FieldDataType.Decimal)]
    [InlineData(AggregateFunction.Min, FieldDataType.Date)]
    [InlineData(AggregateFunction.Max, FieldDataType.Text)]
    [InlineData(AggregateFunction.CountDistinct, FieldDataType.Choice)]
    public void Validate_AcceptsCompatibleAggregate(AggregateFunction aggregate, FieldDataType dataType)
    {
        var field = Field(dataType);
        var query = Query(measures: [new AnalyticsMeasure(aggregate, field.Id, "value")]);

        new AnalyticsQueryValidator().Validate(_entityId, query, [field]);
    }

    [Theory]
    [InlineData(AggregateFunction.Sum, FieldDataType.Text)]
    [InlineData(AggregateFunction.Average, FieldDataType.Date)]
    [InlineData(AggregateFunction.CountDistinct, FieldDataType.LongText)]
    [InlineData(AggregateFunction.CountDistinct, FieldDataType.MultiChoice)]
    public void Validate_RejectsIncompatibleAggregate(AggregateFunction aggregate, FieldDataType dataType)
    {
        var field = Field(dataType);
        var query = Query(measures: [new AnalyticsMeasure(aggregate, field.Id, "value")]);

        Assert.Throws<ValidationException>(() =>
            new AnalyticsQueryValidator().Validate(_entityId, query, [field]));
    }

    [Fact]
    public void Validate_AllowsCountWithoutField()
    {
        var query = Query(measures: [new AnalyticsMeasure(AggregateFunction.Count, null, "record_count")]);

        new AnalyticsQueryValidator().Validate(_entityId, query, []);
    }

    [Fact]
    public void Validate_RejectsDateBucketOnNonDateField()
    {
        var field = Field(FieldDataType.Text);
        var query = Query(
            dimensions: [new AnalyticsDimension(field.Id, DateBucket.Month, "month")],
            measures: [new AnalyticsMeasure(AggregateFunction.Count, null, "count")]);

        Assert.Throws<ValidationException>(() =>
            new AnalyticsQueryValidator().Validate(_entityId, query, [field]));
    }

    [Fact]
    public void Validate_RejectsDeactivatedAndNonFilterableDependencies()
    {
        var inactive = Field(FieldDataType.Integer) with { IsActive = false };
        var notFilterable = Field(FieldDataType.Text) with { IsFilterable = false };
        using var value = JsonDocument.Parse("\"Ada\"");
        var filter = new FilterGroup(FilterLogic.And,
            [new FilterCondition(notFilterable.Id, FilterOperator.Equal, value.RootElement.Clone())]);

        var inactiveQuery = Query(measures: [new AnalyticsMeasure(AggregateFunction.Sum, inactive.Id, "total")]);
        var filterQuery = Query(filter: filter);

        Assert.Contains("deactivated", Assert.Throws<ValidationException>(() =>
            new AnalyticsQueryValidator().Validate(_entityId, inactiveQuery, [inactive])).Message);
        Assert.Contains("not filterable", Assert.Throws<ValidationException>(() =>
            new AnalyticsQueryValidator().Validate(_entityId, filterQuery, [notFilterable])).Message);
    }

    [Fact]
    public void Validate_EnforcesAliasesShapeUniquenessAndSortReferences()
    {
        var field = Field(FieldDataType.Date);
        var duplicate = Query(
            dimensions: [new AnalyticsDimension(field.Id, DateBucket.Day, "result")],
            measures: [new AnalyticsMeasure(AggregateFunction.Count, null, "RESULT")]);
        var unknownSort = Query(sort: [new AnalyticsSort("missing", SortDirection.Asc)]);

        Assert.Throws<ValidationException>(() =>
            new AnalyticsQueryValidator().Validate(_entityId, duplicate, [field]));
        Assert.Throws<ValidationException>(() =>
            new AnalyticsQueryValidator().Validate(_entityId, unknownSort, [field]));
    }

    [Fact]
    public void Validate_RejectsUndefinedEnums()
    {
        var query = Query(measures: [new AnalyticsMeasure((AggregateFunction)999, null, "value")]);

        Assert.Throws<ValidationException>(() =>
            new AnalyticsQueryValidator().Validate(_entityId, query, []));
    }

    private AnalyticsQuery Query(
        FilterGroup? filter = null,
        IReadOnlyList<AnalyticsDimension>? dimensions = null,
        IReadOnlyList<AnalyticsMeasure>? measures = null,
        IReadOnlyList<AnalyticsSort>? sort = null,
        int limit = 100) => new(filter, dimensions ?? [],
            measures ?? [new AnalyticsMeasure(AggregateFunction.Count, null, "count")], sort ?? [], limit);

    private FieldDefinition Field(FieldDataType dataType) => new(
        Guid.NewGuid(), _entityId, "field", "Field", dataType,
        false, false, true, true, false, false, null, null, 0, true,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
