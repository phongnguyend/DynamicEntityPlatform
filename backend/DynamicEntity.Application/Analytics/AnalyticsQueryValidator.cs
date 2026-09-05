using System.Text.RegularExpressions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Queries;

namespace DynamicEntity.Application.Analytics;

public sealed record AnalyticsValidationOptions(int MaximumMeasures = 10, int MaximumRows = 1000);

public sealed partial class AnalyticsQueryValidator(AnalyticsValidationOptions? options = null)
{
    private readonly AnalyticsValidationOptions _options = options ?? new AnalyticsValidationOptions();

    public void Validate(Guid entityId, AnalyticsQuery query, IReadOnlyList<FieldDefinition> fields)
    {
        if (query.Dimensions.Count > 2)
            throw new ValidationException("Analytics queries support at most two dimensions.");
        if (query.Measures.Count is 0)
            throw new ValidationException("Analytics queries require at least one measure.");
        if (query.Measures.Count > _options.MaximumMeasures)
            throw new ValidationException($"Analytics queries support at most {_options.MaximumMeasures} measures.");
        var maximumRows = Math.Min(1000, _options.MaximumRows);
        if (query.Limit < 1 || query.Limit > maximumRows)
            throw new ValidationException($"Analytics query limit must be between 1 and {maximumRows}.");

        var byId = fields.Where(field => field.EntityId == entityId).ToDictionary(field => field.Id);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dimension in query.Dimensions)
        {
            ValidateAlias(dimension.Alias, aliases);
            ValidateEnum(dimension.DateBucket, "date bucket");
            var field = GetActiveField(dimension.FieldId, byId);
            if (dimension.DateBucket != DateBucket.None && field.DataType is not (FieldDataType.Date or FieldDataType.DateTime))
                throw new ValidationException($"Date bucket '{dimension.DateBucket}' requires a date or date-time field ({field.Id}).");
        }

        foreach (var measure in query.Measures)
        {
            ValidateAlias(measure.Alias, aliases);
            ValidateEnum(measure.Aggregate, "aggregate");
            if (measure.Aggregate == AggregateFunction.Count)
            {
                if (measure.FieldId is Guid countFieldId) GetActiveField(countFieldId, byId);
                continue;
            }
            if (measure.FieldId is not Guid fieldId)
                throw new ValidationException($"Aggregate '{measure.Aggregate}' requires a field.");

            var field = GetActiveField(fieldId, byId);
            if (measure.Aggregate is AggregateFunction.Sum or AggregateFunction.Average &&
                field.DataType is not (FieldDataType.Integer or FieldDataType.Decimal))
                throw new ValidationException($"Aggregate '{measure.Aggregate}' requires a numeric field ({field.Id}).");
            if (measure.Aggregate == AggregateFunction.CountDistinct &&
                field.DataType is FieldDataType.LongText or FieldDataType.MultiChoice)
                throw new ValidationException($"Count distinct does not support field type '{field.DataType}' ({field.Id}).");
        }

        if (query.Filter is not null) ValidateFilter(query.Filter, byId);
        foreach (var sort in query.Sort)
        {
            ValidateEnum(sort.Direction, "sort direction");
            if (!aliases.Contains(sort.Alias))
                throw new ValidationException($"Analytics sort alias '{sort.Alias}' does not identify an output column.");
        }
    }

    private static FieldDefinition GetActiveField(Guid fieldId, IReadOnlyDictionary<Guid, FieldDefinition> fields)
    {
        if (!fields.TryGetValue(fieldId, out var field))
            throw new ValidationException($"Field '{fieldId}' does not belong to the requested entity.");
        if (!field.IsActive)
            throw new ValidationException($"Field '{fieldId}' is deactivated and the analytics definition is broken.");
        return field;
    }

    private static void ValidateFilter(FilterNode node, IReadOnlyDictionary<Guid, FieldDefinition> fields)
    {
        switch (node)
        {
            case FilterCondition condition:
                ValidateEnum(condition.Operator, "filter operator");
                var field = GetActiveField(condition.FieldId, fields);
                if (!field.IsFilterable)
                    throw new ValidationException($"Field '{field.Id}' is not filterable.");
                break;
            case FilterGroup group:
                ValidateEnum(group.Logic, "filter logic");
                foreach (var child in group.Conditions) ValidateFilter(child, fields);
                break;
        }
    }

    private static void ValidateEnum<TEnum>(TEnum value, string name) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ValidationException($"Unknown analytics {name} '{value}'.");
    }

    private static void ValidateAlias(string alias, HashSet<string> aliases)
    {
        if (string.IsNullOrWhiteSpace(alias) || !AliasPattern().IsMatch(alias))
            throw new ValidationException("Analytics aliases must start with a letter and contain only letters, numbers, and underscores.");
        if (!aliases.Add(alias))
            throw new ValidationException($"Analytics alias '{alias}' is duplicated.");
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AliasPattern();
}
