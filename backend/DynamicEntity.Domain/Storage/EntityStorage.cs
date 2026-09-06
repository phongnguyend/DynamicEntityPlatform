using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Tenants;
using DynamicEntity.Domain.Queries;

namespace DynamicEntity.Domain.Storage;

public enum EntityStorageMode
{
    DedicatedTable,
    SharedTable
}

public sealed record EntityStorageLocation(
    string DatabaseName,
    string TableName,
    EntityStorageMode Mode,
    bool RequiresEntityPredicate,
    string? ConnectionString = null);

public interface IEntityStorageResolver
{
    ValueTask<EntityStorageLocation> ResolveAsync(
        Guid tenantId,
        Guid entityId,
        CancellationToken cancellationToken);
}

public interface IRecordStore
{
    Task<DynamicRecord?> GetAsync(
        TenantContext tenant,
        EntityDefinition entity,
        Guid recordId,
        CancellationToken cancellationToken);

    Task<PagedResult<DynamicRecord>> QueryAsync(
        TenantContext tenant,
        EntityDefinition entity,
        RecordQuery query,
        CancellationToken cancellationToken);

    Task<DynamicRecord> InsertAsync(
        TenantContext tenant,
        EntityDefinition entity,
        Guid recordId,
        string data,
        Guid? userId,
        CancellationToken cancellationToken);

    Task<RecordWriteResult> UpdateAsync(
        TenantContext tenant,
        EntityDefinition entity,
        Guid recordId,
        string data,
        byte[] expectedVersion,
        Guid? userId,
        CancellationToken cancellationToken);

    Task<RecordWriteStatus> DeleteAsync(
        TenantContext tenant,
        EntityDefinition entity,
        Guid recordId,
        byte[] expectedVersion,
        CancellationToken cancellationToken);
}

public sealed record DynamicRecord(
    Guid Id,
    string Data,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy,
    byte[] Version);

public sealed record RecordQuery(
    int PageSize,
    string? Cursor = null,
    FilterGroup? Filter = null,
    IReadOnlyList<RecordSort>? Sort = null);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor);

public enum RecordWriteStatus
{
    Success,
    NotFound,
    Conflict
}

public sealed record RecordWriteResult(RecordWriteStatus Status, DynamicRecord? Record = null);

public sealed record FacetValue(string Value, long RecordCount);

public interface IFacetStore
{
    Task<IReadOnlyList<FacetValue>> GetValuesAsync(
        TenantContext tenant,
        EntityDefinition entity,
        FieldDefinition field,
        CancellationToken cancellationToken);
}

public interface IBulkRecordStore
{
    Task<int> PatchAsync(TenantContext tenant, EntityDefinition entity, IReadOnlyList<Guid> recordIds,
        string normalizedPatch, Guid? userId, CancellationToken cancellationToken);
    Task<int> DeleteAsync(TenantContext tenant, EntityDefinition entity, IReadOnlyList<Guid> recordIds,
        CancellationToken cancellationToken);
}

public interface IRecordMergeStore
{
    Task<(int Inserted, int Updated)> MergeAsync(TenantContext tenant, EntityDefinition entity,
        FieldDefinition matchField, IReadOnlyList<string> normalizedRecords, Guid? userId,
        CancellationToken cancellationToken);
}
