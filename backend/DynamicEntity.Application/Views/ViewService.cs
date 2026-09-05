using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Views;

namespace DynamicEntity.Application.Views;

public sealed class ViewService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore entities,
    IViewStore views,
    TimeProvider timeProvider)
{
    public async Task<ViewDefinition> CreateAsync(Guid tenantId, Guid entityId, string name, JsonElement definition, Guid? userId, CancellationToken cancellationToken)
    {
        Validate(name, definition);
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        _ = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found for tenant '{tenantId}'.");
        var now = timeProvider.GetUtcNow();
        return await views.CreateAsync(tenantId,
            new ViewDefinition(Guid.NewGuid(), entityId, name.Trim(), definition.GetRawText(), userId, now, now),
            storage, cancellationToken);
    }

    public async Task<IReadOnlyList<ViewDefinition>> ListAsync(Guid tenantId, Guid entityId, CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        _ = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found for tenant '{tenantId}'.");
        return await views.ListAsync(tenantId, entityId, storage, cancellationToken);
    }

    public async Task<ViewDefinition> UpdateAsync(Guid tenantId, Guid viewId, Guid entityId, string name, JsonElement definition, CancellationToken cancellationToken)
    {
        Validate(name, definition);
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var view = new ViewDefinition(viewId, entityId, name.Trim(), definition.GetRawText(), null, now, now);
        return await views.UpdateAsync(tenantId, view, storage, cancellationToken)
            ?? throw new NotFoundException($"View '{viewId}' was not found.");
    }

    public async Task DeleteAsync(Guid tenantId, Guid viewId, CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        if (!await views.DeleteAsync(tenantId, viewId, storage, cancellationToken))
            throw new NotFoundException($"View '{viewId}' was not found.");
    }

    private static void Validate(string name, JsonElement definition)
    {
        if (name.Trim().Length is < 1 or > 200) throw new ValidationException("View name must contain between 1 and 200 characters.");
        if (definition.ValueKind != JsonValueKind.Object) throw new ValidationException("View definition must be a JSON object.");
    }
}
