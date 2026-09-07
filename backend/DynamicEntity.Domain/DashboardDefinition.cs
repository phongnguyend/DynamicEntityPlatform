namespace DynamicEntity.Domain.Dashboards;

public sealed record DashboardDefinition(
    Guid Id,
    Guid TenantId,
    string Name,
    string DefinitionJson,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
