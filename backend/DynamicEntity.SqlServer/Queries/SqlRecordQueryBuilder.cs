using System.Globalization;
using System.Text;
using System.Text.Json;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Queries;
using DynamicEntity.Domain.Storage;

namespace DynamicEntity.SqlServer.Queries;

public sealed record SqlQueryParameter(string Name, object Value);
public sealed record SqlRecordQueryPlan(string Sql, IReadOnlyList<SqlQueryParameter> Parameters, int Offset, bool UsesOffset);

public static class SqlRecordQueryBuilder
{
    public static SqlRecordQueryPlan Build(EntityDefinition entity, EntityStorageLocation storage, RecordQuery query)
    {
        var parameters = new List<SqlQueryParameter>();
        var where = new List<string>();
        if (storage.RequiresEntityPredicate)
        {
            where.Add("EntityId = @entityId");
            parameters.Add(new("@entityId", entity.Id));
        }
        if (query.Filter is not null && query.Filter.Conditions.Count != 0)
            where.Add(BuildGroup(query.Filter, entity.Fields.ToDictionary(field => field.Id), parameters));

        var customSort = query.Sort is { Count: > 0 };
        var offset = 0;
        if (query.Cursor is not null)
        {
            if (customSort)
                offset = DecodeOffset(query.Cursor);
            else
            {
                var cursor = DecodeKeyset(query.Cursor);
                where.Add("(CreatedAt < @cursorCreatedAt OR (CreatedAt = @cursorCreatedAt AND Id < @cursorId))");
                parameters.Add(new("@cursorCreatedAt", cursor.CreatedAt.UtcDateTime));
                parameters.Add(new("@cursorId", cursor.Id));
            }
        }

        parameters.Add(new("@take", query.PageSize + 1));
        var table = PhysicalName.QuoteSqlIdentifier(storage.TableName);
        var whereSql = where.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", where)}";
        string orderAndPage;
        if (customSort)
        {
            var fields = entity.Fields.ToDictionary(field => field.Id);
            var sorts = query.Sort!.Select(sort =>
            {
                var field = fields[sort.FieldId];
                return $"{ValueExpression(field)} {(sort.Direction == SortDirection.Asc ? "ASC" : "DESC")}";
            }).Append("Id ASC");
            parameters.Add(new("@offset", offset));
            orderAndPage = $" ORDER BY {string.Join(", ", sorts)} OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY";
        }
        else
        {
            orderAndPage = " ORDER BY CreatedAt DESC, Id DESC OFFSET 0 ROWS FETCH NEXT @take ROWS ONLY";
        }

        var sql = $"SELECT Id, Data, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, Version FROM dbo.{table}{whereSql}{orderAndPage};";
        return new SqlRecordQueryPlan(sql, parameters, offset, customSort);
    }

    public static string EncodeNextCursor(DynamicRecord record, int nextOffset, bool usesOffset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(usesOffset
            ? $"o|{nextOffset.ToString(CultureInfo.InvariantCulture)}"
            : $"k|{record.CreatedAt:O}|{record.Id:D}"));

    private static string BuildGroup(FilterGroup group, IReadOnlyDictionary<Guid, FieldDefinition> fields, List<SqlQueryParameter> parameters)
    {
        var parts = group.Conditions.Select(node => node switch
        {
            FilterGroup nested => BuildGroup(nested, fields, parameters),
            FilterCondition condition => BuildCondition(condition, fields[condition.FieldId], parameters),
            _ => throw new InvalidOperationException("Unknown filter node.")
        }).ToArray();
        if (parts.Length == 0) return group.Logic == FilterLogic.And ? "1 = 1" : "1 = 0";
        return $"({string.Join(group.Logic == FilterLogic.And ? " AND " : " OR ", parts)})";
    }

    private static string BuildCondition(FilterCondition condition, FieldDefinition field, List<SqlQueryParameter> parameters)
    {
        var expression = ValueExpression(field);
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

    private static string ValueExpression(FieldDefinition field)
    {
        if (field.IndexColumnName is not null)
            return PhysicalName.QuoteSqlIdentifier(field.IndexColumnName);
        var json = $"JSON_VALUE(Data, '$.{field.StorageKey}')";
        return field.DataType switch
        {
            FieldDataType.Integer => $"TRY_CONVERT(BIGINT, {json})",
            FieldDataType.Decimal => $"TRY_CONVERT(DECIMAL(38, 10), {json})",
            FieldDataType.Boolean => $"TRY_CONVERT(BIT, {json})",
            FieldDataType.Date => $"TRY_CONVERT(DATE, {json}, 23)",
            FieldDataType.DateTime => $"TRY_CONVERT(DATETIME2(7), {json}, 127)",
            FieldDataType.Lookup => $"TRY_CONVERT(UNIQUEIDENTIFIER, {json})",
            _ => json
        };
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

    private static int DecodeOffset(string encoded)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split('|');
            if (parts.Length != 2 || parts[0] != "o" || !int.TryParse(parts[1], out var offset) || offset < 0) throw new FormatException();
            return offset;
        }
        catch (FormatException) { throw new ValidationException("The query cursor is invalid."); }
    }

    private static (DateTimeOffset CreatedAt, Guid Id) DecodeKeyset(string encoded)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split('|');
            if (parts.Length != 3 || parts[0] != "k") throw new FormatException();
            return (DateTimeOffset.Parse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Guid.Parse(parts[2]));
        }
        catch (FormatException) { throw new ValidationException("The query cursor is invalid."); }
    }
}
