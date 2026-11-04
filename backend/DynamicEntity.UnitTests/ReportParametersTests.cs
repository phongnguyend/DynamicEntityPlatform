using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Analytics;
using DynamicEntity.Application.Common;
using DynamicEntity.Application.Reports;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Queries;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.UnitTests;

public sealed class ReportParametersTests
{
    [Fact]
    public async Task SaveAndRunPreserveReferenceAndResolveSuppliedValue()
    {
        var repo = new FakeRepository();
        var store = new ReportStore();
        var execution = new CapturingAnalytics();
        var validator = new AnalyticsQueryValidator();
        var analytics = new AnalyticsService(repo, repo, repo, execution, validator, TimeProvider.System);
        var service = new ReportService(repo, repo, repo, store, analytics, validator, TimeProvider.System);
        var query = Query(repo);
        var saved = await service.CreateAsync(repo.Tenant.Id, repo.Entity.Id, "Report", null,
            new(query, VisualizationType.Table), null, CancellationToken.None);
        Assert.Contains("minimum", saved.DefinitionJson);
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RunAsync(repo.Tenant.Id, repo.Entity.Id, saved.Id, CancellationToken.None));
        Assert.Null(execution.Query);
        await service.RunAsync(repo.Tenant.Id, repo.Entity.Id, saved.Id, CancellationToken.None,
            new Dictionary<string, JsonElement> { ["minimum"] = JsonSerializer.SerializeToElement("42") });
        Assert.Equal(42, Assert.IsType<FilterCondition>(execution.Query!.Filter!.Conditions.Single()).Value.GetInt32());
        Assert.Equal("minimum", Assert.IsType<FilterCondition>(ReportService.Deserialize(saved).Query.Filter!.Conditions.Single()).Value.GetProperty("parameter").GetString());
    }

    [Fact]
    public async Task SqlPreparationAllowsUnboundValuesWithoutExecutingReport()
    {
        var repo = new FakeRepository();
        var execution = new CapturingAnalytics();
        var validator = new AnalyticsQueryValidator();
        var service = new ReportService(repo, repo, repo, new ReportStore(),
            new AnalyticsService(repo, repo, repo, execution, validator, TimeProvider.System), validator, TimeProvider.System);
        var prepared = await service.PrepareQueryAsync(repo.Tenant.Id, repo.Entity.Id, Query(repo),
            CancellationToken.None, allowUnbound: true);
        Assert.Equal("minimum", Assert.IsType<FilterCondition>(prepared.Query.Filter!.Conditions.Single()).Value.GetProperty("parameter").GetString());
        Assert.Null(execution.Query);
        await Assert.ThrowsAsync<ValidationException>(() => service.PreviewAsync(repo.Tenant.Id, repo.Entity.Id, Query(repo),
            CancellationToken.None, new Dictionary<string, JsonElement> { ["minimum"] = JsonSerializer.SerializeToElement("invalid") }));
        Assert.Null(execution.Query);
    }

    private static AnalyticsQuery Query(FakeRepository repo) => new(
        new FilterGroup(FilterLogic.And, [new FilterCondition(repo.AgeField.Id, FilterOperator.GreaterThan,
            JsonSerializer.SerializeToElement(new { parameter = "minimum" }))]), [],
        [new(AggregateFunction.Count, null, "count")], [], 100);

    private sealed class CapturingAnalytics : IAnalyticsStore
    {
        public AnalyticsQuery? Query
        {
            get; private set;
        }
        public Task<AnalyticsStoreResult> ExecuteAsync(TenantContext tenant, EntityDefinition entity, AnalyticsQuery query, CancellationToken token)
        {
            Query = query;
            return Task.FromResult(new AnalyticsStoreResult([], false));
        }
    }

    private sealed class ReportStore : IReportStore
    {
        private ReportDefinition? saved;
        public Task<ReportDefinition> CreateAsync(Guid tenantId, ReportDefinition report, EntityStorageLocation storage, CancellationToken token)
        {
            saved = report;
            return Task.FromResult(report);
        }
        public Task<ReportDefinition?> GetAsync(Guid tenantId, Guid entityId, Guid id, EntityStorageLocation storage, CancellationToken token) => Task.FromResult(saved);
        public Task<IReadOnlyList<ReportDefinition>> ListAsync(Guid tenantId, Guid entityId, EntityStorageLocation storage, CancellationToken token) => throw new NotSupportedException();
        public Task<ReportDefinition?> UpdateAsync(Guid tenantId, ReportDefinition report, EntityStorageLocation storage, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid tenantId, Guid entityId, Guid id, EntityStorageLocation storage, CancellationToken token) => throw new NotSupportedException();
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

