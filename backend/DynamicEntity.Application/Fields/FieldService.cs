using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;

namespace DynamicEntity.Application.Fields;

public sealed class FieldService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore entities,
    IFieldMetadataStore fields,
    TimeProvider timeProvider)
{
    public async Task<FieldDefinition> CreateAsync(
        Guid tenantId,
        Guid entityId,
        string name,
        string displayName,
        FieldDataType dataType,
        bool isRequired,
        bool isUnique,
        bool isFilterable,
        bool isSortable,
        bool isFacetable,
        bool isSearchable,
        string? defaultValueJson,
        string? configurationJson,
        int sortOrder,
        CancellationToken cancellationToken)
    {
        name = name.Trim();
        displayName = displayName.Trim();
        if (!IsMachineName(name))
            throw new ValidationException("Field name must start with a letter and contain only letters, numbers, or underscores.");
        if (displayName.Length is < 1 or > 200)
            throw new ValidationException("Field display name must contain between 1 and 200 characters.");
        ValidateJson(defaultValueJson, "Default value");
        ValidateJsonObject(configurationJson, "Field configuration");

        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        _ = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found for tenant '{tenantId}'.");

        var now = timeProvider.GetUtcNow();
        var field = new FieldDefinition(
            Guid.NewGuid(), entityId, name, displayName, dataType, isRequired, isUnique,
            isFilterable, isSortable, isFacetable, isSearchable, defaultValueJson,
            configurationJson, sortOrder, true, now, now);
        return await fields.CreateAsync(tenantId, field, storage, cancellationToken);
    }

    public async Task<IReadOnlyList<FieldDefinition>> ListAsync(
        Guid tenantId,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        _ = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found for tenant '{tenantId}'.");
        return await fields.ListAsync(tenantId, entityId, storage, cancellationToken);
    }

    public async Task<FieldDefinition> UpdateAsync(
        Guid tenantId,
        Guid entityId,
        Guid fieldId,
        string? name,
        string? displayName,
        bool? isRequired,
        bool? isUnique,
        bool? isFilterable,
        bool? isSortable,
        bool? isFacetable,
        bool? isSearchable,
        string? configurationJson,
        int? sortOrder,
        CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        _ = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found for tenant '{tenantId}'.");
        var current = (await fields.ListAsync(tenantId, entityId, storage, cancellationToken))
            .SingleOrDefault(field => field.Id == fieldId)
            ?? throw new NotFoundException($"Field '{fieldId}' was not found.");

        var updatedName = name?.Trim() ?? current.Name;
        var updatedDisplayName = displayName?.Trim() ?? current.DisplayName;
        if (!IsMachineName(updatedName))
            throw new ValidationException("Field name must start with a letter and contain only letters, numbers, or underscores.");
        if (updatedDisplayName.Length is < 1 or > 200)
            throw new ValidationException("Field display name must contain between 1 and 200 characters.");
        ValidateJsonObject(configurationJson, "Field configuration");

        var updated = current with
        {
            Name = updatedName,
            DisplayName = updatedDisplayName,
            IsRequired = isRequired ?? current.IsRequired,
            IsUnique = isUnique ?? current.IsUnique,
            IsFilterable = isFilterable ?? current.IsFilterable,
            IsSortable = isSortable ?? current.IsSortable,
            IsFacetable = isFacetable ?? current.IsFacetable,
            IsSearchable = isSearchable ?? current.IsSearchable,
            ConfigurationJson = configurationJson ?? current.ConfigurationJson,
            SortOrder = sortOrder ?? current.SortOrder,
            UpdatedAt = timeProvider.GetUtcNow()
        };
        return await fields.UpdateAsync(tenantId, updated, storage, cancellationToken);
    }

    public async Task DeactivateAsync(
        Guid tenantId,
        Guid entityId,
        Guid fieldId,
        CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        if (!await fields.DeactivateAsync(tenantId, entityId, fieldId, storage, cancellationToken))
            throw new NotFoundException($"Field '{fieldId}' was not found.");
    }

    private static bool IsMachineName(string value) =>
        value.Length is >= 1 and <= 100 && char.IsAsciiLetter(value[0]) &&
        value.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static void ValidateJson(string? json, string label)
    {
        if (json is null) return;
        try { using var _ = JsonDocument.Parse(json); }
        catch (JsonException) { throw new ValidationException($"{label} must be valid JSON."); }
    }

    private static void ValidateJsonObject(string? json, string label)
    {
        if (json is null) return;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ValidationException($"{label} must be a JSON object.");
        }
        catch (JsonException)
        {
            throw new ValidationException($"{label} must be valid JSON.");
        }
    }
}
