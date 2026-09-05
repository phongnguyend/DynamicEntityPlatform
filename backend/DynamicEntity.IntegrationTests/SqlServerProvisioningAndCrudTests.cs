using System.Text.Json;
using DynamicEntity.Application.Common;
using DynamicEntity.Application.Entities;
using DynamicEntity.Application.Fields;
using DynamicEntity.Application.Records;
using DynamicEntity.Application.Storage;
using DynamicEntity.Application.Tenants;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.SqlServer;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.IntegrationTests;

public sealed class SqlServerProvisioningAndCrudTests
{
    [Fact]
    public async Task ProvisionsTenantEntityAndExercisesJsonCrudWithConcurrency()
    {
        var serverConnection = Environment.GetEnvironmentVariable("DYNAMIC_ENTITY_TEST_SQL");
        if (string.IsNullOrWhiteSpace(serverConnection))
        {
            return;
        }

        var suffix = Guid.NewGuid();
        var controlName = $"DynamicEntityControl_Test_{suffix:N}";
        var controlBuilder = new SqlConnectionStringBuilder(serverConnection) { InitialCatalog = controlName };
        var options = new SqlServerOptions
        {
            ControlDatabaseConnectionString = controlBuilder.ConnectionString,
            TenantServerConnectionString = serverConnection,
            ConnectionKey = "IntegrationSql"
        };
        Guid? tenantId = null;
        try
        {
            var control = new SqlServerControlPlaneStore(options);
            var provisioner = new SqlServerTenantDatabaseProvisioner(options);
            var tenant = await new TenantService(control, control, provisioner, TimeProvider.System)
                .CreateAsync("Integration tenant", CancellationToken.None);
            tenantId = tenant.Id;
            var metadata = new SqlServerEntityMetadataStore(options);
            var entity = await new EntityService(control, metadata, TimeProvider.System)
                .CreateAsync(tenant.Id, "customer", "Customer", null, CancellationToken.None);
            var fieldStore = new SqlServerFieldMetadataStore(options);
            var fieldService = new FieldService(control, metadata, fieldStore, TimeProvider.System);
            var field = await fieldService
                .CreateAsync(tenant.Id, entity.Id, "name", "Name", FieldDataType.Text, true, true,
                    true, true, true, true, null, "{\"maxLength\":200}", 0, CancellationToken.None);

            var resolver = new EntityStorageResolver(control, metadata);
            var indexService = new EntityIndexService(control, metadata, fieldStore,
                new SqlServerEntityIndexManager(options, resolver));
            var index = await indexService.CreateAsync(tenant.Id, entity.Id,
                [new EntityIndexColumnInput(field.Id, false)], CancellationToken.None);
            Assert.NotNull((await fieldService.ListAsync(tenant.Id, entity.Id, CancellationToken.None))
                .Single().IndexColumnName);
            await indexService.DeleteAsync(tenant.Id, entity.Id, index.Id, CancellationToken.None);
            Assert.Null((await fieldService.ListAsync(tenant.Id, entity.Id, CancellationToken.None))
                .Single().IndexColumnName);

            var records = new SqlServerRecordStore(options, resolver);
            var constraints = new SqlServerRecordConstraintValidator(options, resolver);
            var service = new RecordService(control, metadata, fieldStore,
                new RecordValidator(new RecordValidationOptions()), constraints, records);
            using var input = JsonDocument.Parse("{\"name\":\"Ada\"}");
            var created = await service.CreateAsync(tenant.Id, entity.Id, input.RootElement, null, CancellationToken.None);
            var loaded = await service.GetAsync(tenant.Id, entity.Id, created.Id, CancellationToken.None);
            Assert.Contains(field.StorageKey, loaded.Data);

            await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(tenant.Id, entity.Id,
                created.Id, input.RootElement, new byte[8], null, CancellationToken.None));
            await service.DeleteAsync(tenant.Id, entity.Id, created.Id, created.Version, CancellationToken.None);
            await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(tenant.Id, entity.Id, created.Id, CancellationToken.None));
        }
        finally
        {
            if (tenantId is not null) await DropDatabaseAsync(serverConnection, PhysicalName.ForTenantDatabase(tenantId.Value));
            await DropDatabaseAsync(serverConnection, controlName);
        }
    }

    private static async Task DropDatabaseAsync(string serverConnection, string databaseName)
    {
        if (!databaseName.StartsWith("Tenant_", StringComparison.Ordinal) &&
            !databaseName.StartsWith("DynamicEntityControl_Test_", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to drop a database outside the integration-test naming convention.");
        var builder = new SqlConnectionStringBuilder(serverConnection);
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        var quoted = PhysicalName.QuoteSqlIdentifier(databaseName);
        await using var command = new SqlCommand($"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted}; END", connection);
        command.Parameters.AddWithValue("@name", databaseName);
        await command.ExecuteNonQueryAsync();
    }
}
