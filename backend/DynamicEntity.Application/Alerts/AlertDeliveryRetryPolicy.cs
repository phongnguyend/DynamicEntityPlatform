namespace DynamicEntity.Application.Alerts;

/// <summary>Bounded exponential backoff for external alert deliveries.</summary>
public static class AlertDeliveryRetryPolicy
{
    public const int MaxAttempts = 5;

    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Returns the delay before the next attempt, or <c>null</c> when the attempt budget is exhausted
    /// and the notification must be marked failed.
    /// </summary>
    public static TimeSpan? NextDelay(int completedAttempts)
    {
        if (completedAttempts < 1) throw new ArgumentOutOfRangeException(nameof(completedAttempts));
        if (completedAttempts >= MaxAttempts) return null;

        var seconds = FirstDelay.TotalSeconds * Math.Pow(2, completedAttempts - 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxDelay.TotalSeconds));
    }
}
