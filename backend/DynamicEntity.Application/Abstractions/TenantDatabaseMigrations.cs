using DynamicEntity.Domain.Storage;

namespace DynamicEntity.Application.Abstractions;

public interface ITenantDatabaseMigrator
{
    Task MigrateAsync(EntityStorageLocation storage, CancellationToken cancellationToken);
}
