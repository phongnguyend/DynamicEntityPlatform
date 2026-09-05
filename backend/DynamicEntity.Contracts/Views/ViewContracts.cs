using System.Text.Json;

namespace DynamicEntity.Contracts.Views;

public sealed record CreateViewRequest(string Name, JsonElement Definition);
public sealed record UpdateViewRequest(Guid EntityId, string Name, JsonElement Definition);
public sealed record ViewResponse(Guid Id, Guid EntityId, string Name, JsonElement Definition, Guid? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
