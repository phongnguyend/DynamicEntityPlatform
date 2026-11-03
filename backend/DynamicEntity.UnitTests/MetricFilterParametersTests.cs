using System.Text.Json;
using DynamicEntity.Application.Common;
using DynamicEntity.Application.Metrics;
using DynamicEntity.Domain.Entities;
using DynamicEntity.Domain.Queries;
using DynamicEntity.SqlServer.Queries;

namespace DynamicEntity.UnitTests;

public sealed class MetricFilterParametersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ResolvesNestedSuppliedValuesWithoutChangingSavedDefinition()
    {
        var field = Field(FieldDataType.Integer);
        var condition = new FilterCondition(field.Id, FilterOperator.GreaterThan, Json("{\"parameter\":\"minimum\"}"));
        var filter = new FilterGroup(FilterLogic.Or, [new FilterGroup(FilterLogic.And, [condition])]);
        var resolved = MetricFilterParameters.Resolve(filter, [field], new Dictionary<string, JsonElement> { ["minimum"] = Json("42") }, Now)!;
        var nested = Assert.IsType<FilterGroup>(resolved.Conditions.Single());
        Assert.Equal(42, Assert.IsType<FilterCondition>(nested.Conditions.Single()).Value.GetInt32());
        Assert.Equal(JsonValueKind.Object, condition.Value.ValueKind);
    }

    [Fact]
    public void SystemValuesAreUtcAndRecomputedEachEvaluation()
    {
        var date = Field(FieldDataType.Date);
        var timestamp = Field(FieldDataType.DateTime);
        var filter = new FilterGroup(FilterLogic.And, [
            new FilterCondition(date.Id, FilterOperator.Equal, Json("{\"system\":\"today\"}")),
            new FilterCondition(timestamp.Id, FilterOperator.LessThan, Json("{\"system\":\"now\"}"))]);
        var first = MetricFilterParameters.Resolve(filter, [date, timestamp], null, Now.ToOffset(TimeSpan.FromHours(7)))!;
        var second = MetricFilterParameters.Resolve(filter, [date, timestamp], null, Now.AddDays(1))!;
        Assert.Equal("2026-11-03", Assert.IsType<FilterCondition>(first.Conditions[0]).Value.GetString());
        Assert.Equal(Now, Assert.IsType<FilterCondition>(first.Conditions[1]).Value.GetDateTimeOffset());
        Assert.Equal("2026-11-04", Assert.IsType<FilterCondition>(second.Conditions[0]).Value.GetString());
    }

    [Fact]
    public void BetweenResolvesEachBoundAndKeepsLiteralValues()
    {
        var field = Field(FieldDataType.Integer);
        var filter = Filter(field, "[{\"parameter\":\"minimum\",\"defaultValue\":5},10]", FilterOperator.Between);
        var result = MetricFilterParameters.Resolve(filter, [field], null, Now)!;
        var value = Assert.IsType<FilterCondition>(result.Conditions.Single()).Value;
        Assert.Equal(5, value[0].GetInt32());
        Assert.Equal(10, value[1].GetInt32());
    }

    [Fact]
    public void MissingRequiredParameterGivesValidationError()
    {
        var field = Field(FieldDataType.Integer);
        var exception = Assert.Throws<ValidationException>(() => MetricFilterParameters.Resolve(Filter(field, "{\"parameter\":\"minimum\"}"), [field], null, Now));
        Assert.Contains("minimum", exception.Message);
    }

    [Theory]
    [InlineData("\"not a number\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{\"system\":\"now\"}")]
    public void SuppliedValuesMustMatchFieldType(string supplied)
    {
        var field = Field(FieldDataType.Integer);
        Assert.Throws<ValidationException>(() => MetricFilterParameters.Resolve(Filter(field, "{\"parameter\":\"minimum\"}"), [field],
            new Dictionary<string, JsonElement> { ["minimum"] = Json(supplied) }, Now));
    }

    [Theory]
    [InlineData(FieldDataType.Integer, "42", "42")]
    [InlineData(FieldDataType.Decimal, "12.5", "12.5")]
    [InlineData(FieldDataType.Boolean, "true", "true")]
    [InlineData(FieldDataType.Text, "00123", "\"00123\"")]
    [InlineData(FieldDataType.Text, "true", "\"true\"")]
    [InlineData(FieldDataType.Text, "", "\"\"")]
    [InlineData(FieldDataType.Text, " Ada ", "\" Ada \"")]
    public void PlainTextInputsAreConvertedAccordingToFieldType(FieldDataType type, string input, string expected)
    {
        var field = Field(type);
        var resolved = MetricFilterParameters.Resolve(Filter(field, "{\"parameter\":\"input\"}"), [field],
            new Dictionary<string, JsonElement> { ["input"] = JsonSerializer.SerializeToElement(input) }, Now)!;
        Assert.Equal(expected, Assert.IsType<FilterCondition>(resolved.Conditions.Single()).Value.GetRawText());
    }

    [Theory]
    [InlineData("{\"system\":\"unknown\"}", FieldDataType.Date)]
    [InlineData("{\"system\":\"now\"}", FieldDataType.Date)]
    [InlineData("{\"system\":\"today\"}", FieldDataType.Integer)]
    [InlineData("{\"parameter\":\"\"}", FieldDataType.Text)]
    [InlineData("{\"parameter\":\"x\",\"system\":\"now\"}", FieldDataType.DateTime)]
    public void InvalidReferencesAreRejectedWhenSaving(string reference, FieldDataType type) =>
        Assert.Throws<ValidationException>(() => MetricFilterParameters.ValidateValue(Json(reference), type, true));

    [Fact]
    public void ResolvedTextRemainsASqlParameter()
    {
        var field = Field(FieldDataType.Text);
        const string input = "' OR 1=1 --";
        var filter = MetricFilterParameters.Resolve(Filter(field, "{\"parameter\":\"customer\"}"), [field],
            new Dictionary<string, JsonElement> { ["customer"] = JsonSerializer.SerializeToElement(input) }, Now)!;
        var parameters = new List<SqlQueryParameter>();
        var sql = SqlFilterExpression.Build(filter, new Dictionary<Guid, FieldDefinition> { [field.Id] = field }, parameters);
        Assert.DoesNotContain(input, sql);
        Assert.Equal(input, Assert.Single(parameters).Value);
    }

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);
    private static FilterGroup Filter(FieldDefinition field, string value, FilterOperator operation = FilterOperator.Equal) =>
        new(FilterLogic.And, [new FilterCondition(field.Id, operation, Json(value))]);
    private static FieldDefinition Field(FieldDataType type) => new(Guid.NewGuid(), Guid.NewGuid(), "test", "Test", type,
        false, false, true, true, true, true, null, null, 0, true, Now, Now);
}
