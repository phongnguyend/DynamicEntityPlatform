using System.Text.Json;
using DynamicEntity.Domain.Queries;

namespace DynamicEntity.Contracts.Records;

public sealed record FilterNodeRequest(
    FilterLogic? Logic,
    IReadOnlyList<FilterNodeRequest>? Conditions,
    Guid? FieldId,
    FilterOperator? Operator,
    JsonElement? Value);

public sealed record RecordSortRequest(Guid FieldId, SortDirection Direction);

public sealed record RecordQueryRequest(
    FilterNodeRequest? Filter,
    IReadOnlyList<RecordSortRequest>? Sort,
    int PageSize = 50,
    string? Cursor = null);
