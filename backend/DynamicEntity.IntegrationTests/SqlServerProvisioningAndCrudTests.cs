using System.Text.Json;
using DynamicEntity.Application.Common;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Entities;
using DynamicEntity.Application.Fields;
using DynamicEntity.Application.Records;
using DynamicEntity.Application.Storage;
using DynamicEntity.Application.Tenants;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Queries;
using DynamicEntity.SqlServer;
using DynamicEntity.SqlServer.Analytics;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.IntegrationTests;

public sealed class SqlServerProvisioningAndCrudTests
{
    [Fact]
    public async Task ProvisionsTenantEntityAndExercisesJsonCrudWithConcurrency()
    {
        var serverConnection = Environment.GetEnvironmentVariable("DYNAMIC_ENTITY_TEST_SQL");
        if (string.IsNullOrWhiteSpace(serverConnection))
        {
            return;
        }

        var suffix = Guid.NewGuid();
        var controlName = $"DynamicEntityControl_Test_{suffix:N}";
        var tenantDatabaseName = $"DynamicEntityTenant_Test_{suffix:N}";
        var controlBuilder = new SqlConnectionStringBuilder(serverConnection) { InitialCatalog = controlName };
        var tenantBuilder = new SqlConnectionStringBuilder(serverConnection) { InitialCatalog = tenantDatabaseName };
        var options = new SqlServerOptions
        {
            ControlDatabaseConnectionString = controlBuilder.ConnectionString
        };
        try
        {
            await CreateDatabaseAsync(serverConnection, tenantDatabaseName);
            var control = new SqlServerControlPlaneStore(options);
            var provisioner = new SqlServerTenantDatabaseProvisioner();
            var tenant = await new TenantService(control, control, provisioner, TimeProvider.System)
                .CreateAsync("Integration tenant", tenantBuilder.ConnectionString, CancellationToken.None);
            await AssertAnalyticsMigrationsAsync(tenant.Id, tenantBuilder.ConnectionString);
            var metadata = new SqlServerEntityMetadataStore();
            var entity = await new EntityService(control, metadata, TimeProvider.System)
                .CreateAsync(tenant.Id, "customer", "Customer", null, CancellationToken.None);
            var fieldStore = new SqlServerFieldMetadataStore();
            var fieldService = new FieldService(control, metadata, fieldStore, TimeProvider.System);
            var field = await fieldService
                .CreateAsync(tenant.Id, entity.Id, "name", "Name", FieldDataType.Text, true, true,
                    true, true, true, true, null, "{\"maxLength\":200}", 0, CancellationToken.None);
            var amountField = await fieldService
                .CreateAsync(tenant.Id, entity.Id, "amount", "Amount", FieldDataType.Decimal, false, false,
                    true, true, false, false, null, null, 1, CancellationToken.None);
            var dateField = await fieldService
                .CreateAsync(tenant.Id, entity.Id, "occurred", "Occurred", FieldDataType.Date, false, false,
                    true, true, false, false, null, null, 2, CancellationToken.None);

            var resolver = new EntityStorageResolver(control, metadata);
            var indexService = new EntityIndexService(control, metadata, fieldStore,
                new SqlServerEntityIndexManager(resolver));
            var index = await indexService.CreateAsync(tenant.Id, entity.Id,
                [new EntityIndexColumnInput(field.Id, false)], CancellationToken.None);
            Assert.NotNull((await fieldService.ListAsync(tenant.Id, entity.Id, CancellationToken.None))
                .Single(item => item.Id == field.Id).IndexColumnName);
            await indexService.DeleteAsync(tenant.Id, entity.Id, index.Id, CancellationToken.None);
            Assert.Null((await fieldService.ListAsync(tenant.Id, entity.Id, CancellationToken.None))
                .Single(item => item.Id == field.Id).IndexColumnName);
            await indexService.CreateAsync(tenant.Id, entity.Id,
                [new EntityIndexColumnInput(amountField.Id, false)], CancellationToken.None);

            var records = new SqlServerRecordStore(resolver);
            var constraints = new SqlServerRecordConstraintValidator(resolver);
            var service = new RecordService(control, metadata, fieldStore,
                new RecordValidator(new RecordValidationOptions()), constraints, records);
            using var input = JsonDocument.Parse("{\"name\":\"Ada\",\"amount\":12.5,\"occurred\":\"2026-09-05\"}");
            var created = await service.CreateAsync(tenant.Id, entity.Id, input.RootElement, null, CancellationToken.None);
            var loaded = await service.GetAsync(tenant.Id, entity.Id, created.Id, CancellationToken.None);
            Assert.Contains(field.StorageKey, loaded.Data);
            var analyticsFields = await fieldService.ListAsync(tenant.Id, entity.Id, CancellationToken.None);
            Assert.NotNull(analyticsFields.Single(item => item.Id == amountField.Id).IndexColumnName);
            await ExerciseAnalyticsAsync(options, control, resolver, tenant.Id, entity with { Fields = analyticsFields });

            await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(tenant.Id, entity.Id,
                created.Id, input.RootElement, new byte[8], null, CancellationToken.None));
            await service.DeleteAsync(tenant.Id, entity.Id, created.Id, created.Version, CancellationToken.None);
            await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(tenant.Id, entity.Id, created.Id, CancellationToken.None));
        }
        finally
        {
            await DropDatabaseAsync(serverConnection, tenantDatabaseName);
            await DropDatabaseAsync(serverConnection, controlName);
        }
    }

    private static async Task ExerciseAnalyticsAsync(SqlServerOptions options, IControlPlaneStore control,
        IEntityStorageResolver resolver, Guid tenantId, EntityDefinition entity)
    {
        var storage = await control.GetTenantStorageAsync(tenantId, CancellationToken.None);
        Assert.NotNull(storage);
        var query = new AnalyticsQuery(null, [], [new AnalyticsMeasure(AggregateFunction.Count, null, "value")], [], 10);
        var analytics = await new SqlAnalyticsStore(options, resolver).ExecuteAsync(
            new DynamicEntity.Domain.Tenants.TenantContext(tenantId), entity, query, CancellationToken.None);
        Assert.Equal(1L, analytics.Rows.Single()["value"]);
        var amount = entity.Fields.Single(field => field.Name == "amount");
        var sumQuery = new AnalyticsQuery(null, [], [new AnalyticsMeasure(AggregateFunction.Sum, amount.Id, "total")], [], 10);
        var sum = await new SqlAnalyticsStore(options, resolver).ExecuteAsync(
            new DynamicEntity.Domain.Tenants.TenantContext(tenantId), entity, sumQuery, CancellationToken.None);
        Assert.Equal(12.5m, sum.Rows.Single()["total"]);
        var occurred = entity.Fields.Single(field => field.Name == "occurred");
        var groupedQuery = new AnalyticsQuery(null, [new AnalyticsDimension(occurred.Id, DateBucket.Month, "month")],
            [new AnalyticsMeasure(AggregateFunction.Count, null, "count")], [], 10);
        var grouped = await new SqlAnalyticsStore(options, resolver).ExecuteAsync(
            new DynamicEntity.Domain.Tenants.TenantContext(tenantId), entity, groupedQuery, CancellationToken.None);
        Assert.Single(grouped.Rows);

        var now = DateTimeOffset.UtcNow;
        var reportStore = new SqlReportStore();
        var report = await reportStore.CreateAsync(tenantId,
            new ReportDefinition(Guid.NewGuid(), entity.Id, "Count report", null, "{\"query\":{}}", null, now, now),
            storage, CancellationToken.None);
        Assert.Single(await reportStore.ListAsync(tenantId, entity.Id, storage, CancellationToken.None));
        Assert.NotNull(await reportStore.UpdateAsync(tenantId, report with { Name = "Updated report" }, storage, CancellationToken.None));

        var metricStore = new SqlMetricStore();
        var metric = await metricStore.CreateAsync(tenantId,
            new MetricDefinition(Guid.NewGuid(), entity.Id, "Count metric", null, AggregateFunction.Count,
                null, null, "{\"style\":\"integer\"}", null, now, now), storage, CancellationToken.None);
        Assert.NotNull(await metricStore.GetAsync(tenantId, entity.Id, metric.Id, storage, CancellationToken.None));

        var alertStore = new SqlAlertStore();
        var alert = await alertStore.CreateAsync(tenantId,
            new AlertDefinition(Guid.NewGuid(), entity.Id, metric.Id, "Count alert", AlertComparisonOperator.GreaterThan,
                "0", AlertInterval.FiveMinutes, "UTC", TimeSpan.Zero, true, true,
                [new AlertActionConfiguration(AlertActionType.Email, ["ops@example.com"], null),
                 new AlertActionConfiguration(AlertActionType.Webhook, null,
                    [new Uri("https://example.test/hooks/alerts"), new Uri("https://backup.example.test/hooks/alerts")])], null, null, now,
                null, null, null, now, now), storage, CancellationToken.None);
        var savedAlert = (await alertStore.GetAsync(tenantId, entity.Id, alert.Id, storage, CancellationToken.None))!;
        Assert.Equal(2, savedAlert.Actions.Count);
        Assert.Equal(2, savedAlert.Actions.Single(action => action.Type == AlertActionType.Webhook).WebhookUrls!.Count);
        var claimed = await alertStore.ClaimDueAsync(tenantId, "worker-1", now, now.AddMinutes(1), storage, CancellationToken.None);
        Assert.NotNull(claimed);
        Assert.Null(await alertStore.ClaimDueAsync(tenantId, "worker-2", now, now.AddMinutes(1), storage, CancellationToken.None));

        var evaluationStore = new SqlAlertEvaluationStore(options);
        var evaluation = new AlertEvaluation(Guid.NewGuid(), alert.Id, "1", "0", AlertState.Firing, null, now);
        await evaluationStore.CreateAsync(tenantId, evaluation, storage, CancellationToken.None);
        var notificationStore = new SqlAlertNotificationStore(options);
        await notificationStore.CreateAsync(tenantId, new AlertNotification(Guid.NewGuid(), alert.Id, evaluation.Id,
            "InApp", NotificationStatus.Delivered, 1, null, now, now), storage, CancellationToken.None);
        Assert.Single(await evaluationStore.ListAsync(tenantId, alert.Id, storage, CancellationToken.None));
        Assert.Single(await notificationStore.ListAsync(tenantId, alert.Id, storage, CancellationToken.None));
        await alertStore.CompleteAsync(tenantId, claimed! with { LastState = AlertState.Firing,
            LastEvaluatedAt = now, NextEvaluationAt = now.AddMinutes(5) }, storage, CancellationToken.None);
        Assert.True(await alertStore.DeleteAsync(tenantId, entity.Id, alert.Id, storage, CancellationToken.None));
        Assert.True(await metricStore.DeleteAsync(tenantId, entity.Id, metric.Id, storage, CancellationToken.None));
        Assert.True(await reportStore.DeleteAsync(tenantId, entity.Id, report.Id, storage, CancellationToken.None));

        var webhookStore = new SqlWebhookSubscriptionStore();
        var webhook = await webhookStore.CreateAsync(tenantId,
            new WebhookSubscription(Guid.NewGuid(), entity.Id, "Record changes", new Uri("https://example.test/hooks/records"),
                [WebhookEvent.RecordCreated, WebhookEvent.RecordUpdated], true, null, now, now), storage, CancellationToken.None);
        Assert.Single(await webhookStore.ListAsync(tenantId, entity.Id, storage, CancellationToken.None));
        Assert.NotNull(await webhookStore.UpdateAsync(tenantId, webhook with { IsEnabled = false }, storage, CancellationToken.None));
        Assert.True(await webhookStore.DeleteAsync(tenantId, entity.Id, webhook.Id, storage, CancellationToken.None));
    }

    private static async Task AssertAnalyticsMigrationsAsync(Guid tenantId, string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var storage = new EntityStorageLocation(builder.InitialCatalog,
            string.Empty, EntityStorageMode.DedicatedTable, false, builder.ConnectionString);
        var migrator = new DynamicEntity.SqlServer.Migrations.SqlServerTenantDatabaseMigrator();
        await migrator.MigrateAsync(storage, CancellationToken.None);

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM dbo.SchemaMigrations),
                CASE WHEN OBJECT_ID(N'dbo.ReportDefinitions', N'U') IS NULL THEN 0 ELSE 1 END,
                CASE WHEN OBJECT_ID(N'dbo.MetricDefinitions', N'U') IS NULL THEN 0 ELSE 1 END,
                CASE WHEN OBJECT_ID(N'dbo.AlertDefinitions', N'U') IS NULL THEN 0 ELSE 1 END,
                CASE WHEN OBJECT_ID(N'dbo.AlertEvaluations', N'U') IS NULL THEN 0 ELSE 1 END,
                CASE WHEN OBJECT_ID(N'dbo.AlertNotifications', N'U') IS NULL THEN 0 ELSE 1 END,
                CASE WHEN OBJECT_ID(N'dbo.WebhookSubscriptions', N'U') IS NULL THEN 0 ELSE 1 END,
                CASE WHEN OBJECT_ID(N'dbo.AlertActions', N'U') IS NULL THEN 0 ELSE 1 END;
            """;
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(5, reader.GetInt32(0));
        for (var ordinal = 1; ordinal <= 7; ordinal++) Assert.Equal(1, reader.GetInt32(ordinal));
    }

    private static async Task CreateDatabaseAsync(string serverConnection, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(serverConnection);
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        var quoted = PhysicalName.QuoteSqlIdentifier(databaseName);
        await using var command = new SqlCommand($"IF DB_ID(@name) IS NULL CREATE DATABASE {quoted};", connection);
        command.Parameters.AddWithValue("@name", databaseName);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string serverConnection, string databaseName)
    {
        if (!databaseName.StartsWith("Tenant_", StringComparison.Ordinal) &&
            !databaseName.StartsWith("DynamicEntityControl_Test_", StringComparison.Ordinal) &&
            !databaseName.StartsWith("DynamicEntityTenant_Test_", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to drop a database outside the integration-test naming convention.");
        var builder = new SqlConnectionStringBuilder(serverConnection);
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        var quoted = PhysicalName.QuoteSqlIdentifier(databaseName);
        await using var command = new SqlCommand($"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted}; END", connection);
        command.Parameters.AddWithValue("@name", databaseName);
        await command.ExecuteNonQueryAsync();
    }
}
