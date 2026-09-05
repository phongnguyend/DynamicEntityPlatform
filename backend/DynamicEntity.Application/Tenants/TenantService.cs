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

    public async Task<Tenant> CreateAsync(string name, CancellationToken cancellationToken)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 200)
        {
            throw new ValidationException("Tenant name must contain between 1 and 200 characters.");
        }

        await initializer.InitializeAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var tenant = new Tenant(Guid.NewGuid(), name, TenantStatus.Provisioning, now, now);
        await controlPlane.CreateTenantAsync(tenant, cancellationToken);

        try
        {
            var storage = await provisioner.ProvisionAsync(tenant.Id, cancellationToken);
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
}
