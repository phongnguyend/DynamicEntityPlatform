using System.Text.Json;
using DynamicEntity.Domain.Entities;

namespace DynamicEntity.Domain.Validation;

public sealed record RecordValidationError(string? FieldKey, string Message);

public sealed record RecordValidationResult(
    bool IsValid,
    string? NormalizedData,
    IReadOnlyList<RecordValidationError> Errors);

public interface IRecordValidator
{
    Task<RecordValidationResult> ValidateAsync(
        EntityDefinition entity,
        JsonElement data,
        CancellationToken cancellationToken);
}
