namespace DynamicEntity.Application.Common;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ConflictException(string message) : Exception(message);

public class ValidationException(string message) : Exception(message);

public sealed class RecordValidationException(
    IReadOnlyList<Domain.Validation.RecordValidationError> errors)
    : ValidationException("Record data did not satisfy the entity schema.")
{
    public IReadOnlyList<Domain.Validation.RecordValidationError> Errors { get; } = errors;
}
