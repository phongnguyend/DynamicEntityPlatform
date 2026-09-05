using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using DynamicEntity.Domain.Validation;

namespace DynamicEntity.Application.Records;

public sealed class BulkRecordService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore entities,
    IFieldMetadataStore fields,
    IRecordValidator validator,
    IRecordConstraintValidator constraints,
    IBulkRecordStore bulkStore)
{
    public async Task<int> PatchAsync(Guid tenantId, Guid entityId, IReadOnlyList<Guid> recordIds,
        JsonElement patch, Guid? userId, CancellationToken cancellationToken)
    {
        ValidateIds(recordIds);
        var entity = await GetEntityAsync(tenantId, entityId, cancellationToken);
        var partial = entity with { Fields = entity.Fields.Select(field => field with { IsRequired = false }).ToArray() };
        var validation = await validator.ValidateAsync(partial, patch, cancellationToken);
        if (!validation.IsValid) throw new RecordValidationException(validation.Errors);
        using var normalized = JsonDocument.Parse(validation.NormalizedData!);
        foreach (var field in entity.Fields.Where(field => field.IsRequired && normalized.RootElement.TryGetProperty(field.StorageKey, out var value) && value.ValueKind == JsonValueKind.Null))
            throw new RecordValidationException([new RecordValidationError(field.StorageKey, $"{field.DisplayName} is required.")]);
        var uniqueFields = entity.Fields.Where(field => field.IsUnique && normalized.RootElement.TryGetProperty(field.StorageKey, out _)).ToArray();
        if (uniqueFields.Length != 0 && recordIds.Count > 1)
            throw new ValidationException("A unique field cannot be assigned to multiple records in one bulk patch.");
        if (recordIds.Count == 1)
        {
            var errors = await constraints.ValidateAsync(new TenantContext(tenantId), entity, validation.NormalizedData!, recordIds[0], cancellationToken);
            if (errors.Count != 0) throw new RecordValidationException(errors);
        }
        return await bulkStore.PatchAsync(new TenantContext(tenantId), entity, recordIds, validation.NormalizedData!, userId, cancellationToken);
    }

    public async Task<int> DeleteAsync(Guid tenantId, Guid entityId, IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken)
    {
        ValidateIds(recordIds);
        var entity = await GetEntityAsync(tenantId, entityId, cancellationToken);
        return await bulkStore.DeleteAsync(new TenantContext(tenantId), entity, recordIds, cancellationToken);
    }

    private async Task<EntityDefinition> GetEntityAsync(Guid tenantId, Guid entityId, CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var entity = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found.");
        return entity with { Fields = await fields.ListAsync(tenantId, entityId, storage, cancellationToken) };
    }

    private static void ValidateIds(IReadOnlyList<Guid> ids)
    {
        if (ids.Count is < 1 or > 10_000) throw new ValidationException("Bulk operations require between 1 and 10,000 record IDs.");
        if (ids.Distinct().Count() != ids.Count) throw new ValidationException("Bulk record IDs must be unique.");
    }
}
