using System.Collections;
using System.Text.Json;
using System.Text.RegularExpressions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.SqlServer.Queries;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DynamicEntity.SqlServer.Analytics;

public sealed record VirtualSqlTable(EntityDefinition Entity, EntityStorageLocation Storage);

public static class VirtualSqlCompiler
{
    private static readonly HashSet<string> Functions = new(("COUNT COUNT_BIG SUM AVG MIN MAX ABS CEILING FLOOR ROUND " +
        "POWER SQRT SIGN CONCAT CONCAT_WS LEN DATALENGTH LOWER UPPER LTRIM RTRIM TRIM REPLACE SUBSTRING LEFT RIGHT " +
        "CHARINDEX PATINDEX STUFF REPLICATE REVERSE SPACE ISNULL NULLIF COALESCE IIF CHOOSE " +
        "DATEADD DATEDIFF DATEDIFF_BIG DATEPART DATENAME DAY MONTH YEAR EOMONTH DATEFROMPARTS DATETIMEFROMPARTS " +
        "GETDATE GETUTCDATE SYSDATETIME SYSUTCDATETIME SYSDATETIMEOFFSET ROW_NUMBER RANK DENSE_RANK NTILE " +
        "LAG LEAD FIRST_VALUE LAST_VALUE STRING_AGG JSON_VALUE JSON_QUERY ISJSON").Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    public static VirtualSqlPreview Compile(VirtualSqlDefinition definition, IReadOnlyList<VirtualSqlTable> tables,
        IReadOnlyDictionary<string, JsonElement>? values, bool allowUnbound)
    {
        if (string.IsNullOrWhiteSpace(definition.Text) || definition.Text.Length > 50000)
        {
            throw new ValidationException("SQL must contain between 1 and 50000 characters.");
        }
        var script = new TSql160Parser(true).Parse(new StringReader(definition.Text), out var errors) as TSqlScript;
        if (errors.Count > 0)
        {
            throw new ValidationException(string.Join(" ", errors.Select(error => $"Line {error.Line}, column {error.Column}: {error.Message}")));
        }
        if (script is null || script.Batches.Count != 1 || script.Batches[0].Statements.Count != 1 ||
            script.Batches[0].Statements[0] is not SelectStatement select || select.Into is not null || select.On is not null ||
            select.OptimizerHints.Count > 0)
        {
            throw new ValidationException("Use one read-only SELECT query, optionally with CTEs. INTO, hints and FOR clauses are not supported.");
        }
        var nodes = Walk(select).ToArray();
        foreach (var node in nodes)
        {
            if (node is TableReference && node is not (NamedTableReference or QueryDerivedTable or QualifiedJoin or UnqualifiedJoin or JoinParenthesisTableReference) ||
                node is GlobalVariableExpression or SelectSetVariable or NextValueForExpression or UserDataTypeReference or OdbcFunctionCall or ForClause ||
                node is FunctionCall call && (call.CallTarget is not null || !Functions.Contains(call.FunctionName.Value)))
            {
                throw new ValidationException($"Unsupported SQL construct: {node.GetType().Name}. Use virtual tables and supported built-in functions only.");
            }
            if (node is ColumnReferenceExpression column && column.MultiPartIdentifier?.Identifiers.Count > 2)
            {
                throw new ValidationException("Columns must use a field name or table alias and field name; database/schema qualifiers are not allowed.");
            }
        }
        var cteDefinitions = select.WithCtesAndXmlNamespaces?.CommonTableExpressions.ToArray() ?? [];
        var ctes = cteDefinitions.Select(cte => cte.ExpressionName.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edits = new List<(int Start, int Length, string Text)>();
        foreach (var table in nodes.OfType<NamedTableReference>())
        {
            if (table.SchemaObject.Identifiers.Count != 1 || table.TableHints.Count > 0 || table.TableSampleClause is not null || table.TemporalClause is not null)
            {
                throw new ValidationException("Use unqualified virtual entity names without table hints, sampling or temporal clauses.");
            }
            var name = table.SchemaObject.BaseIdentifier.Value;
            if (ctes.Contains(name))
            {
                var targetCte = cteDefinitions.First(cte => cte.ExpressionName.Value.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (table.StartOffset < targetCte.StartOffset + targetCte.FragmentLength)
                {
                    throw new ValidationException("CTEs may reference earlier CTEs only. Recursive and forward CTE references are not supported.");
                }
                continue;
            }
            var matches = tables.Where(candidate => candidate.Entity.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
            {
                throw new ValidationException($"Unknown or ambiguous virtual table '{name}'. Use an entity name from the schema browser.");
            }
            var target = matches[0];
            var columns = new List<string> { "[Id]", "[CreatedAt]", "[UpdatedAt]" };
            if (target.Entity.Fields.Any(field => new[] { "Id", "CreatedAt", "UpdatedAt" }.Contains(field.Name, StringComparer.OrdinalIgnoreCase)))
            {
                throw new ValidationException($"Entity '{name}' has a field conflicting with a system column.");
            }
            columns.AddRange(target.Entity.Fields.Select(field => $"{SqlFieldExpression.ForValue(field)} AS {Quote(field.Name)}"));
            var predicate = target.Storage.RequiresEntityPredicate ? $" WHERE [EntityId] = '{target.Entity.Id:D}'" : "";
            var replacement = $"(SELECT {string.Join(", ", columns)} FROM [dbo].{Quote(target.Storage.TableName)}{predicate}) AS {Quote(table.Alias?.Value ?? name)}";
            edits.Add((table.StartOffset, table.FragmentLength, replacement));
        }
        var supplied = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? new Dictionary<string, JsonElement>())
        {
            if (!supplied.TryAdd(value.Key, value.Value))
            {
                throw new ValidationException($"Duplicate supplied parameter '{value.Key}'. Parameter names are case-insensitive.");
            }
        }
        var definitions = new Dictionary<string, VirtualSqlParameter>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in definition.Parameters ?? [])
        {
            if (string.IsNullOrEmpty(parameter.Parameter) || !Regex.IsMatch(parameter.Parameter, "^[A-Za-z][A-Za-z0-9_]{0,63}$") || !definitions.TryAdd(parameter.Parameter, parameter))
            {
                throw new ValidationException("SQL parameter names must be unique and start with a letter followed by up to 63 letters, digits or underscores.");
            }
        }
        var parameters = new List<VirtualSqlValue>();
        foreach (var name in nodes.OfType<VariableReference>().Select(variable => variable.Name[1..]).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!definitions.TryGetValue(name, out var parameter))
            {
                throw new ValidationException($"Define the type and optional default for parameter '@{name}'.");
            }
            var value = supplied.TryGetValue(name, out var input) ? input : parameter.DefaultValue;
            if (value is null && !allowUnbound)
            {
                throw new ValidationException($"Provide a value for parameter '{name}'.");
            }
            parameters.Add(new("@" + name, ConvertValue(parameter, value)));
        }
        var sql = definition.Text;
        foreach (var edit in edits.OrderByDescending(edit => edit.Start))
        {
            sql = sql.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        }
        return new(sql, parameters, definition.Text, parameters);
    }

    public static object? ConvertValue(VirtualSqlParameter parameter, JsonElement? value)
    {
        var type = parameter.Type;
        if (type is not ("Text" or "Integer" or "Decimal" or "Boolean" or "Date" or "DateTime" or "Guid"))
        {
            throw new ValidationException($"Unsupported type '{type}' for parameter '{parameter.Parameter}'.");
        }
        if (value is null || value.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        var text = value.Value.ToString();
        try
        {
            return type switch
            {
                "Integer" => long.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
                "Decimal" => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
                "Boolean" => bool.Parse(text),
                "Date" => DateOnly.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue),
                "DateTime" => DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                "Guid" => Guid.Parse(text),
                _ when value.Value.ValueKind == JsonValueKind.String => text,
                _ => throw new FormatException()
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new ValidationException($"Parameter '{parameter.Parameter}' must be a valid {type} value.");
        }
    }

    private static string Quote(string name) => PhysicalName.QuoteSqlIdentifier(name);

    // Traverse AST child properties only; token streams and parent metadata are not syntax children.
    private static IEnumerable<TSqlFragment> Walk(TSqlFragment node, int depth = 0)
    {
        if (depth > 128)
        {
            throw new ValidationException("SQL nesting exceeds the supported depth.");
        }
        yield return node;
        foreach (var property in node.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length != 0 || property.Name == "ScriptTokenStream")
            {
                continue;
            }
            if (property.GetValue(node) is TSqlFragment child)
            {
                foreach (var descendant in Walk(child, depth + 1))
                {
                    yield return descendant;
                }
            }
            else if (property.GetValue(node) is IEnumerable children)
            {
                foreach (var item in children.OfType<TSqlFragment>())
                {
                    foreach (var descendant in Walk(item, depth + 1))
                    {
                        yield return descendant;
                    }
                }
            }
        }
    }
}
