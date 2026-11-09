using System.Text.Json;
using DynamicEntity.Domain.Views;

namespace DynamicEntity.Contracts.Views;

public sealed record SaveGlobalViewRequest(string Name, string? Description, GlobalViewSpecification Definition);
public sealed record PreviewGlobalViewRequest(GlobalViewSpecification Definition, IReadOnlyDictionary<string, JsonElement>? Parameters = null);
public sealed record RunGlobalViewRequest(IReadOnlyDictionary<string, JsonElement>? Parameters = null);
