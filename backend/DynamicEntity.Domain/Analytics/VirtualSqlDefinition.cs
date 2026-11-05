using System.Text.Json;

namespace DynamicEntity.Domain.Analytics;

public sealed record VirtualSqlParameter(string Parameter, string Type = "Text", JsonElement? DefaultValue = null);

public sealed record VirtualSqlDefinition(
    bool Enabled, string Text, IReadOnlyList<VirtualSqlParameter> Parameters,
    IReadOnlyList<string>? MeasureColumns = null);

public sealed record VirtualSqlPreview(string Sql, IReadOnlyList<VirtualSqlValue> Parameters,
    string VirtualSql, IReadOnlyList<VirtualSqlValue> VirtualParameters);

public sealed record VirtualSqlValue(string Name, object? Value);
