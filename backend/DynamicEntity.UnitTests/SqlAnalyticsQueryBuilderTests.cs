using System.Text.Json;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Queries;
using DynamicEntity.Domain.Storage;
using DynamicEntity.SqlServer.Analytics;

namespace DynamicEntity.UnitTests;

public sealed class SqlAnalyticsQueryBuilderTests
{
    [Theory]
    [InlineData(DateBucket.Day,"CONVERT(DATE")]
    [InlineData(DateBucket.Week,"DATEDIFF(DAY")]
    [InlineData(DateBucket.Month,"DATEFROMPARTS")]
    [InlineData(DateBucket.Quarter,"DATEPART(QUARTER")]
    [InlineData(DateBucket.Year,"YEAR(")]
    public void Build_GeneratesDateBuckets(DateBucket bucket,string expected)
    {
        var (entity,field,storage)=Fixture(FieldDataType.DateTime);
        var query=new AnalyticsQuery(null,[new(field.Id,bucket,"period")],[new(AggregateFunction.Count,null,"count")],[],25);
        var plan=SqlAnalyticsQueryBuilder.Build(entity,storage,query);
        Assert.Contains(expected,plan.Sql);Assert.Contains("GROUP BY",plan.Sql);Assert.Contains(plan.Parameters,p=>p.Name=="@take"&&(int)p.Value==26);
    }

    [Theory]
    [InlineData(AggregateFunction.Count,"COUNT_BIG(*)")]
    [InlineData(AggregateFunction.CountDistinct,"COUNT_BIG(DISTINCT")]
    [InlineData(AggregateFunction.Sum,"SUM(")]
    [InlineData(AggregateFunction.Average,"AVG(")]
    [InlineData(AggregateFunction.Min,"MIN(")]
    [InlineData(AggregateFunction.Max,"MAX(")]
    public void Build_GeneratesAggregates(AggregateFunction aggregate,string expected)
    {
        var (entity,field,storage)=Fixture(FieldDataType.Decimal);
        var query=new AnalyticsQuery(null,[],[new(aggregate,aggregate==AggregateFunction.Count?null:field.Id,"value")],[],1);
        Assert.Contains(expected,SqlAnalyticsQueryBuilder.Build(entity,storage,query).Sql);
    }

    [Fact]
    public void Build_ParameterizesFilterValues()
    {
        var (entity,field,storage)=Fixture(FieldDataType.Decimal);using var json=JsonDocument.Parse("42.5");
        var filter=new FilterGroup(FilterLogic.And,[new FilterCondition(field.Id,FilterOperator.GreaterThan,json.RootElement.Clone())]);
        var plan=SqlAnalyticsQueryBuilder.Build(entity,storage,new(filter,[],[new(AggregateFunction.Count,null,"count")],[],10));
        Assert.DoesNotContain("42.5",plan.Sql);Assert.Contains(plan.Parameters,p=>p.Value is decimal d&&d==42.5m);
    }

    [Fact]
    public void Build_ReusesIndexedComputedColumn()
    {
        var (entity,field,storage)=Fixture(FieldDataType.Decimal);
        field = field with { IndexColumnName = "IX_Amount" };
        entity = entity with { Fields = [field] };
        var plan=SqlAnalyticsQueryBuilder.Build(entity,storage,new AnalyticsQuery(null,[],[new(AggregateFunction.Sum,field.Id,"total")],[],10));
        Assert.Contains("[IX_Amount]",plan.Sql);
        Assert.DoesNotContain("JSON_VALUE",plan.Sql);
    }

    private static (EntityDefinition,FieldDefinition,EntityStorageLocation) Fixture(FieldDataType type)
    {var now=DateTimeOffset.UtcNow;var entityId=Guid.NewGuid();var field=new FieldDefinition(Guid.NewGuid(),entityId,"amount","Amount",type,false,false,true,true,false,false,null,null,0,true,now,now);var entity=new EntityDefinition(entityId,Guid.NewGuid(),"orders","Orders",null,1,EntityStatus.Active,now,now){Fields=[field]};return(entity,field,new("tenant","Records_Orders",EntityStorageMode.DedicatedTable,false));}
}
