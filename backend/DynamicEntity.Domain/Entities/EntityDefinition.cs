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

    /// <summary>
    /// Name of the icon the client renders for this entity, or <c>null</c> to let the client pick a default.
    /// The platform stores the name without interpreting it; the icon set belongs to the client.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Zero-based position among the tenant's pinned entities, or <c>null</c> when the entity is not
    /// pinned. Clients surface pinned entities as shortcuts in this order.
    /// </summary>
    public int? PinnedOrder { get; init; }
}
