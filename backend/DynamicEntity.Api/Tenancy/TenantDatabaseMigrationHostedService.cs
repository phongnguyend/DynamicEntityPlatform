using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Api.Tenancy;

public sealed class TenantDatabaseMigrationHostedService(
    IControlPlaneInitializer initializer,
    IControlPlaneStore controlPlane,
    ITenantDatabaseMigrator migrator,
    ILogger<TenantDatabaseMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken);
        var tenants = await controlPlane.ListTenantsAsync(cancellationToken);
        foreach (var tenant in tenants.Where(tenant => tenant.Status == TenantStatus.Active))
        {
            var storage = await controlPlane.GetTenantStorageAsync(tenant.Id, cancellationToken);
            if (storage is null)
            {
                logger.LogWarning("Skipping schema migration for tenant {TenantId}; no active storage is registered.", tenant.Id);
                continue;
            }

            await migrator.MigrateAsync(storage, cancellationToken);
            logger.LogInformation("Tenant database schema is current for tenant {TenantId}.", tenant.Id);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
