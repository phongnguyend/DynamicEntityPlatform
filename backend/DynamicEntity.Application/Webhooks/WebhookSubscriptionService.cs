using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Storage;

namespace DynamicEntity.Application.Webhooks;

public sealed class WebhookSubscriptionService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore entities,
    IWebhookSubscriptionStore subscriptions,
    TimeProvider timeProvider)
{
    public async Task<WebhookSubscription> CreateAsync(Guid tenantId, Guid entityId, string? name,
        Uri? endpoint, IReadOnlyList<WebhookEvent>? events, bool isEnabled, Guid? userId, CancellationToken token)
    {
        Validate(name, endpoint, events);
        var storage = await RequireEntityAsync(tenantId, entityId, token);
        var now = timeProvider.GetUtcNow();
        var subscription = new WebhookSubscription(Guid.NewGuid(), entityId, name!.Trim(), endpoint!,
            NormalizeEvents(events!), isEnabled, userId, now, now);
        return await subscriptions.CreateAsync(tenantId, subscription, storage, token);
    }

    public async Task<IReadOnlyList<WebhookSubscription>> ListAsync(Guid tenantId, Guid entityId, CancellationToken token)
    {
        var storage = await RequireEntityAsync(tenantId, entityId, token);
        return await subscriptions.ListAsync(tenantId, entityId, storage, token);
    }

    public async Task<WebhookSubscription> UpdateAsync(Guid tenantId, Guid entityId, Guid id, string? name,
        Uri? endpoint, IReadOnlyList<WebhookEvent>? events, bool isEnabled, CancellationToken token)
    {
        Validate(name, endpoint, events);
        var storage = await RequireEntityAsync(tenantId, entityId, token);
        var current = await subscriptions.GetAsync(tenantId, entityId, id, storage, token)
            ?? throw new NotFoundException($"Webhook subscription '{id}' was not found.");
        var updated = current with
        {
            Name = name!.Trim(), Endpoint = endpoint!, Events = NormalizeEvents(events!),
            IsEnabled = isEnabled, UpdatedAt = timeProvider.GetUtcNow()
        };
        return await subscriptions.UpdateAsync(tenantId, updated, storage, token)
            ?? throw new NotFoundException($"Webhook subscription '{id}' was not found.");
    }

    public async Task DeleteAsync(Guid tenantId, Guid entityId, Guid id, CancellationToken token)
    {
        var storage = await RequireEntityAsync(tenantId, entityId, token);
        if (!await subscriptions.DeleteAsync(tenantId, entityId, id, storage, token))
            throw new NotFoundException($"Webhook subscription '{id}' was not found.");
    }

    private async Task<EntityStorageLocation> RequireEntityAsync(Guid tenantId, Guid entityId, CancellationToken token)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, token);
        _ = await entities.GetAsync(tenantId, entityId, storage, token)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found.");
        return storage;
    }

    internal static void Validate(string? name, Uri? endpoint, IReadOnlyList<WebhookEvent>? events)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ValidationException("Webhook name must contain between 1 and 200 characters.");
        if (endpoint is null || !endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new ValidationException("Webhook endpoint must be an absolute HTTPS URL without embedded credentials.");
        if (endpoint.AbsoluteUri.Length > 2048)
            throw new ValidationException("Webhook endpoint must not exceed 2048 characters.");
        if (events is null || events.Count == 0 || events.Any(value => !Enum.IsDefined(value)))
            throw new ValidationException("Select at least one valid webhook event.");
    }

    private static IReadOnlyList<WebhookEvent> NormalizeEvents(IReadOnlyList<WebhookEvent> events) =>
        events.Distinct().Order().ToArray();
}
