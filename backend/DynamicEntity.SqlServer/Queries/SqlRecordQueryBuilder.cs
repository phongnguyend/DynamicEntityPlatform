using System.Globalization;
using System.Text;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Queries;
using DynamicEntity.Domain.Storage;

namespace DynamicEntity.SqlServer.Queries;

public sealed record SqlQueryParameter(string Name, object Value);
public sealed record SqlRecordQueryPlan(string Sql, IReadOnlyList<SqlQueryParameter> Parameters, int Offset, bool UsesOffset);

public static class SqlRecordQueryBuilder
{
    public static SqlRecordQueryPlan Build(EntityDefinition entity, EntityStorageLocation storage, RecordQuery query)
    {
        var parameters = new List<SqlQueryParameter>();
        var where = new List<string>();
        if (storage.RequiresEntityPredicate)
        {
            where.Add("EntityId = @entityId");
            parameters.Add(new("@entityId", entity.Id));
        }
        if (query.Filter is not null && query.Filter.Conditions.Count != 0)
            where.Add(SqlFilterExpression.Build(query.Filter, entity.Fields.ToDictionary(field => field.Id), parameters));

        var customSort = query.Sort is { Count: > 0 };
        var offset = 0;
        if (query.Cursor is not null)
        {
            if (customSort)
                offset = DecodeOffset(query.Cursor);
            else
            {
                var cursor = DecodeKeyset(query.Cursor);
                where.Add("(CreatedAt < @cursorCreatedAt OR (CreatedAt = @cursorCreatedAt AND Id < @cursorId))");
                parameters.Add(new("@cursorCreatedAt", cursor.CreatedAt.UtcDateTime));
                parameters.Add(new("@cursorId", cursor.Id));
            }
        }

        parameters.Add(new("@take", query.PageSize + 1));
        var table = PhysicalName.QuoteSqlIdentifier(storage.TableName);
        var whereSql = where.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", where)}";
        string orderAndPage;
        if (customSort)
        {
            var fields = entity.Fields.ToDictionary(field => field.Id);
            var sorts = query.Sort!.Select(sort =>
            {
                var field = fields[sort.FieldId];
                return $"{SqlFieldExpression.ForValue(field)} {(sort.Direction == SortDirection.Asc ? "ASC" : "DESC")}";
            }).Append("Id ASC");
            parameters.Add(new("@offset", offset));
            orderAndPage = $" ORDER BY {string.Join(", ", sorts)} OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY";
        }
        else
        {
            orderAndPage = " ORDER BY CreatedAt DESC, Id DESC OFFSET 0 ROWS FETCH NEXT @take ROWS ONLY";
        }

        var sql = $"SELECT Id, Data, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, Version FROM dbo.{table}{whereSql}{orderAndPage};";
        return new SqlRecordQueryPlan(sql, parameters, offset, customSort);
    }

    public static string EncodeNextCursor(DynamicRecord record, int nextOffset, bool usesOffset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(usesOffset
            ? $"o|{nextOffset.ToString(CultureInfo.InvariantCulture)}"
            : $"k|{record.CreatedAt:O}|{record.Id:D}"));

    private static int DecodeOffset(string encoded)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split('|');
            if (parts.Length != 2 || parts[0] != "o" || !int.TryParse(parts[1], out var offset) || offset < 0) throw new FormatException();
            return offset;
        }
        catch (FormatException) { throw new ValidationException("The query cursor is invalid."); }
    }

    private static (DateTimeOffset CreatedAt, Guid Id) DecodeKeyset(string encoded)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split('|');
            if (parts.Length != 3 || parts[0] != "k") throw new FormatException();
            return (DateTimeOffset.Parse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Guid.Parse(parts[2]));
        }
        catch (FormatException) { throw new ValidationException("The query cursor is invalid."); }
    }
}
