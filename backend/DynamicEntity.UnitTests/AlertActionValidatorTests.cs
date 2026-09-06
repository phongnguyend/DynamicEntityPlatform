using DynamicEntity.Application.Alerts;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Storage;

namespace DynamicEntity.UnitTests;

public sealed class AlertActionValidatorTests
{
    [Fact]
    public void Normalize_AcceptsAndNormalizesEmailAndWebhookActions()
    {
        var actions = AlertActionValidator.Normalize([
            new AlertActionConfiguration(AlertActionType.Webhook, null,
                [new Uri("https://example.test/alerts"), new Uri("https://backup.example.test/alerts")]),
            new AlertActionConfiguration(AlertActionType.Email, [" OPS@example.com ", "ops@example.com", "owner@example.com"], null)
        ]);

        Assert.Equal([AlertActionType.Email, AlertActionType.Webhook], actions.Select(action => action.Type));
        Assert.Equal(["OPS@example.com", "owner@example.com"], actions[0].EmailRecipients);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public void Normalize_RejectsInvalidEmailRecipients(string recipient) =>
        Assert.Throws<ValidationException>(() => AlertActionValidator.Normalize([
            new AlertActionConfiguration(AlertActionType.Email, [recipient], null)]));

    [Theory]
    [InlineData("http://example.test/alerts")]
    [InlineData("https://user:secret@example.test/alerts")]
    public void Normalize_RejectsUnsafeWebhookUrls(string url) =>
        Assert.Throws<ValidationException>(() => AlertActionValidator.Normalize([
            new AlertActionConfiguration(AlertActionType.Webhook, null, [new Uri(url)])]));

    [Fact]
    public void Normalize_RejectsDuplicateActionTypes() =>
        Assert.Throws<ValidationException>(() => AlertActionValidator.Normalize([
            new AlertActionConfiguration(AlertActionType.Email, ["one@example.com"], null),
            new AlertActionConfiguration(AlertActionType.Email, ["two@example.com"], null)]));

    [Fact]
    public async Task InAppNotifier_DoesNotProcessConfiguredExternalActions()
    {
        var store = new CapturingNotificationStore();
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var alert = new AlertDefinition(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Alert",
            AlertComparisonOperator.GreaterThan, "1", AlertInterval.Hourly, "UTC", TimeSpan.Zero, true, true,
            [new AlertActionConfiguration(AlertActionType.Email, ["ops@example.com"], null),
             new AlertActionConfiguration(AlertActionType.Webhook, null,
                [new Uri("https://example.test/alerts"), new Uri("https://backup.example.test/alerts")])],
            null, null, now, null, null, null, now, now);
        var evaluation = new AlertEvaluation(Guid.NewGuid(), alert.Id, "2", "1", AlertState.Firing, null, now);

        await new InAppAlertNotifier(store).NotifyAsync(Guid.NewGuid(), alert, evaluation,
            new EntityStorageLocation("tenant", "", EntityStorageMode.DedicatedTable, false), CancellationToken.None);

        Assert.Single(store.Notifications);
        Assert.Equal("InApp", store.Notifications[0].Channel);
    }

    [Fact]
    public void Normalize_DeduplicatesWebhookUrls()
    {
        var actions = AlertActionValidator.Normalize([new AlertActionConfiguration(AlertActionType.Webhook, null,
            [new Uri("https://example.test/alerts"), new Uri("https://EXAMPLE.test/alerts")])]);

        Assert.Single(actions[0].WebhookUrls!);
    }

    private sealed class CapturingNotificationStore : IAlertNotificationStore
    {
        public List<AlertNotification> Notifications { get; } = [];
        public Task CreateAsync(Guid tenantId, AlertNotification notification, EntityStorageLocation storage, CancellationToken token)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<AlertNotification>> ListAsync(Guid tenantId, Guid alertId, EntityStorageLocation storage, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<AlertNotification>>(Notifications);
    }
}
