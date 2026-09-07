using System.Net;
using System.Net.Mail;
using DynamicEntity.Application.Alerts;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Infrastructure.Notifications;

/// <summary>Sends alert notifications over SMTP to the recipients configured on the alert's email action.</summary>
public sealed class EmailAlertChannelSender(EmailAlertOptions options) : IAlertChannelSender
{
    private static readonly SmtpStatusCode[] TransientStatusCodes =
    [
        SmtpStatusCode.ServiceNotAvailable, SmtpStatusCode.MailboxBusy, SmtpStatusCode.LocalErrorInProcessing,
        SmtpStatusCode.InsufficientStorage, SmtpStatusCode.ClientNotPermitted, SmtpStatusCode.TransactionFailed,
        SmtpStatusCode.GeneralFailure
    ];

    public string Channel => AlertChannels.Email;

    public async Task SendAsync(Guid tenantId, AlertNotification notification, AlertNotificationPayload payload,
        CancellationToken token)
    {
        if (!options.IsConfigured)
            throw new AlertDeliveryException("The email channel has no SMTP host or sender address configured.", true);
        if (payload.Targets.Count == 0)
            throw new AlertDeliveryException("The email action has no recipients.", true);

        using var message = BuildMessage(payload, notification);
        using var client = CreateClient();

        try
        {
            await client.SendMailAsync(message, token);
        }
        catch (SmtpException exception)
        {
            throw new AlertDeliveryException($"SMTP delivery failed ({exception.StatusCode}): {exception.Message}",
                !TransientStatusCodes.Contains(exception.StatusCode), exception);
        }
    }

    private MailMessage BuildMessage(AlertNotificationPayload payload, AlertNotification notification)
    {
        MailAddress from;
        try
        {
            from = new MailAddress(options.FromAddress!, options.FromDisplayName ?? "Dynamic Entity Alerts");
        }
        catch (FormatException exception)
        {
            throw new AlertDeliveryException($"The configured sender address is invalid: {exception.Message}", true, exception);
        }

        var message = new MailMessage
        {
            From = from,
            Subject = AlertMessageFormatter.Subject(payload),
            Body = AlertMessageFormatter.Body(payload),
            IsBodyHtml = false
        };
        // Lets a receiving mailbox collapse retries of the same evaluation into one thread.
        message.Headers.Add("X-DynamicEntity-Idempotency-Key", notification.Id.ToString());

        foreach (var recipient in payload.Targets)
        {
            if (!MailAddress.TryCreate(recipient, out var address))
                throw new AlertDeliveryException($"Recipient '{recipient}' is not a valid email address.", true);
            message.To.Add(address);
        }

        return message;
    }

    private SmtpClient CreateClient()
    {
        var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            Timeout = (int)TimeSpan.FromSeconds(options.TimeoutSeconds).TotalMilliseconds,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrEmpty(options.UserName))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(options.UserName, options.Password);
        }

        return client;
    }
}
