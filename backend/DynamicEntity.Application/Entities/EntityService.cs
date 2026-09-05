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

    public async Task<EntityDefinition> UpdateAsync(Guid tenantId, Guid entityId, string? name,
        string? displayName, string? description, CancellationToken cancellationToken)
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
            Description = description?.Trim() ?? current.Description, UpdatedAt = timeProvider.GetUtcNow() };
        return await metadataStore.UpdateAsync(tenantId, updated, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found.");
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
