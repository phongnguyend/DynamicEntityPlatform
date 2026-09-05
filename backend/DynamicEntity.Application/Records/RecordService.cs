using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using DynamicEntity.Domain.Validation;

namespace DynamicEntity.Application.Records;

public sealed class RecordService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore entities,
    IFieldMetadataStore fields,
    IRecordValidator validator,
    IRecordConstraintValidator constraintValidator,
    IRecordStore records)
{
    public async Task<DynamicRecord> CreateAsync(
        Guid tenantId,
        Guid entityId,
        JsonElement data,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var entity = await GetEntityWithFieldsAsync(tenantId, entityId, cancellationToken);
        var result = await validator.ValidateAsync(entity, data, cancellationToken);
        if (!result.IsValid) throw new RecordValidationException(result.Errors);
        var constraintErrors = await constraintValidator.ValidateAsync(
            new TenantContext(tenantId), entity, result.NormalizedData!, null, cancellationToken);
        if (constraintErrors.Count != 0) throw new RecordValidationException(constraintErrors);
        return await records.InsertAsync(
            new TenantContext(tenantId), entity, Guid.NewGuid(), result.NormalizedData!, userId, cancellationToken);
    }

    public async Task<DynamicRecord> GetAsync(
        Guid tenantId,
        Guid entityId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        var entity = await GetEntityWithFieldsAsync(tenantId, entityId, cancellationToken);
        return await records.GetAsync(new TenantContext(tenantId), entity, recordId, cancellationToken)
            ?? throw new NotFoundException($"Record '{recordId}' was not found.");
    }

    public async Task<PagedResult<DynamicRecord>> ListAsync(
        Guid tenantId,
        Guid entityId,
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var entity = await GetEntityWithFieldsAsync(tenantId, entityId, cancellationToken);
        return await records.QueryAsync(
            new TenantContext(tenantId), entity, new RecordQuery(pageSize, cursor), cancellationToken);
    }

    public async Task<PagedResult<DynamicRecord>> QueryAsync(
        Guid tenantId,
        Guid entityId,
        RecordQuery query,
        CancellationToken cancellationToken)
    {
        var entity = await GetEntityWithFieldsAsync(tenantId, entityId, cancellationToken);
        return await records.QueryAsync(new TenantContext(tenantId), entity, query, cancellationToken);
    }

    public async Task<DynamicRecord> UpdateAsync(
        Guid tenantId,
        Guid entityId,
        Guid recordId,
        JsonElement data,
        byte[] expectedVersion,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (expectedVersion.Length != 8)
            throw new ValidationException("Expected version must be an 8-byte SQL rowversion value.");
        var entity = await GetEntityWithFieldsAsync(tenantId, entityId, cancellationToken);
        var context = new TenantContext(tenantId);
        var existing = await records.GetAsync(context, entity, recordId, cancellationToken)
            ?? throw new NotFoundException($"Record '{recordId}' was not found.");
        using var mergedDocument = MergePatch(existing.Data, data, entity.Fields);
        var validation = await validator.ValidateAsync(entity, mergedDocument.RootElement, cancellationToken);
        if (!validation.IsValid) throw new RecordValidationException(validation.Errors);
        var constraintErrors = await constraintValidator.ValidateAsync(
            context, entity, validation.NormalizedData!, recordId, cancellationToken);
        if (constraintErrors.Count != 0) throw new RecordValidationException(constraintErrors);
        var result = await records.UpdateAsync(
            context, entity, recordId, validation.NormalizedData!, expectedVersion, userId, cancellationToken);
        return result.Status switch
        {
            RecordWriteStatus.Success => result.Record!,
            RecordWriteStatus.NotFound => throw new NotFoundException($"Record '{recordId}' was not found."),
            _ => throw new ConflictException($"Record '{recordId}' was changed by another request.")
        };
    }

    public async Task DeleteAsync(
        Guid tenantId,
        Guid entityId,
        Guid recordId,
        byte[] expectedVersion,
        CancellationToken cancellationToken)
    {
        if (expectedVersion.Length != 8)
            throw new ValidationException("Expected version must be an 8-byte SQL rowversion value.");
        var entity = await GetEntityWithFieldsAsync(tenantId, entityId, cancellationToken);
        var status = await records.DeleteAsync(
            new TenantContext(tenantId), entity, recordId, expectedVersion, cancellationToken);
        if (status == RecordWriteStatus.NotFound)
            throw new NotFoundException($"Record '{recordId}' was not found.");
        if (status == RecordWriteStatus.Conflict)
            throw new ConflictException($"Record '{recordId}' was changed by another request.");
    }

    private async Task<EntityDefinition> GetEntityWithFieldsAsync(
        Guid tenantId,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var entity = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found for tenant '{tenantId}'.");
        var definitions = await fields.ListAsync(tenantId, entityId, storage, cancellationToken);
        return entity with { Fields = definitions };
    }

    private static JsonDocument MergePatch(
        string existingJson,
        JsonElement patch,
        IReadOnlyList<FieldDefinition> fields)
    {
        if (patch.ValueKind != JsonValueKind.Object)
            return JsonDocument.Parse(patch.GetRawText());

        var merged = JsonNode.Parse(existingJson)?.AsObject() ?? new JsonObject();
        var aliases = new Dictionary<string, FieldDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields.Where(static field => field.IsActive))
        {
            aliases[field.Name] = field;
            aliases[field.Id.ToString("D")] = field;
            aliases[field.StorageKey] = field;
        }

        foreach (var property in patch.EnumerateObject())
        {
            var key = aliases.TryGetValue(property.Name, out var field) ? field.StorageKey : property.Name;
            merged[key] = JsonNode.Parse(property.Value.GetRawText());
        }
        return JsonDocument.Parse(merged.ToJsonString());
    }
}
