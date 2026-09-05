using System.Globalization;
using System.Text.Json;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Queries;

namespace DynamicEntity.SqlServer.Queries;

public static class SqlFilterExpression
{
    public static string Build(FilterGroup group, IReadOnlyDictionary<Guid, FieldDefinition> fields,
        List<SqlQueryParameter> parameters)
    {
        var parts = group.Conditions.Select(node => node switch
        {
            FilterGroup nested => Build(nested, fields, parameters),
            FilterCondition condition => BuildCondition(condition, fields[condition.FieldId], parameters),
            _ => throw new InvalidOperationException("Unknown filter node.")
        }).ToArray();
        if (parts.Length == 0) return group.Logic == FilterLogic.And ? "1 = 1" : "1 = 0";
        return $"({string.Join(group.Logic == FilterLogic.And ? " AND " : " OR ", parts)})";
    }

    private static string BuildCondition(FilterCondition condition, FieldDefinition field,
        List<SqlQueryParameter> parameters)
    {
        var expression = SqlFieldExpression.ForValue(field);
        if (field.DataType == FieldDataType.MultiChoice && condition.Operator == FilterOperator.Contains)
        {
            var parameter = Add(parameters, Scalar(condition.Value, field.DataType));
            return $"EXISTS (SELECT 1 FROM OPENJSON(Data, '$.{field.StorageKey}') AS j WHERE j.[value] = {parameter})";
        }
        return condition.Operator switch
        {
            FilterOperator.IsNull => $"{expression} IS NULL",
            FilterOperator.IsNotNull => $"{expression} IS NOT NULL",
            FilterOperator.Equal => Binary("=", condition.Value),
            FilterOperator.NotEqual => Binary("<>", condition.Value),
            FilterOperator.GreaterThan => Binary(">", condition.Value),
            FilterOperator.GreaterThanOrEqual => Binary(">=", condition.Value),
            FilterOperator.LessThan => Binary("<", condition.Value),
            FilterOperator.LessThanOrEqual => Binary("<=", condition.Value),
            FilterOperator.Contains => Like($"%{EscapeLike(condition.Value.GetString()!)}%"),
            FilterOperator.StartsWith => Like($"{EscapeLike(condition.Value.GetString()!)}%"),
            FilterOperator.EndsWith => Like($"%{EscapeLike(condition.Value.GetString()!)}"),
            FilterOperator.In => In(false),
            FilterOperator.NotIn => In(true),
            FilterOperator.Between => Between(),
            _ => throw new InvalidOperationException($"Unsupported operator '{condition.Operator}'.")
        };

        string Binary(string operation, JsonElement value) => $"{expression} {operation} {Add(parameters, Scalar(value, field.DataType))}";
        string Like(string pattern) => $"{expression} LIKE {Add(parameters, pattern)} ESCAPE '\\'";
        string In(bool negate)
        {
            var items = condition.Value.EnumerateArray().Select(value => Add(parameters, Scalar(value, field.DataType))).ToArray();
            if (items.Length == 0) return negate ? "1 = 1" : "1 = 0";
            return $"{expression} {(negate ? "NOT IN" : "IN")} ({string.Join(", ", items)})";
        }
        string Between()
        {
            var values = condition.Value.EnumerateArray().ToArray();
            return $"{expression} BETWEEN {Add(parameters, Scalar(values[0], field.DataType))} AND {Add(parameters, Scalar(values[1], field.DataType))}";
        }
    }

    private static object Scalar(JsonElement value, FieldDataType type) => type switch
    {
        FieldDataType.Integer => value.GetInt64(),
        FieldDataType.Decimal => value.GetDecimal(),
        FieldDataType.Boolean => value.GetBoolean(),
        FieldDataType.Date => DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        FieldDataType.DateTime => DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime,
        FieldDataType.Lookup => value.GetGuid(),
        _ => value.GetString() ?? string.Empty
    };

    private static string Add(List<SqlQueryParameter> parameters, object value)
    {
        var name = $"@p{parameters.Count}";
        parameters.Add(new(name, value));
        return name;
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal);
}
