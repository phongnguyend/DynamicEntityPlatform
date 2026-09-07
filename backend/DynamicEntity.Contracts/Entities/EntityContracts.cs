namespace DynamicEntity.Contracts.Entities;

public sealed record CreateEntityRequest(string Name, string DisplayName, string? Description);

/// <summary>An omitted member leaves the stored value alone; an empty <see cref="Icon"/> clears it.</summary>
public sealed record UpdateEntityRequest(string? Name, string? DisplayName, string? Description, string? Icon);

/// <summary>The pinned entities in the order they should appear; every other entity is unpinned.</summary>
public sealed record SetEntityPinsRequest(IReadOnlyList<Guid>? EntityIds);

public sealed record EntityResponse(
    Guid Id,
    string Name,
    string DisplayName,
    string? Description,
    string? Icon,
    int? PinnedOrder,
    int SchemaVersion,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
