namespace DynamicEntity.Domain.Imports;

public enum ImportStatus { Uploaded, Validating, Ready, Committed, Failed }

public sealed record ImportJob(
    Guid Id, Guid EntityId, string FileName, ImportStatus Status, IReadOnlyList<string> Columns,
    int TotalRows, int ValidRows, int InvalidRows, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ImportColumnMapping(string SourceColumn, Guid TargetFieldId);
public sealed record ImportSourceRow(long RowNumber, string SourceJson, string? ConvertedData, string? ValidationError);
public sealed record ImportPreview(int TotalRows, int ValidRows, int InvalidRows, IReadOnlyList<ImportRowError> Errors);
public sealed record ImportRowError(long Row, Guid? FieldId, string Message);
