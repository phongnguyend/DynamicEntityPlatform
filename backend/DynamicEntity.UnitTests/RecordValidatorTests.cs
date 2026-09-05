using System.Text.Json;
using DynamicEntity.Application.Records;
using DynamicEntity.Domain.Entities;

namespace DynamicEntity.UnitTests;

public sealed class RecordValidatorTests
{
    [Fact]
    public async Task ValidateAsync_NormalizesFieldNameToStableStorageKey()
    {
        var field = Field("customer_name", "Customer Name", FieldDataType.Text, required: true);
        var entity = Entity(field);
        using var data = JsonDocument.Parse("""{"customer_name":"Ada"}""");

        var result = await Validator().ValidateAsync(entity, data.RootElement, CancellationToken.None);

        Assert.True(result.IsValid);
        using var normalized = JsonDocument.Parse(result.NormalizedData!);
        Assert.Equal("Ada", normalized.RootElement.GetProperty(field.StorageKey).GetString());
        Assert.False(normalized.RootElement.TryGetProperty("customer_name", out _));
    }

    [Fact]
    public async Task ValidateAsync_StableKeyStillWorksAfterDisplayRename()
    {
        var field = Field("customer_name", "Full Name", FieldDataType.Text, required: true);
        var entity = Entity(field);
        using var data = JsonDocument.Parse($$"""{"{{field.StorageKey}}":"Ada"}""");

        var result = await Validator().ValidateAsync(entity, data.RootElement, CancellationToken.None);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_ReportsUnknownAndMissingRequiredFields()
    {
        var field = Field("age", "Age", FieldDataType.Integer, required: true);
        using var data = JsonDocument.Parse("""{"other":42}""");

        var result = await Validator().ValidateAsync(Entity(field), data.RootElement, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Message == "Unknown field.");
        Assert.Contains(result.Errors, error => error.Message == "Age is required.");
    }

    [Theory]
    [InlineData("\"not-a-number\"", FieldDataType.Integer)]
    [InlineData("12", FieldDataType.Boolean)]
    [InlineData("\"2026-99-01\"", FieldDataType.Date)]
    [InlineData("\"invalid\"", FieldDataType.Email)]
    [InlineData("\"ftp://example.com\"", FieldDataType.Url)]
    [InlineData("42", FieldDataType.Lookup)]
    public async Task ValidateAsync_RejectsInvalidTypedValues(string jsonValue, FieldDataType dataType)
    {
        var field = Field("value", "Value", dataType);
        using var data = JsonDocument.Parse($$"""{"value":{{jsonValue}}}""");

        var result = await Validator().ValidateAsync(Entity(field), data.RootElement, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_EnforcesTextLengthAndChoiceConfiguration()
    {
        var text = Field("code", "Code", FieldDataType.Text) with
        {
            ConfigurationJson = """{"minLength":2,"maxLength":4}"""
        };
        var choice = Field("status", "Status", FieldDataType.Choice) with
        {
            ConfigurationJson = """{"allowCustomValues":false,"values":["New","Done"]}"""
        };
        using var data = JsonDocument.Parse("""{"code":"A","status":"Other"}""");

        var result = await Validator().ValidateAsync(Entity(text, choice), data.RootElement, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public async Task ValidateAsync_CanIgnoreUnknownFieldsWhenConfigured()
    {
        using var data = JsonDocument.Parse("""{"legacy":"value"}""");

        var result = await Validator(allowUnknown: true)
            .ValidateAsync(Entity(), data.RootElement, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("{}", result.NormalizedData);
    }

    private static RecordValidator Validator(bool allowUnknown = false) =>
        new(new RecordValidationOptions(allowUnknown));

    private static EntityDefinition Entity(params FieldDefinition[] fields)
    {
        var now = DateTimeOffset.UtcNow;
        return new EntityDefinition(Guid.NewGuid(), Guid.NewGuid(), "entity", "Entity", null, 1,
            EntityStatus.Active, now, now) { Fields = fields };
    }

    private static FieldDefinition Field(
        string name,
        string displayName,
        FieldDataType dataType,
        bool required = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new FieldDefinition(Guid.NewGuid(), Guid.NewGuid(), name, displayName, dataType,
            required, false, false, false, false, false, null, null, 0, true, now, now);
    }
}
