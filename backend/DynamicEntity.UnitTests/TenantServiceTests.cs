using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Tenants;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.UnitTests;

public sealed class TenantServiceTests
{
    [Fact]
    public async Task CreateAsync_ProvisionsStorageBeforeActivatingTenant()
    {
        var store = new FakeControlPlaneStore();
        var provisioner = new FakeProvisioner(store.Events);
        var service = new TenantService(store, store, provisioner, TimeProvider.System);

        var tenant = await service.CreateAsync("Acme", CancellationToken.None);

        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(new[] { "initialize", "create", "provision", "storage", "Active" }, store.Events);
        Assert.Equal(tenant.Id, store.Tenant!.Id);
    }

    [Fact]
    public async Task CreateAsync_MarksTenantFailedWhenProvisioningFails()
    {
        var store = new FakeControlPlaneStore();
        var service = new TenantService(store, store, new FakeProvisioner(store.Events, shouldFail: true), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync("Acme", CancellationToken.None));

        Assert.Equal(TenantStatus.Failed, store.Tenant!.Status);
    }

    [Fact]
    public async Task ListAsync_InitializesControlPlaneAndReturnsTenants()
    {
        var store = new FakeControlPlaneStore
        {
            Tenant = new Tenant(Guid.NewGuid(), "Acme", TenantStatus.Active,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };
        var service = new TenantService(store, store, new FakeProvisioner(store.Events), TimeProvider.System);

        var tenants = await service.ListAsync(CancellationToken.None);

        Assert.Single(tenants);
        Assert.Equal(store.Tenant, tenants[0]);
        Assert.Equal(new[] { "initialize", "list" }, store.Events);
    }

    private sealed class FakeControlPlaneStore : IControlPlaneStore, IControlPlaneInitializer
    {
        public List<string> Events { get; } = [];
        public Tenant? Tenant { get; set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            Events.Add("initialize");
            return Task.CompletedTask;
        }

        public Task CreateTenantAsync(Tenant tenant, CancellationToken cancellationToken)
        {
            Events.Add("create");
            Tenant = tenant;
            return Task.CompletedTask;
        }

        public Task SetTenantStatusAsync(Guid tenantId, TenantStatus status, CancellationToken cancellationToken)
        {
            Events.Add(status.ToString());
            Tenant = Tenant! with { Status = status };
            return Task.CompletedTask;
        }

        public Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(Tenant);

        public Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken)
        {
            Events.Add("list");
            return Task.FromResult<IReadOnlyList<Tenant>>(Tenant is null ? [] : [Tenant]);
        }

        public Task SaveTenantStorageAsync(Guid tenantId, EntityStorageLocation location, CancellationToken cancellationToken)
        {
            Events.Add("storage");
            return Task.CompletedTask;
        }

        public Task<EntityStorageLocation?> GetTenantStorageAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<EntityStorageLocation?>(null);
    }

    private sealed class FakeProvisioner(List<string> events, bool shouldFail = false) : ITenantDatabaseProvisioner
    {
        public Task<EntityStorageLocation> ProvisionAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            events.Add("provision");
            var store = shouldFail
                ? throw new InvalidOperationException("Provisioning failed")
                : new EntityStorageLocation("test", PhysicalName.ForTenantDatabase(tenantId), string.Empty,
                    EntityStorageMode.DedicatedTable, false);
            return Task.FromResult(store);
        }
    }
}
