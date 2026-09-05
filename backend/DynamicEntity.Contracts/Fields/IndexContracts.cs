namespace DynamicEntity.Contracts.Fields;

public sealed record EntityIndexResponse(
    Guid Id,
    Guid FieldId,
    string PhysicalColumnName,
    string IndexName,
    string Status,
    DateTimeOffset CreatedAt);
