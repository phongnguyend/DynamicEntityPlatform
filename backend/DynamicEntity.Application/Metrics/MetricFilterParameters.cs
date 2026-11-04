using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Queries;

namespace DynamicEntity.Application.Metrics;

public static class MetricFilterParameters
{
    public static bool IsReference(JsonElement value) => value.ValueKind == JsonValueKind.Object;

    public static void ValidateValue(JsonElement value, FieldDataType type, bool allowReferences)
    {
        if (allowReferences && IsReference(value))
        {
            var hasParameter = value.TryGetProperty("parameter", out var parameter);
            var hasSystem = value.TryGetProperty("system", out var system);
            if (hasParameter == hasSystem)
            {
                throw new ValidationException("Use either a parameter name or a system value in a filter reference.");
            }

            if (hasParameter)
            {
                if (parameter.ValueKind != JsonValueKind.String || !Regex.IsMatch(parameter.GetString()!, "^[A-Za-z][A-Za-z0-9_]{0,63}$"))
                {
                    throw new ValidationException("Parameter names must start with a letter and contain up to 64 letters, digits or underscores.");
                }

                if (value.TryGetProperty("defaultValue", out var fallback))
                {
                    ValidateValue(fallback, type, false);
                }

                if (value.EnumerateObject().Any(property => property.Name is not ("parameter" or "defaultValue")))
                {
                    throw new ValidationException("Unknown parameter reference property.");
                }
            }
            else
            {
                if (system.ValueKind != JsonValueKind.String || system.GetString() is not ("today" or "now"))
                {
                    throw new ValidationException("Supported system filter values are today and now (UTC).");
                }

                if (type is not (FieldDataType.Date or FieldDataType.DateTime) || type == FieldDataType.Date && system.GetString() == "now")
                {
                    throw new ValidationException("Today requires a date or date-time field; now requires a date-time field.");
                }

                if (value.EnumerateObject().Any(property => property.Name != "system"))
                {
                    throw new ValidationException("Unknown system reference property.");
                }
            }
            return;
        }
        var valid = type switch
        {
            FieldDataType.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            FieldDataType.Decimal => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            FieldDataType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            FieldDataType.Date => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            FieldDataType.DateTime => value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            FieldDataType.Lookup => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _),
            _ => value.ValueKind == JsonValueKind.String
        };
        if (!valid)
        {
            throw new ValidationException($"Filter value must be a valid {type} value.");
        }
    }

    public static FilterGroup? Resolve(FilterGroup? filter, IReadOnlyList<FieldDefinition> fields,
        IReadOnlyDictionary<string, JsonElement>? parameters, DateTimeOffset now, bool validateOnly = false, bool allowUnbound = false)
    {
        if (filter is null)
        {
            return null;
        }

        var byId = fields.Where(field => field.IsActive).ToDictionary(field => field.Id);
        return Visit(filter, 0);

        FilterGroup Visit(FilterGroup group, int depth)
        {
            if (depth > 10 || group.Conditions.Count > 50)
            {
                throw new ValidationException("Metric filter exceeds nesting or condition limits.");
            }

            return group with
            {
                Conditions = group.Conditions.Select(node => node switch
            {
                FilterGroup nested => (FilterNode)Visit(nested, depth + 1),
                FilterCondition condition => ResolveCondition(condition),
                _ => throw new ValidationException("Unknown metric filter node.")
            }).ToArray()
            };
        }

        FilterCondition ResolveCondition(FilterCondition condition)
        {
            if (!byId.TryGetValue(condition.FieldId, out var field))
            {
                throw new ValidationException("Metric filter field was not found.");
            }

            if (condition.Operator is FilterOperator.IsNull or FilterOperator.IsNotNull)
            {
                return condition;
            }

            var value = condition.Value;
            if (condition.Operator is FilterOperator.In or FilterOperator.NotIn or FilterOperator.Between)
            {
                if (value.ValueKind != JsonValueKind.Array || condition.Operator == FilterOperator.Between && value.GetArrayLength() != 2)
                {
                    throw new ValidationException("Set filters require an array; Between requires exactly two values.");
                }

                value = JsonSerializer.SerializeToElement(value.EnumerateArray().Select(item => ResolveValue(item, field.DataType)).ToArray());
            }
            else
            {
                value = ResolveValue(value, field.DataType);
            }

            return condition with
            {
                Value = value
            };
        }

        JsonElement ResolveValue(JsonElement value, FieldDataType type)
        {
            ValidateValue(value, type, true);
            if (!IsReference(value) || validateOnly)
            {
                return value;
            }

            if (value.TryGetProperty("parameter", out var parameter))
            {
                var name = parameter.GetString()!;
                if (parameters is null || !parameters.TryGetValue(name, out var supplied))
                {
                    if (!value.TryGetProperty("defaultValue", out supplied))
                    {
                        if (allowUnbound)
                        {
                            return value.Clone();
                        }

                        throw new ValidationException($"Metric parameter '{name}' is required.");
                    }
                }
                supplied = ConvertInput(supplied, type);
                try
                {
                    ValidateValue(supplied, type, false);
                }
                catch (ValidationException exception) { throw new ValidationException($"Metric parameter '{name}': {exception.Message}"); }
                return supplied.Clone();
            }
            var resolved = value.GetProperty("system").GetString() == "now" ? now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                : type == FieldDataType.Date ? now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture);
            return JsonSerializer.SerializeToElement(resolved);
        }
    }

    private static JsonElement ConvertInput(JsonElement value, FieldDataType type)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return value;
        }

        var text = value.GetString();
        if (type == FieldDataType.Integer && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonSerializer.SerializeToElement(integer);
        }

        if (type == FieldDataType.Decimal && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonSerializer.SerializeToElement(number);
        }

        if (type == FieldDataType.Boolean && bool.TryParse(text, out var boolean))
        {
            return JsonSerializer.SerializeToElement(boolean);
        }

        return value;
    }
}
