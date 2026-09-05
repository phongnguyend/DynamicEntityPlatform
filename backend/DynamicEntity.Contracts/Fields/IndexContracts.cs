namespace DynamicEntity.Contracts.Fields;

public sealed record CreateEntityIndexColumnRequest(
    Guid FieldId,
    bool Descending = false);

public sealed record CreateEntityIndexRequest(
    IReadOnlyList<CreateEntityIndexColumnRequest> Columns);

public sealed record EntityIndexColumnResponse(
    Guid FieldId,
    string PhysicalColumnName,
    int SortOrder,
    bool IsDescending);

public sealed record EntityIndexResponse(
    Guid Id,
    string IndexName,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<EntityIndexColumnResponse> Columns);
