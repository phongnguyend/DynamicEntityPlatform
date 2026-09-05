using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Application.Alerts;

public static class AlertNotificationPolicy
{
    public static bool ShouldNotify(AlertState? previousState, bool isFiring, bool notifyOnRecovery,
        TimeSpan cooldown, DateTimeOffset? lastNotificationAt, DateTimeOffset now)
    {
        if (!isFiring) return previousState == AlertState.Firing && notifyOnRecovery;
        if (previousState != AlertState.Firing) return true;
        return lastNotificationAt is null || lastNotificationAt.Value + cooldown <= now;
    }
}
