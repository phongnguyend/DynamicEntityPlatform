using DynamicEntity.Domain.Storage;

namespace DynamicEntity.Domain.Entities;

public enum FieldDataType
{
    Text,
    LongText,
    Integer,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Email,
    Url,
    Choice,
    MultiChoice,
    Lookup
}

public sealed record FieldDefinition(
    Guid Id,
    Guid EntityId,
    string Name,
    string DisplayName,
    FieldDataType DataType,
    bool IsRequired,
    bool IsUnique,
    bool IsFilterable,
    bool IsSortable,
    bool IsFacetable,
    bool IsSearchable,
    string? DefaultValueJson,
    string? ConfigurationJson,
    int SortOrder,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string StorageKey => PhysicalName.ForFieldStorageKey(Id);
    public string? IndexColumnName { get; init; }
}
