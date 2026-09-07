using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Application.Entities;

public sealed class EntityService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore metadataStore,
    TimeProvider timeProvider)
{
    public async Task<EntityDefinition> CreateAsync(
        Guid tenantId,
        string name,
        string displayName,
        string? description,
        CancellationToken cancellationToken)
    {
        name = name.Trim();
        displayName = displayName.Trim();
        if (name.Length is < 1 or > 100 || !IsMachineName(name))
        {
            throw new ValidationException("Entity name must start with a letter and contain only letters, numbers, or underscores.");
        }

        if (displayName.Length is < 1 or > 200)
        {
            throw new ValidationException("Display name must contain between 1 and 200 characters.");
        }

        var storage = await RequireActiveTenantStorageAsync(tenantId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var entity = new EntityDefinition(
            Guid.NewGuid(), tenantId, name, displayName, description?.Trim(), 1,
            EntityStatus.Active, now, now);

        return await metadataStore.CreateAsync(entity, storage, cancellationToken);
    }

    public async Task<IReadOnlyList<EntityDefinition>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var storage = await RequireActiveTenantStorageAsync(tenantId, cancellationToken);
        return await metadataStore.ListAsync(tenantId, storage, cancellationToken);
    }

    public async Task<EntityDefinition> GetAsync(Guid tenantId, Guid entityId, CancellationToken cancellationToken)
    {
        var storage = await RequireActiveTenantStorageAsync(tenantId, cancellationToken);
        return await metadataStore.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found.");
    }

    /// <summary>
    /// Applies the supplied changes; a <c>null</c> argument leaves the stored value alone. An empty
    /// <paramref name="icon"/> clears the icon, which is how a client goes back to the default.
    /// </summary>
    public async Task<EntityDefinition> UpdateAsync(Guid tenantId, Guid entityId, string? name,
        string? displayName, string? description, string? icon, CancellationToken cancellationToken)
    {
        var storage = await RequireActiveTenantStorageAsync(tenantId, cancellationToken);
        var current = await metadataStore.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found.");
        var updatedName = name?.Trim() ?? current.Name;
        var updatedDisplay = displayName?.Trim() ?? current.DisplayName;
        if (updatedName.Length is < 1 or > 100 || !IsMachineName(updatedName))
            throw new ValidationException("Entity name must start with a letter and contain only letters, numbers, or underscores.");
        if (updatedDisplay.Length is < 1 or > 200)
            throw new ValidationException("Display name must contain between 1 and 200 characters.");
        var updated = current with { Name = updatedName, DisplayName = updatedDisplay,
            Description = description?.Trim() ?? current.Description,
            Icon = icon is null ? current.Icon : NormalizeIcon(icon), UpdatedAt = timeProvider.GetUtcNow() };
        return await metadataStore.UpdateAsync(tenantId, updated, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found.");
    }

    /// <summary>
    /// Replaces the tenant's pinned set with <paramref name="entityIds"/>, in that order, and returns the
    /// tenant's entities so a caller can refresh both the pinned shortcuts and the full list in one round trip.
    /// </summary>
    public async Task<IReadOnlyList<EntityDefinition>> SetPinsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> entityIds,
        CancellationToken cancellationToken)
    {
        var storage = await RequireActiveTenantStorageAsync(tenantId, cancellationToken);
        if (entityIds.Distinct().Count() != entityIds.Count)
        {
            throw new ValidationException("An entity cannot be pinned more than once.");
        }

        var existing = await metadataStore.ListAsync(tenantId, storage, cancellationToken);
        var unknown = entityIds.Where(id => existing.All(entity => entity.Id != id)).ToList();
        if (unknown.Count > 0)
        {
            throw new NotFoundException($"Entity '{unknown[0]}' was not found.");
        }

        return await metadataStore.SetPinnedOrderAsync(tenantId, entityIds, storage, cancellationToken);
    }

    public async Task ArchiveAsync(Guid tenantId, Guid entityId, CancellationToken cancellationToken)
    {
        var storage = await RequireActiveTenantStorageAsync(tenantId, cancellationToken);
        if (!await metadataStore.ArchiveAsync(tenantId, entityId, storage, cancellationToken))
            throw new NotFoundException($"Entity '{entityId}' was not found.");
    }

    private async Task<Domain.Storage.EntityStorageLocation> RequireActiveTenantStorageAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await controlPlane.GetTenantAsync(tenantId, cancellationToken)
            ?? throw new NotFoundException($"Tenant '{tenantId}' was not found.");
        if (tenant.Status != TenantStatus.Active)
        {
            throw new ConflictException($"Tenant '{tenantId}' is not active.");
        }

        return await controlPlane.GetTenantStorageAsync(tenantId, cancellationToken)
            ?? throw new ConflictException($"Tenant '{tenantId}' has no storage mapping.");
    }

    private static string? NormalizeIcon(string icon)
    {
        var trimmed = icon.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > 64 || !trimmed.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' || character is '_'))
        {
            throw new ValidationException("Icon must be at most 64 letters, numbers, dashes, or underscores.");
        }

        return trimmed;
    }

    private static bool IsMachineName(string value)
    {
        if (!char.IsAsciiLetter(value[0]))
        {
            return false;
        }

        return value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}
