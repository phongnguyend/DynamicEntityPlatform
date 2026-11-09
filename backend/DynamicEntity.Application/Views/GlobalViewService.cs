using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Views;

namespace DynamicEntity.Application.Views;

public sealed class GlobalViewService(IControlPlaneStore controlPlane, IGlobalViewStore views,
    IVirtualSqlService sql, TimeProvider timeProvider)
{
    public async Task<GlobalViewDefinition> CreateAsync(Guid tenantId, string name, string? description,
        GlobalViewSpecification definition, CancellationToken token)
    {
        ValidateName(name);
        ValidateDefinition(definition);
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, token);
        await sql.PreviewAsync(tenantId, null, definition.Sql, null, token);
        var now = timeProvider.GetUtcNow();
        return await views.CreateAsync(tenantId, new(Guid.Empty, name.Trim(), description?.Trim(), definition, null, now, now), storage, token);
    }

    public async Task<IReadOnlyList<GlobalViewDefinition>> ListAsync(Guid tenantId, CancellationToken token)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, token);
        return await views.ListAsync(tenantId, storage, token);
    }

    public async Task<GlobalViewDefinition> GetAsync(Guid tenantId, Guid id, CancellationToken token)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, token);
        return await views.GetAsync(tenantId, id, storage, token) ?? throw new NotFoundException($"Global view '{id}' was not found.");
    }

    public async Task<GlobalViewDefinition> UpdateAsync(Guid tenantId, Guid id, string name, string? description,
        GlobalViewSpecification definition, CancellationToken token)
    {
        ValidateName(name);
        ValidateDefinition(definition);
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, token);
        var current = await views.GetAsync(tenantId, id, storage, token) ?? throw new NotFoundException($"Global view '{id}' was not found.");
        await sql.PreviewAsync(tenantId, null, definition.Sql, null, token);
        return await views.UpdateAsync(tenantId, current with
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            Definition = definition,
            UpdatedAt = timeProvider.GetUtcNow()
        }, storage, token) ?? throw new NotFoundException($"Global view '{id}' was not found.");
    }

    public async Task DeleteAsync(Guid tenantId, Guid id, CancellationToken token)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, token);
        if (!await views.DeleteAsync(tenantId, id, storage, token))
        {
            throw new NotFoundException($"Global view '{id}' was not found.");
        }
    }

    public async Task<VirtualSqlPreview> PreviewSqlAsync(Guid tenantId, GlobalViewSpecification definition,
        IReadOnlyDictionary<string, JsonElement>? parameters, CancellationToken token)
    {
        ValidateDefinition(definition);
        return await sql.PreviewAsync(tenantId, null, definition.Sql, parameters, token);
    }

    public async Task<AnalyticsResult> PreviewAsync(Guid tenantId, GlobalViewSpecification definition,
        IReadOnlyDictionary<string, JsonElement>? parameters, CancellationToken token)
    {
        ValidateDefinition(definition);
        return await sql.ExecuteAsync(tenantId, null, definition.Sql, parameters, definition.RowLimit, false, token);
    }

    public async Task<AnalyticsResult> RunAsync(Guid tenantId, Guid id,
        IReadOnlyDictionary<string, JsonElement>? parameters, CancellationToken token)
    {
        var view = await GetAsync(tenantId, id, token);
        return await PreviewAsync(tenantId, view.Definition, parameters, token);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new ValidationException("View name must contain between 1 and 200 characters.");
        }
    }

    private static void ValidateDefinition(GlobalViewSpecification? definition)
    {
        if (definition?.Sql?.Enabled != true || string.IsNullOrWhiteSpace(definition.Sql.Text))
        {
            throw new ValidationException("Global views require a SQL query.");
        }
        if (definition.RowLimit is < 1 or > 1000)
        {
            throw new ValidationException("Row limit must be between 1 and 1000.");
        }
        if (definition.Sql.Parameters is null)
        {
            throw new ValidationException("SQL parameters must be an array.");
        }
        if (definition.ColumnOrder?.Any(string.IsNullOrWhiteSpace) == true || definition.HiddenColumns?.Any(string.IsNullOrWhiteSpace) == true)
        {
            throw new ValidationException("Column names must not be empty.");
        }
    }
}
