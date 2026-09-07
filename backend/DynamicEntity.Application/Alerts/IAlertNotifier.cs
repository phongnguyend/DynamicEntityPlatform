using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Storage;

namespace DynamicEntity.Application.Alerts;

public interface IAlertNotifier
{
    Task NotifyAsync(Guid tenantId, AlertDefinition alert, AlertEvaluation evaluation,
        EntityStorageLocation storage, CancellationToken token);
}

public static class AlertNotificationPayloads
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    public static string Serialize(AlertNotificationPayload payload) => JsonSerializer.Serialize(payload, Options);

    public static AlertNotificationPayload? Deserialize(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<AlertNotificationPayload>(json, Options);

    public static AlertNotificationPayload Create(AlertDefinition alert, AlertEvaluation evaluation,
        IReadOnlyList<string> targets) =>
        new(alert.Id, alert.EntityId, alert.MetricId, alert.Name, alert.ComparisonOperator,
            alert.ThresholdJson, evaluation.ValueJson, evaluation.State, evaluation.EvaluatedAt, targets);

    public static IReadOnlyList<string> Targets(AlertActionConfiguration action) => action.Type switch
    {
        AlertActionType.Email => action.EmailRecipients ?? [],
        AlertActionType.Webhook => action.WebhookUrls?.Select(url => url.AbsoluteUri).ToArray() ?? [],
        _ => []
    };
}

public sealed class InAppAlertNotifier(IAlertNotificationStore notifications) : IAlertNotifier
{
    public Task NotifyAsync(Guid tenantId, AlertDefinition alert, AlertEvaluation evaluation,
        EntityStorageLocation storage, CancellationToken token)
    {
        var now = evaluation.EvaluatedAt;
        return notifications.CreateAsync(tenantId, new AlertNotification(Guid.NewGuid(), alert.Id,
            evaluation.Id, AlertChannels.InApp, NotificationStatus.Delivered, 1, null, now, now), storage, token);
    }
}

/// <summary>
/// Records the in-app notification immediately and enqueues one pending row per configured external action.
/// A background delivery worker drains the queue, so evaluation never blocks on SMTP or HTTP.
/// </summary>
public sealed class QueuedAlertNotifier(IAlertNotificationStore notifications) : IAlertNotifier
{
    public async Task NotifyAsync(Guid tenantId, AlertDefinition alert, AlertEvaluation evaluation,
        EntityStorageLocation storage, CancellationToken token)
    {
        var now = evaluation.EvaluatedAt;
        await notifications.CreateAsync(tenantId, new AlertNotification(Guid.NewGuid(), alert.Id,
            evaluation.Id, AlertChannels.InApp, NotificationStatus.Delivered, 1, null, now, now), storage, token);

        foreach (var action in alert.Actions)
        {
            var targets = AlertNotificationPayloads.Targets(action);
            if (targets.Count == 0) continue;

            // (EvaluationId, Channel) is unique, so a replayed evaluation enqueues the delivery at most once.
            await notifications.CreateAsync(tenantId, new AlertNotification(Guid.NewGuid(), alert.Id,
                evaluation.Id, AlertChannels.For(action.Type), NotificationStatus.Pending, 0, null, now, null,
                AlertNotificationPayloads.Serialize(AlertNotificationPayloads.Create(alert, evaluation, targets)),
                now), storage, token);
        }
    }
}
