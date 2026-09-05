using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;

namespace DynamicEntity.SqlServer.Queries;

public static class SqlFieldExpression
{
    public static string ForValue(FieldDefinition field)
    {
        if (field.IndexColumnName is not null)
            return PhysicalName.QuoteSqlIdentifier(field.IndexColumnName);
        var json = $"JSON_VALUE(Data, '$.{field.StorageKey}')";
        return field.DataType switch
        {
            FieldDataType.Integer => $"TRY_CONVERT(BIGINT, {json})",
            FieldDataType.Decimal => $"TRY_CONVERT(DECIMAL(38, 10), {json})",
            FieldDataType.Boolean => $"TRY_CONVERT(BIT, {json})",
            FieldDataType.Date => $"TRY_CONVERT(DATE, {json}, 23)",
            FieldDataType.DateTime => $"TRY_CONVERT(DATETIME2(7), {json}, 127)",
            FieldDataType.Lookup => $"TRY_CONVERT(UNIQUEIDENTIFIER, {json})",
            _ => json
        };
    }
}
