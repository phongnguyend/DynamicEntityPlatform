using System.Text.Json;
using DynamicEntity.Application.Common;
using DynamicEntity.Application.Alerts;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Entities;
using DynamicEntity.Application.Fields;
using DynamicEntity.Application.Records;
using DynamicEntity.Application.Storage;
using DynamicEntity.Application.Tenants;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Dashboards;
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
            Assert.All(new[] { tenant.Id, entity.Id, field.Id, index.Id, created.Id }, id => Assert.NotEqual(Guid.Empty, id));
            var loaded = await service.GetAsync(tenant.Id, entity.Id, created.Id, CancellationToken.None);
            Assert.Contains(field.StorageKey, loaded.Data);
            var analyticsFields = await fieldService.ListAsync(tenant.Id, entity.Id, CancellationToken.None);
            Assert.NotNull(analyticsFields.Single(item => item.Id == amountField.Id).IndexColumnName);
            await ExerciseAnalyticsAsync(options, control, resolver, tenant.Id, entity with
            {
                Fields = analyticsFields
            });

            var virtualSql = new VirtualSqlService(control, metadata, fieldStore, resolver, options, TimeProvider.System);
            var sqlDefinition = new VirtualSqlDefinition(true,
                "WITH selected AS (SELECT name, amount FROM customer WHERE name = @name) SELECT SUM(amount) AS total FROM selected",
                [new("name", "Text")]);
            var sqlParameters = new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("Ada") };
            var sqlPreview = await virtualSql.PreviewAsync(tenant.Id, entity.Id, sqlDefinition, null, CancellationToken.None);
            Assert.Contains("SELECT", sqlPreview.Sql);
            var sqlResult = await virtualSql.ExecuteAsync(tenant.Id, entity.Id, sqlDefinition, sqlParameters, 10, true, CancellationToken.None);
            Assert.Equal(12.5m, sqlResult.Rows.Single()["value"]);
            await Assert.ThrowsAsync<ValidationException>(() => virtualSql.PreviewAsync(tenant.Id, entity.Id,
                sqlDefinition with
                {
                    Text = "SELECT missing FROM customer"
                }, null, CancellationToken.None));
            await Assert.ThrowsAsync<ValidationException>(() => virtualSql.PreviewAsync(tenant.Id, entity.Id,
                sqlDefinition with
                {
                    Text = "SELECT Id FROM customer a JOIN customer b ON a.Id=b.Id"
                }, null, CancellationToken.None));
            await Assert.ThrowsAsync<ValidationException>(() => virtualSql.ExecuteAsync(tenant.Id, entity.Id,
                sqlDefinition with
                {
                    Text = "SELECT name, amount FROM customer"
                }, sqlParameters, 10, true, CancellationToken.None));
            var metricStore = new SqlMetricStore();
            var metricDefinition = new MetricDefinition(Guid.NewGuid(), entity.Id, "SQL metric", null, AggregateFunction.Count,
                null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, sqlDefinition);
            var tenantStorage = await control.GetTenantStorageAsync(tenant.Id, CancellationToken.None);
            metricDefinition = await metricStore.CreateAsync(tenant.Id, metricDefinition, tenantStorage!, CancellationToken.None);
            var storedMetric = await metricStore.GetAsync(tenant.Id, entity.Id, metricDefinition.Id, tenantStorage!, CancellationToken.None);
            Assert.Equal(sqlDefinition.Text, storedMetric!.Sql!.Text);
            var limited = await virtualSql.ExecuteAsync(tenant.Id, entity.Id,
                new(true, "SELECT 1 AS value UNION ALL SELECT 2 UNION ALL SELECT 3", []), null, 1, false, CancellationToken.None);
            Assert.Single(limited.Rows);
            Assert.True(limited.Truncated);
            var renamed = metricDefinition with
            {
                Name = "Updated SQL metric"
            };
            var updatedMetric = await metricStore.UpdateAsync(tenant.Id, renamed, tenantStorage!, CancellationToken.None);
            Assert.Equal(sqlDefinition.Text, updatedMetric!.Sql!.Text);
            await ExerciseGlobalViewsAsync(control, metadata, virtualSql, tenant.Id, tenantStorage!);
            await ExerciseGlobalAnalyticsAsync(options, control, metadata, fieldStore, resolver, virtualSql,
                tenant.Id, entity.Id, tenantStorage!, sqlDefinition, sqlParameters, metricDefinition.Id);
            await using (var restrictedConnection = new SqlConnection(tenantBuilder.ConnectionString))
            {
                await restrictedConnection.OpenAsync();
                await using var restrict = new SqlCommand("EXECUTE AS USER = 'VirtualSqlReader';", restrictedConnection);
                await restrict.ExecuteNonQueryAsync();
                await using var forbidden = new SqlCommand("DELETE FROM dbo.MetricDefinitions;", restrictedConnection);
                await Assert.ThrowsAsync<SqlException>(() => forbidden.ExecuteNonQueryAsync());
                await using var revert = new SqlCommand("REVERT;", restrictedConnection);
                await revert.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(tenant.Id, entity.Id,
                created.Id, input.RootElement, new byte[8], null, CancellationToken.None));
            await service.DeleteAsync(tenant.Id, entity.Id, created.Id, created.Version, CancellationToken.None);
            await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(tenant.Id, entity.Id, created.Id, CancellationToken.None));
            await ExerciseGeneratedIdsAsync(tenant.Id, entity, field, tenantStorage!, resolver, metadata, records);
        }
        finally
        {
            await DropDatabaseAsync(serverConnection, tenantDatabaseName);
            await DropDatabaseAsync(serverConnection, controlName);
        }
    }

    private static async Task ExerciseGeneratedIdsAsync(Guid tenantId, EntityDefinition entity, FieldDefinition field,
        EntityStorageLocation storage, IEntityStorageResolver resolver, SqlServerEntityMetadataStore metadata,
        SqlServerRecordStore records)
    {
        var token = CancellationToken.None;
        var entityStorage = await resolver.ResolveAsync(tenantId, entity.Id, token);
        await using var connection = new SqlConnection(storage.ConnectionString);
        await connection.OpenAsync(token);

        // Recreate the old schema while retaining metadata and an existing physical table.
        const string previousSchema = """
            DECLARE @sql NVARCHAR(MAX) = N'';
            SELECT @sql += N'ALTER TABLE dbo.' + QUOTENAME(t.name)
                + N' DROP CONSTRAINT ' + QUOTENAME(d.name) + N';'
            FROM sys.default_constraints d
            JOIN sys.tables t ON t.object_id = d.parent_object_id
            JOIN sys.columns c ON c.object_id = t.object_id AND c.column_id = d.parent_column_id
            WHERE c.name = N'Id' AND (t.name IN (N'EntityDefinitions', N'FieldDefinitions') OR t.name = @table);
            EXEC sys.sp_executesql @sql;
            DELETE FROM dbo.SchemaMigrations WHERE Version = 15;
            """;
        await using (var downgrade = new SqlCommand(previousSchema, connection))
        {
            downgrade.Parameters.AddWithValue("@table", entityStorage.TableName);
            await downgrade.ExecuteNonQueryAsync(token);
        }

        var migrator = new DynamicEntity.SqlServer.Migrations.SqlServerTenantDatabaseMigrator();
        await migrator.MigrateAsync(storage, token);
        await migrator.MigrateAsync(storage, token);
        Assert.Equal(entity.Id, (await metadata.GetAsync(tenantId, entity.Id, storage, token))!.Id);
        Assert.Equal(entityStorage.TableName, (await metadata.GetStorageAsync(tenantId, entity.Id, storage, token))!.TableName);
        var savedField = (await new SqlServerFieldMetadataStore().ListAsync(tenantId, entity.Id, storage, token))
            .Single(item => item.Id == field.Id);
        Assert.Equal(field.StorageKey, savedField.StorageKey);

        const string missingDefaultsSql = """
            SELECT COUNT(*) FROM sys.tables t
            JOIN sys.columns c ON c.object_id = t.object_id
            LEFT JOIN sys.default_constraints d ON d.object_id = c.default_object_id
            WHERE c.name = N'Id' AND c.system_type_id = TYPE_ID(N'uniqueidentifier')
                AND (d.definition IS NULL OR d.definition NOT LIKE N'%newsequentialid%');
            """;
        await using (var defaults = new SqlCommand(missingDefaultsSql, connection))
        {
            Assert.Equal(0, (int)(await defaults.ExecuteScalarAsync(token))!);
        }

        var tenant = new DynamicEntity.Domain.Tenants.TenantContext(tenantId);
        string Data(string name) => JsonSerializer.Serialize(new Dictionary<string, string> { [field.StorageKey] = name });
        var first = await records.InsertAsync(tenant, entity, Data("Direct"), null, token);
        var second = await records.InsertAsync(tenant, entity, Data("Second"), null, token);
        Assert.NotEqual(first.Id, second.Id);
        var merge = new SqlServerRecordMergeStore(resolver);
        Assert.Equal((1, 0), await merge.MergeAsync(tenant, entity, field, [Data("Merged")], null, token));
        Assert.Equal((0, 1), await merge.MergeAsync(tenant, entity, field, [Data("Merged")], null, token));

        var imports = new SqlServerImportStore();
        var now = DateTimeOffset.UtcNow;
        var job = await imports.CreateAsync(tenantId, new DynamicEntity.Domain.Imports.ImportJob(
            Guid.Empty, entity.Id, "generated-ids.csv", DynamicEntity.Domain.Imports.ImportStatus.Uploaded,
            [], 0, 0, 0, now, now), storage, token);
        Assert.NotEqual(Guid.Empty, job.Id);
        DynamicEntity.Domain.Imports.ImportSourceRow[] rows = [new(1, "{}", Data("Imported"), null)];
        await imports.StageRowsAsync(tenantId, job.Id, rows, storage, token);
        await imports.SaveValidationAsync(tenantId, job.Id, rows, storage, token);
        Assert.Equal(1, await imports.CommitAsync(tenantId, job, entityStorage, storage, token));
        var saved = await records.QueryAsync(tenant, entity, new RecordQuery(10), token);
        Assert.Equal(4, saved.Items.Count);
        Assert.All(saved.Items, record => Assert.NotEqual(Guid.Empty, record.Id));
        Assert.Equal(4, saved.Items.Select(record => record.Id).Distinct().Count());
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
        var dashboardStore = new SqlServerDashboardStore();
        var dashboard = await dashboardStore.CreateAsync(
            new DashboardDefinition(Guid.NewGuid(), tenantId, "Operations", "{\"items\":[],\"layouts\":{}}", null, now, now),
            storage, CancellationToken.None);
        Assert.Single(await dashboardStore.ListAsync(tenantId, storage, CancellationToken.None));
        Assert.NotNull(await dashboardStore.GetAsync(tenantId, dashboard.Id, storage, CancellationToken.None));
        Assert.True(await dashboardStore.NameExistsAsync(tenantId, dashboard.Name, null, storage, CancellationToken.None));
        Assert.NotNull(await dashboardStore.UpdateAsync(dashboard with
        {
            Name = "Executive"
        }, storage, CancellationToken.None));

        var reportStore = new SqlReportStore();
        var report = await reportStore.CreateAsync(tenantId,
            new ReportDefinition(Guid.NewGuid(), entity.Id, "Count report", null, "{\"query\":{}}", null, now, now),
            storage, CancellationToken.None);
        Assert.Single(await reportStore.ListAsync(tenantId, entity.Id, storage, CancellationToken.None));
        Assert.NotNull(await reportStore.UpdateAsync(tenantId, report with
        {
            Name = "Updated report"
        }, storage, CancellationToken.None));

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
        evaluation = await evaluationStore.CreateAsync(tenantId, evaluation, storage, CancellationToken.None);
        var notificationStore = new SqlAlertNotificationStore(options);
        await notificationStore.CreateAsync(tenantId, new AlertNotification(Guid.NewGuid(), alert.Id, evaluation.Id,
            "InApp", NotificationStatus.Delivered, 1, null, now, now), storage, CancellationToken.None);
        Assert.Single(await evaluationStore.ListAsync(tenantId, alert.Id, storage, CancellationToken.None));
        Assert.Single(await notificationStore.ListAsync(tenantId, alert.Id, storage, CancellationToken.None));
        await alertStore.CompleteAsync(tenantId, claimed! with
        {
            LastState = AlertState.Firing,
            LastEvaluatedAt = now,
            NextEvaluationAt = now.AddMinutes(5)
        }, storage, CancellationToken.None);
        Assert.True(await alertStore.DeleteAsync(tenantId, entity.Id, alert.Id, storage, CancellationToken.None));
        Assert.True(await metricStore.DeleteAsync(tenantId, entity.Id, metric.Id, storage, CancellationToken.None));
        Assert.True(await reportStore.DeleteAsync(tenantId, entity.Id, report.Id, storage, CancellationToken.None));
        Assert.True(await dashboardStore.DeleteAsync(tenantId, dashboard.Id, storage, CancellationToken.None));

        var webhookStore = new SqlWebhookSubscriptionStore();
        var webhook = await webhookStore.CreateAsync(tenantId,
            new WebhookSubscription(Guid.NewGuid(), entity.Id, "Record changes", new Uri("https://example.test/hooks/records"),
                [WebhookEvent.RecordCreated, WebhookEvent.RecordUpdated], true, null, now, now), storage, CancellationToken.None);
        Assert.Single(await webhookStore.ListAsync(tenantId, entity.Id, storage, CancellationToken.None));
        Assert.NotNull(await webhookStore.UpdateAsync(tenantId, webhook with
        {
            IsEnabled = false
        }, storage, CancellationToken.None));
        Assert.True(await webhookStore.DeleteAsync(tenantId, entity.Id, webhook.Id, storage, CancellationToken.None));
    }

    private static async Task ExerciseGlobalAnalyticsAsync(SqlServerOptions options, IControlPlaneStore control,
        IEntityMetadataStore entities, IFieldMetadataStore fields, IEntityStorageResolver resolver, IVirtualSqlService sql,
        Guid tenantId, Guid entityId, EntityStorageLocation storage, VirtualSqlDefinition definition,
        IReadOnlyDictionary<string, JsonElement> parameters, Guid existingMetricId)
    {
        // Recreate the v11 shape in this test-owned database to exercise the real data backfill.
        await using (var connection = new SqlConnection(new SqlConnectionStringBuilder(storage.ConnectionString)
        {
            InitialCatalog = storage.DatabaseName
        }.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                DROP INDEX IX_ReportDefinitions_TenantScope ON dbo.ReportDefinitions;
                DROP INDEX IX_MetricDefinitions_TenantScope ON dbo.MetricDefinitions;
                ALTER TABLE dbo.ReportDefinitions DROP COLUMN TenantId;
                ALTER TABLE dbo.MetricDefinitions DROP COLUMN TenantId;
                DROP INDEX IX_ReportDefinitions_EntityId ON dbo.ReportDefinitions;
                DROP INDEX IX_MetricDefinitions_EntityId ON dbo.MetricDefinitions;
                ALTER TABLE dbo.ReportDefinitions ALTER COLUMN EntityId UNIQUEIDENTIFIER NOT NULL;
                ALTER TABLE dbo.MetricDefinitions ALTER COLUMN EntityId UNIQUEIDENTIFIER NOT NULL;
                CREATE INDEX IX_ReportDefinitions_EntityId ON dbo.ReportDefinitions(EntityId);
                CREATE INDEX IX_MetricDefinitions_EntityId ON dbo.MetricDefinitions(EntityId);
                DELETE FROM dbo.SchemaMigrations WHERE Version=12;
                """, connection);
            await command.ExecuteNonQueryAsync();
        }
        await new DynamicEntity.SqlServer.Migrations.SqlServerTenantDatabaseMigrator().MigrateAsync(storage, CancellationToken.None);
        var metricStore = new SqlMetricStore();
        var reportStore = new SqlReportStore();
        Assert.NotNull(await metricStore.GetAsync(tenantId, entityId, existingMetricId, storage, CancellationToken.None));
        Assert.Null(await metricStore.GetAsync(tenantId, null, existingMetricId, storage, CancellationToken.None));
        var validator = new DynamicEntity.Application.Analytics.AnalyticsQueryValidator();
        var analytics = new DynamicEntity.Application.Analytics.AnalyticsService(control, entities, fields,
            new SqlAnalyticsStore(options, resolver), validator, TimeProvider.System);
        var metrics = new DynamicEntity.Application.Metrics.MetricService(control, entities, fields, metricStore,
            analytics, validator, TimeProvider.System, sql);
        var reports = new DynamicEntity.Application.Reports.ReportService(control, entities, fields, reportStore,
            analytics, validator, TimeProvider.System, sql);
        var metric = await metrics.CreateAsync(tenantId, null, "Global sum", null, AggregateFunction.Count,
            null, null, null, null, CancellationToken.None, definition);
        Assert.Null(metric.EntityId);
        Assert.Single(await metrics.ListAsync(tenantId, null, CancellationToken.None));
        Assert.DoesNotContain(await metrics.ListAsync(tenantId, entityId, CancellationToken.None), item => item.Id == metric.Id);
        Assert.Null(await metricStore.GetAsync(Guid.NewGuid(), null, metric.Id, storage, CancellationToken.None));
        Assert.Null(await metricStore.GetAsync(tenantId, entityId, metric.Id, storage, CancellationToken.None));
        Assert.Null(await metricStore.UpdateAsync(Guid.NewGuid(), metric, storage, CancellationToken.None));
        Assert.False(await metricStore.DeleteAsync(Guid.NewGuid(), null, metric.Id, storage, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => metrics.EvaluateAsync(tenantId, null, metric.Id, CancellationToken.None));
        Assert.Equal(12.5m, (await metrics.EvaluateAsync(tenantId, null, metric.Id, CancellationToken.None, parameters)).Value);
        await metrics.UpdateAsync(tenantId, null, metric.Id, "Renamed global sum", null, AggregateFunction.Count,
            null, null, null, CancellationToken.None, definition);
        Assert.Equal("Renamed global sum", (await metrics.GetAsync(tenantId, null, metric.Id, CancellationToken.None)).Name);
        var specification = new ReportSpecification(new(null, [], [], [], 100, definition), VisualizationType.Table);
        var report = await reports.CreateAsync(tenantId, null, "Global report", null, specification, null, CancellationToken.None);
        Assert.Null(report.EntityId);
        Assert.Single(await reports.ListAsync(tenantId, null, CancellationToken.None));
        Assert.DoesNotContain(await reports.ListAsync(tenantId, entityId, CancellationToken.None), item => item.Id == report.Id);
        Assert.Null(await reportStore.GetAsync(Guid.NewGuid(), null, report.Id, storage, CancellationToken.None));
        Assert.Null(await reportStore.GetAsync(tenantId, entityId, report.Id, storage, CancellationToken.None));
        Assert.Null(await reportStore.UpdateAsync(Guid.NewGuid(), report, storage, CancellationToken.None));
        Assert.False(await reportStore.DeleteAsync(Guid.NewGuid(), null, report.Id, storage, CancellationToken.None));
        Assert.Equal(12.5m, (await reports.RunAsync(tenantId, null, report.Id, CancellationToken.None, parameters)).Rows.Single()["total"]);
        await reports.UpdateAsync(tenantId, null, report.Id, "Renamed global report", null, specification, CancellationToken.None);
        Assert.Equal("Renamed global report", (await reports.GetAsync(tenantId, null, report.Id, CancellationToken.None)).Name);
        await ExerciseGlobalAlertsAsync(options, control, entities, fields, sql, metrics, tenantId, entityId, storage, metric.Id, existingMetricId, parameters);
        await reports.DeleteAsync(tenantId, null, report.Id, CancellationToken.None);
        await metrics.DeleteAsync(tenantId, null, metric.Id, CancellationToken.None);
        Assert.Empty(await reports.ListAsync(tenantId, null, CancellationToken.None));
        Assert.Empty(await metrics.ListAsync(tenantId, null, CancellationToken.None));
        // A global constant does not need any entity to be selected or referenced.
        Assert.Equal(1, (await sql.ExecuteAsync(tenantId, null, new(true, "SELECT 1 AS value", []), null, 1, true, CancellationToken.None)).Rows.Single()["value"]);
    }

    private static async Task ExerciseGlobalViewsAsync(IControlPlaneStore control, IEntityMetadataStore entities,
        IVirtualSqlService sql, Guid tenantId, EntityStorageLocation storage)
    {
        var token = CancellationToken.None;
        var company = await new EntityService(control, entities, TimeProvider.System).CreateAsync(tenantId, "company", "Company", null, token);
        var customer = (await entities.ListAsync(tenantId, storage, token)).Single(entity => entity.Name == "customer");
        var entityViews = new DynamicEntity.Application.Views.ViewService(control, entities, new SqlServerViewStore(), TimeProvider.System);
        var entityDefinition = JsonSerializer.SerializeToElement(new { pageSize = 100 });
        var entityView = await entityViews.CreateAsync(tenantId, customer.Id, "Entity view", entityDefinition, null, token);
        await Assert.ThrowsAsync<NotFoundException>(() => entityViews.DeleteAsync(tenantId, company.Id, entityView.Id, token));
        await Assert.ThrowsAsync<NotFoundException>(() => entityViews.UpdateAsync(tenantId, entityView.Id, company.Id, "Wrong entity", entityDefinition, token));
        Assert.Single(await entityViews.ListAsync(tenantId, customer.Id, token));
        await entityViews.UpdateAsync(tenantId, entityView.Id, customer.Id, "Renamed entity view", entityDefinition, token);
        await entityViews.DeleteAsync(tenantId, customer.Id, entityView.Id, token);
        Assert.Empty(await entityViews.ListAsync(tenantId, customer.Id, token));
        var store = new SqlGlobalViewStore();
        var service = new DynamicEntity.Application.Views.GlobalViewService(control, store, sql, TimeProvider.System);
        var definition = new DynamicEntity.Domain.Views.GlobalViewSpecification(new(true,
            "SELECT c.name AS customerName, c.amount, co.Id AS companyId FROM customer c LEFT JOIN company co ON c.Id=co.Id WHERE c.name=@name",
            [new("name", "Text")]), 10, ["amount", "customerName", "companyId"], ["companyId"], "#176b4d");
        var saved = await service.CreateAsync(tenantId, "Cross entity view", "Read only", definition, token);
        var loaded = await service.GetAsync(tenantId, saved.Id, token);
        Assert.Equal(definition.Sql.Text, loaded.Definition.Sql.Text);
        Assert.Equal(definition.ColumnOrder, loaded.Definition.ColumnOrder);
        Assert.Equal(definition.HiddenColumns, loaded.Definition.HiddenColumns);
        Assert.Equal(definition.Color, loaded.Definition.Color);
        Assert.Single(await service.ListAsync(tenantId, token));
        Assert.Null(await store.GetAsync(Guid.NewGuid(), saved.Id, storage, token));
        Assert.Empty(await store.ListAsync(Guid.NewGuid(), storage, token));
        Assert.Null(await store.UpdateAsync(Guid.NewGuid(), saved, storage, token));
        Assert.False(await store.DeleteAsync(Guid.NewGuid(), saved.Id, storage, token));
        await Assert.ThrowsAsync<ValidationException>(() => service.RunAsync(tenantId, saved.Id, null, token));
        var parameters = new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("Ada") };
        var preview = await service.PreviewSqlAsync(tenantId, definition, null, token);
        Assert.Contains("customer", preview.VirtualSql, StringComparison.OrdinalIgnoreCase);
        var result = await service.RunAsync(tenantId, saved.Id, parameters, token);
        Assert.Equal("Ada", Assert.Single(result.Rows)["customerName"]);
        Assert.Equal(12.5m, result.Rows[0]["amount"]);
        Assert.Null(result.Rows[0]["companyId"]);
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(tenantId, "Write query", null,
            definition with { Sql = new(true, "DELETE FROM customer", []) }, token));
        var changed = definition with
        {
            Sql = new(true, "SELECT 1 AS value UNION ALL SELECT 2", []),
            RowLimit = 1
        };
        var updated = await service.UpdateAsync(tenantId, saved.Id, "Renamed", null, changed, token);
        Assert.Equal(saved.CreatedAt, updated.CreatedAt);
        Assert.Equal("Renamed", (await service.GetAsync(tenantId, saved.Id, token)).Name);
        var limited = await service.RunAsync(tenantId, saved.Id, null, token);
        Assert.Single(limited.Rows);
        Assert.True(limited.Truncated);
        await service.DeleteAsync(tenantId, saved.Id, token);
        Assert.Empty(await service.ListAsync(tenantId, token));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(tenantId, saved.Id, token));
    }

    private static async Task ExerciseGlobalAlertsAsync(SqlServerOptions options, IControlPlaneStore control,
        IEntityMetadataStore entities, IFieldMetadataStore fields, IVirtualSqlService sql,
        DynamicEntity.Application.Metrics.MetricService metrics, Guid tenantId, Guid entityId,
        EntityStorageLocation storage, Guid globalMetricId, Guid entityMetricId, IReadOnlyDictionary<string, JsonElement> parameters)
    {
        var store = new SqlAlertStore();
        var metricStore = new SqlMetricStore();
        var evaluations = new SqlAlertEvaluationStore(options);
        var notifications = new SqlAlertNotificationStore(options);
        var service = new AlertService(control, entities, metricStore, store, evaluations, notifications, TimeProvider.System, fields, sql);
        var worker = new AlertEvaluationWorker(control, store, evaluations, notifications, new QueuedAlertNotifier(notifications),
            metrics, new AlertThresholdEvaluator(), TimeProvider.System);
        var token = CancellationToken.None;
        var existing = await service.CreateAsync(tenantId, entityId, entityMetricId, "Existing entity alert",
            AlertComparisonOperator.GreaterThan, "0", AlertInterval.Hourly, "UTC", TimeSpan.Zero, false, false, [], null, token, parameters);
        // Recreate the previous schema with an existing alert to verify migration 13 backfills ownership.
        await using (var connection = new SqlConnection(new SqlConnectionStringBuilder(storage.ConnectionString)
        {
            InitialCatalog = storage.DatabaseName
        }.ConnectionString))
        {
            await connection.OpenAsync(token);
            await using var command = new SqlCommand("""
                DROP INDEX IX_AlertDefinitions_TenantScope ON dbo.AlertDefinitions;
                DROP INDEX IX_AlertDefinitions_EntityId ON dbo.AlertDefinitions;
                ALTER TABLE dbo.AlertDefinitions DROP COLUMN TenantId;
                ALTER TABLE dbo.AlertDefinitions ALTER COLUMN EntityId UNIQUEIDENTIFIER NOT NULL;
                CREATE INDEX IX_AlertDefinitions_EntityId ON dbo.AlertDefinitions(EntityId);
                DELETE FROM dbo.SchemaMigrations WHERE Version=13;
                """, connection);
            await command.ExecuteNonQueryAsync(token);
        }
        await new DynamicEntity.SqlServer.Migrations.SqlServerTenantDatabaseMigrator().MigrateAsync(storage, token);
        Assert.Equal(existing.Id, (await service.GetAsync(tenantId, entityId, existing.Id, token)).Id);
        Assert.Empty(await service.ListAsync(tenantId, null, token));
        var builder = await metrics.CreateAsync(tenantId, entityId, "Builder count", null, AggregateFunction.Count,
            null, null, null, null, token);
        foreach (var metricId in new[] { globalMetricId, entityMetricId, builder.Id })
        {
            if (metricId != builder.Id)
            {
                await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(tenantId, null, metricId, "Missing input",
                    AlertComparisonOperator.GreaterThan, "0", AlertInterval.Hourly, "UTC", TimeSpan.Zero, false, true, [], null, token));
            }
            var alert = await service.CreateAsync(tenantId, null, metricId, "Global alert", AlertComparisonOperator.GreaterThan,
                "0", AlertInterval.Hourly, "UTC", TimeSpan.Zero, true, true,
                [new(AlertActionType.Webhook, null, [new Uri("https://example.test/alerts")])], null, token, parameters);
            Assert.Null(alert.EntityId);
            Assert.Single(await service.ListAsync(tenantId, null, token));
            Assert.Null(await store.GetAsync(tenantId, entityId, alert.Id, storage, token));
            Assert.Null(await store.GetAsync(Guid.NewGuid(), null, alert.Id, storage, token));
            Assert.Null(await metricStore.GetByIdAsync(Guid.NewGuid(), metricId, storage, token));
            Assert.Null(await store.UpdateAsync(Guid.NewGuid(), alert, storage, token));
            Assert.False(await store.DeleteAsync(Guid.NewGuid(), null, alert.Id, storage, token));
            Assert.Equal(1, await worker.ProcessTenantAsync(tenantId, "global-test", token));
            var history = await service.HistoryAsync(tenantId, null, alert.Id, token);
            Assert.Equal(AlertState.Firing, Assert.Single(history.Item1).State);
            Assert.Equal(2, history.Item2.Count);
            var queued = await notifications.ClaimPendingAsync(tenantId, "delivery-test", DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(1), storage, token);
            Assert.NotNull(queued);
            Assert.Null(AlertNotificationPayloads.Deserialize(queued.PayloadJson)!.EntityId);
            await notifications.CompleteAsync(tenantId, queued with
            {
                Status = NotificationStatus.Delivered,
                DeliveredAt = DateTimeOffset.UtcNow
            }, storage, token);
            Assert.All(await notifications.ListAsync(tenantId, alert.Id, storage, token), item => Assert.Equal(NotificationStatus.Delivered, item.Status));
            Assert.Empty(await notifications.ListAsync(Guid.NewGuid(), alert.Id, storage, token));
            Assert.Empty(await evaluations.ListAsync(Guid.NewGuid(), alert.Id, storage, token));
            await Assert.ThrowsAsync<ConflictException>(() => metrics.DeleteAsync(tenantId, metricId == globalMetricId ? null : entityId, metricId, token));
            var updated = await service.UpdateAsync(tenantId, null, alert.Id, builder.Id, "Updated global alert",
                alert.ComparisonOperator, alert.ThresholdJson, alert.Interval, alert.Timezone, alert.Cooldown, true, false, [], token);
            Assert.Equal(builder.Id, updated.MetricId);
            Assert.Empty((await service.GetAsync(tenantId, null, alert.Id, token)).Actions);
            await service.DeleteAsync(tenantId, null, alert.Id, token);
            Assert.Empty(await service.ListAsync(tenantId, null, token));
        }
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(tenantId, entityId, globalMetricId, "Wrong scope",
            AlertComparisonOperator.GreaterThan, "0", AlertInterval.Hourly, "UTC", TimeSpan.Zero, false, true, [], null, token, parameters));
        await service.DeleteAsync(tenantId, entityId, existing.Id, token);
        await metrics.DeleteAsync(tenantId, entityId, builder.Id, token);
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
                CASE WHEN OBJECT_ID(N'dbo.AlertActions', N'U') IS NULL THEN 0 ELSE 1 END,
                CASE WHEN OBJECT_ID(N'dbo.DashboardDefinitions', N'U') IS NULL THEN 0 ELSE 1 END;
            """;
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(DynamicEntity.SqlServer.Migrations.SqlServerTenantDatabaseMigrator.MigrationCount,
            reader.GetInt32(0));
        for (var ordinal = 1; ordinal <= 8; ordinal++)
        {
            Assert.Equal(1, reader.GetInt32(ordinal));
        }
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
        {
            throw new InvalidOperationException("Refusing to drop a database outside the integration-test naming convention.");
        }

        var builder = new SqlConnectionStringBuilder(serverConnection);
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        var quoted = PhysicalName.QuoteSqlIdentifier(databaseName);
        await using var command = new SqlCommand($"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted}; END", connection);
        command.Parameters.AddWithValue("@name", databaseName);
        await command.ExecuteNonQueryAsync();
    }
}
