using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Alerts;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.UnitTests;

public sealed class AlertParametersTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAndUpdateRequireValidInputsAndPersistAlertSpecificValues(bool global)
    {
        var repo = new FakeRepository();
        var metrics = new Metrics(repo);
        var alerts = new Alerts();
        Guid? scope = global ? null : repo.Entity.Id;
        var service = new AlertService(repo, repo, metrics, alerts, null!, null!, TimeProvider.System, repo);
        Task<AlertDefinition> Create(IReadOnlyDictionary<string, JsonElement>? parameters) =>
            service.CreateAsync(repo.Tenant.Id, scope, metrics.Metric.Id, "Age alert", AlertComparisonOperator.GreaterThan,
                "10", AlertInterval.Hourly, "UTC", TimeSpan.Zero, false, true, [], null, CancellationToken.None, parameters);
        await Assert.ThrowsAsync<ValidationException>(() => Create(null));
        await Assert.ThrowsAsync<ValidationException>(() => Create(new Dictionary<string, JsonElement> { ["minimum"] = JsonSerializer.SerializeToElement("invalid") }));
        Assert.Null(alerts.Saved);
        var values = new Dictionary<string, JsonElement> { ["minimum"] = JsonSerializer.SerializeToElement("42") };
        var saved = await Create(values);
        Assert.Equal("42", alerts.Saved!.Parameters!["minimum"].GetString());
        Assert.Contains("parameter", metrics.Metric.FilterJson!);
        var updated = await service.UpdateAsync(repo.Tenant.Id, scope, saved.Id, metrics.Metric.Id, saved.Name,
            saved.ComparisonOperator, saved.ThresholdJson, saved.Interval, saved.Timezone, saved.Cooldown, false, true, [],
            CancellationToken.None, new Dictionary<string, JsonElement> { ["minimum"] = JsonSerializer.SerializeToElement("75") });
        Assert.Equal("75", updated.Parameters!["minimum"].GetString());
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(repo.Tenant.Id, scope, saved.Id,
            metrics.Metric.Id, saved.Name, saved.ComparisonOperator, saved.ThresholdJson, saved.Interval, saved.Timezone,
            saved.Cooldown, false, true, [], CancellationToken.None));
        Assert.Equal("75", alerts.Saved!.Parameters!["minimum"].GetString());
    }

    private sealed class Metrics : IMetricStore
    {
        public MetricDefinition Metric
        {
            get;
        }
        public Metrics(FakeRepository repo)
        {
            var filter = JsonSerializer.Serialize(new DynamicEntity.Domain.Queries.FilterGroup(DynamicEntity.Domain.Queries.FilterLogic.And,
                [new DynamicEntity.Domain.Queries.FilterCondition(repo.AgeField.Id, DynamicEntity.Domain.Queries.FilterOperator.GreaterThan,
                    JsonSerializer.SerializeToElement(new { parameter = "minimum" }))]),
                new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
            Metric = new(Guid.NewGuid(), repo.Entity.Id, "Count", null, AggregateFunction.Count, null, filter, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        }
        public Task<MetricDefinition?> GetByIdAsync(Guid tenantId, Guid id, EntityStorageLocation storage, CancellationToken token) => Task.FromResult<MetricDefinition?>(Metric);
        public Task<MetricDefinition?> GetAsync(Guid tenantId, Guid? entityId, Guid id, EntityStorageLocation storage, CancellationToken token) => Task.FromResult<MetricDefinition?>(Metric);
        public Task<MetricDefinition> CreateAsync(Guid tenantId, MetricDefinition metric, EntityStorageLocation storage, CancellationToken token) => throw new NotSupportedException();
        public Task<MetricDefinition?> UpdateAsync(Guid tenantId, MetricDefinition metric, EntityStorageLocation storage, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<MetricDefinition>> ListAsync(Guid tenantId, Guid? entityId, EntityStorageLocation storage, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid tenantId, Guid? entityId, Guid id, EntityStorageLocation storage, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Alerts : IAlertStore
    {
        public AlertDefinition? Saved
        {
            get; private set;
        }
        public Task<AlertDefinition> CreateAsync(Guid tenantId, AlertDefinition alert, EntityStorageLocation storage, CancellationToken token)
        {
            Saved = alert;
            return Task.FromResult(alert);
        }
        public Task<AlertDefinition?> UpdateAsync(Guid tenantId, AlertDefinition alert, EntityStorageLocation storage, CancellationToken token)
        {
            Saved = alert;
            return Task.FromResult<AlertDefinition?>(alert);
        }
        public Task<AlertDefinition?> GetAsync(Guid tenantId, Guid? entityId, Guid id, EntityStorageLocation storage, CancellationToken token) => Task.FromResult(Saved);
        public Task<IReadOnlyList<AlertDefinition>> ListAsync(Guid tenantId, Guid? entityId, EntityStorageLocation storage, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid tenantId, Guid? entityId, Guid id, EntityStorageLocation storage, CancellationToken token) => throw new NotSupportedException();
        public Task<AlertDefinition?> ClaimDueAsync(Guid tenantId, string owner, DateTimeOffset now, DateTimeOffset expires, EntityStorageLocation storage, CancellationToken token) => throw new NotSupportedException();
        public Task CompleteAsync(Guid tenantId, AlertDefinition alert, EntityStorageLocation storage, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class FakeRepository : IControlPlaneStore, IEntityMetadataStore, IFieldMetadataStore
    {
        public Tenant Tenant { get; } = new(Guid.NewGuid(), "Acme", TenantStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        public EntityDefinition Entity
        {
            get;
        }

        public FieldDefinition NameField
        {
            get;
        }

        public FieldDefinition AgeField
        {
            get;
        }

        private EntityStorageLocation Storage { get; } = new("tenant", "entity", EntityStorageMode.DedicatedTable, false);

        public FakeRepository()
        {
            var now = DateTimeOffset.UtcNow;
            Entity = new EntityDefinition(Guid.NewGuid(), Tenant.Id, "customer", "Customer", null, 1, EntityStatus.Active, now, now);
            NameField = new FieldDefinition(Guid.NewGuid(), Entity.Id, "name", "Name", FieldDataType.Text, true,
                false, true, false, false, false, null, null, 0, true, now, now);
            AgeField = new FieldDefinition(Guid.NewGuid(), Entity.Id, "age", "Age", FieldDataType.Integer, false,
                false, true, false, false, false, null, null, 1, true, now, now);
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
        public Task<IReadOnlyList<EntityDefinition>> SetPinnedOrderAsync(Guid tenantId, IReadOnlyList<Guid> entityIds, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ArchiveAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FieldDefinition> CreateAsync(Guid tenantId, FieldDefinition field, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FieldDefinition> UpdateAsync(Guid tenantId, FieldDefinition field, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeactivateAsync(Guid tenantId, Guid entityId, Guid fieldId, EntityStorageLocation tenantStorage, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

