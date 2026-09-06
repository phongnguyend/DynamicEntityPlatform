using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Application.Tenants;

public sealed class TenantService(
    IControlPlaneInitializer initializer,
    IControlPlaneStore controlPlane,
    ITenantDatabaseProvisioner provisioner,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken);
        return await controlPlane.ListTenantsAsync(cancellationToken);
    }

    public async Task<Tenant> CreateAsync(string name, string connectionString, CancellationToken cancellationToken)
    {
        name = ValidateName(name);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ValidationException("A connection string for an existing tenant database is required.");

        await initializer.InitializeAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var tenant = new Tenant(Guid.NewGuid(), name, TenantStatus.Provisioning, now, now);
        await controlPlane.CreateTenantAsync(tenant, cancellationToken);

        try
        {
            var storage = await provisioner.ProvisionAsync(tenant.Id, connectionString.Trim(), cancellationToken);
            await controlPlane.SaveTenantStorageAsync(tenant.Id, storage, cancellationToken);
            await controlPlane.SetTenantStatusAsync(tenant.Id, TenantStatus.Active, cancellationToken);
            return tenant with { Status = TenantStatus.Active, UpdatedAt = timeProvider.GetUtcNow() };
        }
        catch
        {
            await controlPlane.SetTenantStatusAsync(tenant.Id, TenantStatus.Failed, CancellationToken.None);
            throw;
        }
    }

    public async Task<Tenant> UpdateAsync(Guid tenantId, string name, string connectionString, CancellationToken cancellationToken)
    {
        name = ValidateName(name);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ValidationException("A connection string for an existing tenant database is required.");
        await initializer.InitializeAsync(cancellationToken);
        var current = await controlPlane.GetTenantAsync(tenantId, cancellationToken)
            ?? throw new NotFoundException($"Tenant '{tenantId}' was not found.");

        var storage = await provisioner.ProvisionAsync(tenantId, connectionString.Trim(), cancellationToken);
        await controlPlane.SaveTenantStorageAsync(tenantId, storage, cancellationToken);
        var nextStatus = current.Status;
        if (current.Status is TenantStatus.Failed or TenantStatus.Provisioning)
        {
            nextStatus = TenantStatus.Active;
            await controlPlane.SetTenantStatusAsync(tenantId, nextStatus, cancellationToken);
        }
        await controlPlane.UpdateTenantNameAsync(tenantId, name, cancellationToken);
        return current with { Name = name, Status = nextStatus, UpdatedAt = timeProvider.GetUtcNow() };
    }

    public async Task DisableAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken);
        _ = await controlPlane.GetTenantAsync(tenantId, cancellationToken)
            ?? throw new NotFoundException($"Tenant '{tenantId}' was not found.");
        await controlPlane.SetTenantStatusAsync(tenantId, TenantStatus.Disabled, cancellationToken);
    }

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 200)
            throw new ValidationException("Tenant name must contain between 1 and 200 characters.");
        return name;
    }
}
