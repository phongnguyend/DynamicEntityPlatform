namespace DynamicEntity.Application.Abstractions;

public sealed record EntitySqlSchema(string TableName, string Sql);

public interface IEntitySqlSchemaReader
{
    Task<EntitySqlSchema> GetAsync(Guid tenantId, Guid entityId, CancellationToken cancellationToken);
}
