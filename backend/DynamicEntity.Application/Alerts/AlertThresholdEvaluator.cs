using System.Globalization;
using DynamicEntity.Application.Common;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Application.Alerts;

public sealed class AlertThresholdEvaluator
{
    public bool IsMatch(object? value, decimal threshold, AlertComparisonOperator comparison)
    {
        if (value is null) throw new ValidationException("A null metric value cannot be compared to a threshold.");
        decimal actual;
        try { actual = Convert.ToDecimal(value, CultureInfo.InvariantCulture); }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        { throw new ValidationException("The metric value is not numeric and cannot be used by an alert."); }
        return comparison switch
        {
            AlertComparisonOperator.GreaterThan => actual > threshold,
            AlertComparisonOperator.GreaterThanOrEqual => actual >= threshold,
            AlertComparisonOperator.LessThan => actual < threshold,
            AlertComparisonOperator.LessThanOrEqual => actual <= threshold,
            AlertComparisonOperator.Equal => actual == threshold,
            AlertComparisonOperator.NotEqual => actual != threshold,
            _ => throw new ValidationException($"Unknown alert comparison '{comparison}'.")
        };
    }
}
