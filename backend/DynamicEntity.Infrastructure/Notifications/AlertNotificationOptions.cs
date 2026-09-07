namespace DynamicEntity.Infrastructure.Notifications;

/// <summary>
/// SMTP transport settings for the email alert channel. Credentials belong in a secret store or
/// environment variables, never in an alert definition or a checked-in settings file.
/// </summary>
public sealed class EmailAlertOptions
{
    public const string SectionName = "AlertNotifications:Email";

    public string? Host { get; init; }
    public int Port { get; init; } = 587;
    public bool EnableSsl { get; init; } = true;
    public string? UserName { get; init; }
    public string? Password { get; init; }
    public string? FromAddress { get; init; }
    public string? FromDisplayName { get; init; }
    public int TimeoutSeconds { get; init; } = 30;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>Delivery settings for the webhook alert channel.</summary>
public sealed class WebhookAlertOptions
{
    public const string SectionName = "AlertNotifications:Webhook";

    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>Shared secret used to sign payloads so receivers can verify origin. Optional but recommended.</summary>
    public string? SigningSecret { get; init; }

    /// <summary>Only enable for trusted internal endpoints; it disables the SSRF address filter.</summary>
    public bool AllowPrivateNetworks { get; init; }
}
