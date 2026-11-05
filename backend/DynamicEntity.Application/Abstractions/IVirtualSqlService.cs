using System.Text.Json;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Application.Abstractions;

public interface IVirtualSqlService
{
    Task<VirtualSqlPreview> PreviewAsync(Guid tenantId, Guid? entityId, VirtualSqlDefinition definition,
        IReadOnlyDictionary<string, JsonElement>? parameters, CancellationToken token);

    Task<AnalyticsResult> ExecuteAsync(Guid tenantId, Guid? entityId, VirtualSqlDefinition definition,
        IReadOnlyDictionary<string, JsonElement>? parameters, int limit, bool metric, CancellationToken token);
}
