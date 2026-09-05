using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Analytics;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Application.Metrics;

public sealed class MetricService(IControlPlaneStore controlPlane,IEntityMetadataStore entities,IFieldMetadataStore fields,IMetricStore metrics,AnalyticsService analytics,AnalyticsQueryValidator validator,TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions=new(){Converters={new JsonStringEnumConverter()}};
    public async Task<MetricDefinition> CreateAsync(Guid tenantId,Guid entityId,string name,string? description,AggregateFunction aggregate,Guid? fieldId,Domain.Queries.FilterGroup? filter,string? formatJson,Guid? userId,CancellationToken token)
    {ValidateName(name);var s=await RequireEntityAsync(tenantId,entityId,token);ValidateFormat(formatJson);var q=Query(aggregate,fieldId,filter);validator.Validate(entityId,q,await fields.ListAsync(tenantId,entityId,s,token));var now=timeProvider.GetUtcNow();var m=new MetricDefinition(Guid.NewGuid(),entityId,name.Trim(),description?.Trim(),aggregate,fieldId,filter is null?null:JsonSerializer.Serialize(filter,JsonOptions),formatJson,userId,now,now);return await metrics.CreateAsync(tenantId,m,s,token);}
    public async Task<IReadOnlyList<MetricDefinition>> ListAsync(Guid tenantId,Guid entityId,CancellationToken token){var s=await RequireEntityAsync(tenantId,entityId,token);return await metrics.ListAsync(tenantId,entityId,s,token);}
    public async Task<MetricDefinition> GetAsync(Guid tenantId,Guid entityId,Guid id,CancellationToken token){var s=await RequireEntityAsync(tenantId,entityId,token);return await metrics.GetAsync(tenantId,entityId,id,s,token)??throw new NotFoundException($"Metric '{id}' was not found.");}
    public async Task<MetricDefinition> UpdateAsync(Guid tenantId,Guid entityId,Guid id,string name,string? description,AggregateFunction aggregate,Guid? fieldId,Domain.Queries.FilterGroup? filter,string? formatJson,CancellationToken token)
    {ValidateName(name);ValidateFormat(formatJson);var s=await RequireEntityAsync(tenantId,entityId,token);validator.Validate(entityId,Query(aggregate,fieldId,filter),await fields.ListAsync(tenantId,entityId,s,token));var current=await metrics.GetAsync(tenantId,entityId,id,s,token)??throw new NotFoundException($"Metric '{id}' was not found.");var updated=current with{Name=name.Trim(),Description=description?.Trim(),Aggregate=aggregate,FieldId=fieldId,FilterJson=filter is null?null:JsonSerializer.Serialize(filter,JsonOptions),FormatJson=formatJson,UpdatedAt=timeProvider.GetUtcNow()};return await metrics.UpdateAsync(tenantId,updated,s,token)??throw new NotFoundException($"Metric '{id}' was not found.");}
    public async Task DeleteAsync(Guid tenantId,Guid entityId,Guid id,CancellationToken token){var s=await RequireEntityAsync(tenantId,entityId,token);if(!await metrics.DeleteAsync(tenantId,entityId,id,s,token))throw new ConflictException($"Metric '{id}' was not found or is referenced by an alert.");}
    public async Task<(object? Value,DateTimeOffset EvaluatedAt)> EvaluateAsync(Guid tenantId,Guid entityId,Guid id,CancellationToken token){using var activity=AnalyticsTelemetry.Source.StartActivity("metric.evaluate");activity?.SetTag("tenant.id",tenantId);activity?.SetTag("entity.id",entityId);activity?.SetTag("metric.id",id);var m=await GetAsync(tenantId,entityId,id,token);var result=await analytics.ExecuteAsync(tenantId,entityId,Query(m.Aggregate,m.FieldId,DeserializeFilter(m.FilterJson)),token);return(result.Rows.SingleOrDefault()?.GetValueOrDefault("value"),result.GeneratedAt);}
    public Task<AnalyticsResult> PreviewAsync(Guid tenantId,Guid entityId,AggregateFunction aggregate,Guid? fieldId,Domain.Queries.FilterGroup? filter,CancellationToken token)=>analytics.ExecuteAsync(tenantId,entityId,Query(aggregate,fieldId,filter),token);
    public static Domain.Queries.FilterGroup? DeserializeFilter(string? json)=>json is null?null:JsonSerializer.Deserialize<Domain.Queries.FilterGroup>(json,JsonOptions);
    private static AnalyticsQuery Query(AggregateFunction aggregate,Guid? fieldId,Domain.Queries.FilterGroup? filter)=>new(filter,[],[new AnalyticsMeasure(aggregate,fieldId,"value")],[],1);
    private async Task<Domain.Storage.EntityStorageLocation> RequireEntityAsync(Guid tenantId,Guid entityId,CancellationToken token){var s=await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId,token);_=await entities.GetAsync(tenantId,entityId,s,token)??throw new NotFoundException($"Entity '{entityId}' was not found.");return s;}
    private static void ValidateName(string n){if(n.Trim().Length is <1 or >200)throw new ValidationException("Metric name must contain between 1 and 200 characters.");}
    private static void ValidateFormat(string? json){if(json is null)return;try{using var d=JsonDocument.Parse(json);if(d.RootElement.ValueKind!=JsonValueKind.Object)throw new ValidationException("Metric format must be a JSON object.");}catch(JsonException){throw new ValidationException("Metric format must be valid JSON.");}}
}
