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

    [Fact]
    public async Task UpdateAsync_StoresTheSelectedIconAndLeavesItAloneWhenOmitted()
    {
        var store = new FakeStore();
        var service = new EntityService(store, store, TimeProvider.System);
        var entity = await service.CreateAsync(store.Tenant.Id, "customers", "Customers", null, CancellationToken.None);

        var withIcon = await service.UpdateAsync(store.Tenant.Id, entity.Id, null, null, null, "shopping-cart", CancellationToken.None);
        Assert.Equal("shopping-cart", withIcon.Icon);

        var renamed = await service.UpdateAsync(store.Tenant.Id, entity.Id, null, "Buyers", null, null, CancellationToken.None);
        Assert.Equal("shopping-cart", renamed.Icon);
    }

    [Fact]
    public async Task UpdateAsync_ClearsTheIconWhenGivenAnEmptyValue()
    {
        var store = new FakeStore();
        var service = new EntityService(store, store, TimeProvider.System);
        var entity = await service.CreateAsync(store.Tenant.Id, "customers", "Customers", null, CancellationToken.None);
        await service.UpdateAsync(store.Tenant.Id, entity.Id, null, null, null, "users", CancellationToken.None);

        var cleared = await service.UpdateAsync(store.Tenant.Id, entity.Id, null, null, null, "  ", CancellationToken.None);
        Assert.Null(cleared.Icon);
    }

    [Theory]
    [InlineData("shopping cart")]
    [InlineData("<script>")]
    public async Task UpdateAsync_RejectsIconsThatAreNotSimpleNames(string icon)
    {
        var store = new FakeStore();
        var service = new EntityService(store, store, TimeProvider.System);
        var entity = await service.CreateAsync(store.Tenant.Id, "customers", "Customers", null, CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateAsync(store.Tenant.Id, entity.Id, null, null, null, icon, CancellationToken.None));
    }

    [Fact]
    public async Task SetPinsAsync_NumbersTheGivenOrderAndUnpinsEverythingElse()
    {
        var store = new FakeStore();
        var service = new EntityService(store, store, TimeProvider.System);
        var orders = await service.CreateAsync(store.Tenant.Id, "orders", "Orders", null, CancellationToken.None);
        var customers = await service.CreateAsync(store.Tenant.Id, "customers", "Customers", null, CancellationToken.None);
        var invoices = await service.CreateAsync(store.Tenant.Id, "invoices", "Invoices", null, CancellationToken.None);

        var pinned = await service.SetPinsAsync(store.Tenant.Id, [orders.Id, customers.Id], CancellationToken.None);
        Assert.Equal(0, pinned.Single(entity => entity.Id == orders.Id).PinnedOrder);
        Assert.Equal(1, pinned.Single(entity => entity.Id == customers.Id).PinnedOrder);
        Assert.Null(pinned.Single(entity => entity.Id == invoices.Id).PinnedOrder);

        // Reordering and unpinning are the same write, so the previous positions do not linger.
        var reordered = await service.SetPinsAsync(store.Tenant.Id, [invoices.Id, orders.Id], CancellationToken.None);
        Assert.Equal(0, reordered.Single(entity => entity.Id == invoices.Id).PinnedOrder);
        Assert.Equal(1, reordered.Single(entity => entity.Id == orders.Id).PinnedOrder);
        Assert.Null(reordered.Single(entity => entity.Id == customers.Id).PinnedOrder);
    }

    [Fact]
    public async Task SetPinsAsync_RejectsDuplicatesAndUnknownEntities()
    {
        var store = new FakeStore();
        var service = new EntityService(store, store, TimeProvider.System);
        var entity = await service.CreateAsync(store.Tenant.Id, "orders", "Orders", null, CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.SetPinsAsync(store.Tenant.Id, [entity.Id, entity.Id], CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.SetPinsAsync(store.Tenant.Id, [Guid.NewGuid()], CancellationToken.None));
    }

    private sealed class FakeStore : IControlPlaneStore, IEntityMetadataStore
    {
        private readonly List<EntityDefinition> stored = [];

        public Tenant Tenant { get; } = new(
            Guid.NewGuid(), "Acme", TenantStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        private EntityStorageLocation Storage { get; } =
            new("tenant", string.Empty, EntityStorageMode.DedicatedTable, false);

        public Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<Tenant?>(Tenant);
        public Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Tenant>>([Tenant]);
        public Task<EntityStorageLocation?> GetTenantStorageAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<EntityStorageLocation?>(Storage);
        public Task<EntityDefinition> CreateAsync(EntityDefinition entity, EntityStorageLocation tenantStorage, CancellationToken cancellationToken)
        {
            stored.Add(entity);
            return Task.FromResult(entity);
        }
        public Task CreateTenantAsync(Tenant tenant, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateTenantNameAsync(Guid tenantId, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetTenantStatusAsync(Guid tenantId, TenantStatus status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveTenantStorageAsync(Guid tenantId, EntityStorageLocation location, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityDefinition?> GetAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) =>
            Task.FromResult(stored.SingleOrDefault(entity => entity.Id == entityId));
        public Task<IReadOnlyList<EntityDefinition>> ListAsync(Guid tenantId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EntityDefinition>>([.. stored.OrderBy(entity => entity.DisplayName)]);
        public Task<EntityStorageLocation?> GetStorageAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityDefinition?> UpdateAsync(Guid tenantId, EntityDefinition entity, EntityStorageLocation tenantStorage, CancellationToken cancellationToken)
        {
            stored[stored.FindIndex(current => current.Id == entity.Id)] = entity;
            return Task.FromResult<EntityDefinition?>(entity);
        }
        public Task<bool> ArchiveAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<EntityDefinition>> SetPinnedOrderAsync(Guid tenantId, IReadOnlyList<Guid> entityIds,
            EntityStorageLocation tenantStorage, CancellationToken cancellationToken)
        {
            var positions = entityIds.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
            for (var index = 0; index < stored.Count; index++)
            {
                stored[index] = stored[index] with
                {
                    PinnedOrder = positions.TryGetValue(stored[index].Id, out var position) ? position : null
                };
            }

            return ListAsync(tenantId, tenantStorage, cancellationToken);
        }
    }
}
