using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Contracts.Webhooks;

public sealed record SaveWebhookSubscriptionRequest(string? Name, Uri? Endpoint, IReadOnlyList<WebhookEvent>? Events, bool IsEnabled);
public sealed record WebhookSubscriptionResponse(Guid Id, Guid EntityId, string Name, Uri Endpoint,
    IReadOnlyList<WebhookEvent> Events, bool IsEnabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
