using System.Text.Json;
using DynamicEntity.Contracts.Analytics;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Contracts.Reports;

public sealed record SaveReportRequest(
    string Name,
    string? Description,
    AnalyticsPreviewRequest Query,
    VisualizationType Visualization = VisualizationType.Table,
    JsonElement? VisualizationConfiguration = null);

public sealed record ReportResponse(
    Guid Id, Guid EntityId, string Name, string? Description,
    AnalyticsPreviewRequest Query, VisualizationType Visualization,
    JsonElement? VisualizationConfiguration, Guid? CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
