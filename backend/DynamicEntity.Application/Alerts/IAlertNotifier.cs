using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Storage;

namespace DynamicEntity.Application.Alerts;

public interface IAlertNotifier
{
    Task NotifyAsync(Guid tenantId, AlertDefinition alert, AlertEvaluation evaluation,
        EntityStorageLocation storage, CancellationToken token);
}

public sealed class InAppAlertNotifier(IAlertNotificationStore notifications) : IAlertNotifier
{
    public Task NotifyAsync(Guid tenantId, AlertDefinition alert, AlertEvaluation evaluation,
        EntityStorageLocation storage, CancellationToken token)
    {
        var now = evaluation.EvaluatedAt;
        return notifications.CreateAsync(tenantId, new AlertNotification(Guid.NewGuid(), alert.Id,
            evaluation.Id, "InApp", NotificationStatus.Delivered, 1, null, now, now), storage, token);
    }
}
