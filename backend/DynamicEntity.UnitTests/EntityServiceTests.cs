using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Application.Entities;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.UnitTests;

public sealed class EntityServiceTests
{
    [Theory]
    [InlineData("customer-name")]
    [InlineData("123customers")]
    [InlineData("customer name")]
    public async Task CreateAsync_RejectsNamesThatCannotBeMachineNames(string name)
    {
        var store = new FakeStore();
        var service = new EntityService(store, store, TimeProvider.System);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(store.Tenant.Id, name, "Customers", null, CancellationToken.None));
    }

    private sealed class FakeStore : IControlPlaneStore, IEntityMetadataStore
    {
        public Tenant Tenant { get; } = new(
            Guid.NewGuid(), "Acme", TenantStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        private EntityStorageLocation Storage { get; } =
            new("test", "tenant", string.Empty, EntityStorageMode.DedicatedTable, false);

        public Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<Tenant?>(Tenant);
        public Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Tenant>>([Tenant]);
        public Task<EntityStorageLocation?> GetTenantStorageAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<EntityStorageLocation?>(Storage);
        public Task<EntityDefinition> CreateAsync(EntityDefinition entity, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) =>
            Task.FromResult(entity);
        public Task CreateTenantAsync(Tenant tenant, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateTenantNameAsync(Guid tenantId, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetTenantStatusAsync(Guid tenantId, TenantStatus status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveTenantStorageAsync(Guid tenantId, EntityStorageLocation location, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityDefinition?> GetAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<EntityDefinition>> ListAsync(Guid tenantId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityStorageLocation?> GetStorageAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityDefinition?> UpdateAsync(Guid tenantId, EntityDefinition entity, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ArchiveAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
