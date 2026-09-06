namespace DynamicEntity.Domain.Tenants;

public enum TenantStatus
{
    Provisioning,
    Active,
    Disabled,
    Failed,
    Archived
}

public sealed record Tenant(
    Guid Id,
    string Name,
    TenantStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TenantContext(Guid TenantId);
