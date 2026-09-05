namespace DynamicEntity.Domain.Entities;

public sealed record EntityIndexDefinition(
    Guid Id,
    Guid EntityId,
    Guid FieldId,
    string PhysicalColumnName,
    string IndexName,
    string Status,
    DateTimeOffset CreatedAt);
