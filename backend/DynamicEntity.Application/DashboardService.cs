using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Dashboards;

namespace DynamicEntity.Application.Dashboards;

public sealed class DashboardService(
    IControlPlaneStore controlPlane,
    IDashboardStore dashboards,
    TimeProvider timeProvider)
{
    public async Task<DashboardDefinition> CreateAsync(
        Guid tenantId, string name, JsonElement definition, Guid? userId, CancellationToken cancellationToken)
    {
        Validate(name, definition);
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        if (await dashboards.NameExistsAsync(tenantId, name.Trim(), null, storage, cancellationToken))
            throw new ConflictException($"A dashboard named '{name.Trim()}' already exists.");
        var now = timeProvider.GetUtcNow();
        return await dashboards.CreateAsync(new DashboardDefinition(
            Guid.NewGuid(), tenantId, name.Trim(), definition.GetRawText(), userId, now, now), storage, cancellationToken);
    }

    public async Task<IReadOnlyList<DashboardDefinition>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        return await dashboards.ListAsync(tenantId, storage, cancellationToken);
    }

    public async Task<DashboardDefinition> GetAsync(Guid tenantId, Guid dashboardId, CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        return await dashboards.GetAsync(tenantId, dashboardId, storage, cancellationToken)
            ?? throw new NotFoundException($"Dashboard '{dashboardId}' was not found.");
    }

    public async Task<DashboardDefinition> UpdateAsync(
        Guid tenantId, Guid dashboardId, string name, JsonElement definition, CancellationToken cancellationToken)
    {
        Validate(name, definition);
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var current = await dashboards.GetAsync(tenantId, dashboardId, storage, cancellationToken)
            ?? throw new NotFoundException($"Dashboard '{dashboardId}' was not found.");
        if (await dashboards.NameExistsAsync(tenantId, name.Trim(), dashboardId, storage, cancellationToken))
            throw new ConflictException($"A dashboard named '{name.Trim()}' already exists.");
        var updated = current with
        {
            Name = name.Trim(),
            DefinitionJson = definition.GetRawText(),
            UpdatedAt = timeProvider.GetUtcNow()
        };
        return await dashboards.UpdateAsync(updated, storage, cancellationToken)
            ?? throw new NotFoundException($"Dashboard '{dashboardId}' was not found.");
    }

    public async Task DeleteAsync(Guid tenantId, Guid dashboardId, CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        if (!await dashboards.DeleteAsync(tenantId, dashboardId, storage, cancellationToken))
            throw new NotFoundException($"Dashboard '{dashboardId}' was not found.");
    }

    private static void Validate(string name, JsonElement definition)
    {
        if (name.Trim().Length is < 1 or > 80)
            throw new ValidationException("Dashboard name must contain between 1 and 80 characters.");
        if (definition.ValueKind != JsonValueKind.Object)
            throw new ValidationException("Dashboard definition must be a JSON object.");
    }
}
