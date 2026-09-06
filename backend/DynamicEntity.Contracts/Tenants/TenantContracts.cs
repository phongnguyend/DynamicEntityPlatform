namespace DynamicEntity.Contracts.Tenants;

public sealed record CreateTenantRequest(string Name, string ConnectionString);

public sealed record UpdateTenantRequest(string Name);

public sealed record ConfigureTenantConnectionRequest(string ConnectionString);

public sealed record TenantResponse(Guid Id, string Name, string Status, DateTimeOffset CreatedAt, bool ConnectionConfigured, string? DatabaseName);
