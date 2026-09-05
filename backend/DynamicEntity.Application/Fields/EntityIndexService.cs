using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Application.Fields;

public sealed class EntityIndexService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore entities,
    IFieldMetadataStore fields,
    IEntityIndexManager indexManager)
{
    public async Task<EntityIndexDefinition> CreateAsync(
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
        if (field.DataType is FieldDataType.LongText or FieldDataType.MultiChoice)
            throw new ValidationException($"{field.DataType} fields cannot use scalar computed indexes.");
        return await indexManager.CreateAsync(
            new TenantContext(tenantId), entity with { Fields = definitions }, field, cancellationToken);
    }

    public async Task DeleteAsync(
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
        if (field.IndexColumnName is null)
            throw new NotFoundException($"Field '{fieldId}' does not have an active index.");
        if (!await indexManager.DeleteAsync(
                new TenantContext(tenantId), entity with { Fields = definitions }, field, cancellationToken))
            throw new NotFoundException($"Index for field '{fieldId}' was not found.");
    }
}
