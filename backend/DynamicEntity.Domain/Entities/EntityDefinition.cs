namespace DynamicEntity.Domain.Entities;

public enum EntityStatus
{
    Active,
    Archived
}

public sealed record EntityDefinition(
    Guid Id,
    Guid TenantId,
    string Name,
    string DisplayName,
    string? Description,
    int SchemaVersion,
    EntityStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public IReadOnlyList<FieldDefinition> Fields { get; init; } = [];
}
