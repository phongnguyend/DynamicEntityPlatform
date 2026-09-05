using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Application.Abstractions;

public interface IEntityAuthorizationService
{
    Task<bool> CanReadAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken);
    Task<bool> CanWriteAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken);
    Task<bool> CanManageSchemaAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken);
}
