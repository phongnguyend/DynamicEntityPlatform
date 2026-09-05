using DynamicEntity.Application.Alerts;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.UnitTests;

public sealed class AlertEvaluationTests
{
    [Theory]
    [InlineData(AlertComparisonOperator.GreaterThan,11,true)]
    [InlineData(AlertComparisonOperator.GreaterThanOrEqual,10,true)]
    [InlineData(AlertComparisonOperator.LessThan,9,true)]
    [InlineData(AlertComparisonOperator.LessThanOrEqual,10,true)]
    [InlineData(AlertComparisonOperator.Equal,10,true)]
    [InlineData(AlertComparisonOperator.NotEqual,11,true)]
    public void ThresholdEvaluator_HandlesEveryOperator(AlertComparisonOperator comparison,int value,bool expected)=>
        Assert.Equal(expected,new AlertThresholdEvaluator().IsMatch(value,10,comparison));

    [Fact]
    public void ThresholdEvaluator_RejectsNull()=>Assert.Throws<ValidationException>(()=>new AlertThresholdEvaluator().IsMatch(null,1,AlertComparisonOperator.Equal));

    [Theory]
    [InlineData(AlertInterval.FiveMinutes,5)]
    [InlineData(AlertInterval.Hourly,60)]
    [InlineData(AlertInterval.Daily,1440)]
    public void Next_UsesConfiguredInterval(AlertInterval interval,int minutes)
    {var now=DateTimeOffset.Parse("2026-01-01T00:00:00Z");Assert.Equal(now.AddMinutes(minutes),AlertEvaluationWorker.Next(now,interval));}

    [Fact]
    public void NotificationPolicy_CoversTransitionCooldownAndRecovery()
    {
        var now=DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        Assert.True(AlertNotificationPolicy.ShouldNotify(AlertState.Normal,true,true,TimeSpan.FromHours(1),null,now));
        Assert.False(AlertNotificationPolicy.ShouldNotify(AlertState.Firing,true,true,TimeSpan.FromHours(1),now.AddMinutes(-30),now));
        Assert.True(AlertNotificationPolicy.ShouldNotify(AlertState.Firing,true,true,TimeSpan.FromHours(1),now.AddHours(-1),now));
        Assert.True(AlertNotificationPolicy.ShouldNotify(AlertState.Firing,false,true,TimeSpan.Zero,now,now));
        Assert.False(AlertNotificationPolicy.ShouldNotify(AlertState.Firing,false,false,TimeSpan.Zero,now,now));
    }
}
