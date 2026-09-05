using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.SqlServer.Queries;

namespace DynamicEntity.SqlServer.Analytics;

public sealed record SqlAnalyticsQueryPlan(
    string Sql,
    IReadOnlyList<SqlQueryParameter> Parameters,
    IReadOnlyList<(string Alias, FieldDataType? DataType, AnalyticsColumnRole Role)> Columns);

public static class SqlAnalyticsQueryBuilder
{
    public static SqlAnalyticsQueryPlan Build(EntityDefinition entity, EntityStorageLocation storage, AnalyticsQuery query)
    {
        var fields = entity.Fields.ToDictionary(field => field.Id);
        var parameters = new List<SqlQueryParameter> { new("@take", query.Limit + 1) };
        var dimensions = query.Dimensions.Select(dimension =>
        {
            var field = fields[dimension.FieldId];
            return (dimension, field, expression: Bucket(SqlFieldExpression.ForValue(field), dimension.DateBucket));
        }).ToArray();
        var measures = query.Measures.Select(measure =>
        {
            var field = measure.FieldId is Guid id ? fields[id] : null;
            return (measure, field, expression: Measure(measure, field));
        }).ToArray();

        var select = dimensions.Select(item => $"{item.expression} AS {QuoteAlias(item.dimension.Alias)}")
            .Concat(measures.Select(item => $"{item.expression} AS {QuoteAlias(item.measure.Alias)}"));
        var where = new List<string>();
        if (storage.RequiresEntityPredicate)
        {
            where.Add("EntityId = @entityId");
            parameters.Add(new("@entityId", entity.Id));
        }
        if (query.Filter is { Conditions.Count: > 0 })
            where.Add(SqlFilterExpression.Build(query.Filter, fields, parameters));

        var whereSql = where.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", where)}";
        var groupSql = dimensions.Length == 0 ? string.Empty :
            $" GROUP BY {string.Join(", ", dimensions.Select(item => item.expression))}";
        var orderSql = query.Sort.Count == 0 ? string.Empty :
            $" ORDER BY {string.Join(", ", query.Sort.Select(sort => $"{QuoteAlias(sort.Alias)} {(sort.Direction == Domain.Queries.SortDirection.Asc ? "ASC" : "DESC")}"))}";
        var table = PhysicalName.QuoteSqlIdentifier(storage.TableName);
        var sql = $"SELECT TOP (@take) {string.Join(", ", select)} FROM dbo.{table}{whereSql}{groupSql}{orderSql};";
        var columns = dimensions.Select(item => (item.dimension.Alias, (FieldDataType?)item.field.DataType, AnalyticsColumnRole.Dimension))
            .Concat(measures.Select(item => (item.measure.Alias, item.field?.DataType, AnalyticsColumnRole.Measure))).ToArray();
        return new(sql, parameters, columns);
    }

    private static string Measure(AnalyticsMeasure measure, FieldDefinition? field)
    {
        var expression = field is null ? null : SqlFieldExpression.ForValue(field);
        return measure.Aggregate switch
        {
            AggregateFunction.Count => expression is null ? "COUNT_BIG(*)" : $"COUNT_BIG({expression})",
            AggregateFunction.CountDistinct => $"COUNT_BIG(DISTINCT {expression})",
            AggregateFunction.Sum => $"SUM({expression})",
            AggregateFunction.Average => $"AVG({expression})",
            AggregateFunction.Min => $"MIN({expression})",
            AggregateFunction.Max => $"MAX({expression})",
            _ => throw new InvalidOperationException($"Unknown aggregate '{measure.Aggregate}'.")
        };
    }

    private static string Bucket(string expression, DateBucket bucket) => bucket switch
    {
        DateBucket.None => expression,
        DateBucket.Day => $"CONVERT(DATE, {expression})",
        DateBucket.Week => $"DATEADD(DAY, -((DATEDIFF(DAY, CONVERT(DATE, '19000101', 112), CONVERT(DATE, {expression})) % 7 + 7) % 7), CONVERT(DATE, {expression}))",
        DateBucket.Month => $"DATEFROMPARTS(YEAR({expression}), MONTH({expression}), 1)",
        DateBucket.Quarter => $"DATEFROMPARTS(YEAR({expression}), ((DATEPART(QUARTER, {expression}) - 1) * 3) + 1, 1)",
        DateBucket.Year => $"DATEFROMPARTS(YEAR({expression}), 1, 1)",
        _ => throw new InvalidOperationException($"Unknown date bucket '{bucket}'.")
    };

    private static string QuoteAlias(string alias) => $"[{alias.Replace("]", "]]", StringComparison.Ordinal)}]";
}
