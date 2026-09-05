using System.Globalization;

namespace DynamicEntity.Domain.Storage;

public static class PhysicalName
{
    public static string ForTenantDatabase(Guid tenantId) =>
        $"Tenant_{tenantId:N}";

    public static string ForEntityTable(Guid entityId) =>
        $"E_{entityId:N}";

    public static string ForFieldStorageKey(Guid fieldId) =>
        $"f_{fieldId:N}";

    public static string QuoteSqlIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
