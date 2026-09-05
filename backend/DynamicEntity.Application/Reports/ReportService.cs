using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Analytics;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Application.Reports;

public sealed class ReportService(
    IControlPlaneStore controlPlane, IEntityMetadataStore entities, IFieldMetadataStore fields,
    IReportStore reports, AnalyticsService analytics, AnalyticsQueryValidator validator,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    public Task<AnalyticsResult> PreviewAsync(Guid tenantId, Guid entityId, AnalyticsQuery query, CancellationToken token) =>
        analytics.ExecuteAsync(tenantId, entityId, query, token);

    public async Task<ReportDefinition> CreateAsync(Guid tenantId, Guid entityId, string name, string? description,
        ReportSpecification specification, Guid? userId, CancellationToken token)
    {
        ValidateName(name); ValidateSpecification(specification); var storage=await RequireEntityAsync(tenantId,entityId,token);
        validator.Validate(entityId,specification.Query,await fields.ListAsync(tenantId,entityId,storage,token));
        var now=timeProvider.GetUtcNow(); var report=new ReportDefinition(Guid.NewGuid(),entityId,name.Trim(),description?.Trim(),JsonSerializer.Serialize(specification,JsonOptions),userId,now,now);
        return await reports.CreateAsync(tenantId,report,storage,token);
    }
    public async Task<IReadOnlyList<ReportDefinition>> ListAsync(Guid tenantId,Guid entityId,CancellationToken token){var s=await RequireEntityAsync(tenantId,entityId,token);return await reports.ListAsync(tenantId,entityId,s,token);}
    public async Task<ReportDefinition> GetAsync(Guid tenantId,Guid entityId,Guid id,CancellationToken token){var s=await RequireEntityAsync(tenantId,entityId,token);return await reports.GetAsync(tenantId,entityId,id,s,token)??throw new NotFoundException($"Report '{id}' was not found.");}
    public async Task<ReportDefinition> UpdateAsync(Guid tenantId,Guid entityId,Guid id,string name,string? description,ReportSpecification specification,CancellationToken token)
    {ValidateName(name);ValidateSpecification(specification);var s=await RequireEntityAsync(tenantId,entityId,token);validator.Validate(entityId,specification.Query,await fields.ListAsync(tenantId,entityId,s,token));var current=await reports.GetAsync(tenantId,entityId,id,s,token)??throw new NotFoundException($"Report '{id}' was not found.");var updated=current with{Name=name.Trim(),Description=description?.Trim(),DefinitionJson=JsonSerializer.Serialize(specification,JsonOptions),UpdatedAt=timeProvider.GetUtcNow()};return await reports.UpdateAsync(tenantId,updated,s,token)??throw new NotFoundException($"Report '{id}' was not found.");}
    public async Task DeleteAsync(Guid tenantId,Guid entityId,Guid id,CancellationToken token){var s=await RequireEntityAsync(tenantId,entityId,token);if(!await reports.DeleteAsync(tenantId,entityId,id,s,token))throw new NotFoundException($"Report '{id}' was not found.");}
    public async Task<AnalyticsResult> RunAsync(Guid tenantId,Guid entityId,Guid id,CancellationToken token){using var activity=AnalyticsTelemetry.Source.StartActivity("report.run");activity?.SetTag("tenant.id",tenantId);activity?.SetTag("entity.id",entityId);activity?.SetTag("report.id",id);var report=await GetAsync(tenantId,entityId,id,token);return await analytics.ExecuteAsync(tenantId,entityId,Deserialize(report).Query,token);}
    public static ReportSpecification Deserialize(ReportDefinition report)=>JsonSerializer.Deserialize<ReportSpecification>(report.DefinitionJson,JsonOptions)??throw new ValidationException($"Report '{report.Id}' has an invalid definition.");
    private async Task<Domain.Storage.EntityStorageLocation> RequireEntityAsync(Guid tenantId,Guid entityId,CancellationToken token){var s=await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId,token);_=await entities.GetAsync(tenantId,entityId,s,token)??throw new NotFoundException($"Entity '{entityId}' was not found.");return s;}
    private static void ValidateName(string name){if(name.Trim().Length is <1 or >200)throw new ValidationException("Report name must contain between 1 and 200 characters.");}
    private static void ValidateSpecification(ReportSpecification specification)
    {
        if (!Enum.IsDefined(specification.Visualization)) throw new ValidationException("Report visualization is invalid.");
        if (specification.Visualization == VisualizationType.Number &&
            (specification.Query.Dimensions.Count != 0 || specification.Query.Measures.Count != 1))
            throw new ValidationException("Number visualizations require exactly one measure and no dimensions.");
        if (specification.Visualization is VisualizationType.Bar or VisualizationType.Line or VisualizationType.Donut &&
            (specification.Query.Dimensions.Count == 0 || specification.Query.Measures.Count == 0))
            throw new ValidationException($"{specification.Visualization} visualizations require a dimension and a measure.");
        if (specification.VisualizationConfigurationJson is null) return;
        try { using var document=JsonDocument.Parse(specification.VisualizationConfigurationJson);if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new ValidationException("Visualization configuration must be a JSON object."); }
        catch(JsonException){throw new ValidationException("Visualization configuration must be valid JSON.");}
    }
}
