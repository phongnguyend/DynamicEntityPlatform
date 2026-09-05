using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;

namespace DynamicEntity.UnitTests;

public sealed class PhysicalNameTests
{
    [Fact]
    public void EntityTableName_UsesOnlyImmutableGuid()
    {
        var id = Guid.Parse("1d52db99-21ca-4ff0-b66c-536456d86cc5");

        var name = PhysicalName.ForEntityTable(id);

        Assert.Equal("E_1d52db9921ca4ff0b66c536456d86cc5", name);
    }

    [Fact]
    public void FieldStorageKey_DoesNotChangeWithDisplayName()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var original = Field(id, "Customer Name", now);
        var renamed = Field(id, "Full Name", now);

        Assert.Equal(original.StorageKey, renamed.StorageKey);
        Assert.Equal($"f_{id:N}", renamed.StorageKey);
    }

    [Fact]
    public void QuoteSqlIdentifier_EscapesClosingBrackets()
    {
        Assert.Equal("[safe]]name]", PhysicalName.QuoteSqlIdentifier("safe]name"));
    }

    private static FieldDefinition Field(Guid id, string displayName, DateTimeOffset now) =>
        new(id, Guid.NewGuid(), "customer_name", displayName, FieldDataType.Text,
            false, false, false, false, false, false, null, null, 0, true, now, now);
}
