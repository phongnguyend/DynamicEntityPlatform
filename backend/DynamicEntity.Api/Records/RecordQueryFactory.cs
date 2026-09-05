using System.Text.Json;
using System.Globalization;
using DynamicEntity.Application.Common;
using DynamicEntity.Contracts.Records;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Queries;
using DynamicEntity.Domain.Storage;

namespace DynamicEntity.Api.Records;

public static class RecordQueryFactory
{
    public static RecordQuery Create(RecordQueryRequest request, IReadOnlyList<FieldDefinition> fields)
    {
        if (request.PageSize is < 1 or > 200)
            throw new ValidationException("Page size must be between 1 and 200.");
        var byId = fields.Where(static field => field.IsActive).ToDictionary(field => field.Id);
        var filter = request.Filter is null ? null : ParseGroup(request.Filter, byId, 0);
        var sort = request.Sort?.Select(item =>
        {
            if (!byId.TryGetValue(item.FieldId, out var field))
                throw new ValidationException($"Sort field '{item.FieldId}' was not found.");
            if (!field.IsSortable)
                throw new ValidationException($"Field '{field.DisplayName}' is not sortable.");
            return new RecordSort(item.FieldId, item.Direction);
        }).ToArray();
        return new RecordQuery(request.PageSize, request.Cursor, filter, sort);
    }

    public static FilterGroup? CreateFilter(FilterNodeRequest? request, IReadOnlyList<FieldDefinition> fields)
    {
        if (request is null) return null;
        return ParseGroup(request, fields.Where(static field => field.IsActive).ToDictionary(field => field.Id), 0);
    }

    private static FilterGroup ParseGroup(FilterNodeRequest request, IReadOnlyDictionary<Guid, FieldDefinition> fields, int depth)
    {
        if (depth > 10) throw new ValidationException("Filter nesting cannot exceed 10 levels.");
        if (request.Logic is null || request.Conditions is null)
            throw new ValidationException("A filter group requires logic and conditions.");
        if (request.Conditions.Count > 50)
            throw new ValidationException("A filter group cannot contain more than 50 conditions.");
        return new FilterGroup(request.Logic.Value, request.Conditions.Select(condition => (FilterNode)(
            condition.Conditions is not null || condition.Logic is not null
                ? ParseGroup(condition, fields, depth + 1)
                : ParseCondition(condition, fields))).ToArray());
    }

    private static FilterCondition ParseCondition(FilterNodeRequest request, IReadOnlyDictionary<Guid, FieldDefinition> fields)
    {
        if (request.FieldId is null || request.Operator is null)
            throw new ValidationException("A filter condition requires fieldId and operator.");
        if (!fields.TryGetValue(request.FieldId.Value, out var field))
            throw new ValidationException($"Filter field '{request.FieldId}' was not found.");
        if (!field.IsFilterable)
            throw new ValidationException($"Field '{field.DisplayName}' is not filterable.");
        ValidateOperator(field, request.Operator.Value);
        var noValue = request.Operator is FilterOperator.IsNull or FilterOperator.IsNotNull;
        if (!noValue && request.Value is null)
            throw new ValidationException($"Operator '{request.Operator}' requires a value.");
        var value = request.Value ?? JsonSerializer.Deserialize<JsonElement>("null");
        if (request.Operator is FilterOperator.In or FilterOperator.NotIn && value.ValueKind != JsonValueKind.Array)
            throw new ValidationException($"Operator '{request.Operator}' requires an array value.");
        if (request.Operator == FilterOperator.Between && (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2))
            throw new ValidationException("Between requires an array containing exactly two values.");
        if (!noValue)
        {
            var values = request.Operator is FilterOperator.In or FilterOperator.NotIn or FilterOperator.Between
                ? value.EnumerateArray().ToArray() : [value];
            if (values.Any(item => !IsValidValue(field.DataType, item)))
                throw new ValidationException($"Filter value is invalid for field '{field.DisplayName}'.");
        }
        return new FilterCondition(field.Id, request.Operator.Value, value.Clone());
    }

    private static void ValidateOperator(FieldDefinition field, FilterOperator @operator)
    {
        var allowed = field.DataType switch
        {
            FieldDataType.Text or FieldDataType.LongText or FieldDataType.Email or FieldDataType.Url or FieldDataType.Choice =>
                @operator is FilterOperator.Equal or FilterOperator.NotEqual or FilterOperator.Contains or FilterOperator.StartsWith or
                    FilterOperator.EndsWith or FilterOperator.IsNull or FilterOperator.IsNotNull or FilterOperator.In or FilterOperator.NotIn,
            FieldDataType.MultiChoice => @operator is FilterOperator.Contains or FilterOperator.IsNull or FilterOperator.IsNotNull,
            FieldDataType.Boolean or FieldDataType.Lookup => @operator is FilterOperator.Equal or FilterOperator.NotEqual or
                FilterOperator.IsNull or FilterOperator.IsNotNull or FilterOperator.In or FilterOperator.NotIn,
            _ => @operator is FilterOperator.Equal or FilterOperator.NotEqual or FilterOperator.GreaterThan or FilterOperator.GreaterThanOrEqual or
                FilterOperator.LessThan or FilterOperator.LessThanOrEqual or FilterOperator.IsNull or FilterOperator.IsNotNull or
                FilterOperator.In or FilterOperator.NotIn or FilterOperator.Between
        };
        if (!allowed) throw new ValidationException($"Operator '{@operator}' is not valid for {field.DataType} fields.");
    }

    private static bool IsValidValue(FieldDataType type, JsonElement value) => type switch
    {
        FieldDataType.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        FieldDataType.Decimal => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
        FieldDataType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        FieldDataType.Date => value.ValueKind == JsonValueKind.String &&
            DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        FieldDataType.DateTime => value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
        FieldDataType.Lookup => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _),
        _ => value.ValueKind == JsonValueKind.String
    };
}
