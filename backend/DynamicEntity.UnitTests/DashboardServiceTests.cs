using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Application.Dashboards;
using DynamicEntity.Domain.Dashboards;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.UnitTests;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task Crud_PersistsTenantScopedDashboardDefinition()
    {
        var tenantId = Guid.NewGuid();
        var store = new FakeDashboardStore();
        var service = new DashboardService(new FakeControlPlaneStore(tenantId), store, TimeProvider.System);
        using var createdJson = JsonDocument.Parse("{\"items\":[],\"layouts\":{}}");

        var created = await service.CreateAsync(tenantId, " Operations ", createdJson.RootElement, null, CancellationToken.None);
        var listed = await service.ListAsync(tenantId, CancellationToken.None);
        var loaded = await service.GetAsync(tenantId, created.Id, CancellationToken.None);
        using var updatedJson = JsonDocument.Parse("{\"items\":[{\"kind\":\"metric\"}],\"layouts\":{}}");
        var updated = await service.UpdateAsync(tenantId, created.Id, "Executive", updatedJson.RootElement, CancellationToken.None);
        await service.DeleteAsync(tenantId, created.Id, CancellationToken.None);

        Assert.Equal("Operations", created.Name);
        Assert.Single(listed);
        Assert.Equal(created, loaded);
        Assert.Equal("Executive", updated.Name);
        Assert.Equal(updatedJson.RootElement.GetRawText(), updated.DefinitionJson);
        Assert.Empty(await service.ListAsync(tenantId, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateName()
    {
        var tenantId = Guid.NewGuid();
        var service = new DashboardService(new FakeControlPlaneStore(tenantId), new FakeDashboardStore(), TimeProvider.System);
        using var json = JsonDocument.Parse("{}");
        await service.CreateAsync(tenantId, "Sales", json.RootElement, null, CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(tenantId, "sales", json.RootElement, null, CancellationToken.None));
    }

    private sealed class FakeDashboardStore : IDashboardStore
    {
        private readonly List<DashboardDefinition> dashboards = [];

        public Task<DashboardDefinition> CreateAsync(DashboardDefinition dashboard, EntityStorageLocation storage, CancellationToken cancellationToken)
        {
            dashboards.Add(dashboard);
            return Task.FromResult(dashboard);
        }

        public Task<IReadOnlyList<DashboardDefinition>> ListAsync(Guid tenantId, EntityStorageLocation storage, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DashboardDefinition>>(dashboards.Where(item => item.TenantId == tenantId).ToArray());

        public Task<DashboardDefinition?> GetAsync(Guid tenantId, Guid dashboardId, EntityStorageLocation storage, CancellationToken cancellationToken) =>
            Task.FromResult(dashboards.SingleOrDefault(item => item.TenantId == tenantId && item.Id == dashboardId));

        public Task<DashboardDefinition?> UpdateAsync(DashboardDefinition dashboard, EntityStorageLocation storage, CancellationToken cancellationToken)
        {
            var index = dashboards.FindIndex(item => item.TenantId == dashboard.TenantId && item.Id == dashboard.Id);
            if (index < 0) return Task.FromResult<DashboardDefinition?>(null);
            dashboards[index] = dashboard;
            return Task.FromResult<DashboardDefinition?>(dashboard);
        }

        public Task<bool> DeleteAsync(Guid tenantId, Guid dashboardId, EntityStorageLocation storage, CancellationToken cancellationToken) =>
            Task.FromResult(dashboards.RemoveAll(item => item.TenantId == tenantId && item.Id == dashboardId) == 1);

        public Task<bool> NameExistsAsync(Guid tenantId, string name, Guid? excludedDashboardId, EntityStorageLocation storage, CancellationToken cancellationToken) =>
            Task.FromResult(dashboards.Any(item => item.TenantId == tenantId && item.Id != excludedDashboardId &&
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class FakeControlPlaneStore(Guid tenantId) : IControlPlaneStore
    {
        private readonly Tenant tenant = new(tenantId, "Tenant", TenantStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        private readonly EntityStorageLocation storage = new("Tenant", string.Empty, EntityStorageMode.DedicatedTable, false, "connection");

        public Task<Tenant?> GetTenantAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Tenant?>(id == tenant.Id ? tenant : null);
        public Task<EntityStorageLocation?> GetTenantStorageAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<EntityStorageLocation?>(id == tenant.Id ? storage : null);
        public Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Tenant>>([tenant]);
        public Task CreateTenantAsync(Tenant value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateTenantNameAsync(Guid id, string name, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetTenantStatusAsync(Guid id, TenantStatus status, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveTenantStorageAsync(Guid id, EntityStorageLocation location, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
