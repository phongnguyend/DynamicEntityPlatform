using System.Text.Json;
using System.Text.Json.Serialization;
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
using DynamicEntity.Contracts.Entities;
using DynamicEntity.Contracts.Fields;
using DynamicEntity.Contracts.Records;
using DynamicEntity.Contracts.Imports;
using DynamicEntity.Contracts.Tenants;
using DynamicEntity.Contracts.Views;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Imports;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using DynamicEntity.Domain.Validation;
using DynamicEntity.Domain.Views;
using DynamicEntity.SqlServer;
using DynamicEntity.Infrastructure.Imports;
using DynamicEntity.Infrastructure.Authorization;
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
builder.Services.AddSingleton<IEntityMetadataStore, SqlServerEntityMetadataStore>();
builder.Services.AddSingleton<IFieldMetadataStore, SqlServerFieldMetadataStore>();
builder.Services.AddSingleton<IEntityStorageResolver, EntityStorageResolver>();
builder.Services.AddSingleton<IRecordStore, SqlServerRecordStore>();
builder.Services.AddSingleton<IRecordConstraintValidator, SqlServerRecordConstraintValidator>();
builder.Services.AddSingleton<IFacetStore, SqlServerFacetStore>();
builder.Services.AddSingleton<IEntityIndexManager, SqlServerEntityIndexManager>();
builder.Services.AddSingleton<IViewStore, SqlServerViewStore>();
builder.Services.AddSingleton<IImportStore, SqlServerImportStore>();
builder.Services.AddSingleton<IBulkRecordStore, SqlServerBulkRecordStore>();
builder.Services.AddSingleton<IRecordMergeStore, SqlServerRecordMergeStore>();
builder.Services.AddSingleton<ITabularFileParser, TabularFileParser>();
builder.Services.AddSingleton<IEntityAuthorizationService, AllowAllEntityAuthorizationService>();
builder.Services.AddSingleton(new RecordValidationOptions(AllowUnknownFields: false));
builder.Services.AddSingleton<IRecordValidator, RecordValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<EntityService>();
builder.Services.AddScoped<FieldService>();
builder.Services.AddScoped<RecordService>();
builder.Services.AddScoped<FacetService>();
builder.Services.AddScoped<EntityIndexService>();
builder.Services.AddScoped<ViewService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<BulkRecordService>();
builder.Services.AddScoped<MergeService>();
builder.Services.AddScoped<RecordExportService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContextAccessor, HttpTenantContextAccessor>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddSqlClientInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

var app = builder.Build();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/api/tenants", async (
    CreateTenantRequest request,
    TenantService service,
    CancellationToken cancellationToken) =>
{
    var tenant = await service.CreateAsync(request.Name, cancellationToken);
    return Results.Created($"/api/tenants/{tenant.Id}", ToTenantResponse(tenant));
});

app.MapGet("/api/tenants", async (TenantService service, CancellationToken cancellationToken) =>
    Results.Ok((await service.ListAsync(cancellationToken)).Select(ToTenantResponse)));

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
    var allowed = http.Request.Method == HttpMethods.Get
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
        request.Name, request.DisplayName, request.Description, cancellationToken)));
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

static TenantResponse ToTenantResponse(Tenant tenant) =>
    new(tenant.Id, tenant.Name, tenant.Status.ToString(), tenant.CreatedAt);

static EntityResponse ToEntityResponse(EntityDefinition entity) =>
    new(entity.Id, entity.Name, entity.DisplayName, entity.Description, entity.SchemaVersion,
        entity.Status.ToString(), entity.CreatedAt, entity.UpdatedAt);

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

static ImportJobResponse ToImportResponse(ImportJob job) =>
    new(job.Id, job.EntityId, job.FileName, job.Status, job.Columns, job.TotalRows,
        job.ValidRows, job.InvalidRows, job.CreatedAt, job.UpdatedAt);

public partial class Program;
