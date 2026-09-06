using System.Globalization;
using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.Domain.Tenants;
using DynamicEntity.Domain.Validation;
using Microsoft.Data.SqlClient;

namespace DynamicEntity.SqlServer;

public sealed class SqlServerRecordConstraintValidator(
    IEntityStorageResolver storageResolver) : IRecordConstraintValidator
{
    public async Task<IReadOnlyList<RecordValidationError>> ValidateAsync(
        TenantContext tenant,
        EntityDefinition entity,
        string normalizedData,
        Guid? currentRecordId,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(normalizedData);
        var errors = new List<RecordValidationError>();
        foreach (var field in entity.Fields.Where(static field => field.IsActive))
        {
            if (!document.RootElement.TryGetProperty(field.StorageKey, out var value) || value.ValueKind == JsonValueKind.Null)
                continue;

            if (field.IsUnique && await DuplicateExistsAsync(tenant, entity, field, value, currentRecordId, cancellationToken))
                errors.Add(new(field.StorageKey, $"{field.DisplayName} must be unique."));

            if (field.DataType == FieldDataType.Lookup &&
                !await LookupTargetExistsAsync(tenant, field, value, cancellationToken))
                errors.Add(new(field.StorageKey, $"{field.DisplayName} references a record that does not exist."));
        }
        return errors;
    }

    private async Task<bool> DuplicateExistsAsync(
        TenantContext tenant,
        EntityDefinition entity,
        FieldDefinition field,
        JsonElement value,
        Guid? currentRecordId,
        CancellationToken cancellationToken)
    {
        if (value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            return false;
        var storage = await storageResolver.ResolveAsync(tenant.TenantId, entity.Id, cancellationToken);
        await using var connection = await OpenAsync(storage, cancellationToken);
        var table = PhysicalName.QuoteSqlIdentifier(storage.TableName);
        var entityPredicate = storage.RequiresEntityPredicate ? " AND EntityId = @entityId" : string.Empty;
        var sql = $"SELECT TOP (1) 1 FROM dbo.{table} WHERE JSON_VALUE(Data, @path) = @value AND (@currentId IS NULL OR Id <> @currentId){entityPredicate};";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@path", $"$.{field.StorageKey}");
        command.Parameters.AddWithValue("@value", ScalarText(value));
        command.Parameters.AddWithValue("@currentId", (object?)currentRecordId ?? DBNull.Value);
        if (storage.RequiresEntityPredicate) command.Parameters.AddWithValue("@entityId", entity.Id);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private async Task<bool> LookupTargetExistsAsync(
        TenantContext tenant,
        FieldDefinition field,
        JsonElement value,
        CancellationToken cancellationToken)
    {
        if (!TryGetTargetEntityId(field.ConfigurationJson, out var targetEntityId)) return false;
        var targetRecordId = value.GetGuid();
        EntityStorageLocation storage;
        try
        {
            storage = await storageResolver.ResolveAsync(tenant.TenantId, targetEntityId, cancellationToken);
        }
        catch (NotFoundException)
        {
            return false;
        }
        await using var connection = await OpenAsync(storage, cancellationToken);
        var table = PhysicalName.QuoteSqlIdentifier(storage.TableName);
        var entityPredicate = storage.RequiresEntityPredicate ? " AND EntityId = @entityId" : string.Empty;
        await using var command = new SqlCommand($"SELECT TOP (1) 1 FROM dbo.{table} WHERE Id = @id{entityPredicate};", connection);
        command.Parameters.AddWithValue("@id", targetRecordId);
        if (storage.RequiresEntityPredicate) command.Parameters.AddWithValue("@entityId", targetEntityId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private async Task<SqlConnection> OpenAsync(EntityStorageLocation storage, CancellationToken cancellationToken)
    {
        var connection = SqlServerTenantConnection.Create(storage);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static bool TryGetTargetEntityId(string? configurationJson, out Guid targetEntityId)
    {
        targetEntityId = default;
        if (configurationJson is null) return false;
        using var document = JsonDocument.Parse(configurationJson);
        return document.RootElement.TryGetProperty("targetEntityId", out var value) &&
               value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out targetEntityId);
    }

    private static string ScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}
