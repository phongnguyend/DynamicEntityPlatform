using DynamicEntity.Domain.Queries;

namespace DynamicEntity.Domain.Analytics;

public enum AggregateFunction { Count, CountDistinct, Sum, Average, Min, Max }
public enum DateBucket { None, Day, Week, Month, Quarter, Year }
public enum AnalyticsColumnRole { Dimension, Measure }
public enum VisualizationType { Table, Number, Bar, Line, Donut }
public enum AlertComparisonOperator { GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Equal, NotEqual }
public enum AlertInterval { FiveMinutes, Hourly, Daily }
public enum AlertState { Normal, Firing, Error, Recovered }
public enum AlertActionType { Email, Webhook }
public enum NotificationStatus { Pending, Delivered, Failed }
public enum WebhookEvent { RecordCreated, RecordUpdated, RecordDeleted }

public sealed record AnalyticsDimension(Guid FieldId, DateBucket DateBucket, string Alias);
public sealed record AnalyticsMeasure(AggregateFunction Aggregate, Guid? FieldId, string Alias);
public sealed record AnalyticsSort(string Alias, SortDirection Direction);

public sealed record AnalyticsQuery(
    FilterGroup? Filter,
    IReadOnlyList<AnalyticsDimension> Dimensions,
    IReadOnlyList<AnalyticsMeasure> Measures,
    IReadOnlyList<AnalyticsSort> Sort,
    int Limit);

public sealed record AnalyticsColumn(
    string Key, string Label, string DataType, AnalyticsColumnRole Role);

public sealed record AnalyticsResult(
    IReadOnlyList<AnalyticsColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    DateTimeOffset GeneratedAt,
    bool Truncated);

public sealed record AnalyticsStoreResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    bool Truncated);

public sealed record ReportSpecification(
    AnalyticsQuery Query,
    VisualizationType Visualization,
    string? VisualizationConfigurationJson = null);

public sealed record ReportDefinition(
    Guid Id, Guid EntityId, string Name, string? Description, string DefinitionJson,
    Guid? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record MetricDefinition(
    Guid Id, Guid EntityId, string Name, string? Description, AggregateFunction Aggregate,
    Guid? FieldId, string? FilterJson, string? FormatJson, Guid? CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record AlertDefinition(
    Guid Id, Guid EntityId, Guid MetricId, string Name, AlertComparisonOperator ComparisonOperator,
    string ThresholdJson, AlertInterval Interval, string Timezone, TimeSpan Cooldown,
    bool NotifyOnRecovery, bool IsEnabled, IReadOnlyList<AlertActionConfiguration> Actions,
    AlertState? LastState, DateTimeOffset? LastEvaluatedAt,
    DateTimeOffset NextEvaluationAt, string? LeaseOwner, DateTimeOffset? LeaseExpiresAt,
    Guid? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record AlertActionConfiguration(
    AlertActionType Type, IReadOnlyList<string>? EmailRecipients, IReadOnlyList<Uri>? WebhookUrls);

public sealed record AlertEvaluation(
    Guid Id, Guid AlertId, string? ValueJson, string ThresholdJson, AlertState State,
    string? Error, DateTimeOffset EvaluatedAt);

public sealed record AlertNotification(
    Guid Id, Guid AlertId, Guid EvaluationId, string Channel, NotificationStatus Status,
    int Attempts, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt,
    string? PayloadJson = null, DateTimeOffset? NextAttemptAt = null,
    string? LeaseOwner = null, DateTimeOffset? LeaseExpiresAt = null);

public sealed record AlertNotificationPayload(
    Guid AlertId, Guid EntityId, Guid MetricId, string AlertName,
    AlertComparisonOperator ComparisonOperator, string ThresholdJson, string? ValueJson,
    AlertState State, DateTimeOffset EvaluatedAt, IReadOnlyList<string> Targets);

public sealed record WebhookSubscription(
    Guid Id, Guid EntityId, string Name, Uri Endpoint, IReadOnlyList<WebhookEvent> Events,
    bool IsEnabled, Guid? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
