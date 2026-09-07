using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Application.Alerts;

/// <summary>
/// Builds the human-readable text shared by external channels. Only aggregate metric values are included;
/// no record data ever reaches a notification body.
/// </summary>
public static class AlertMessageFormatter
{
    public static string Subject(AlertNotificationPayload payload) =>
        $"[{Verb(payload.State)}] {payload.AlertName}";

    public static string Body(AlertNotificationPayload payload)
    {
        var value = payload.ValueJson ?? "unavailable";
        return $"""
            Alert: {payload.AlertName}
            State: {payload.State}
            Condition: value {Symbol(payload.ComparisonOperator)} {payload.ThresholdJson}
            Observed value: {value}
            Evaluated at: {payload.EvaluatedAt:u}
            """;
    }

    public static string Verb(AlertState state) => state switch
    {
        AlertState.Firing => "FIRING",
        AlertState.Recovered => "RECOVERED",
        AlertState.Error => "ERROR",
        _ => "NORMAL"
    };

    public static string Symbol(AlertComparisonOperator comparison) => comparison switch
    {
        AlertComparisonOperator.GreaterThan => ">",
        AlertComparisonOperator.GreaterThanOrEqual => ">=",
        AlertComparisonOperator.LessThan => "<",
        AlertComparisonOperator.LessThanOrEqual => "<=",
        AlertComparisonOperator.Equal => "==",
        AlertComparisonOperator.NotEqual => "!=",
        _ => throw new ArgumentOutOfRangeException(nameof(comparison))
    };
}
