using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Application.Records;

public sealed class FacetService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore entities,
    IFieldMetadataStore fields,
    IFacetStore facets)
{
    public async Task<IReadOnlyList<FacetValue>> GetValuesAsync(
        Guid tenantId,
        Guid entityId,
        Guid fieldId,
        CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var entity = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found for tenant '{tenantId}'.");
        var definitions = await fields.ListAsync(tenantId, entityId, storage, cancellationToken);
        var field = definitions.SingleOrDefault(field => field.Id == fieldId)
            ?? throw new NotFoundException($"Field '{fieldId}' was not found.");
        if (!field.IsFacetable)
            throw new ValidationException($"Field '{field.DisplayName}' is not facetable.");
        return await facets.GetValuesAsync(new TenantContext(tenantId), entity with { Fields = definitions }, field, cancellationToken);
    }
}
