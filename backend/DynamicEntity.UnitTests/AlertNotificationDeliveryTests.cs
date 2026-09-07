using System.Net;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Alerts;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using DynamicEntity.Infrastructure.Notifications;

namespace DynamicEntity.UnitTests;

public sealed class AlertNotificationDeliveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task QueuedNotifier_RecordsInAppDeliveryAndQueuesConfiguredActions()
    {
        var store = new FakeNotificationStore();
        var (alert, evaluation) = Fixture();

        await new QueuedAlertNotifier(store).NotifyAsync(TenantId, alert, evaluation, Storage, CancellationToken.None);

        Assert.Equal([AlertChannels.InApp, AlertChannels.Email, AlertChannels.Webhook],
            store.Notifications.Select(notification => notification.Channel));
        Assert.Equal(NotificationStatus.Delivered, store.Notifications[0].Status);
        Assert.All(store.Notifications.Skip(1), notification =>
        {
            Assert.Equal(NotificationStatus.Pending, notification.Status);
            Assert.Equal(0, notification.Attempts);
            Assert.Equal(Now, notification.NextAttemptAt);
        });

        var email = AlertNotificationPayloads.Deserialize(store.Notifications[1].PayloadJson)!;
        var webhook = AlertNotificationPayloads.Deserialize(store.Notifications[2].PayloadJson)!;
        Assert.Equal(["ops@example.com"], email.Targets);
        Assert.Equal(["https://example.test/alerts", "https://backup.example.test/alerts"], webhook.Targets);
        Assert.Equal(AlertState.Firing, webhook.State);
        Assert.Equal("12", webhook.ValueJson);
    }

    [Fact]
    public async Task QueuedNotifier_SkipsActionsWithoutTargets()
    {
        var store = new FakeNotificationStore();
        var (alert, evaluation) = Fixture();
        alert = alert with { Actions = [new AlertActionConfiguration(AlertActionType.Email, [], null)] };

        await new QueuedAlertNotifier(store).NotifyAsync(TenantId, alert, evaluation, Storage, CancellationToken.None);

        Assert.Equal([AlertChannels.InApp], store.Notifications.Select(notification => notification.Channel));
    }

    [Fact]
    public async Task Worker_MarksDeliveredAndLeavesNothingClaimable()
    {
        var store = new FakeNotificationStore();
        await QueueAsync(store);
        var sender = new FakeSender(AlertChannels.Email);
        var worker = Worker(store, sender, new FakeSender(AlertChannels.Webhook));

        var processed = await worker.ProcessTenantAsync(TenantId, "owner", CancellationToken.None);

        Assert.Equal(2, processed);
        Assert.Single(sender.Sent);
        Assert.All(store.Notifications, notification =>
        {
            Assert.Equal(NotificationStatus.Delivered, notification.Status);
            Assert.Equal(Now, notification.DeliveredAt);
            Assert.Null(notification.LeaseOwner);
            Assert.Null(notification.NextAttemptAt);
        });
        Assert.Equal(1, store.Notifications[1].Attempts);
    }

    [Fact]
    public async Task Worker_RetriesTransientFailuresWithBoundedBackoffThenFails()
    {
        var store = new FakeNotificationStore();
        await QueueAsync(store, AlertActionType.Email);
        var time = new StubTimeProvider(Now);
        var worker = Worker(store, time, new ThrowingSender(AlertChannels.Email, new HttpRequestException("smtp down")));

        var delays = new List<TimeSpan>();
        for (var pass = 1; pass <= AlertDeliveryRetryPolicy.MaxAttempts; pass++)
        {
            Assert.Equal(1, await worker.ProcessTenantAsync(TenantId, "owner", CancellationToken.None));
            var notification = store.Notifications.Single(item => item.Channel == AlertChannels.Email);
            Assert.Equal(pass, notification.Attempts);
            Assert.Contains("smtp down", notification.LastError);

            if (pass < AlertDeliveryRetryPolicy.MaxAttempts)
            {
                Assert.Equal(NotificationStatus.Pending, notification.Status);
                delays.Add(notification.NextAttemptAt!.Value - time.GetUtcNow());
                time.Advance(notification.NextAttemptAt.Value - time.GetUtcNow());
            }
            else
            {
                Assert.Equal(NotificationStatus.Failed, notification.Status);
                Assert.Null(notification.NextAttemptAt);
            }
        }

        Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4)], delays);
        Assert.Equal(0, await worker.ProcessTenantAsync(TenantId, "owner", CancellationToken.None));
    }

    [Fact]
    public async Task Worker_FailsPermanentRejectionsOnTheFirstAttempt()
    {
        var store = new FakeNotificationStore();
        await QueueAsync(store, AlertActionType.Webhook);
        var worker = Worker(store, new ThrowingSender(AlertChannels.Webhook,
            new AlertDeliveryException("Webhook 'example.test/alerts' returned HTTP 403.", true)));

        await worker.ProcessTenantAsync(TenantId, "owner", CancellationToken.None);

        var notification = store.Notifications.Single(item => item.Channel == AlertChannels.Webhook);
        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(1, notification.Attempts);
        Assert.Contains("HTTP 403", notification.LastError);
    }

    [Fact]
    public async Task Worker_FailsChannelsThatHaveNoRegisteredSender()
    {
        var store = new FakeNotificationStore();
        await QueueAsync(store, AlertActionType.Email);
        var worker = Worker(store, new FakeSender(AlertChannels.Webhook));

        await worker.ProcessTenantAsync(TenantId, "owner", CancellationToken.None);

        var notification = store.Notifications.Single(item => item.Channel == AlertChannels.Email);
        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Contains("No delivery channel is configured for 'Email'", notification.LastError);
    }

    [Fact]
    public async Task Worker_NeverClaimsInAppOrLeasedNotifications()
    {
        var store = new FakeNotificationStore();
        await QueueAsync(store, AlertActionType.Email);
        var claimed = await store.ClaimPendingAsync(TenantId, "other-worker", Now, Now.AddMinutes(2), Storage, CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(AlertChannels.Email, claimed.Channel);
        Assert.Null(await store.ClaimPendingAsync(TenantId, "owner", Now, Now.AddMinutes(2), Storage, CancellationToken.None));
        Assert.Equal(0, await Worker(store, new FakeSender(AlertChannels.Email))
            .ProcessTenantAsync(TenantId, "owner", CancellationToken.None));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.10", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("93.184.216.34", false)]
    [InlineData("2606:2800:220:1::1", false)]
    public void PublicNetworkGuard_BlocksNonPublicDestinations(string address, bool blocked) =>
        Assert.Equal(blocked, PublicNetworkGuard.IsBlocked(IPAddress.Parse(address)));

    [Fact]
    public void MessageFormatter_StatesTheConditionWithoutRecordData()
    {
        var (alert, evaluation) = Fixture();
        var payload = AlertNotificationPayloads.Create(alert, evaluation, ["ops@example.com"]);

        Assert.Equal("[FIRING] Orders above threshold", AlertMessageFormatter.Subject(payload));
        Assert.Contains("Condition: value > 10", AlertMessageFormatter.Body(payload));
        Assert.Contains("Observed value: 12", AlertMessageFormatter.Body(payload));
    }

    [Fact]
    public void RetryPolicy_CapsTheDelayAndTheAttemptBudget()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), AlertDeliveryRetryPolicy.NextDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(4), AlertDeliveryRetryPolicy.NextDelay(4));
        Assert.Null(AlertDeliveryRetryPolicy.NextDelay(AlertDeliveryRetryPolicy.MaxAttempts));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlertDeliveryRetryPolicy.NextDelay(0));
    }

    private static EntityStorageLocation Storage =>
        new("Tenant", string.Empty, EntityStorageMode.DedicatedTable, false, "connection");

    private static (AlertDefinition Alert, AlertEvaluation Evaluation) Fixture()
    {
        var alert = new AlertDefinition(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Orders above threshold",
            AlertComparisonOperator.GreaterThan, "10", AlertInterval.Hourly, "UTC", TimeSpan.Zero, true, true,
            [new AlertActionConfiguration(AlertActionType.Email, ["ops@example.com"], null),
             new AlertActionConfiguration(AlertActionType.Webhook, null,
                [new Uri("https://example.test/alerts"), new Uri("https://backup.example.test/alerts")])],
            null, null, Now, null, null, null, Now, Now);
        return (alert, new AlertEvaluation(Guid.NewGuid(), alert.Id, "12", "10", AlertState.Firing, null, Now));
    }

    private static async Task QueueAsync(FakeNotificationStore store, AlertActionType? only = null)
    {
        var (alert, evaluation) = Fixture();
        if (only is not null) alert = alert with { Actions = alert.Actions.Where(a => a.Type == only).ToArray() };
        await new QueuedAlertNotifier(store).NotifyAsync(TenantId, alert, evaluation, Storage, CancellationToken.None);
    }

    private static AlertNotificationDeliveryWorker Worker(FakeNotificationStore store, params IAlertChannelSender[] senders) =>
        Worker(store, new StubTimeProvider(Now), senders);

    private static AlertNotificationDeliveryWorker Worker(FakeNotificationStore store, TimeProvider time,
        params IAlertChannelSender[] senders) =>
        new(new SingleTenantControlPlaneStore(TenantId), store, senders, time);

    private sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan delta) => current += delta;
    }

    private sealed class FakeSender(string channel) : IAlertChannelSender
    {
        public string Channel => channel;
        public List<AlertNotificationPayload> Sent { get; } = [];
        public Task SendAsync(Guid tenantId, AlertNotification notification, AlertNotificationPayload payload, CancellationToken token)
        {
            Sent.Add(payload);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSender(string channel, Exception exception) : IAlertChannelSender
    {
        public string Channel => channel;
        public Task SendAsync(Guid tenantId, AlertNotification notification, AlertNotificationPayload payload, CancellationToken token) =>
            Task.FromException(exception);
    }

    /// <summary>Mirrors the leasing and fencing semantics of <c>SqlAlertNotificationStore</c>.</summary>
    private sealed class FakeNotificationStore : IAlertNotificationStore
    {
        public List<AlertNotification> Notifications { get; } = [];

        public Task CreateAsync(Guid tenantId, AlertNotification notification, EntityStorageLocation storage, CancellationToken token)
        {
            if (!Notifications.Any(item => item.EvaluationId == notification.EvaluationId && item.Channel == notification.Channel))
                Notifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AlertNotification>> ListAsync(Guid tenantId, Guid alertId, EntityStorageLocation storage, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<AlertNotification>>(Notifications.Where(item => item.AlertId == alertId).ToArray());

        public Task<AlertNotification?> ClaimPendingAsync(Guid tenantId, string owner, DateTimeOffset now,
            DateTimeOffset leaseExpiresAt, EntityStorageLocation storage, CancellationToken token)
        {
            var index = Notifications.FindIndex(item => item.Status == NotificationStatus.Pending
                && item.Channel != AlertChannels.InApp
                && (item.NextAttemptAt is null || item.NextAttemptAt <= now)
                && (item.LeaseExpiresAt is null || item.LeaseExpiresAt <= now));
            if (index < 0) return Task.FromResult<AlertNotification?>(null);

            Notifications[index] = Notifications[index] with { LeaseOwner = owner, LeaseExpiresAt = leaseExpiresAt };
            return Task.FromResult<AlertNotification?>(Notifications[index]);
        }

        public Task CompleteAsync(Guid tenantId, AlertNotification notification, EntityStorageLocation storage, CancellationToken token)
        {
            var index = Notifications.FindIndex(item => item.Id == notification.Id && item.LeaseOwner == notification.LeaseOwner);
            if (index >= 0)
                Notifications[index] = notification with { LeaseOwner = null, LeaseExpiresAt = null };
            return Task.CompletedTask;
        }
    }

    private sealed class SingleTenantControlPlaneStore(Guid tenantId) : IControlPlaneStore
    {
        private readonly Tenant tenant = new(tenantId, "Tenant", TenantStatus.Active, Now, Now);

        public Task<EntityStorageLocation?> GetTenantStorageAsync(Guid id, CancellationToken token) =>
            Task.FromResult<EntityStorageLocation?>(id == tenant.Id ? Storage : null);
        public Task<Tenant?> GetTenantAsync(Guid id, CancellationToken token) =>
            Task.FromResult<Tenant?>(id == tenant.Id ? tenant : null);
        public Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<Tenant>>([tenant]);
        public Task CreateTenantAsync(Tenant value, CancellationToken token) => Task.CompletedTask;
        public Task UpdateTenantNameAsync(Guid id, string name, CancellationToken token) => Task.CompletedTask;
        public Task SetTenantStatusAsync(Guid id, TenantStatus status, CancellationToken token) => Task.CompletedTask;
        public Task SaveTenantStorageAsync(Guid id, EntityStorageLocation location, CancellationToken token) => Task.CompletedTask;
    }
}
