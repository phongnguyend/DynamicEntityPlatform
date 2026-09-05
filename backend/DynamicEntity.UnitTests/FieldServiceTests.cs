using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Fields;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.UnitTests;

public sealed class FieldServiceTests
{
    [Fact]
    public async Task UpdateAsync_UpdatesAllBehaviorFlags()
    {
        var repository = new FakeRepository();
        var service = new FieldService(repository, repository, repository, TimeProvider.System);

        var updated = await service.UpdateAsync(
            repository.Tenant.Id, repository.Entity.Id, repository.Field.Id,
            null, null, true, true, true, true, true, true,
            null, null, CancellationToken.None);

        Assert.True(updated.IsRequired);
        Assert.True(updated.IsUnique);
        Assert.True(updated.IsFilterable);
        Assert.True(updated.IsSortable);
        Assert.True(updated.IsFacetable);
        Assert.True(updated.IsSearchable);
        Assert.Equal(updated, repository.SavedField);
    }

    private sealed class FakeRepository : IControlPlaneStore, IEntityMetadataStore, IFieldMetadataStore
    {
        public Tenant Tenant { get; }
        public EntityDefinition Entity { get; }
        public FieldDefinition Field { get; }
        public FieldDefinition? SavedField { get; private set; }
        private EntityStorageLocation Storage { get; } =
            new("test", "tenant", string.Empty, EntityStorageMode.DedicatedTable, false);

        public FakeRepository()
        {
            var now = DateTimeOffset.UtcNow;
            Tenant = new Tenant(Guid.NewGuid(), "Acme", TenantStatus.Active, now, now);
            Entity = new EntityDefinition(Guid.NewGuid(), Tenant.Id, "customer", "Customer", null,
                1, EntityStatus.Active, now, now);
            Field = new FieldDefinition(Guid.NewGuid(), Entity.Id, "name", "Name", FieldDataType.Text,
                false, false, false, false, false, false, null, null, 0, true, now, now);
        }

        public Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<Tenant?>(Tenant);
        public Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Tenant>>([Tenant]);
        public Task<EntityStorageLocation?> GetTenantStorageAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<EntityStorageLocation?>(Storage);
        public Task<EntityDefinition?> GetAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage,
            CancellationToken cancellationToken) => Task.FromResult<EntityDefinition?>(Entity);
        public Task<IReadOnlyList<FieldDefinition>> ListAsync(Guid tenantId, Guid entityId,
            EntityStorageLocation tenantStorage, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FieldDefinition>>([Field]);
        public Task<FieldDefinition> UpdateAsync(Guid tenantId, FieldDefinition field,
            EntityStorageLocation tenantStorage, CancellationToken cancellationToken)
        {
            SavedField = field;
            return Task.FromResult(field);
        }

        public Task CreateTenantAsync(Tenant tenant, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetTenantStatusAsync(Guid tenantId, TenantStatus status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveTenantStorageAsync(Guid tenantId, EntityStorageLocation location, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityDefinition> CreateAsync(EntityDefinition entity, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<EntityDefinition>> ListAsync(Guid tenantId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityStorageLocation?> GetStorageAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityDefinition?> UpdateAsync(Guid tenantId, EntityDefinition entity, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ArchiveAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FieldDefinition> CreateAsync(Guid tenantId, FieldDefinition field, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeactivateAsync(Guid tenantId, Guid entityId, Guid fieldId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
