using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Domain.Views;

public sealed record GlobalViewSpecification(VirtualSqlDefinition Sql, int RowLimit = 100,
    IReadOnlyList<string>? ColumnOrder = null, IReadOnlyList<string>? HiddenColumns = null, string? Color = null);

public sealed record GlobalViewDefinition(Guid Id, string Name, string? Description,
    GlobalViewSpecification Definition, Guid? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
