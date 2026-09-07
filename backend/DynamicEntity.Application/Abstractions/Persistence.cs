using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using DynamicEntity.Domain.Validation;
using DynamicEntity.Domain.Views;
using DynamicEntity.Domain.Imports;
using DynamicEntity.Domain.Dashboards;

namespace DynamicEntity.Application.Abstractions;

public interface IControlPlaneInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

public interface IControlPlaneStore
{
    Task CreateTenantAsync(Tenant tenant, CancellationToken cancellationToken);
    Task UpdateTenantNameAsync(Guid tenantId, string name, CancellationToken cancellationToken);
    Task SetTenantStatusAsync(Guid tenantId, TenantStatus status, CancellationToken cancellationToken);
    Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken);
    Task SaveTenantStorageAsync(Guid tenantId, EntityStorageLocation location, CancellationToken cancellationToken);
    Task<EntityStorageLocation?> GetTenantStorageAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface ITenantDatabaseProvisioner
{
    Task<EntityStorageLocation> ProvisionAsync(Guid tenantId, string connectionString, CancellationToken cancellationToken);
}

public interface IEntityMetadataStore
{
    Task<EntityDefinition> CreateAsync(
        EntityDefinition entity,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken);

    Task<EntityDefinition?> GetAsync(
        Guid tenantId,
        Guid entityId,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EntityDefinition>> ListAsync(
        Guid tenantId,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken);

    Task<EntityStorageLocation?> GetStorageAsync(
        Guid tenantId,
        Guid entityId,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken);

    Task<EntityDefinition?> UpdateAsync(Guid tenantId, EntityDefinition entity,
        EntityStorageLocation tenantStorage, CancellationToken cancellationToken);
    Task<bool> ArchiveAsync(Guid tenantId, Guid entityId, EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the tenant's whole pinned set: the entities in <paramref name="entityIds"/> take that
    /// order, every other entity is unpinned. Pinning, unpinning and reordering are all the same write,
    /// so concurrent callers cannot interleave into a set with duplicate or missing positions.
    /// </summary>
    Task<IReadOnlyList<EntityDefinition>> SetPinnedOrderAsync(Guid tenantId, IReadOnlyList<Guid> entityIds,
        EntityStorageLocation tenantStorage, CancellationToken cancellationToken);
}

public interface IFieldMetadataStore
{
    Task<FieldDefinition> CreateAsync(
        Guid tenantId,
        FieldDefinition field,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FieldDefinition>> ListAsync(
        Guid tenantId,
        Guid entityId,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken);

    Task<FieldDefinition> UpdateAsync(
        Guid tenantId,
        FieldDefinition field,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken);

    Task<bool> DeactivateAsync(
        Guid tenantId,
        Guid entityId,
        Guid fieldId,
        EntityStorageLocation tenantStorage,
        CancellationToken cancellationToken);
}

public interface IRecordConstraintValidator
{
    Task<IReadOnlyList<RecordValidationError>> ValidateAsync(
        TenantContext tenant,
        EntityDefinition entity,
        string normalizedData,
        Guid? currentRecordId,
        CancellationToken cancellationToken);
}

public interface IEntityIndexManager
{
    Task<EntityIndexDefinition> CreateAsync(
        TenantContext tenant,
        EntityDefinition entity,
        IReadOnlyList<(FieldDefinition Field, bool Descending)> columns,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<EntityIndexDefinition>> ListAsync(
        TenantContext tenant,
        EntityDefinition entity,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(
        TenantContext tenant,
        EntityDefinition entity,
        Guid indexId,
        CancellationToken cancellationToken);
}

public interface IViewStore
{
    Task<ViewDefinition> CreateAsync(Guid tenantId, ViewDefinition view, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<IReadOnlyList<ViewDefinition>> ListAsync(Guid tenantId, Guid entityId, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<ViewDefinition?> UpdateAsync(Guid tenantId, ViewDefinition view, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid tenantId, Guid viewId, EntityStorageLocation storage, CancellationToken cancellationToken);
}

public interface IDashboardStore
{
    Task<DashboardDefinition> CreateAsync(DashboardDefinition dashboard, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<IReadOnlyList<DashboardDefinition>> ListAsync(Guid tenantId, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<DashboardDefinition?> GetAsync(Guid tenantId, Guid dashboardId, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<DashboardDefinition?> UpdateAsync(DashboardDefinition dashboard, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid tenantId, Guid dashboardId, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(Guid tenantId, string name, Guid? excludedDashboardId, EntityStorageLocation storage, CancellationToken cancellationToken);
}

public interface ITabularFileParser
{
    Task ParseAsync(
        Stream stream,
        string fileName,
        Func<IReadOnlyList<string>, CancellationToken, ValueTask> onHeader,
        Func<long, IReadOnlyDictionary<string, string?>, CancellationToken, ValueTask> onRow,
        CancellationToken cancellationToken);
}

public interface IImportStore
{
    Task CreateAsync(Guid tenantId, ImportJob job, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task SetColumnsAsync(Guid tenantId, Guid importId, IReadOnlyList<string> columns, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task StageRowsAsync(Guid tenantId, Guid importId, IReadOnlyList<ImportSourceRow> rows, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<ImportJob?> GetAsync(Guid tenantId, Guid importId, EntityStorageLocation storage, CancellationToken cancellationToken);
    IAsyncEnumerable<ImportSourceRow> ReadRowsAsync(Guid tenantId, Guid importId, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task SaveValidationAsync(Guid tenantId, Guid importId, IReadOnlyList<ImportSourceRow> rows, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task SetPreviewAsync(Guid tenantId, Guid importId, int total, int valid, int invalid, ImportStatus status, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<int> CommitAsync(Guid tenantId, ImportJob job, EntityStorageLocation entityStorage, EntityStorageLocation tenantStorage, CancellationToken cancellationToken);
}

public interface IAnalyticsStore
{
    Task<AnalyticsStoreResult> ExecuteAsync(
        TenantContext tenant, EntityDefinition entity, AnalyticsQuery query,
        CancellationToken cancellationToken);
}

public interface IReportStore
{
    Task<ReportDefinition> CreateAsync(Guid tenantId, ReportDefinition report, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportDefinition>> ListAsync(Guid tenantId, Guid entityId, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<ReportDefinition?> GetAsync(Guid tenantId, Guid entityId, Guid reportId, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<ReportDefinition?> UpdateAsync(Guid tenantId, ReportDefinition report, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid tenantId, Guid entityId, Guid reportId, EntityStorageLocation storage, CancellationToken cancellationToken);
}

public interface IMetricStore
{
    Task<MetricDefinition> CreateAsync(Guid tenantId, MetricDefinition metric, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<IReadOnlyList<MetricDefinition>> ListAsync(Guid tenantId, Guid entityId, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<MetricDefinition?> GetAsync(Guid tenantId, Guid entityId, Guid metricId, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<MetricDefinition?> UpdateAsync(Guid tenantId, MetricDefinition metric, EntityStorageLocation storage, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid tenantId, Guid entityId, Guid metricId, EntityStorageLocation storage, CancellationToken cancellationToken);
}

public interface IAlertStore
{
    Task<AlertDefinition> CreateAsync(Guid tenantId, AlertDefinition alert, EntityStorageLocation storage, CancellationToken token);
    Task<IReadOnlyList<AlertDefinition>> ListAsync(Guid tenantId, Guid entityId, EntityStorageLocation storage, CancellationToken token);
    Task<AlertDefinition?> GetAsync(Guid tenantId, Guid entityId, Guid alertId, EntityStorageLocation storage, CancellationToken token);
    Task<AlertDefinition?> UpdateAsync(Guid tenantId, AlertDefinition alert, EntityStorageLocation storage, CancellationToken token);
    Task<bool> DeleteAsync(Guid tenantId, Guid entityId, Guid alertId, EntityStorageLocation storage, CancellationToken token);
    Task<AlertDefinition?> ClaimDueAsync(Guid tenantId, string owner, DateTimeOffset now, DateTimeOffset leaseExpiresAt,
        EntityStorageLocation storage, CancellationToken token);
    Task CompleteAsync(Guid tenantId, AlertDefinition alert, EntityStorageLocation storage, CancellationToken token);
}

public interface IAlertEvaluationStore
{
    Task CreateAsync(Guid tenantId, AlertEvaluation evaluation, EntityStorageLocation storage, CancellationToken token);
    Task<IReadOnlyList<AlertEvaluation>> ListAsync(Guid tenantId, Guid alertId, EntityStorageLocation storage, CancellationToken token);
}

public interface IAlertNotificationStore
{
    Task CreateAsync(Guid tenantId, AlertNotification notification, EntityStorageLocation storage, CancellationToken token);
    Task<IReadOnlyList<AlertNotification>> ListAsync(Guid tenantId, Guid alertId, EntityStorageLocation storage, CancellationToken token);

    /// <summary>Leases the oldest pending notification that is due for a delivery attempt.</summary>
    Task<AlertNotification?> ClaimPendingAsync(Guid tenantId, string owner, DateTimeOffset now,
        DateTimeOffset leaseExpiresAt, EntityStorageLocation storage, CancellationToken token) =>
        Task.FromResult<AlertNotification?>(null);

    /// <summary>Records the outcome of a delivery attempt and releases the lease.</summary>
    Task CompleteAsync(Guid tenantId, AlertNotification notification, EntityStorageLocation storage, CancellationToken token) =>
        Task.CompletedTask;
}

public interface IWebhookSubscriptionStore
{
    Task<WebhookSubscription> CreateAsync(Guid tenantId, WebhookSubscription subscription, EntityStorageLocation storage, CancellationToken token);
    Task<IReadOnlyList<WebhookSubscription>> ListAsync(Guid tenantId, Guid entityId, EntityStorageLocation storage, CancellationToken token);
    Task<WebhookSubscription?> GetAsync(Guid tenantId, Guid entityId, Guid subscriptionId, EntityStorageLocation storage, CancellationToken token);
    Task<WebhookSubscription?> UpdateAsync(Guid tenantId, WebhookSubscription subscription, EntityStorageLocation storage, CancellationToken token);
    Task<bool> DeleteAsync(Guid tenantId, Guid entityId, Guid subscriptionId, EntityStorageLocation storage, CancellationToken token);
}
