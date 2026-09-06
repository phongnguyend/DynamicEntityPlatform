using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Application.Records;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using DynamicEntity.Domain.Validation;

namespace DynamicEntity.UnitTests;

public sealed class RecordServiceTests
{
    [Fact]
    public async Task UpdateAsync_MergesPatchWithoutDroppingRequiredFields()
    {
        var repository = new FakeRepository();
        var recordStore = new FakeRecordStore(repository.NameField, repository.AgeField);
        var service = CreateService(repository, recordStore);
        using var patch = JsonDocument.Parse("""{"age":31}""");

        var updated = await service.UpdateAsync(
            repository.Tenant.Id, repository.Entity.Id, recordStore.RecordId, patch.RootElement,
            recordStore.Version, null, CancellationToken.None);

        using var stored = JsonDocument.Parse(updated.Data);
        Assert.Equal("Ada", stored.RootElement.GetProperty(repository.NameField.StorageKey).GetString());
        Assert.Equal(31, stored.RootElement.GetProperty(repository.AgeField.StorageKey).GetInt32());
    }

    [Fact]
    public async Task UpdateAsync_TranslatesVersionMismatchToConflict()
    {
        var repository = new FakeRepository();
        var recordStore = new FakeRecordStore(repository.NameField, repository.AgeField)
        {
            UpdateStatus = RecordWriteStatus.Conflict
        };
        var service = CreateService(repository, recordStore);
        using var patch = JsonDocument.Parse("""{"age":31}""");

        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(
            repository.Tenant.Id, repository.Entity.Id, recordStore.RecordId, patch.RootElement,
            recordStore.Version, null, CancellationToken.None));
    }

    private static RecordService CreateService(FakeRepository repository, FakeRecordStore records) =>
        new(repository, repository, repository, new RecordValidator(new RecordValidationOptions()),
            new NoConstraints(), records);

    private sealed class NoConstraints : IRecordConstraintValidator
    {
        public Task<IReadOnlyList<RecordValidationError>> ValidateAsync(
            TenantContext tenant, EntityDefinition entity, string normalizedData, Guid? currentRecordId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RecordValidationError>>([]);
    }

    private sealed class FakeRepository : IControlPlaneStore, IEntityMetadataStore, IFieldMetadataStore
    {
        public Tenant Tenant { get; } = new(Guid.NewGuid(), "Acme", TenantStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public EntityDefinition Entity { get; }
        public FieldDefinition NameField { get; }
        public FieldDefinition AgeField { get; }
        private EntityStorageLocation Storage { get; } = new("test", "tenant", "entity", EntityStorageMode.DedicatedTable, false);

        public FakeRepository()
        {
            var now = DateTimeOffset.UtcNow;
            Entity = new EntityDefinition(Guid.NewGuid(), Tenant.Id, "customer", "Customer", null, 1, EntityStatus.Active, now, now);
            NameField = new FieldDefinition(Guid.NewGuid(), Entity.Id, "name", "Name", FieldDataType.Text, true,
                false, false, false, false, false, null, null, 0, true, now, now);
            AgeField = new FieldDefinition(Guid.NewGuid(), Entity.Id, "age", "Age", FieldDataType.Integer, false,
                false, false, false, false, false, null, null, 1, true, now, now);
        }

        public Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult<Tenant?>(Tenant);
        public Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Tenant>>([Tenant]);
        public Task<EntityStorageLocation?> GetTenantStorageAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult<EntityStorageLocation?>(Storage);
        public Task<EntityDefinition?> GetAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => Task.FromResult<EntityDefinition?>(Entity);
        public Task<IReadOnlyList<FieldDefinition>> ListAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FieldDefinition>>([NameField, AgeField]);
        public Task CreateTenantAsync(Tenant tenant, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateTenantNameAsync(Guid tenantId, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetTenantStatusAsync(Guid tenantId, TenantStatus status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveTenantStorageAsync(Guid tenantId, EntityStorageLocation location, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityDefinition> CreateAsync(EntityDefinition entity, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<EntityDefinition>> ListAsync(Guid tenantId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityStorageLocation?> GetStorageAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityDefinition?> UpdateAsync(Guid tenantId, EntityDefinition entity, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ArchiveAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FieldDefinition> CreateAsync(Guid tenantId, FieldDefinition field, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FieldDefinition> UpdateAsync(Guid tenantId, FieldDefinition field, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeactivateAsync(Guid tenantId, Guid entityId, Guid fieldId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeRecordStore : IRecordStore
    {
        public Guid RecordId { get; } = Guid.NewGuid();
        public byte[] Version { get; } = [0, 0, 0, 0, 0, 0, 0, 1];
        public RecordWriteStatus UpdateStatus { get; init; } = RecordWriteStatus.Success;
        private readonly DynamicRecord record;

        public FakeRecordStore(FieldDefinition name, FieldDefinition age)
        {
            var data = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                [name.StorageKey] = "Ada",
                [age.StorageKey] = 30
            });
            record = new DynamicRecord(RecordId, data, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null, Version);
        }

        public Task<DynamicRecord?> GetAsync(TenantContext tenant, EntityDefinition entity, Guid recordId, CancellationToken cancellationToken) => Task.FromResult<DynamicRecord?>(record);
        public Task<RecordWriteResult> UpdateAsync(TenantContext tenant, EntityDefinition entity, Guid recordId, string data, byte[] expectedVersion, Guid? userId, CancellationToken cancellationToken) =>
            Task.FromResult(UpdateStatus == RecordWriteStatus.Success
                ? new RecordWriteResult(UpdateStatus, record with { Data = data })
                : new RecordWriteResult(UpdateStatus));
        public Task<DynamicRecord> InsertAsync(TenantContext tenant, EntityDefinition entity, Guid recordId, string data, Guid? userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<DynamicRecord>> QueryAsync(TenantContext tenant, EntityDefinition entity, RecordQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RecordWriteStatus> DeleteAsync(TenantContext tenant, EntityDefinition entity, Guid recordId, byte[] expectedVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
