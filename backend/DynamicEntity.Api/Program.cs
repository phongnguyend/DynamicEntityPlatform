using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using DynamicEntity.Api.Errors;
using DynamicEntity.Api.Tenancy;
using DynamicEntity.Api.Records;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Entities;
using DynamicEntity.Application.Fields;
using DynamicEntity.Application.Records;
using DynamicEntity.Application.Imports;
using DynamicEntity.Application.Storage;
using DynamicEntity.Application.Tenants;
using DynamicEntity.Application.Views;
using DynamicEntity.Application.Analytics;
using DynamicEntity.Application.Reports;
using DynamicEntity.Application.Metrics;
using DynamicEntity.Application.Alerts;
using DynamicEntity.Application.Webhooks;
using DynamicEntity.Application.Dashboards;
using DynamicEntity.Contracts.Analytics;
using DynamicEntity.Contracts.Reports;
using DynamicEntity.Contracts.Metrics;
using DynamicEntity.Contracts.Alerts;
using DynamicEntity.Contracts.Webhooks;
using DynamicEntity.Contracts.Entities;
using DynamicEntity.Contracts.Fields;
using DynamicEntity.Contracts.Records;
using DynamicEntity.Contracts.Imports;
using DynamicEntity.Contracts.Tenants;
using DynamicEntity.Contracts.Views;
using DynamicEntity.Contracts.Dashboards;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Imports;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using DynamicEntity.Domain.Validation;
using DynamicEntity.Domain.Views;
using DynamicEntity.Domain.Dashboards;
using DynamicEntity.SqlServer;
using DynamicEntity.SqlServer.Migrations;
using DynamicEntity.SqlServer.Analytics;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Queries;
using DynamicEntity.Infrastructure.Imports;
using DynamicEntity.Infrastructure.Authorization;
using DynamicEntity.Infrastructure.Notifications;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var sqlServerOptions = builder.Configuration
    .GetRequiredSection(SqlServerOptions.SectionName)
    .Get<SqlServerOptions>()
    ?? throw new InvalidOperationException("SqlServer configuration is required.");

builder.Services.AddSingleton(sqlServerOptions);
builder.Services.AddSingleton<SqlServerControlPlaneStore>();
builder.Services.AddSingleton<IControlPlaneStore>(services => services.GetRequiredService<SqlServerControlPlaneStore>());
builder.Services.AddSingleton<IControlPlaneInitializer>(services => services.GetRequiredService<SqlServerControlPlaneStore>());
builder.Services.AddSingleton<ITenantDatabaseProvisioner, SqlServerTenantDatabaseProvisioner>();
builder.Services.AddSingleton<ITenantDatabaseMigrator, SqlServerTenantDatabaseMigrator>();
builder.Services.AddHostedService<TenantDatabaseMigrationHostedService>();
builder.Services.AddSingleton<IEntityMetadataStore, SqlServerEntityMetadataStore>();
builder.Services.AddSingleton<IFieldMetadataStore, SqlServerFieldMetadataStore>();
builder.Services.AddSingleton<IEntityStorageResolver, EntityStorageResolver>();
builder.Services.AddSingleton<IRecordStore, SqlServerRecordStore>();
builder.Services.AddSingleton<IRecordConstraintValidator, SqlServerRecordConstraintValidator>();
builder.Services.AddSingleton<IFacetStore, SqlServerFacetStore>();
builder.Services.AddSingleton<IEntityIndexManager, SqlServerEntityIndexManager>();
builder.Services.AddSingleton<IViewStore, SqlServerViewStore>();
builder.Services.AddSingleton<IDashboardStore, SqlServerDashboardStore>();
builder.Services.AddSingleton<IAnalyticsStore, SqlAnalyticsStore>();
builder.Services.AddSingleton<IReportStore, SqlReportStore>();
builder.Services.AddSingleton<IMetricStore, SqlMetricStore>();
builder.Services.AddSingleton<IAlertStore, SqlAlertStore>();
builder.Services.AddSingleton<IAlertEvaluationStore, SqlAlertEvaluationStore>();
builder.Services.AddSingleton<IAlertNotificationStore, SqlAlertNotificationStore>();
builder.Services.AddSingleton<IWebhookSubscriptionStore, SqlWebhookSubscriptionStore>();
builder.Services.AddSingleton<IImportStore, SqlServerImportStore>();
builder.Services.AddSingleton<IBulkRecordStore, SqlServerBulkRecordStore>();
builder.Services.AddSingleton<IRecordMergeStore, SqlServerRecordMergeStore>();
builder.Services.AddSingleton<ITabularFileParser, TabularFileParser>();
builder.Services.AddSingleton<IEntityAuthorizationService, AllowAllEntityAuthorizationService>();
builder.Services.AddSingleton(new RecordValidationOptions(AllowUnknownFields: false));
builder.Services.AddSingleton<IRecordValidator, RecordValidator>();
builder.Services.AddSingleton(new AnalyticsValidationOptions());
builder.Services.AddSingleton<AnalyticsQueryValidator>();
builder.Services.AddSingleton<AlertThresholdEvaluator>();
builder.Services.AddSingleton(TimeProvider.System);

var emailAlertOptions = builder.Configuration.GetSection(EmailAlertOptions.SectionName).Get<EmailAlertOptions>() ?? new EmailAlertOptions();
var webhookAlertOptions = builder.Configuration.GetSection(WebhookAlertOptions.SectionName).Get<WebhookAlertOptions>() ?? new WebhookAlertOptions();
builder.Services.AddSingleton(emailAlertOptions);
builder.Services.AddSingleton(webhookAlertOptions);
builder.Services.AddSingleton<IAlertNotifier, QueuedAlertNotifier>();
builder.Services.AddSingleton<IAlertChannelSender, EmailAlertChannelSender>();
builder.Services.AddHttpClient<WebhookAlertChannelSender>()
    .ConfigurePrimaryHttpMessageHandler(() => WebhookAlertChannelSender.CreateHandler(webhookAlertOptions));
builder.Services.AddTransient<IAlertChannelSender>(services => services.GetRequiredService<WebhookAlertChannelSender>());
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<EntityService>();
builder.Services.AddScoped<FieldService>();
builder.Services.AddScoped<RecordService>();
builder.Services.AddScoped<FacetService>();
builder.Services.AddScoped<EntityIndexService>();
builder.Services.AddScoped<ViewService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<BulkRecordService>();
builder.Services.AddScoped<MergeService>();
builder.Services.AddScoped<RecordExportService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<MetricService>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<AlertEvaluationWorker>();
builder.Services.AddScoped<AlertNotificationDeliveryWorker>();
builder.Services.AddScoped<WebhookSubscriptionService>();
builder.Services.AddHostedService<AlertSchedulerHostedService>();
builder.Services.AddHostedService<AlertNotificationDeliveryHostedService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContextAccessor, HttpTenantContextAccessor>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(AnalyticsTelemetry.SourceName).AddAspNetCoreInstrumentation().AddSqlClientInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

var app = builder.Build();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/api/tenants", async (
    CreateTenantRequest request,
    TenantService service,
    IControlPlaneStore controlPlane,
    CancellationToken cancellationToken) =>
{
    var tenant = await service.CreateAsync(request.Name, request.ConnectionString ?? string.Empty, cancellationToken);
    var storage = await controlPlane.GetTenantStorageAsync(tenant.Id, cancellationToken);
    return Results.Created($"/api/tenants/{tenant.Id}", ToTenantResponse(tenant, storage));
});

app.MapGet("/api/tenants", async (TenantService service, IControlPlaneStore controlPlane, CancellationToken cancellationToken) =>
{
    var tenants = await service.ListAsync(cancellationToken);
    var responses = await Task.WhenAll(tenants.Select(async tenant =>
        ToTenantResponse(tenant, await controlPlane.GetTenantStorageAsync(tenant.Id, cancellationToken))));
    return Results.Ok(responses);
});

app.MapPatch("/api/tenants/{tenantId:guid}", async (
    Guid tenantId, UpdateTenantRequest request, TenantService service, IControlPlaneStore controlPlane, CancellationToken cancellationToken) =>
{
    var tenant = await service.UpdateAsync(tenantId, request.Name, cancellationToken);
    var storage = await controlPlane.GetTenantStorageAsync(tenant.Id, cancellationToken);
    return Results.Ok(ToTenantResponse(tenant, storage));
});

app.MapPut("/api/tenants/{tenantId:guid}/connection", async (
    Guid tenantId, ConfigureTenantConnectionRequest request, TenantService service, IControlPlaneStore controlPlane,
    CancellationToken cancellationToken) =>
{
    var tenant = await service.ConfigureConnectionAsync(tenantId, request.ConnectionString ?? string.Empty, cancellationToken);
    var storage = await controlPlane.GetTenantStorageAsync(tenant.Id, cancellationToken);
    return Results.Ok(ToTenantResponse(tenant, storage));
});

app.MapPost("/api/tenants/{tenantId:guid}/disable", async (
    Guid tenantId, TenantService service, CancellationToken cancellationToken) =>
{
    await service.DisableAsync(tenantId, cancellationToken);
    return Results.NoContent();
});

var dashboards = app.MapGroup("/api/dashboards");

dashboards.MapGet("/", async (ITenantContextAccessor tenantAccessor, DashboardService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok((await service.ListAsync(tenant.TenantId, cancellationToken)).Select(ToDashboardResponse));
});

dashboards.MapGet("/{dashboardId:guid}", async (Guid dashboardId, ITenantContextAccessor tenantAccessor,
    DashboardService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(ToDashboardResponse(await service.GetAsync(tenant.TenantId, dashboardId, cancellationToken)));
});

dashboards.MapPost("/", async (CreateDashboardRequest request, ITenantContextAccessor tenantAccessor,
    DashboardService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var dashboard = await service.CreateAsync(tenant.TenantId, request.Name, request.Definition, null, cancellationToken);
    return Results.Created($"/api/dashboards/{dashboard.Id}", ToDashboardResponse(dashboard));
});

dashboards.MapPut("/{dashboardId:guid}", async (Guid dashboardId, UpdateDashboardRequest request,
    ITenantContextAccessor tenantAccessor, DashboardService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(ToDashboardResponse(await service.UpdateAsync(
        tenant.TenantId, dashboardId, request.Name, request.Definition, cancellationToken)));
});

dashboards.MapDelete("/{dashboardId:guid}", async (Guid dashboardId, ITenantContextAccessor tenantAccessor,
    DashboardService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    await service.DeleteAsync(tenant.TenantId, dashboardId, cancellationToken);
    return Results.NoContent();
});

var entities = app.MapGroup("/api/entities");
entities.AddEndpointFilter(async (context, next) =>
{
    var http = context.HttpContext;
    var tenant = http.RequestServices.GetRequiredService<ITenantContextAccessor>().GetRequiredTenant();
    var authorization = http.RequestServices.GetRequiredService<IEntityAuthorizationService>();
    var entityId = Guid.TryParse(Convert.ToString(http.Request.RouteValues["entityId"]), out var parsed) ? parsed : Guid.Empty;
    var path = http.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
    var schemaOperation = entityId == Guid.Empty || path.Contains("/fields", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(entityId.ToString("D"), StringComparison.OrdinalIgnoreCase);
    var isAlert = path.Contains("/alerts", StringComparison.OrdinalIgnoreCase);
    var isWebhook = path.Contains("/webhooks", StringComparison.OrdinalIgnoreCase);
    var isAnalytics = path.Contains("/analytics", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/reports", StringComparison.OrdinalIgnoreCase) || path.Contains("/metrics", StringComparison.OrdinalIgnoreCase);
    var allowed = isWebhook
        ? await authorization.CanManageWebhooksAsync(tenant, entityId, http.RequestAborted)
        : isAlert
        ? http.Request.Method == HttpMethods.Get && path.EndsWith("/history", StringComparison.OrdinalIgnoreCase)
            ? await authorization.CanViewAlertHistoryAsync(tenant, entityId, http.RequestAborted)
            : http.Request.Method == HttpMethods.Get
                ? await authorization.CanReadAnalyticsAsync(tenant, entityId, http.RequestAborted)
                : await authorization.CanManageAlertsAsync(tenant, entityId, http.RequestAborted)
        : isAnalytics
            ? http.Request.Method == HttpMethods.Get
                ? await authorization.CanReadAnalyticsAsync(tenant, entityId, http.RequestAborted)
                : await authorization.CanManageAnalyticsAsync(tenant, entityId, http.RequestAborted)
            : http.Request.Method == HttpMethods.Get
                ? await authorization.CanReadAsync(tenant, entityId, http.RequestAborted)
                : schemaOperation
                    ? await authorization.CanManageSchemaAsync(tenant, entityId, http.RequestAborted)
                    : await authorization.CanWriteAsync(tenant, entityId, http.RequestAborted);
    return allowed ? await next(context) : Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied.");
});

entities.MapPost("/", async (
    CreateEntityRequest request,
    ITenantContextAccessor tenantAccessor,
    EntityService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var entity = await service.CreateAsync(
        tenant.TenantId, request.Name, request.DisplayName, request.Description, cancellationToken);
    return Results.Created($"/api/entities/{entity.Id}", ToEntityResponse(entity));
});

entities.MapGet("/", async (
    ITenantContextAccessor tenantAccessor,
    EntityService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var result = await service.ListAsync(tenant.TenantId, cancellationToken);
    return Results.Ok(result.Select(ToEntityResponse));
});

entities.MapPut("/pins", async (SetEntityPinsRequest request, ITenantContextAccessor tenantAccessor,
    EntityService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var result = await service.SetPinsAsync(tenant.TenantId, request.EntityIds ?? [], cancellationToken);
    return Results.Ok(result.Select(ToEntityResponse));
});

entities.MapGet("/{entityId:guid}", async (Guid entityId, ITenantContextAccessor tenantAccessor,
    EntityService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(ToEntityResponse(await service.GetAsync(tenant.TenantId, entityId, cancellationToken)));
});

entities.MapPatch("/{entityId:guid}", async (Guid entityId, UpdateEntityRequest request,
    ITenantContextAccessor tenantAccessor, EntityService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(ToEntityResponse(await service.UpdateAsync(tenant.TenantId, entityId,
        request.Name, request.DisplayName, request.Description, request.Icon, cancellationToken)));
});

entities.MapDelete("/{entityId:guid}", async (Guid entityId, ITenantContextAccessor tenantAccessor,
    EntityService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    await service.ArchiveAsync(tenant.TenantId, entityId, cancellationToken);
    return Results.NoContent();
});

entities.MapPost("/{entityId:guid}/fields", async (
    Guid entityId,
    CreateFieldRequest request,
    ITenantContextAccessor tenantAccessor,
    FieldService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var field = await service.CreateAsync(
        tenant.TenantId, entityId, request.Name, request.DisplayName, request.DataType,
        request.IsRequired, request.IsUnique, request.IsFilterable, request.IsSortable,
        request.IsFacetable, request.IsSearchable, request.DefaultValue?.GetRawText(),
        request.Configuration?.GetRawText(), request.SortOrder, cancellationToken);
    return Results.Created($"/api/entities/{entityId}/fields/{field.Id}", ToFieldResponse(field));
});

entities.MapGet("/{entityId:guid}/fields", async (
    Guid entityId,
    ITenantContextAccessor tenantAccessor,
    FieldService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var result = await service.ListAsync(tenant.TenantId, entityId, cancellationToken);
    return Results.Ok(result.Select(ToFieldResponse));
});

entities.MapPatch("/{entityId:guid}/fields/{fieldId:guid}", async (
    Guid entityId,
    Guid fieldId,
    UpdateFieldRequest request,
    ITenantContextAccessor tenantAccessor,
    FieldService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var field = await service.UpdateAsync(
        tenant.TenantId, entityId, fieldId, request.Name, request.DisplayName,
        request.IsRequired, request.IsUnique, request.IsFilterable, request.IsSortable,
        request.IsFacetable, request.IsSearchable,
        request.Configuration?.GetRawText(), request.SortOrder, cancellationToken);
    return Results.Ok(ToFieldResponse(field));
});

entities.MapDelete("/{entityId:guid}/fields/{fieldId:guid}", async (
    Guid entityId,
    Guid fieldId,
    ITenantContextAccessor tenantAccessor,
    FieldService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    await service.DeactivateAsync(tenant.TenantId, entityId, fieldId, cancellationToken);
    return Results.NoContent();
});

entities.MapPost("/{entityId:guid}/analytics/preview", async (Guid entityId, AnalyticsPreviewRequest request,
    ITenantContextAccessor tenantAccessor, FieldService fieldService, AnalyticsService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var fields = await fieldService.ListAsync(tenant.TenantId, entityId, token);
    return Results.Ok(ToAnalyticsResponse(await service.ExecuteAsync(
        tenant.TenantId, entityId, AnalyticsRequestFactory.Create(request, fields), token)));
});

entities.MapGet("/{entityId:guid}/reports", async (Guid entityId, ITenantContextAccessor tenantAccessor,
    ReportService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok((await service.ListAsync(tenant.TenantId, entityId, token)).Select(ToReportResponse));
});
entities.MapGet("/{entityId:guid}/reports/{reportId:guid}", async (Guid entityId, Guid reportId,
    ITenantContextAccessor tenantAccessor, ReportService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(ToReportResponse(await service.GetAsync(tenant.TenantId, entityId, reportId, token)));
});
entities.MapPost("/{entityId:guid}/reports/preview", async (Guid entityId, AnalyticsPreviewRequest request,
    ITenantContextAccessor tenantAccessor, FieldService fieldService, ReportService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var fields = await fieldService.ListAsync(tenant.TenantId, entityId, token);
    return Results.Ok(ToAnalyticsResponse(await service.PreviewAsync(
        tenant.TenantId, entityId, AnalyticsRequestFactory.Create(request, fields), token)));
});
entities.MapPost("/{entityId:guid}/reports", async (Guid entityId, SaveReportRequest request,
    ITenantContextAccessor tenantAccessor, FieldService fieldService, ReportService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var fields = await fieldService.ListAsync(tenant.TenantId, entityId, token);
    var specification = new ReportSpecification(AnalyticsRequestFactory.Create(request.Query, fields),
        request.Visualization, request.VisualizationConfiguration?.GetRawText());
    var report = await service.CreateAsync(tenant.TenantId, entityId, request.Name, request.Description,
        specification, null, token);
    return Results.Created($"/api/entities/{entityId}/reports/{report.Id}", ToReportResponse(report));
});
entities.MapPatch("/{entityId:guid}/reports/{reportId:guid}", async (Guid entityId, Guid reportId,
    SaveReportRequest request, ITenantContextAccessor tenantAccessor, FieldService fieldService,
    ReportService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var fields = await fieldService.ListAsync(tenant.TenantId, entityId, token);
    var specification = new ReportSpecification(AnalyticsRequestFactory.Create(request.Query, fields),
        request.Visualization, request.VisualizationConfiguration?.GetRawText());
    return Results.Ok(ToReportResponse(await service.UpdateAsync(tenant.TenantId, entityId, reportId,
        request.Name, request.Description, specification, token)));
});
entities.MapDelete("/{entityId:guid}/reports/{reportId:guid}", async (Guid entityId, Guid reportId,
    ITenantContextAccessor tenantAccessor, ReportService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    await service.DeleteAsync(tenant.TenantId, entityId, reportId, token);
    return Results.NoContent();
});
entities.MapPost("/{entityId:guid}/reports/{reportId:guid}/run", async (Guid entityId, Guid reportId,
    ITenantContextAccessor tenantAccessor, ReportService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(ToAnalyticsResponse(await service.RunAsync(tenant.TenantId, entityId, reportId, token)));
});
entities.MapGet("/{entityId:guid}/reports/{reportId:guid}/export", async (Guid entityId, Guid reportId,
    ITenantContextAccessor tenantAccessor, ReportService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant(); var result = await service.RunAsync(tenant.TenantId, entityId, reportId, token);
    var csv = new StringBuilder(); csv.AppendLine(string.Join(',', result.Columns.Select(column => Csv(column.Label))));
    foreach (var row in result.Rows) csv.AppendLine(string.Join(',', result.Columns.Select(column => Csv(
        row.TryGetValue(column.Key, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : null))));
    return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"report-{reportId:D}.csv");
});

entities.MapGet("/{entityId:guid}/metrics", async (Guid entityId, ITenantContextAccessor tenantAccessor,
    MetricService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok((await service.ListAsync(tenant.TenantId, entityId, token)).Select(ToMetricResponse));
});
entities.MapPost("/{entityId:guid}/metrics", async (Guid entityId, SaveMetricRequest request,
    ITenantContextAccessor tenantAccessor, FieldService fieldService, MetricService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant(); var fields = await fieldService.ListAsync(tenant.TenantId, entityId, token);
    var metric = await service.CreateAsync(tenant.TenantId, entityId, request.Name, request.Description,
        request.Aggregate, request.FieldId, RecordQueryFactory.CreateFilter(request.Filter, fields),
        request.Format?.GetRawText(), null, token);
    return Results.Created($"/api/entities/{entityId}/metrics/{metric.Id}", ToMetricResponse(metric));
});
entities.MapPost("/{entityId:guid}/metrics/preview", async (Guid entityId, SaveMetricRequest request,
    ITenantContextAccessor tenantAccessor, FieldService fieldService, MetricService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant(); var fields = await fieldService.ListAsync(tenant.TenantId, entityId, token);
    var result = await service.PreviewAsync(tenant.TenantId, entityId, request.Aggregate, request.FieldId,
        RecordQueryFactory.CreateFilter(request.Filter, fields), token);
    return Results.Ok(new MetricEvaluationResponse(result.Rows.SingleOrDefault()?.GetValueOrDefault("value"), result.GeneratedAt));
});
entities.MapPatch("/{entityId:guid}/metrics/{metricId:guid}", async (Guid entityId, Guid metricId,
    SaveMetricRequest request, ITenantContextAccessor tenantAccessor, FieldService fieldService,
    MetricService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant(); var fields = await fieldService.ListAsync(tenant.TenantId, entityId, token);
    return Results.Ok(ToMetricResponse(await service.UpdateAsync(tenant.TenantId, entityId, metricId,
        request.Name, request.Description, request.Aggregate, request.FieldId,
        RecordQueryFactory.CreateFilter(request.Filter, fields), request.Format?.GetRawText(), token)));
});
entities.MapDelete("/{entityId:guid}/metrics/{metricId:guid}", async (Guid entityId, Guid metricId,
    ITenantContextAccessor tenantAccessor, MetricService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant(); await service.DeleteAsync(tenant.TenantId, entityId, metricId, token);
    return Results.NoContent();
});
entities.MapPost("/{entityId:guid}/metrics/{metricId:guid}/evaluate", async (Guid entityId, Guid metricId,
    ITenantContextAccessor tenantAccessor, MetricService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant(); var result = await service.EvaluateAsync(tenant.TenantId, entityId, metricId, token);
    return Results.Ok(new MetricEvaluationResponse(result.Value, result.EvaluatedAt));
});

entities.MapGet("/{entityId:guid}/alerts", async (Guid entityId, ITenantContextAccessor tenantAccessor,
    AlertService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok((await service.ListAsync(tenant.TenantId, entityId, token)).Select(ToAlertResponse));
});
entities.MapPost("/{entityId:guid}/alerts", async (Guid entityId, SaveAlertRequest request,
    ITenantContextAccessor tenantAccessor, AlertService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var alert = await service.CreateAsync(tenant.TenantId, entityId, request.MetricId, request.Name,
        request.ComparisonOperator, request.Threshold.GetRawText(), request.Interval, request.Timezone,
        TimeSpan.FromSeconds(request.CooldownSeconds), request.NotifyOnRecovery, request.IsEnabled,
        ToAlertActions(request.Actions), null, token);
    return Results.Created($"/api/entities/{entityId}/alerts/{alert.Id}", ToAlertResponse(alert));
});
entities.MapPatch("/{entityId:guid}/alerts/{alertId:guid}", async (Guid entityId, Guid alertId,
    SaveAlertRequest request, ITenantContextAccessor tenantAccessor, AlertService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(ToAlertResponse(await service.UpdateAsync(tenant.TenantId, entityId, alertId,
        request.MetricId, request.Name, request.ComparisonOperator, request.Threshold.GetRawText(),
        request.Interval, request.Timezone, TimeSpan.FromSeconds(request.CooldownSeconds),
        request.NotifyOnRecovery, request.IsEnabled, ToAlertActions(request.Actions), token)));
});
entities.MapDelete("/{entityId:guid}/alerts/{alertId:guid}", async (Guid entityId, Guid alertId,
    ITenantContextAccessor tenantAccessor, AlertService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    await service.DeleteAsync(tenant.TenantId, entityId, alertId, token);
    return Results.NoContent();
});
entities.MapGet("/{entityId:guid}/alerts/{alertId:guid}/history", async (Guid entityId, Guid alertId,
    ITenantContextAccessor tenantAccessor, AlertService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant(); var history = await service.HistoryAsync(tenant.TenantId, entityId, alertId, token);
    return Results.Ok(new AlertHistoryResponse(history.Item1.Select(ToAlertEvaluationResponse).ToArray(),
        history.Item2.Select(ToAlertNotificationResponse).ToArray()));
});
entities.MapPost("/{entityId:guid}/alerts/{alertId:guid}/test", async (Guid entityId, Guid alertId,
    ITenantContextAccessor tenantAccessor, AlertService service, IAlertStore store, IControlPlaneStore control,
    AlertEvaluationWorker worker, TimeProvider timeProvider, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant(); var alert = await service.GetAsync(tenant.TenantId, entityId, alertId, token);
    var storage = await control.GetTenantStorageAsync(tenant.TenantId, token)
        ?? throw new DynamicEntity.Application.Common.NotFoundException("Tenant storage was not found.");
    var now = timeProvider.GetUtcNow();
    await store.UpdateAsync(tenant.TenantId, alert with { IsEnabled = true, NextEvaluationAt = now, UpdatedAt = now }, storage, token);
    await worker.ProcessTenantAsync(tenant.TenantId, $"test:{Guid.NewGuid():N}", token);
    if (!alert.IsEnabled)
    {
        var evaluated = await service.GetAsync(tenant.TenantId, entityId, alertId, token);
        await store.UpdateAsync(tenant.TenantId, evaluated with { IsEnabled = false, UpdatedAt = timeProvider.GetUtcNow() }, storage, token);
    }
    var history = await service.HistoryAsync(tenant.TenantId, entityId, alertId, token);
    return Results.Ok(ToAlertEvaluationResponse(history.Item1.First()));
});

entities.MapGet("/{entityId:guid}/webhooks", async (Guid entityId, ITenantContextAccessor tenantAccessor,
    WebhookSubscriptionService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok((await service.ListAsync(tenant.TenantId, entityId, token)).Select(ToWebhookResponse));
});
entities.MapPost("/{entityId:guid}/webhooks", async (Guid entityId, SaveWebhookSubscriptionRequest request,
    ITenantContextAccessor tenantAccessor, WebhookSubscriptionService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var subscription = await service.CreateAsync(tenant.TenantId, entityId, request.Name, request.Endpoint,
        request.Events, request.IsEnabled, null, token);
    return Results.Created($"/api/entities/{entityId}/webhooks/{subscription.Id}", ToWebhookResponse(subscription));
});
entities.MapPatch("/{entityId:guid}/webhooks/{subscriptionId:guid}", async (Guid entityId, Guid subscriptionId,
    SaveWebhookSubscriptionRequest request, ITenantContextAccessor tenantAccessor,
    WebhookSubscriptionService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(ToWebhookResponse(await service.UpdateAsync(tenant.TenantId, entityId, subscriptionId,
        request.Name, request.Endpoint, request.Events, request.IsEnabled, token)));
});
entities.MapDelete("/{entityId:guid}/webhooks/{subscriptionId:guid}", async (Guid entityId, Guid subscriptionId,
    ITenantContextAccessor tenantAccessor, WebhookSubscriptionService service, CancellationToken token) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    await service.DeleteAsync(tenant.TenantId, entityId, subscriptionId, token);
    return Results.NoContent();
});

entities.MapPost("/{entityId:guid}/records", async (
    Guid entityId,
    CreateRecordRequest request,
    ITenantContextAccessor tenantAccessor,
    RecordService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var record = await service.CreateAsync(tenant.TenantId, entityId, request.Data, null, cancellationToken);
    return Results.Created($"/api/entities/{entityId}/records/{record.Id}", ToRecordResponse(record));
});

entities.MapGet("/{entityId:guid}/records/{recordId:guid}", async (
    Guid entityId,
    Guid recordId,
    ITenantContextAccessor tenantAccessor,
    RecordService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var record = await service.GetAsync(tenant.TenantId, entityId, recordId, cancellationToken);
    return Results.Ok(ToRecordResponse(record));
});

entities.MapGet("/{entityId:guid}/records", async (
    Guid entityId,
    int? pageSize,
    string? cursor,
    ITenantContextAccessor tenantAccessor,
    RecordService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var page = await service.ListAsync(tenant.TenantId, entityId, pageSize ?? 50, cursor, cancellationToken);
    return Results.Ok(new RecordPageResponse(page.Items.Select(ToRecordResponse).ToArray(), page.NextCursor));
});

entities.MapPost("/{entityId:guid}/query", async (
    Guid entityId,
    RecordQueryRequest request,
    ITenantContextAccessor tenantAccessor,
    FieldService fieldService,
    RecordService recordService,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var fields = await fieldService.ListAsync(tenant.TenantId, entityId, cancellationToken);
    var query = RecordQueryFactory.Create(request, fields);
    var page = await recordService.QueryAsync(tenant.TenantId, entityId, query, cancellationToken);
    return Results.Ok(new RecordPageResponse(page.Items.Select(ToRecordResponse).ToArray(), page.NextCursor));
});

entities.MapGet("/{entityId:guid}/fields/{fieldId:guid}/facets", async (
    Guid entityId,
    Guid fieldId,
    ITenantContextAccessor tenantAccessor,
    FacetService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var values = await service.GetValuesAsync(tenant.TenantId, entityId, fieldId, cancellationToken);
    return Results.Ok(values.Select(value => new FacetValueResponse(value.Value, value.RecordCount)));
});

entities.MapGet("/{entityId:guid}/indexes", async (
    Guid entityId,
    ITenantContextAccessor tenantAccessor,
    EntityIndexService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var indexes = await service.ListAsync(tenant.TenantId, entityId, cancellationToken);
    return Results.Ok(indexes.Select(ToIndexResponse));
});

entities.MapPost("/{entityId:guid}/indexes", async (
    Guid entityId,
    CreateEntityIndexRequest request,
    ITenantContextAccessor tenantAccessor,
    EntityIndexService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var columns = request.Columns
        .Select(column => new EntityIndexColumnInput(column.FieldId, column.Descending))
        .ToArray();
    var index = await service.CreateAsync(tenant.TenantId, entityId, columns, cancellationToken);
    return Results.Ok(ToIndexResponse(index));
});

entities.MapDelete("/{entityId:guid}/indexes/{indexId:guid}", async (
    Guid entityId,
    Guid indexId,
    ITenantContextAccessor tenantAccessor,
    EntityIndexService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    await service.DeleteAsync(tenant.TenantId, entityId, indexId, cancellationToken);
    return Results.NoContent();
});

entities.MapPost("/{entityId:guid}/views", async (Guid entityId, CreateViewRequest request,
    ITenantContextAccessor tenantAccessor, ViewService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var view = await service.CreateAsync(tenant.TenantId, entityId, request.Name, request.Definition, null, cancellationToken);
    return Results.Created($"/api/views/{view.Id}", ToViewResponse(view));
});

entities.MapGet("/{entityId:guid}/views", async (Guid entityId, ITenantContextAccessor tenantAccessor,
    ViewService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok((await service.ListAsync(tenant.TenantId, entityId, cancellationToken)).Select(ToViewResponse));
});

app.MapPatch("/api/views/{viewId:guid}", async (Guid viewId, UpdateViewRequest request,
    ITenantContextAccessor tenantAccessor, ViewService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(ToViewResponse(await service.UpdateAsync(
        tenant.TenantId, viewId, request.EntityId, request.Name, request.Definition, cancellationToken)));
});

app.MapDelete("/api/views/{viewId:guid}", async (Guid viewId, ITenantContextAccessor tenantAccessor,
    ViewService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    await service.DeleteAsync(tenant.TenantId, viewId, cancellationToken);
    return Results.NoContent();
});

entities.MapPost("/{entityId:guid}/imports", async (Guid entityId, IFormFile file,
    ITenantContextAccessor tenantAccessor, ImportService service, CancellationToken cancellationToken) =>
{
    if (file.Length == 0) throw new DynamicEntity.Application.Common.ValidationException("The import file is empty.");
    var tenant = tenantAccessor.GetRequiredTenant();
    await using var stream = file.OpenReadStream();
    var job = await service.UploadAsync(tenant.TenantId, entityId, stream, file.FileName, cancellationToken);
    return Results.Accepted($"/api/imports/{job.Id}", ToImportResponse(job));
}).DisableAntiforgery();

app.MapGet("/api/imports/{importId:guid}", async (Guid importId, ITenantContextAccessor tenantAccessor,
    ImportService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(ToImportResponse(await service.GetAsync(tenant.TenantId, importId, cancellationToken)));
});

app.MapPost("/api/imports/{importId:guid}/preview", async (Guid importId, ImportPreviewRequest request,
    ITenantContextAccessor tenantAccessor, ImportService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var mappings = request.Mappings.Select(mapping => new ImportColumnMapping(mapping.SourceColumn, mapping.TargetFieldId)).ToArray();
    var preview = await service.PreviewAsync(tenant.TenantId, importId, mappings, cancellationToken);
    return Results.Ok(new ImportPreviewResponse(preview.TotalRows, preview.ValidRows, preview.InvalidRows, preview.Errors));
});

app.MapPost("/api/imports/{importId:guid}/commit", async (Guid importId, ITenantContextAccessor tenantAccessor,
    ImportService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(new ImportCommitResponse(await service.CommitAsync(tenant.TenantId, importId, cancellationToken)));
});

entities.MapPost("/{entityId:guid}/bulk-update", async (Guid entityId, BulkPatchRequest request,
    ITenantContextAccessor tenantAccessor, BulkRecordService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(new BulkResultResponse(await service.PatchAsync(
        tenant.TenantId, entityId, request.RecordIds, request.Data, null, cancellationToken)));
});

entities.MapPost("/{entityId:guid}/bulk-delete", async (Guid entityId, BulkDeleteRequest request,
    ITenantContextAccessor tenantAccessor, BulkRecordService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    return Results.Ok(new BulkResultResponse(await service.DeleteAsync(
        tenant.TenantId, entityId, request.RecordIds, cancellationToken)));
});

entities.MapPost("/{entityId:guid}/merge", async (Guid entityId, MergeRequest request,
    ITenantContextAccessor tenantAccessor, MergeService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var result = await service.MergeAsync(tenant.TenantId, entityId, request.MatchFieldId, request.Records, null, cancellationToken);
    return Results.Ok(new MergeResultResponse(result.Inserted, result.Updated));
});

entities.MapGet("/{entityId:guid}/records/export", async (Guid entityId, HttpContext http,
    ITenantContextAccessor tenantAccessor, RecordExportService service, CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    http.Response.ContentType = "text/csv; charset=utf-8";
    http.Response.Headers.ContentDisposition = $"attachment; filename=entity-{entityId:D}.csv";
    await service.ExportCsvAsync(tenant.TenantId, entityId, http.Response.Body, cancellationToken);
});

entities.MapPatch("/{entityId:guid}/records/{recordId:guid}", async (
    Guid entityId,
    Guid recordId,
    UpdateRecordRequest request,
    ITenantContextAccessor tenantAccessor,
    RecordService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    var record = await service.UpdateAsync(
        tenant.TenantId, entityId, recordId, request.Data, ParseVersion(request.ExpectedVersion), null, cancellationToken);
    return Results.Ok(ToRecordResponse(record));
});

entities.MapDelete("/{entityId:guid}/records/{recordId:guid}", async (
    Guid entityId,
    Guid recordId,
    HttpRequest request,
    ITenantContextAccessor tenantAccessor,
    RecordService service,
    CancellationToken cancellationToken) =>
{
    var tenant = tenantAccessor.GetRequiredTenant();
    await service.DeleteAsync(
        tenant.TenantId, entityId, recordId, ParseVersion(request.Headers.IfMatch.FirstOrDefault()), cancellationToken);
    return Results.NoContent();
});

app.Run();

static TenantResponse ToTenantResponse(Tenant tenant, EntityStorageLocation? storage) =>
    new(tenant.Id, tenant.Name, tenant.Status.ToString(), tenant.CreatedAt,
        storage is not null, storage?.DatabaseName);

static EntityResponse ToEntityResponse(EntityDefinition entity) =>
    new(entity.Id, entity.Name, entity.DisplayName, entity.Description, entity.Icon, entity.PinnedOrder,
        entity.SchemaVersion, entity.Status.ToString(), entity.CreatedAt, entity.UpdatedAt);

static FieldResponse ToFieldResponse(FieldDefinition field) =>
    new(field.Id, field.Name, field.DisplayName, field.StorageKey, field.DataType, field.IsRequired,
        field.IsUnique, field.IsFilterable, field.IsSortable, field.IsFacetable, field.IsSearchable,
        ParseOptionalJson(field.DefaultValueJson), ParseOptionalJson(field.ConfigurationJson), field.SortOrder,
        field.IndexColumnName);

static RecordResponse ToRecordResponse(DynamicRecord record) =>
    new(record.Id, JsonSerializer.Deserialize<JsonElement>(record.Data), record.CreatedAt, record.CreatedBy,
        record.UpdatedAt, record.UpdatedBy, Convert.ToBase64String(record.Version));

static EntityIndexResponse ToIndexResponse(EntityIndexDefinition index) =>
    new(index.Id, index.IndexName, index.Status, index.CreatedAt,
        index.Columns.Select(column => new EntityIndexColumnResponse(
            column.FieldId, column.PhysicalColumnName, column.SortOrder, column.IsDescending)).ToArray());

static JsonElement? ParseOptionalJson(string? json) =>
    json is null ? null : JsonSerializer.Deserialize<JsonElement>(json);

static byte[] ParseVersion(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        throw new DynamicEntity.Application.Common.ValidationException("An expected row version is required.");
    try
    {
        var bytes = Convert.FromBase64String(value.Trim().Trim('"'));
        if (bytes.Length != 8) throw new FormatException();
        return bytes;
    }
    catch (FormatException)
    {
        throw new DynamicEntity.Application.Common.ValidationException("Expected version must be a base64-encoded SQL rowversion.");
    }
}

static ViewResponse ToViewResponse(ViewDefinition view) =>
    new(view.Id, view.EntityId, view.Name, JsonSerializer.Deserialize<JsonElement>(view.DefinitionJson),
        view.CreatedBy, view.CreatedAt, view.UpdatedAt);

static DashboardResponse ToDashboardResponse(DashboardDefinition dashboard) =>
    new(dashboard.Id, dashboard.Name, JsonSerializer.Deserialize<JsonElement>(dashboard.DefinitionJson),
        dashboard.CreatedBy, dashboard.CreatedAt, dashboard.UpdatedAt);

static ImportJobResponse ToImportResponse(ImportJob job) =>
    new(job.Id, job.EntityId, job.FileName, job.Status, job.Columns, job.TotalRows,
        job.ValidRows, job.InvalidRows, job.CreatedAt, job.UpdatedAt);

static AnalyticsResultResponse ToAnalyticsResponse(AnalyticsResult result) => new(
    result.Columns.Select(column => new AnalyticsColumnResponse(column.Key, column.Label, column.DataType, column.Role)).ToArray(),
    result.Rows.Select(row => (IReadOnlyDictionary<string, JsonElement>)row.ToDictionary(
        item => item.Key, item => JsonSerializer.SerializeToElement(item.Value))).ToArray(),
    result.GeneratedAt, result.Truncated);

static ReportResponse ToReportResponse(ReportDefinition report)
{
    var definition = ReportService.Deserialize(report);
    return new(report.Id, report.EntityId, report.Name, report.Description, ToAnalyticsRequest(definition.Query),
        definition.Visualization, definition.VisualizationConfigurationJson is null ? null :
            JsonSerializer.Deserialize<JsonElement>(definition.VisualizationConfigurationJson),
        report.CreatedBy, report.CreatedAt, report.UpdatedAt);
}

static MetricResponse ToMetricResponse(MetricDefinition metric) => new(
    metric.Id, metric.EntityId, metric.Name, metric.Description, metric.Aggregate, metric.FieldId,
    ToFilterRequest(MetricService.DeserializeFilter(metric.FilterJson)), metric.FormatJson is null ? null :
        JsonSerializer.Deserialize<JsonElement>(metric.FormatJson), metric.CreatedBy, metric.CreatedAt, metric.UpdatedAt);

static AnalyticsPreviewRequest ToAnalyticsRequest(AnalyticsQuery query) => new(
    ToFilterRequest(query.Filter), query.Dimensions, query.Measures, query.Sort, query.Limit);

static FilterNodeRequest? ToFilterRequest(FilterNode? node) => node switch
{
    null => null,
    FilterGroup group => new(group.Logic, group.Conditions.Select(ToFilterRequest).Cast<FilterNodeRequest>().ToArray(), null, null, null),
    FilterCondition condition => new(null, null, condition.FieldId, condition.Operator, condition.Value),
    _ => throw new InvalidOperationException("Unknown filter node.")
};

static AlertResponse ToAlertResponse(AlertDefinition alert) => new(
    alert.Id, alert.EntityId, alert.MetricId, alert.Name, alert.ComparisonOperator,
    JsonSerializer.Deserialize<JsonElement>(alert.ThresholdJson), alert.Interval, alert.Timezone,
    Convert.ToInt32(alert.Cooldown.TotalSeconds), alert.NotifyOnRecovery, alert.IsEnabled,
    alert.Actions.Select(action => new AlertActionResponse(action.Type, action.EmailRecipients, action.WebhookUrls)).ToArray(),
    alert.LastState, alert.LastEvaluatedAt, alert.NextEvaluationAt, alert.CreatedAt, alert.UpdatedAt);

static IReadOnlyList<AlertActionConfiguration> ToAlertActions(IReadOnlyList<SaveAlertActionRequest>? actions) =>
    actions?.Select(action => new AlertActionConfiguration(action.Type, action.EmailRecipients, action.WebhookUrls)).ToArray() ?? [];

static AlertEvaluationResponse ToAlertEvaluationResponse(AlertEvaluation evaluation) => new(
    evaluation.Id, evaluation.AlertId,
    evaluation.ValueJson is null ? null : JsonSerializer.Deserialize<JsonElement>(evaluation.ValueJson),
    JsonSerializer.Deserialize<JsonElement>(evaluation.ThresholdJson), evaluation.State,
    evaluation.Error, evaluation.EvaluatedAt);

static AlertNotificationResponse ToAlertNotificationResponse(AlertNotification notification) => new(
    notification.Id, notification.AlertId, notification.EvaluationId, notification.Channel,
    notification.Status, notification.Attempts, notification.LastError, notification.CreatedAt,
    notification.DeliveredAt, notification.NextAttemptAt);

static WebhookSubscriptionResponse ToWebhookResponse(WebhookSubscription subscription) => new(
    subscription.Id, subscription.EntityId, subscription.Name, subscription.Endpoint,
    subscription.Events, subscription.IsEnabled, subscription.CreatedAt, subscription.UpdatedAt);

static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

public partial class Program;
