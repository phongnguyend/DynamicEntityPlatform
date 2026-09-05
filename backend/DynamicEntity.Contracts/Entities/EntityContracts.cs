namespace DynamicEntity.Contracts.Entities;

public sealed record CreateEntityRequest(string Name, string DisplayName, string? Description);
public sealed record UpdateEntityRequest(string? Name, string? DisplayName, string? Description);

public sealed record EntityResponse(
    Guid Id,
    string Name,
    string DisplayName,
    string? Description,
    int SchemaVersion,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
