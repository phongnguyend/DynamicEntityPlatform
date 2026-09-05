using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using DynamicEntity.Domain.Validation;
using DynamicEntity.Domain.Views;
using DynamicEntity.Domain.Imports;

namespace DynamicEntity.Application.Abstractions;

public interface IControlPlaneInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

public interface IControlPlaneStore
{
    Task CreateTenantAsync(Tenant tenant, CancellationToken cancellationToken);
    Task SetTenantStatusAsync(Guid tenantId, TenantStatus status, CancellationToken cancellationToken);
    Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken);
    Task SaveTenantStorageAsync(Guid tenantId, EntityStorageLocation location, CancellationToken cancellationToken);
    Task<EntityStorageLocation?> GetTenantStorageAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface ITenantDatabaseProvisioner
{
    Task<EntityStorageLocation> ProvisionAsync(Guid tenantId, CancellationToken cancellationToken);
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
