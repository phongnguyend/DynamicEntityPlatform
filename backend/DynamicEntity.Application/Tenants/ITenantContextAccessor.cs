using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Application.Tenants;

public interface ITenantContextAccessor
{
    TenantContext GetRequiredTenant();
}
