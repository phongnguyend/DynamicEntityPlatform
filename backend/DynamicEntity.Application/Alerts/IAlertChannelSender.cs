using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Application.Alerts;

public static class AlertChannels
{
    public const string InApp = "InApp";
    public const string Email = "Email";
    public const string Webhook = "Webhook";

    public static string For(AlertActionType type) => type switch
    {
        AlertActionType.Email => Email,
        AlertActionType.Webhook => Webhook,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}

/// <summary>
/// Delivers an alert notification over a single external channel. Implementations throw on failure so the
/// delivery worker can record the attempt and schedule a retry.
/// </summary>
public interface IAlertChannelSender
{
    string Channel { get; }

    Task SendAsync(Guid tenantId, AlertNotification notification, AlertNotificationPayload payload,
        CancellationToken token);
}

/// <summary>Thrown when a channel rejects a delivery in a way that retrying cannot fix.</summary>
public sealed class AlertDeliveryException(string message, bool isPermanent, Exception? innerException = null)
    : Exception(message, innerException)
{
    public bool IsPermanent { get; } = isPermanent;
}
