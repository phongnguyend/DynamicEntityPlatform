using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using DynamicEntity.Domain.Validation;

namespace DynamicEntity.Application.Records;

public sealed class MergeService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore entities,
    IFieldMetadataStore fields,
    IRecordValidator validator,
    IRecordMergeStore mergeStore)
{
    public async Task<(int Inserted, int Updated)> MergeAsync(Guid tenantId, Guid entityId, Guid matchFieldId,
        IReadOnlyList<JsonElement> inputRecords, Guid? userId, CancellationToken cancellationToken)
    {
        if (inputRecords.Count is < 1 or > 10_000) throw new ValidationException("Merge requires between 1 and 10,000 records.");
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var entity = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found.");
        var definitions = await fields.ListAsync(tenantId, entityId, storage, cancellationToken);
        entity = entity with { Fields = definitions };
        var matchField = definitions.SingleOrDefault(field => field.Id == matchFieldId)
            ?? throw new NotFoundException($"Match field '{matchFieldId}' was not found.");
        if (!matchField.IsUnique || matchField.DataType is FieldDataType.MultiChoice or FieldDataType.LongText)
            throw new ValidationException("Merge match field must be a scalar unique field.");

        var normalized = new List<string>(inputRecords.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in inputRecords)
        {
            var result = await validator.ValidateAsync(entity, input, cancellationToken);
            if (!result.IsValid) throw new RecordValidationException(result.Errors);
            using var data = JsonDocument.Parse(result.NormalizedData!);
            if (!data.RootElement.TryGetProperty(matchField.StorageKey, out var key) || key.ValueKind == JsonValueKind.Null)
                throw new ValidationException($"Merge record is missing match field '{matchField.DisplayName}'.");
            if (!keys.Add(key.GetRawText())) throw new ValidationException("Merge input contains duplicate match values.");
            normalized.Add(result.NormalizedData!);
        }
        return await mergeStore.MergeAsync(new TenantContext(tenantId), entity, matchField, normalized, userId, cancellationToken);
    }
}
