namespace DynamicEntity.Contracts.Tenants;

public sealed record CreateTenantRequest(string Name);

public sealed record TenantResponse(Guid Id, string Name, string Status, DateTimeOffset CreatedAt);
