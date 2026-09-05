using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Application.Common;

internal sealed class TenantStorageGuard(IControlPlaneStore controlPlane)
{
    public async Task<EntityStorageLocation> RequireActiveAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await controlPlane.GetTenantAsync(tenantId, cancellationToken)
            ?? throw new NotFoundException($"Tenant '{tenantId}' was not found.");
        if (tenant.Status != TenantStatus.Active)
            throw new ConflictException($"Tenant '{tenantId}' is not active.");

        return await controlPlane.GetTenantStorageAsync(tenantId, cancellationToken)
            ?? throw new ConflictException($"Tenant '{tenantId}' has no storage mapping.");
    }
}
