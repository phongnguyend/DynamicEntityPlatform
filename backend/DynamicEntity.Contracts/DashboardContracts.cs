using System.Text.Json;

namespace DynamicEntity.Contracts.Dashboards;

public sealed record CreateDashboardRequest(string Name, JsonElement Definition);
public sealed record UpdateDashboardRequest(string Name, JsonElement Definition);
public sealed record DashboardResponse(
    Guid Id, string Name, JsonElement Definition, Guid? CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
