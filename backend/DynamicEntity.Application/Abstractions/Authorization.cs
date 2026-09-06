using DynamicEntity.Domain.Tenants;

namespace DynamicEntity.Application.Abstractions;

public interface IEntityAuthorizationService
{
    Task<bool> CanReadAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken);
    Task<bool> CanWriteAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken);
    Task<bool> CanManageSchemaAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken);
    Task<bool> CanReadAnalyticsAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken);
    Task<bool> CanManageAnalyticsAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken);
    Task<bool> CanManageAlertsAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken);
    Task<bool> CanViewAlertHistoryAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken);
    Task<bool> CanManageWebhooksAsync(TenantContext tenant, Guid entityId, CancellationToken cancellationToken);
}
