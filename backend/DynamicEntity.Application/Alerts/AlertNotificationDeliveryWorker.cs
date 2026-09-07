using System.Diagnostics;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Analytics;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Storage;

namespace DynamicEntity.Application.Alerts;

/// <summary>
/// Drains the pending alert notification queue of one tenant and delivers each row over its channel.
/// Deliveries are at-least-once: a lease guards concurrent workers and every attempt is recorded on the row.
/// </summary>
public sealed class AlertNotificationDeliveryWorker(
    IControlPlaneStore controlPlane,
    IAlertNotificationStore notifications,
    IEnumerable<IAlertChannelSender> senders,
    TimeProvider timeProvider)
{
    private const int MaxDeliveriesPerPass = 100;
    private const int MaxErrorLength = 3900;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private readonly Dictionary<string, IAlertChannelSender> _senders =
        senders.ToDictionary(sender => sender.Channel, StringComparer.OrdinalIgnoreCase);

    public async Task<int> ProcessTenantAsync(Guid tenantId, string owner, CancellationToken token)
    {
        var storage = await controlPlane.GetTenantStorageAsync(tenantId, token);
        if (storage is null) return 0;

        var delivered = 0;
        while (delivered < MaxDeliveriesPerPass && !token.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var notification = await notifications.ClaimPendingAsync(tenantId, owner, now,
                now + LeaseDuration, storage, token);
            if (notification is null) break;

            delivered++;
            await DeliverAsync(tenantId, notification, storage, token);
        }

        return delivered;
    }

    private async Task DeliverAsync(Guid tenantId, AlertNotification notification,
        EntityStorageLocation storage, CancellationToken token)
    {
        using var activity = AnalyticsTelemetry.Source.StartActivity("alert.notify");
        activity?.SetTag("tenant.id", tenantId);
        activity?.SetTag("alert.id", notification.AlertId);
        activity?.SetTag("notification.channel", notification.Channel);
        activity?.SetTag("notification.attempt", notification.Attempts + 1);

        var attempts = notification.Attempts + 1;
        var attempted = notification with { Attempts = attempts };

        var payload = AlertNotificationPayloads.Deserialize(notification.PayloadJson);
        if (payload is null)
        {
            await FailAsync(tenantId, attempted, "The notification payload is missing or unreadable.",
                storage, activity);
            return;
        }

        if (!_senders.TryGetValue(notification.Channel, out var sender))
        {
            await FailAsync(tenantId, attempted, $"No delivery channel is configured for '{notification.Channel}'.",
                storage, activity);
            return;
        }

        try
        {
            await sender.SendAsync(tenantId, attempted, payload, token);
            activity?.SetTag("notification.status", nameof(NotificationStatus.Delivered));
            var now = timeProvider.GetUtcNow();
            // LeaseOwner stays set: the store fences the write on it and clears the lease itself.
            await notifications.CompleteAsync(tenantId, attempted with
            {
                Status = NotificationStatus.Delivered, LastError = null, DeliveredAt = now, NextAttemptAt = null
            }, storage, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Leave the row leased; the lease expires and another pass retries it.
            throw;
        }
        catch (Exception exception)
        {
            var permanent = exception is AlertDeliveryException { IsPermanent: true };
            var delay = permanent ? null : AlertDeliveryRetryPolicy.NextDelay(attempts);
            if (delay is null)
            {
                await FailAsync(tenantId, attempted, exception.Message, storage, activity);
                return;
            }

            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag("notification.status", nameof(NotificationStatus.Pending));
            await notifications.CompleteAsync(tenantId, attempted with
            {
                Status = NotificationStatus.Pending, LastError = Truncate(exception.Message),
                NextAttemptAt = timeProvider.GetUtcNow() + delay.Value
            }, storage, CancellationToken.None);
        }
    }

    private Task FailAsync(Guid tenantId, AlertNotification notification, string error,
        EntityStorageLocation storage, Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Error, error);
        activity?.SetTag("notification.status", nameof(NotificationStatus.Failed));
        return notifications.CompleteAsync(tenantId, notification with
        {
            Status = NotificationStatus.Failed, LastError = Truncate(error), NextAttemptAt = null
        }, storage, CancellationToken.None);
    }

    private static string Truncate(string value) =>
        value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
}
