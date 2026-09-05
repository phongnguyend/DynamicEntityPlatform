using System.Text.Json;

namespace DynamicEntity.Domain.Queries;

public enum FilterLogic { And, Or }
public enum SortDirection { Asc, Desc }
public enum FilterOperator
{
    Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual,
    Contains, StartsWith, EndsWith, IsNull, IsNotNull, In, NotIn, Between
}

public abstract record FilterNode;
public sealed record FilterGroup(FilterLogic Logic, IReadOnlyList<FilterNode> Conditions) : FilterNode;
public sealed record FilterCondition(Guid FieldId, FilterOperator Operator, JsonElement Value) : FilterNode;
public sealed record RecordSort(Guid FieldId, SortDirection Direction);
