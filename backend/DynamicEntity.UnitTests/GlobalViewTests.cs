using DynamicEntity.Application.Common;
using DynamicEntity.Application.Views;
using DynamicEntity.Domain.Analytics;
using DynamicEntity.Domain.Views;

namespace DynamicEntity.UnitTests;

public sealed class GlobalViewTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task InvalidRowLimitsAreRejectedBeforeStorageOrExecution(int limit)
    {
        var service = new GlobalViewService(null!, null!, null!, TimeProvider.System);
        var definition = new GlobalViewSpecification(new(true, "SELECT 1 AS value", []), limit);
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Guid.NewGuid(), "View", null, definition, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), "View", null, definition, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.PreviewAsync(Guid.NewGuid(), definition, null, CancellationToken.None));
    }

    [Theory]
    [InlineData(false, "SELECT 1")]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public async Task GlobalViewsRequireEnabledNonemptySql(bool enabled, string text)
    {
        var service = new GlobalViewService(null!, null!, null!, TimeProvider.System);
        var definition = new GlobalViewSpecification(new(enabled, text, []));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Guid.NewGuid(), "View", null, definition, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.PreviewSqlAsync(Guid.NewGuid(), definition, null, CancellationToken.None));
    }

    [Fact]
    public async Task MissingSqlOrParameterCollectionsAreValidationErrors()
    {
        var service = new GlobalViewService(null!, null!, null!, TimeProvider.System);
        foreach (var definition in new GlobalViewSpecification[] { null!, new(null!), new(new VirtualSqlDefinition(true, "SELECT 1", null!)) })
        {
            await Assert.ThrowsAsync<ValidationException>(() => service.PreviewSqlAsync(Guid.NewGuid(), definition, null, CancellationToken.None));
        }
    }
}
