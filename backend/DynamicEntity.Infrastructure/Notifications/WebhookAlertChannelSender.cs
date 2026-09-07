using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DynamicEntity.Application.Alerts;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Infrastructure.Notifications;

/// <summary>
/// Posts alert notifications to the HTTPS endpoints configured on the alert's webhook action. Every request
/// carries the notification id as an idempotency key, because a retry re-posts to endpoints that already
/// accepted the payload.
/// </summary>
public sealed class WebhookAlertChannelSender(HttpClient client, WebhookAlertOptions options) : IAlertChannelSender
{
    public const string SignatureHeader = "X-DynamicEntity-Signature";
    public const string IdempotencyHeader = "X-DynamicEntity-Idempotency-Key";

    public string Channel => AlertChannels.Webhook;

    /// <summary>
    /// Builds a handler that resolves the destination itself, refuses non-public addresses and never follows
    /// redirects, so a redirect cannot walk the request into the internal network.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(WebhookAlertOptions options) => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(Math.Max(1, Math.Min(options.TimeoutSeconds, 10))),
        ConnectCallback = async (context, token) =>
        {
            var host = context.DnsEndPoint.Host;
            IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, token);
            if (addresses.Length == 0)
                throw new WebhookDestinationException($"Webhook host '{host}' did not resolve to any address.");
            if (!options.AllowPrivateNetworks)
                foreach (var address in addresses) PublicNetworkGuard.EnsureAllowed(address, host);

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                // Connect only to the addresses just validated so a rebind cannot substitute another target.
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };

    public async Task SendAsync(Guid tenantId, AlertNotification notification, AlertNotificationPayload payload,
        CancellationToken token)
    {
        if (payload.Targets.Count == 0)
            throw new AlertDeliveryException("The webhook action has no endpoints.", true);

        var body = BuildBody(tenantId, notification, payload);
        var signature = Sign(body);

        foreach (var target in payload.Targets)
        {
            var endpoint = ParseEndpoint(target);
            await PostAsync(endpoint, body, signature, notification, token);
        }
    }

    private async Task PostAsync(Uri endpoint, byte[] body, string? signature, AlertNotification notification,
        CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } }
        };
        request.Headers.TryAddWithoutValidation(IdempotencyHeader, notification.Id.ToString());
        if (signature is not null) request.Headers.TryAddWithoutValidation(SignatureHeader, signature);

        HttpResponseMessage response;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new AlertDeliveryException($"Webhook '{Describe(endpoint)}' timed out after {options.TimeoutSeconds}s.", false);
        }
        catch (HttpRequestException exception)
        {
            var destination = Unwrap(exception);
            throw new AlertDeliveryException(
                $"Webhook '{Describe(endpoint)}' could not be reached: {destination?.Message ?? exception.Message}",
                destination is not null, exception);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            var status = (int)response.StatusCode;
            var retryable = status >= 500 || status is 408 or 429;
            throw new AlertDeliveryException(
                $"Webhook '{Describe(endpoint)}' returned HTTP {status}.", !retryable);
        }
    }

    private static WebhookDestinationException? Unwrap(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is WebhookDestinationException destination) return destination;
        return null;
    }

    private static Uri ParseEndpoint(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new AlertDeliveryException(
                "Webhook endpoints must be absolute HTTPS URLs without embedded credentials.", true);
        return endpoint;
    }

    /// <summary>Keeps query strings, which may carry endpoint tokens, out of error messages and logs.</summary>
    private static string Describe(Uri endpoint) => $"{endpoint.Host}{endpoint.AbsolutePath}";

    private string? Sign(byte[] body)
    {
        if (string.IsNullOrEmpty(options.SigningSecret)) return null;
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.SigningSecret), body);
        return $"sha256={Convert.ToHexStringLower(signature)}";
    }

    private static byte[] BuildBody(Guid tenantId, AlertNotification notification, AlertNotificationPayload payload)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "alert");
            writer.WriteString("tenantId", tenantId);
            writer.WriteString("notificationId", notification.Id);
            writer.WriteString("evaluationId", notification.EvaluationId);
            writer.WriteNumber("attempt", notification.Attempts);
            writer.WriteString("alertId", payload.AlertId);
            writer.WriteString("entityId", payload.EntityId);
            writer.WriteString("metricId", payload.MetricId);
            writer.WriteString("alertName", payload.AlertName);
            writer.WriteString("state", payload.State.ToString());
            writer.WriteString("comparisonOperator", payload.ComparisonOperator.ToString());
            writer.WritePropertyName("threshold");
            writer.WriteRawValue(payload.ThresholdJson);
            writer.WritePropertyName("value");
            if (payload.ValueJson is null) writer.WriteNullValue(); else writer.WriteRawValue(payload.ValueJson);
            writer.WriteString("evaluatedAt", payload.EvaluatedAt);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
