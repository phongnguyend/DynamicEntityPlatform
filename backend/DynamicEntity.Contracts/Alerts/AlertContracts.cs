using System.Text.Json;
using DynamicEntity.Domain.Analytics;

namespace DynamicEntity.Contracts.Alerts;

public sealed record SaveAlertRequest(Guid MetricId,string Name,AlertComparisonOperator ComparisonOperator,
    JsonElement Threshold,AlertInterval Interval,string Timezone,int CooldownSeconds,bool NotifyOnRecovery,bool IsEnabled);
public sealed record AlertResponse(Guid Id,Guid EntityId,Guid MetricId,string Name,AlertComparisonOperator ComparisonOperator,
    JsonElement Threshold,AlertInterval Interval,string Timezone,int CooldownSeconds,bool NotifyOnRecovery,bool IsEnabled,
    AlertState? LastState,DateTimeOffset? LastEvaluatedAt,DateTimeOffset NextEvaluationAt,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt);
public sealed record AlertEvaluationResponse(Guid Id,Guid AlertId,JsonElement? Value,JsonElement Threshold,AlertState State,string? Error,DateTimeOffset EvaluatedAt);
public sealed record AlertNotificationResponse(Guid Id,Guid AlertId,Guid EvaluationId,string Channel,NotificationStatus Status,int Attempts,string? LastError,DateTimeOffset CreatedAt,DateTimeOffset? DeliveredAt);
public sealed record AlertHistoryResponse(IReadOnlyList<AlertEvaluationResponse> Evaluations,IReadOnlyList<AlertNotificationResponse> Notifications);
