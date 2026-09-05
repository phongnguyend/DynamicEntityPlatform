namespace DynamicEntity.Domain.Entities;

public sealed record EntityIndexColumn(
    Guid FieldId,
    string PhysicalColumnName,
    int SortOrder,
    bool IsDescending);

public sealed record EntityIndexDefinition(
    Guid Id,
    Guid EntityId,
    string IndexName,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<EntityIndexColumn> Columns);
