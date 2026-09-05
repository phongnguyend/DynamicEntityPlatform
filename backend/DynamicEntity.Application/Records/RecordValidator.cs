using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Validation;

namespace DynamicEntity.Application.Records;

public sealed record RecordValidationOptions(bool AllowUnknownFields = false);

public sealed class RecordValidator(RecordValidationOptions options) : IRecordValidator
{
    public Task<RecordValidationResult> ValidateAsync(
        EntityDefinition entity,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<RecordValidationError>();
        if (data.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new(null, "Data must be a JSON object."));
            return Task.FromResult(new RecordValidationResult(false, null, errors));
        }

        var aliases = BuildAliases(entity.Fields);
        var normalized = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var supplied = new HashSet<Guid>();
        foreach (var property in data.EnumerateObject())
        {
            if (!aliases.TryGetValue(property.Name, out var field))
            {
                if (!options.AllowUnknownFields)
                    errors.Add(new(property.Name, "Unknown field."));
                continue;
            }

            if (!supplied.Add(field.Id))
            {
                errors.Add(new(property.Name, "Field was supplied more than once using different keys."));
                continue;
            }

            ValidateValue(field, property.Value, errors);
            normalized[field.StorageKey] = property.Value.Clone();
        }

        foreach (var field in entity.Fields.Where(static field => field.IsActive && field.IsRequired))
        {
            if (!supplied.Contains(field.Id) || normalized[field.StorageKey].ValueKind == JsonValueKind.Null)
                errors.Add(new(field.StorageKey, $"{field.DisplayName} is required."));
        }

        var normalizedJson = errors.Count == 0 ? JsonSerializer.Serialize(normalized) : null;
        return Task.FromResult(new RecordValidationResult(errors.Count == 0, normalizedJson, errors));
    }

    private static Dictionary<string, FieldDefinition> BuildAliases(IEnumerable<FieldDefinition> fields)
    {
        var result = new Dictionary<string, FieldDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields.Where(static field => field.IsActive))
        {
            result[field.Name] = field;
            result[field.Id.ToString("D")] = field;
            result[field.StorageKey] = field;
        }
        return result;
    }

    private static void ValidateValue(
        FieldDefinition field,
        JsonElement value,
        ICollection<RecordValidationError> errors)
    {
        if (value.ValueKind == JsonValueKind.Null) return;
        var valid = field.DataType switch
        {
            FieldDataType.Text or FieldDataType.LongText or FieldDataType.Choice => value.ValueKind == JsonValueKind.String,
            FieldDataType.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            FieldDataType.Decimal => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            FieldDataType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            FieldDataType.Date => IsDate(value),
            FieldDataType.DateTime => IsDateTime(value),
            FieldDataType.Email => IsEmail(value),
            FieldDataType.Url => IsUrl(value),
            FieldDataType.MultiChoice => IsStringArray(value),
            FieldDataType.Lookup => IsGuid(value),
            _ => false
        };
        if (!valid)
        {
            errors.Add(new(field.StorageKey, $"{field.DisplayName} must be a valid {field.DataType} value."));
            return;
        }

        if (value.ValueKind == JsonValueKind.String)
            ValidateStringConfiguration(field, value.GetString()!, errors);
        if (field.DataType == FieldDataType.Choice)
            ValidateChoice(field, value.GetString()!, errors);
    }

    private static void ValidateStringConfiguration(
        FieldDefinition field,
        string value,
        ICollection<RecordValidationError> errors)
    {
        if (field.ConfigurationJson is null) return;
        using var configuration = JsonDocument.Parse(field.ConfigurationJson);
        var root = configuration.RootElement;
        if (root.TryGetProperty("minLength", out var min) && min.TryGetInt32(out var minLength) && value.Length < minLength)
            errors.Add(new(field.StorageKey, $"{field.DisplayName} must contain at least {minLength} characters."));
        if (root.TryGetProperty("maxLength", out var max) && max.TryGetInt32(out var maxLength) && value.Length > maxLength)
            errors.Add(new(field.StorageKey, $"{field.DisplayName} cannot exceed {maxLength} characters."));
    }

    private static void ValidateChoice(FieldDefinition field, string value, ICollection<RecordValidationError> errors)
    {
        if (field.ConfigurationJson is null) return;
        using var configuration = JsonDocument.Parse(field.ConfigurationJson);
        var root = configuration.RootElement;
        var allowCustom = root.TryGetProperty("allowCustomValues", out var allow) && allow.ValueKind == JsonValueKind.True;
        if (allowCustom || !root.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) return;
        if (!values.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == value))
            errors.Add(new(field.StorageKey, $"{field.DisplayName} contains a value outside its allowed choices."));
    }

    private static bool IsDate(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    private static bool IsDateTime(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _);
    private static bool IsEmail(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        try { return new MailAddress(value.GetString()!).Address == value.GetString(); }
        catch (FormatException) { return false; }
    }
    private static bool IsUrl(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https";
    private static bool IsGuid(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _);
    private static bool IsStringArray(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(static item => item.ValueKind == JsonValueKind.String);
}
