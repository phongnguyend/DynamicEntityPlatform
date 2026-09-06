using System.Text.Json;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Queries;
using DynamicEntity.Domain.Storage;
using DynamicEntity.SqlServer.Queries;

namespace DynamicEntity.UnitTests;

public sealed class SqlRecordQueryBuilderTests
{
    [Fact]
    public void Build_ProducesNestedParameterizedTypedFilter()
    {
        var amount = Field("amount", FieldDataType.Decimal);
        var department = Field("department", FieldDataType.Text);
        var entity = Entity(amount, department);
        var filter = new FilterGroup(FilterLogic.And,
        [
            new FilterCondition(amount.Id, FilterOperator.GreaterThan, Json("5000")),
            new FilterGroup(FilterLogic.Or,
            [
                new FilterCondition(department.Id, FilterOperator.Equal, Json("\"IT\"")),
                new FilterCondition(department.Id, FilterOperator.Equal, Json("\"Finance\""))
            ])
        ]);

        var plan = SqlRecordQueryBuilder.Build(entity, Storage(entity), new RecordQuery(50, Filter: filter));

        Assert.Contains("TRY_CONVERT(DECIMAL(38, 10)", plan.Sql);
        Assert.Contains(" AND ", plan.Sql);
        Assert.Contains(" OR ", plan.Sql);
        Assert.DoesNotContain("Finance", plan.Sql);
        Assert.Contains(plan.Parameters, parameter => Equals(parameter.Value, "Finance"));
    }

    [Fact]
    public void Build_EscapesLikeWildcardsAndKeepsValueOutOfSql()
    {
        var name = Field("name", FieldDataType.Text);
        var attack = "%_['; DROP TABLE Records;--";
        var filter = new FilterGroup(FilterLogic.And,
            [new FilterCondition(name.Id, FilterOperator.Contains, Json(JsonSerializer.Serialize(attack)))]);

        var plan = SqlRecordQueryBuilder.Build(Entity(name), Storage(Entity(name)), new RecordQuery(20, Filter: filter));

        Assert.DoesNotContain("DROP TABLE", plan.Sql);
        Assert.Contains("LIKE @p0 ESCAPE", plan.Sql);
        Assert.Equal("%\\%\\_\\['; DROP TABLE Records;--%", plan.Parameters[0].Value);
    }

    [Fact]
    public void Build_UsesOnlyMetadataDerivedJsonPath()
    {
        var field = Field("user supplied display name", FieldDataType.Integer);
        var condition = new FilterCondition(field.Id, FilterOperator.Equal, Json("42"));

        var plan = SqlRecordQueryBuilder.Build(Entity(field), Storage(Entity(field)),
            new RecordQuery(10, Filter: new FilterGroup(FilterLogic.And, [condition])));

        Assert.Contains(field.StorageKey, plan.Sql);
        Assert.DoesNotContain(field.DisplayName, plan.Sql);
        Assert.Equal(42L, plan.Parameters[0].Value);
    }

    [Fact]
    public void Build_DecodesDefaultKeysetCursorIntoParameters()
    {
        var entity = Entity();
        var record = new DynamicRecord(Guid.NewGuid(), "{}", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null, new byte[8]);
        var cursor = SqlRecordQueryBuilder.EncodeNextCursor(record, 0, false);

        var plan = SqlRecordQueryBuilder.Build(entity, Storage(entity), new RecordQuery(10, cursor));

        Assert.Contains("CreatedAt < @cursorCreatedAt", plan.Sql);
        Assert.Contains(plan.Parameters, parameter => parameter.Name == "@cursorId" && Equals(parameter.Value, record.Id));
        Assert.False(plan.UsesOffset);
    }

    [Fact]
    public void Build_UsesOffsetCursorForCustomSort()
    {
        var field = Field("name", FieldDataType.Text);
        var entity = Entity(field);
        var first = SqlRecordQueryBuilder.Build(entity, Storage(entity),
            new RecordQuery(10, Sort: [new RecordSort(field.Id, SortDirection.Asc)]));
        var cursor = SqlRecordQueryBuilder.EncodeNextCursor(
            new DynamicRecord(Guid.NewGuid(), "{}", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null, new byte[8]), 10, true);

        var next = SqlRecordQueryBuilder.Build(entity, Storage(entity),
            new RecordQuery(10, cursor, Sort: [new RecordSort(field.Id, SortDirection.Asc)]));

        Assert.True(first.UsesOffset);
        Assert.Contains("OFFSET @offset", next.Sql);
        Assert.Equal(10, next.Offset);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static FieldDefinition Field(string displayName, FieldDataType type)
    {
        var now = DateTimeOffset.UtcNow;
        return new FieldDefinition(Guid.NewGuid(), Guid.NewGuid(), "field", displayName, type, false,
            false, true, true, false, false, null, null, 0, true, now, now);
    }

    private static EntityDefinition Entity(params FieldDefinition[] fields)
    {
        var now = DateTimeOffset.UtcNow;
        return new EntityDefinition(Guid.NewGuid(), Guid.NewGuid(), "entity", "Entity", null, 1,
            EntityStatus.Active, now, now) { Fields = fields };
    }

    private static EntityStorageLocation Storage(EntityDefinition entity) =>
        new("tenant", PhysicalName.ForEntityTable(entity.Id), EntityStorageMode.DedicatedTable, false);
}
