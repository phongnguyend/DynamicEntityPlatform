using System.Text;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Storage;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerEntitySqlSchemaReader(
    IControlPlaneStore controlPlane,
    IEntityStorageResolver resolver) : IEntitySqlSchemaReader
{
    public async Task<EntitySqlSchema> GetAsync(Guid tenantId, Guid entityId, CancellationToken cancellationToken)
    {
        _ = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        var storage = await resolver.ResolveAsync(tenantId, entityId, cancellationToken);
        var table = $"[dbo].{PhysicalName.QuoteSqlIdentifier(storage.TableName)}";
        const string query = """
            DECLARE @objectId INT = OBJECT_ID(@table, N'U');
            SELECT c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable,
                cc.definition, cc.is_persisted, dc.name, dc.definition, c.collation_name,
                CONVERT(VARCHAR(40), ic.seed_value), CONVERT(VARCHAR(40), ic.increment_value)
            FROM sys.columns c
            JOIN sys.types t ON t.user_type_id = c.user_type_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
            LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE c.object_id = @objectId ORDER BY c.column_id;

            SELECT name, definition, is_not_trusted, is_disabled
            FROM sys.check_constraints WHERE parent_object_id = @objectId ORDER BY name;

            SELECT i.name, i.is_primary_key, i.is_unique_constraint, i.is_unique, i.type,
                i.filter_definition, i.is_disabled,
                (SELECT STRING_AGG(CONVERT(NVARCHAR(MAX), QUOTENAME(c.name) +
                    CASE WHEN ic.is_descending_key = 1 THEN N' DESC' ELSE N' ASC' END), N', ')
                    WITHIN GROUP (ORDER BY ic.key_ordinal)
                 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                 WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0),
                (SELECT STRING_AGG(CONVERT(NVARCHAR(MAX), QUOTENAME(c.name)), N', ')
                    WITHIN GROUP (ORDER BY ic.index_column_id)
                 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                 WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
            FROM sys.indexes i WHERE i.object_id = @objectId AND i.type IN (1, 2) AND i.is_hypothetical = 0
            ORDER BY i.is_primary_key DESC, i.index_id;
            """;
        await using var connection = SqlServerTenantConnection.Create(storage);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var column = new StringBuilder("    ").Append(PhysicalName.QuoteSqlIdentifier(reader.GetString(0)));
            if (!reader.IsDBNull(6))
            {
                column.Append(" AS ").Append(reader.GetString(6));
                if (reader.GetBoolean(7))
                {
                    column.Append(" PERSISTED");
                }
            }
            else
            {
                var type = reader.GetString(1);
                column.Append(' ').Append(type.ToUpperInvariant());
                if (type is "nvarchar" or "nchar" or "varchar" or "char" or "varbinary" or "binary")
                {
                    var length = reader.GetInt16(2);
                    column.Append('(').Append(length == -1 ? "MAX" :
                        (type is "nvarchar" or "nchar" ? length / 2 : length).ToString()).Append(')');
                }
                else if (type is "decimal" or "numeric")
                {
                    column.Append($"({reader.GetByte(3)},{reader.GetByte(4)})");
                }
                else if (type is "datetime2" or "datetimeoffset" or "time")
                {
                    column.Append($"({reader.GetByte(4)})");
                }
                if (!reader.IsDBNull(10))
                {
                    column.Append(" COLLATE ").Append(reader.GetString(10));
                }
                if (!reader.IsDBNull(11))
                {
                    column.Append($" IDENTITY({reader.GetString(11)},{reader.GetString(12)})");
                }
                column.Append(reader.GetBoolean(5) ? " NULL" : " NOT NULL");
                if (!reader.IsDBNull(9))
                {
                    column.Append(" CONSTRAINT ").Append(PhysicalName.QuoteSqlIdentifier(reader.GetString(8)))
                        .Append(" DEFAULT ").Append(reader.GetString(9));
                }
            }
            columns.Add(column.ToString());
        }
        if (columns.Count == 0)
        {
            throw new NotFoundException("The entity's physical SQL table was not found or is not visible.");
        }

        var sql = new StringBuilder().AppendLine($"CREATE TABLE {table}")
            .AppendLine("(").AppendLine(string.Join("," + Environment.NewLine, columns)).AppendLine(");");
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = PhysicalName.QuoteSqlIdentifier(reader.GetString(0));
            sql.AppendLine().Append($"ALTER TABLE {table} WITH ")
                .Append(reader.GetBoolean(2) ? "NOCHECK" : "CHECK")
                .AppendLine($" ADD CONSTRAINT {name} CHECK {reader.GetString(1)};");
            if (reader.GetBoolean(3))
            {
                sql.AppendLine($"ALTER TABLE {table} NOCHECK CONSTRAINT {name};");
            }
        }
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = PhysicalName.QuoteSqlIdentifier(reader.GetString(0));
            var kind = reader.GetByte(4) == 1 ? "CLUSTERED" : "NONCLUSTERED";
            sql.AppendLine();
            if (reader.GetBoolean(1) || reader.GetBoolean(2))
            {
                sql.Append($"ALTER TABLE {table} ADD CONSTRAINT {name} ")
                    .Append(reader.GetBoolean(1) ? "PRIMARY KEY" : "UNIQUE").Append($" {kind}");
            }
            else
            {
                sql.Append("CREATE ").Append(reader.GetBoolean(3) ? "UNIQUE " : string.Empty)
                    .Append($"{kind} INDEX {name} ON {table}");
            }
            sql.Append($" ({reader.GetString(7)})");
            if (!reader.IsDBNull(8))
            {
                sql.Append($" INCLUDE ({reader.GetString(8)})");
            }
            if (!reader.IsDBNull(5))
            {
                sql.Append(" WHERE ").Append(reader.GetString(5));
            }
            sql.AppendLine(";");
            if (reader.GetBoolean(6))
            {
                sql.AppendLine($"ALTER INDEX {name} ON {table} DISABLE;");
            }
        }
        return new EntitySqlSchema(table, sql.ToString());
    }
}
