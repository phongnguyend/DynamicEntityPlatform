using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Application.Fields;

public sealed record EntityIndexColumnInput(Guid FieldId, bool Descending);

public sealed class EntityIndexService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore entities,
    IFieldMetadataStore fields,
    IEntityIndexManager indexManager)
{
    public async Task<IReadOnlyList<EntityIndexDefinition>> ListAsync(
        Guid tenantId,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var entity = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found for tenant '{tenantId}'.");
        return await indexManager.ListAsync(new TenantContext(tenantId), entity, cancellationToken);
    }

    public async Task<EntityIndexDefinition> CreateAsync(
        Guid tenantId,
        Guid entityId,
        IReadOnlyList<EntityIndexColumnInput> columns,
        CancellationToken cancellationToken)
    {
        if (columns is null || columns.Count == 0)
            throw new ValidationException("At least one column is required to create an index.");
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var entity = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found for tenant '{tenantId}'.");
        var definitions = await fields.ListAsync(tenantId, entityId, storage, cancellationToken);
        var resolved = new List<(FieldDefinition Field, bool Descending)>();
        var seen = new HashSet<Guid>();
        foreach (var column in columns)
        {
            if (!seen.Add(column.FieldId))
                throw new ValidationException("Each field can only be used once per index.");
            var field = definitions.SingleOrDefault(field => field.Id == column.FieldId)
                ?? throw new NotFoundException($"Field '{column.FieldId}' was not found.");
            if (field.DataType is FieldDataType.LongText or FieldDataType.MultiChoice)
                throw new ValidationException($"{field.DataType} fields cannot use scalar computed indexes.");
            resolved.Add((field, column.Descending));
        }
        return await indexManager.CreateAsync(
            new TenantContext(tenantId), entity with { Fields = definitions }, resolved, cancellationToken);
    }

    public async Task DeleteAsync(
        Guid tenantId,
        Guid entityId,
        Guid indexId,
        CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var entity = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found for tenant '{tenantId}'.");
        var definitions = await fields.ListAsync(tenantId, entityId, storage, cancellationToken);
        if (!await indexManager.DeleteAsync(
                new TenantContext(tenantId), entity with { Fields = definitions }, indexId, cancellationToken))
            throw new NotFoundException($"Index '{indexId}' was not found.");
    }
}
