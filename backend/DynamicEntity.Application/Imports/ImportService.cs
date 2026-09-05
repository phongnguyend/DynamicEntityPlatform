using System.Globalization;
using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Imports;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using DynamicEntity.Domain.Validation;

namespace DynamicEntity.Application.Imports;

public sealed class ImportService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore entities,
    IFieldMetadataStore fields,
    ITabularFileParser parser,
    IImportStore imports,
    IRecordValidator validator,
    IRecordConstraintValidator constraints,
    IEntityStorageResolver storageResolver,
    TimeProvider timeProvider)
{
    public async Task<ImportJob> UploadAsync(Guid tenantId, Guid entityId, Stream stream, string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 260) throw new ValidationException("A valid file name is required.");
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        _ = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found for tenant '{tenantId}'.");
        var now = timeProvider.GetUtcNow();
        var job = new ImportJob(Guid.NewGuid(), entityId, Path.GetFileName(fileName), ImportStatus.Uploaded, [], 0, 0, 0, now, now);
        await imports.CreateAsync(tenantId, job, storage, cancellationToken);
        var batch = new List<ImportSourceRow>(500);
        IReadOnlyList<string> columns = [];
        await parser.ParseAsync(stream, fileName,
            async (header, token) => { columns = header; await imports.SetColumnsAsync(tenantId, job.Id, header, storage, token); },
            async (rowNumber, values, token) =>
            {
                batch.Add(new ImportSourceRow(rowNumber, JsonSerializer.Serialize(values), null, null));
                if (batch.Count < 500) return;
                await imports.StageRowsAsync(tenantId, job.Id, batch, storage, token);
                batch.Clear();
            }, cancellationToken);
        if (batch.Count != 0) await imports.StageRowsAsync(tenantId, job.Id, batch, storage, cancellationToken);
        return job with { Columns = columns, UpdatedAt = timeProvider.GetUtcNow() };
    }

    public async Task<ImportPreview> PreviewAsync(Guid tenantId, Guid importId, IReadOnlyList<ImportColumnMapping> mappings, CancellationToken cancellationToken)
    {
        var tenantStorage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var job = await imports.GetAsync(tenantId, importId, tenantStorage, cancellationToken)
            ?? throw new NotFoundException($"Import '{importId}' was not found.");
        if (job.Status == ImportStatus.Committed) throw new ConflictException("A committed import cannot be previewed again.");
        var entity = await entities.GetAsync(tenantId, job.EntityId, tenantStorage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{job.EntityId}' was not found.");
        var definitions = await fields.ListAsync(tenantId, job.EntityId, tenantStorage, cancellationToken);
        var byId = definitions.ToDictionary(field => field.Id);
        ValidateMappings(job.Columns, mappings, byId);
        entity = entity with { Fields = definitions };

        var total = 0; var valid = 0; var invalid = 0;
        var previewErrors = new List<ImportRowError>();
        var validationBatch = new List<ImportSourceRow>(500);
        await foreach (var row in imports.ReadRowsAsync(tenantId, importId, tenantStorage, cancellationToken))
        {
            total++;
            using var source = JsonDocument.Parse(row.SourceJson);
            var input = new Dictionary<string, object?>();
            var conversionErrors = new List<RecordValidationError>();
            foreach (var mapping in mappings)
            {
                var field = byId[mapping.TargetFieldId];
                var sourceValue = source.RootElement.TryGetProperty(mapping.SourceColumn, out var value) ? value.GetString() : null;
                try { input[field.Name] = ConvertValue(sourceValue, field.DataType); }
                catch (FormatException) { conversionErrors.Add(new(field.StorageKey, $"{field.DisplayName} has an invalid {field.DataType} value.")); }
            }
            var inputJson = JsonSerializer.Serialize(input);
            using var inputDocument = JsonDocument.Parse(inputJson);
            var validation = conversionErrors.Count == 0
                ? await validator.ValidateAsync(entity, inputDocument.RootElement, cancellationToken)
                : new RecordValidationResult(false, null, conversionErrors);
            var errors = validation.Errors;
            if (validation.IsValid)
                errors = await constraints.ValidateAsync(new TenantContext(tenantId), entity, validation.NormalizedData!, null, cancellationToken);
            var errorJson = errors.Count == 0 ? null : JsonSerializer.Serialize(errors);
            if (errors.Count == 0) valid++; else
            {
                invalid++;
                if (previewErrors.Count < 100) previewErrors.AddRange(errors.Select(error => new ImportRowError(row.RowNumber,
                    definitions.FirstOrDefault(field => field.StorageKey == error.FieldKey)?.Id, error.Message)).Take(100 - previewErrors.Count));
            }
            validationBatch.Add(row with { ConvertedData = errors.Count == 0 ? validation.NormalizedData : null, ValidationError = errorJson });
            if (validationBatch.Count == 500) { await imports.SaveValidationAsync(tenantId, importId, validationBatch, tenantStorage, cancellationToken); validationBatch.Clear(); }
        }
        if (validationBatch.Count != 0) await imports.SaveValidationAsync(tenantId, importId, validationBatch, tenantStorage, cancellationToken);
        await imports.SetPreviewAsync(tenantId, importId, total, valid, invalid, ImportStatus.Ready, tenantStorage, cancellationToken);
        return new ImportPreview(total, valid, invalid, previewErrors);
    }

    public async Task<int> CommitAsync(Guid tenantId, Guid importId, CancellationToken cancellationToken)
    {
        var tenantStorage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var job = await imports.GetAsync(tenantId, importId, tenantStorage, cancellationToken)
            ?? throw new NotFoundException($"Import '{importId}' was not found.");
        if (job.Status != ImportStatus.Ready) throw new ConflictException("Import must be validated before it can be committed.");
        var entityStorage = await storageResolver.ResolveAsync(tenantId, job.EntityId, cancellationToken);
        return await imports.CommitAsync(tenantId, job, entityStorage, tenantStorage, cancellationToken);
    }

    public async Task<ImportJob> GetAsync(Guid tenantId, Guid importId, CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        return await imports.GetAsync(tenantId, importId, storage, cancellationToken)
            ?? throw new NotFoundException($"Import '{importId}' was not found.");
    }

    private static void ValidateMappings(IReadOnlyList<string> columns, IReadOnlyList<ImportColumnMapping> mappings, IReadOnlyDictionary<Guid, FieldDefinition> fields)
    {
        if (mappings.Count == 0) throw new ValidationException("At least one import mapping is required.");
        if (mappings.Select(mapping => mapping.SourceColumn).Distinct(StringComparer.OrdinalIgnoreCase).Count() != mappings.Count ||
            mappings.Select(mapping => mapping.TargetFieldId).Distinct().Count() != mappings.Count)
            throw new ValidationException("Source and target fields may only be mapped once.");
        foreach (var mapping in mappings)
        {
            if (!columns.Contains(mapping.SourceColumn, StringComparer.OrdinalIgnoreCase)) throw new ValidationException($"Source column '{mapping.SourceColumn}' was not found.");
            if (!fields.ContainsKey(mapping.TargetFieldId)) throw new ValidationException($"Target field '{mapping.TargetFieldId}' was not found.");
        }
    }

    private static object? ConvertValue(string? value, FieldDataType type)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return type switch
        {
            FieldDataType.Integer => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            FieldDataType.Decimal => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture),
            FieldDataType.Boolean => bool.Parse(value),
            FieldDataType.MultiChoice => value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            _ => value
        };
    }
}
