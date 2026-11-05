using System.Text.Json;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Storage;
using DynamicEntity.SqlServer.Analytics;

namespace DynamicEntity.UnitTests;

public sealed class VirtualSqlCompilerTests
{
    [Theory]
    [InlineData("SELECT o.amount FROM orders o WHERE o.amount > @amount")]
    [InlineData("WITH totals AS (SELECT SUM(amount) AS amount FROM orders) SELECT amount FROM totals WHERE amount > @amount")]
    [InlineData("SELECT o.amount, ROW_NUMBER() OVER(ORDER BY o.amount) AS rowNo FROM orders o WHERE EXISTS(SELECT 1 FROM orders x WHERE x.amount > @amount)")]
    [InlineData("SELECT a.amount FROM orders a JOIN orders b ON a.Id=b.Id WHERE b.amount > @amount")]
    public void CompilesVirtualTablesAndBindsValues(string sql)
    {
        var plan = VirtualSqlCompiler.Compile(new(true, sql, [new("amount", "Decimal")]), Tables(),
            new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement("12.50") }, false);
        Assert.Contains("FROM [dbo].[Records_Orders]", plan.Sql);
        Assert.Contains("[EntityId] = '", plan.Sql);
        Assert.Contains(" AS [amount]", plan.Sql);
        Assert.Equal(12.50m, plan.Parameters.Single().Value);
        Assert.Equal(sql, plan.VirtualSql);
    }

    [Theory]
    [InlineData("DELETE FROM orders")]
    [InlineData("SELECT 1 AS a; DELETE FROM orders")]
    [InlineData("SELECT * INTO stolen FROM orders")]
    [InlineData("SELECT * FROM dbo.orders")]
    [InlineData("SELECT * FROM otherdb.dbo.orders")]
    [InlineData("SELECT * FROM Records_Orders")]
    [InlineData("SELECT * FROM orders WITH (UPDLOCK)")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'x', 'SELECT 1')")]
    [InlineData("SELECT dbo.Secret() AS value")]
    [InlineData("SELECT NEXT VALUE FOR secret AS value")]
    [InlineData("SELECT @@VERSION AS value")]
    [InlineData("SELECT @a = 1")]
    [InlineData("SELECT * FROM sys.tables")]
    [InlineData("SELECT * FROM orders OPTION (MAXRECURSION 0)")]
    [InlineData("SELECT * FROM orders FOR XML AUTO")]
    [InlineData("WITH hidden AS (SELECT * FROM hidden) SELECT * FROM hidden")]
    [InlineData("WITH firstCte AS (SELECT * FROM hidden), hidden AS (SELECT 1 AS value) SELECT * FROM firstCte")]
    public void RejectsUnsafeQueries(string sql)
    {
        Assert.Throws<ValidationException>(() => VirtualSqlCompiler.Compile(new(true, sql, []), Tables(), null, true));
    }

    [Fact]
    public void ParametersAreNeverInterpolatedAndStringsAreNotReferences()
    {
        const string sql = "SELECT '@ignored' AS literal, @name AS value FROM orders -- @comment";
        var input = "'; DROP TABLE orders; --";
        var plan = VirtualSqlCompiler.Compile(new(true, sql, [new("name")]), Tables(),
            new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement(input) }, false);
        Assert.Single(plan.Parameters);
        Assert.Equal(input, plan.Parameters[0].Value);
        Assert.DoesNotContain(input, plan.Sql);
    }

    [Fact]
    public void MissingParametersAreAllowedOnlyForPreview()
    {
        var definition = new VirtualSqlDefinition(true, "SELECT @amount AS value", [new("amount", "Integer")]);
        Assert.Null(VirtualSqlCompiler.Compile(definition, Tables(), null, true).Parameters.Single().Value);
        Assert.Throws<ValidationException>(() => VirtualSqlCompiler.Compile(definition, Tables(), null, false));
        Assert.Throws<ValidationException>(() => VirtualSqlCompiler.Compile(definition, Tables(),
            new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement("invalid") }, false));
    }

    private static VirtualSqlTable[] Tables()
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var field = new FieldDefinition(Guid.NewGuid(), id, "amount", "Amount", FieldDataType.Decimal,
            false, false, true, true, false, false, null, null, 0, true, now, now);
        var entity = new EntityDefinition(id, Guid.NewGuid(), "orders", "Orders", null, 1, EntityStatus.Active, now, now)
        {
            Fields = [field]
        };
        return [new(entity, new("tenant", "Records_Orders", EntityStorageMode.SharedTable, true))];
    }
}
