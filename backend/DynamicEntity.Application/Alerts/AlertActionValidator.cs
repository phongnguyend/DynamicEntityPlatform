using System.Net.Mail;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Application.Alerts;

public static class AlertActionValidator
{
    public static IReadOnlyList<AlertActionConfiguration> Normalize(IReadOnlyList<AlertActionConfiguration>? actions)
    {
        if (actions is null || actions.Count == 0) return [];
        if (actions.Count > 2 || actions.GroupBy(action => action.Type).Any(group => group.Count() > 1))
            throw new ValidationException("Configure at most one email action and one webhook action.");

        return actions.Select(Normalize).OrderBy(action => action.Type).ToArray();
    }

    private static AlertActionConfiguration Normalize(AlertActionConfiguration action)
    {
        if (!Enum.IsDefined(action.Type)) throw new ValidationException("Alert action type is invalid.");
        if (action.Type == AlertActionType.Email)
        {
            var recipients = action.EmailRecipients?.Select(value => value?.Trim()).Where(value => !string.IsNullOrEmpty(value))
                .Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
            if (recipients.Length is < 1 or > 50 || recipients.Any(value => value.Length > 320 || !MailAddress.TryCreate(value, out _)))
                throw new ValidationException("Email actions require between 1 and 50 valid recipient addresses.");
            if (action.WebhookUrls is { Count: > 0 }) throw new ValidationException("Email actions cannot contain webhook URLs.");
            return action with { EmailRecipients = recipients, WebhookUrls = null };
        }

        var configuredEndpoints = action.WebhookUrls;
        if (configuredEndpoints is null || configuredEndpoints.Count is < 1 or > 20 || configuredEndpoints.Any(endpoint => endpoint is null || !endpoint.IsAbsoluteUri ||
            endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo) || endpoint.AbsoluteUri.Length > 2048))
            throw new ValidationException("Webhook actions require between 1 and 20 unique absolute HTTPS URLs without embedded credentials.");
        var endpoints = configuredEndpoints.DistinctBy(endpoint => endpoint.AbsoluteUri, StringComparer.OrdinalIgnoreCase).ToArray();
        if (action.EmailRecipients is { Count: > 0 }) throw new ValidationException("Webhook actions cannot contain email recipients.");
        return action with { EmailRecipients = null, WebhookUrls = endpoints };
    }
}
