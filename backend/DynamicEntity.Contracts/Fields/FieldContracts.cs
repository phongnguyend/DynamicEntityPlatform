using System.Text.Json;
using DynamicEntity.Domain.Entities;

namespace DynamicEntity.Contracts.Fields;

public sealed record CreateFieldRequest(
    string Name,
    string DisplayName,
    FieldDataType DataType,
    bool IsRequired = false,
    bool IsUnique = false,
    bool IsFilterable = false,
    bool IsSortable = false,
    bool IsFacetable = false,
    bool IsSearchable = false,
    JsonElement? DefaultValue = null,
    JsonElement? Configuration = null,
    int SortOrder = 0);

public sealed record FieldResponse(
    Guid Id,
    string Name,
    string DisplayName,
    string StorageKey,
    FieldDataType DataType,
    bool IsRequired,
    bool IsUnique,
    bool IsFilterable,
    bool IsSortable,
    bool IsFacetable,
    bool IsSearchable,
    JsonElement? DefaultValue,
    JsonElement? Configuration,
    int SortOrder,
    string? IndexColumnName);

public sealed record UpdateFieldRequest(
    string? Name = null,
    string? DisplayName = null,
    bool? IsRequired = null,
    bool? IsUnique = null,
    bool? IsFilterable = null,
    bool? IsSortable = null,
    bool? IsFacetable = null,
    bool? IsSearchable = null,
    JsonElement? Configuration = null,
    int? SortOrder = null);
