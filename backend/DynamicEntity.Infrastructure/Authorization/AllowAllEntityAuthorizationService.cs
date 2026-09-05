using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Infrastructure.Authorization;

public sealed class AllowAllEntityAuthorizationService : IEntityAuthorizationService
{
    public Task<bool> CanReadAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> CanWriteAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> CanManageSchemaAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken) => Task.FromResult(true);
}
