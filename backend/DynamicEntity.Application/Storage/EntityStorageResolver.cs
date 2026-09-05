using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Storage;

namespace DynamicEntity.Application.Storage;

public sealed class EntityStorageResolver(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore metadataStore) : IEntityStorageResolver
{
    public async ValueTask<EntityStorageLocation> ResolveAsync(
        Guid tenantId,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        var tenantStorage = await controlPlane.GetTenantStorageAsync(tenantId, cancellationToken)
            ?? throw new NotFoundException($"Storage for tenant '{tenantId}' was not found.");

        return await metadataStore.GetStorageAsync(tenantId, entityId, tenantStorage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found for tenant '{tenantId}'.");
    }
}
