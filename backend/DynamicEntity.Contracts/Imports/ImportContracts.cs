using DynamicEntity.Domain.Imports;

namespace DynamicEntity.Contracts.Imports;

public sealed record ImportJobResponse(Guid Id, Guid EntityId, string FileName, ImportStatus Status,
    IReadOnlyList<string> Columns, int TotalRows, int ValidRows, int InvalidRows, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ImportMappingRequest(string SourceColumn, Guid TargetFieldId);
public sealed record ImportPreviewRequest(IReadOnlyList<ImportMappingRequest> Mappings);
public sealed record ImportPreviewResponse(int TotalRows, int ValidRows, int InvalidRows, IReadOnlyList<ImportRowError> Errors);
public sealed record ImportCommitResponse(int ImportedRows);
