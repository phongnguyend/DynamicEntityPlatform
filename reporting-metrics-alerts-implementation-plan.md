# Reporting, Metrics, and Alerts Implementation Plan

> Implementation status: the in-app MVP described in phases 1–5 is complete. Phase 6 external email/webhook adapters remain deferred as designed, pending production validation of in-app alerts.

## Purpose

Implement tenant-scoped reporting, reusable metrics, and scheduled alerts over dynamic entity records.

The three features must share one analytics engine:

```text
Entity records -> Aggregation engine -> Reports
                                  |-> Metrics
                                  `-> Alert evaluation -> Notifications
```

- A **report** returns grouped rows suitable for a table or chart.
- A **metric** is a reusable scalar aggregation.
- An **alert** evaluates a metric against a threshold on a schedule.

Keeping aggregation and validation in one place prevents reports, metrics, and alerts from producing different results for the same definition.

## Existing architecture

- Backend: .NET 10 with Domain, Application, Contracts, SqlServer, Infrastructure, and API projects.
- Frontend: React 19, TypeScript, TanStack Query, and React Router.
- Tenancy: control database plus one database per tenant.
- Records: dynamic JSON is the source of truth; immutable field IDs resolve to generated storage keys.
- Queries: nested filters, typed operators, sorting, facets, and pagination already exist.
- Performance: selected fields can have computed SQL columns and indexes.
- Authorization: `IEntityAuthorizationService` exists, but the current implementation allows all access.
- Schema provisioning: tenant tables are currently created with idempotent `IF OBJECT_ID ... IS NULL` SQL.

Relevant starting points:

- `backend/DynamicEntity.Domain/Queries/RecordQueryModels.cs`
- `backend/DynamicEntity.SqlServer/Queries/SqlRecordQueryBuilder.cs`
- `backend/DynamicEntity.SqlServer/SqlServerSchema.cs`
- `backend/DynamicEntity.Application/Abstractions/Persistence.cs`
- `backend/DynamicEntity.Api/Program.cs`
- `frontend/src/App.tsx`
- `frontend/src/components/EntityTabs.tsx`

## MVP scope

Include:

- Reports over one entity at a time.
- Existing record filters reused in analytics queries.
- Up to two grouping dimensions.
- Date buckets: day, week, month, quarter, and year.
- Aggregations: count, count distinct, sum, average, minimum, and maximum.
- Table, number, bar, line, and donut visualizations.
- Reusable scalar metrics.
- Alert comparisons: `>`, `>=`, `<`, `<=`, `=`, and `!=`.
- Alert intervals: every 5 minutes, hourly, and daily.
- Alert cooldown and recovery behavior.
- In-app alert history and notifications.

Defer:

- Arbitrary SQL.
- Cross-entity joins.
- User-defined formulas.
- Real-time streaming evaluation.
- Anomaly detection.
- External delivery channels until in-app alerts are stable.

## Architectural decisions

1. Store report, metric, and alert definitions in each tenant database.
2. Reference fields by immutable `FieldId`, never display name or JSON path.
3. Generate all SQL from validated enums and resolved metadata.
4. Parameterize every user-supplied filter value.
5. Keep visualization settings separate from analytics query definitions.
6. Let alerts reference saved metrics instead of embedding duplicate calculations.
7. Start metric evaluation on demand; introduce caching only after measuring query cost.
8. Use database leases for alert execution so multiple application instances cannot evaluate the same alert concurrently.
9. Introduce schema migration tracking before adding analytics tables.

## Phase 1: Schema migrations and domain models

### 1.1 Add migration tracking

Add a `SchemaMigrations` table and numbered, idempotent tenant-database migrations. Existing tenant databases must receive the new tables, not only newly provisioned tenants.

Suggested structure:

```text
backend/DynamicEntity.SqlServer/Migrations/
  001_InitialSchema.sql
  002_AnalyticsDefinitions.sql
```

If embedded SQL files are not introduced immediately, implement the same versioned behavior in C# and move the current schema into migration `001` conceptually.

### 1.2 Add domain models

Create a new `DynamicEntity.Domain.Analytics` namespace.

#### ReportDefinition

```text
Id
EntityId
Name
Description
DefinitionJson
CreatedBy
CreatedAt
UpdatedAt
```

The typed report definition represented by `DefinitionJson` contains:

```text
Filter
Dimensions[]
Measures[]
Sort[]
Limit
Visualization
```

#### MetricDefinition

```text
Id
EntityId
Name
Description
Aggregate
FieldId (nullable for Count)
FilterJson
FormatJson
CreatedBy
CreatedAt
UpdatedAt
```

#### AlertDefinition

```text
Id
EntityId
MetricId
Name
ComparisonOperator
ThresholdJson
Interval
Timezone
Cooldown
NotifyOnRecovery
IsEnabled
LastState
LastEvaluatedAt
NextEvaluationAt
LeaseOwner
LeaseExpiresAt
CreatedBy
CreatedAt
UpdatedAt
```

#### AlertEvaluation

```text
Id
AlertId
ValueJson
ThresholdJson
State
Error
EvaluatedAt
```

#### AlertNotification

```text
Id
AlertId
EvaluationId
Channel
Status
Attempts
LastError
CreatedAt
DeliveredAt
```

### 1.3 Add tenant tables

Add:

- `ReportDefinitions`
- `MetricDefinitions`
- `AlertDefinitions`
- `AlertEvaluations`
- `AlertNotifications`

Add foreign keys to entity, metric, alert, and evaluation records where appropriate. Add indexes for:

- definitions by `EntityId`
- enabled alerts by `NextEvaluationAt`
- evaluations by `AlertId, EvaluatedAt DESC`
- pending notifications by `Status, CreatedAt`

### 1.4 Add persistence abstractions

Extend `Persistence.cs` with:

- `IReportStore`
- `IMetricStore`
- `IAlertStore`
- `IAlertEvaluationStore`
- `IAlertNotificationStore`

Create SQL Server implementations following the existing view and field stores.

### Phase 1 acceptance criteria

- A fresh tenant receives all analytics tables.
- An existing tenant is migrated without losing data.
- Migrations are safe to run more than once.
- CRUD persistence integration tests pass for reports, metrics, and alerts.

## Phase 2: Shared analytics engine

### 2.1 Add query models

Create typed models similar to:

```csharp
public sealed record AnalyticsQuery(
    FilterGroup? Filter,
    IReadOnlyList<AnalyticsDimension> Dimensions,
    IReadOnlyList<AnalyticsMeasure> Measures,
    IReadOnlyList<AnalyticsSort> Sort,
    int Limit);
```

Supporting enums and records:

- `AggregateFunction`: Count, CountDistinct, Sum, Average, Min, Max
- `DateBucket`: None, Day, Week, Month, Quarter, Year
- `AnalyticsDimension`: FieldId, optional DateBucket, Alias
- `AnalyticsMeasure`: Aggregate, optional FieldId, Alias
- `AnalyticsSort`: Alias, Direction

### 2.2 Extract typed SQL field expressions

`SqlRecordQueryBuilder` currently contains the conversion logic for JSON field values. Extract it into a reusable helper, for example:

```text
SqlFieldExpression.ForValue(FieldDefinition field)
```

Both record queries and analytics queries must use the same conversions for integer, decimal, Boolean, date, date-time, lookup, and text fields.

### 2.3 Implement validation

Create `AnalyticsQueryValidator` with these rules:

- All fields belong to the requested entity and are active.
- Filters use filterable fields.
- Sum and average use integer or decimal fields.
- Date buckets use date or date-time fields.
- Count may omit `FieldId`; other aggregates require one.
- Count distinct does not support `LongText` or `MultiChoice` in the MVP.
- Aliases are generated or strictly validated; never accept them as raw SQL.
- No more than two dimensions and a configured maximum number of measures.
- Limit is bounded by a server-side maximum.

If a referenced field is deactivated, return a validation error that identifies the broken report or metric dependency.

### 2.4 Implement SQL generation

Add:

- `IAnalyticsStore`
- `AnalyticsService`
- `SqlAnalyticsStore`
- `SqlAnalyticsQueryBuilder`

The SQL builder should produce parameterized queries of this form:

```sql
SELECT
    <validated dimension expressions>,
    COUNT(*) AS <generated alias>,
    SUM(<typed field expression>) AS <generated alias>
FROM dbo.<resolved entity table>
WHERE <parameterized filter>
GROUP BY <validated dimension expressions>
ORDER BY <validated output alias>
```

Do not accept client-provided SQL fragments, column names, JSON paths, or table names.

### 2.5 Add result contracts

```ts
interface AnalyticsResult {
  columns: Array<{
    key: string
    label: string
    dataType: string
    role: 'Dimension' | 'Measure'
  }>
  rows: Array<Record<string, unknown>>
  generatedAt: string
  truncated: boolean
}
```

### Phase 2 acceptance criteria

- Unit tests cover every aggregate and date bucket.
- Tests prove that all values remain SQL parameters.
- Integration tests cover indexed and non-indexed JSON fields.
- Queries respect tenant and entity storage resolution.
- Cancellation and command timeout are honored.
- High-cardinality results are limited and report `truncated: true`.

## Phase 3: Reports

### 3.1 Backend services and contracts

Add:

- `ReportService`
- report request/response contracts
- report-definition validation
- create, list, get, update, delete, preview, and run operations

Suggested endpoints:

```text
GET    /api/entities/{entityId}/reports
POST   /api/entities/{entityId}/reports
POST   /api/entities/{entityId}/reports/preview
GET    /api/entities/{entityId}/reports/{reportId}
PATCH  /api/entities/{entityId}/reports/{reportId}
DELETE /api/entities/{entityId}/reports/{reportId}
POST   /api/entities/{entityId}/reports/{reportId}/run
```

Preview accepts an unsaved definition. Run executes a saved definition.

### 3.2 Frontend

Add:

```text
frontend/src/pages/ReportsPage.tsx
frontend/src/pages/ReportBuilderPage.tsx
frontend/src/components/ReportBuilder.tsx
frontend/src/components/ReportViewer.tsx
frontend/src/components/AnalyticsTable.tsx
frontend/src/components/charts/
```

Add a **Reports** link to `EntityTabs` and routes in `App.tsx`.

Report builder sections:

- name and description
- filter definition
- dimensions
- measures
- sorting and row limit
- visualization type and configuration
- debounced live preview

Use drag-and-drop for dimension and measure ordering. Display validation and truncation warnings next to the preview.

Keep chart configuration independent from the analytics query so a user can change the visualization without changing the data calculation.

### 3.3 Export

Add aggregated CSV export based on a saved report or preview definition. Export the analytics result, not raw records.

### Phase 3 acceptance criteria

- Users can create, preview, save, edit, run, and delete reports.
- Refreshing the page preserves the saved report definition.
- Table and chart output agree for the same report.
- Report field references survive field renaming.
- Deactivated fields produce a clear broken-definition message.

## Phase 4: Metrics

### 4.1 Backend

A metric uses the analytics engine with exactly one scalar measure and no dimensions.

Suggested endpoints:

```text
GET    /api/entities/{entityId}/metrics
POST   /api/entities/{entityId}/metrics
POST   /api/entities/{entityId}/metrics/preview
PATCH  /api/entities/{entityId}/metrics/{metricId}
DELETE /api/entities/{entityId}/metrics/{metricId}
POST   /api/entities/{entityId}/metrics/{metricId}/evaluate
```

Add metric formatting options:

- integer
- decimal and decimal places
- percentage
- currency and currency code
- duration

Evaluate on demand initially. Return the value and evaluation time:

```json
{
  "value": 42,
  "evaluatedAt": "2026-01-01T00:00:00Z"
}
```

### 4.2 Frontend

Add a Metrics section under Reports or a dedicated entity tab. Support:

- metric list
- create/edit form
- preview
- formatted metric cards
- last evaluation timestamp
- refresh action

### Phase 4 acceptance criteria

- Metrics and equivalent report measures return the same result.
- Count works without selecting a field.
- Numeric aggregations reject non-numeric fields.
- Formatting affects presentation only, never the stored value.

## Phase 5: Scheduled alerts

### 5.1 Application services

Add:

- `AlertService`
- `MetricEvaluator`
- `AlertThresholdEvaluator`
- `AlertEvaluationWorker`
- `IAlertNotifier`
- initial `InAppAlertNotifier`

Suggested endpoints:

```text
GET    /api/entities/{entityId}/alerts
POST   /api/entities/{entityId}/alerts
PATCH  /api/entities/{entityId}/alerts/{alertId}
DELETE /api/entities/{entityId}/alerts/{alertId}
GET    /api/entities/{entityId}/alerts/{alertId}/history
POST   /api/entities/{entityId}/alerts/{alertId}/test
```

### 5.2 Evaluation flow

1. Find a due, enabled alert.
2. Claim it using `LeaseOwner` and `LeaseExpiresAt` in one atomic update.
3. Evaluate the referenced metric.
4. Compare the value with the threshold.
5. Persist an `AlertEvaluation` row.
6. Create a notification when the configured transition requires one.
7. Set `LastState`, `LastEvaluatedAt`, and `NextEvaluationAt`.
8. Release the lease.

Required semantics:

- Trigger when state changes from normal to firing.
- Do not repeatedly notify while firing unless cooldown has expired.
- Optionally notify when state changes from firing to recovered.
- Store evaluation errors without treating them as threshold matches.
- Use `TimeProvider` for all scheduling and cooldown logic.
- Treat notification delivery as at-least-once and make handlers idempotent.

### 5.3 Tenant scheduling

For the first version, the worker may enumerate active tenants from the control plane and check each tenant database for due alerts.

If tenant volume makes scanning expensive, add a control-plane schedule projection containing only `TenantId` and the next due time. Full alert definitions remain in tenant databases.

### 5.4 Frontend

Add an Alerts section containing:

- name and enabled state
- metric selector
- threshold operator and value
- evaluation interval and timezone
- cooldown
- recovery-notification option
- test-now action
- current normal, firing, or error state
- evaluation and notification history

### Phase 5 acceptance criteria

- A due alert is evaluated once even with multiple worker instances.
- State transition, cooldown, recovery, and error cases are covered by deterministic tests.
- Alert history displays metric value, threshold, outcome, and timestamp.
- Disabled alerts are never claimed.
- Deleting a metric with dependent alerts is rejected or requires explicit cascading confirmation.

## Phase 6: External notifications

After in-app alerting is stable, add notifier adapters behind `IAlertNotifier`:

- email
- webhook

Requirements:

- encrypted secrets
- delivery timeout
- retry with bounded exponential backoff
- idempotency key per alert evaluation and channel
- delivery audit history
- SSRF protection and destination restrictions for webhooks
- no sensitive record values in logs

Do not store channel credentials inside `AlertDefinition.DefinitionJson`.

## Authorization

Extend the authorization seam to cover:

- reading reports and metrics
- managing report and metric definitions
- managing alerts
- viewing alert history
- configuring notification destinations

Replace `AllowAllEntityAuthorizationService` before production use. Route classification based only on HTTP method is insufficient for sensitive notification configuration.

## Performance and operational safeguards

- Configure analytics SQL command timeouts.
- Propagate cancellation tokens.
- Enforce output row and grouping-cardinality limits.
- Limit the number of dimensions and measures.
- Warn when reports group or filter frequently on non-indexed JSON fields.
- Reuse existing generated computed columns where available.
- Add OpenTelemetry spans for report execution, metric evaluation, and alert delivery.
- Record execution duration, failures, truncated results, triggered alerts, and delivery retries.
- Include tenant, entity, report, metric, and alert IDs in structured logs.
- Never log full record data, thresholds containing secrets, or notification credentials.

## Testing strategy

### Unit tests

- analytics query validation
- aggregate/data-type compatibility
- SQL generation and parameterization
- date-bucket expressions
- metric scalar enforcement
- threshold comparisons
- alert state transitions
- cooldown and next-run calculation using a fake `TimeProvider`

### SQL integration tests

- report aggregation over all supported field types
- indexed and non-indexed field expressions
- tenant isolation
- migration from the current schema
- alert lease contention
- report/metric behavior after field rename and deactivation

### Frontend tests

- report builder definition generation
- drag-and-drop ordering
- preview loading, errors, and truncation warnings
- metric formatting
- alert form validation and history rendering

### End-to-end scenarios

1. Create an entity and fields.
2. Add records.
3. Build and save a grouped report.
4. Create a metric using the same filter.
5. Create an alert from the metric.
6. Change records so the threshold becomes true.
7. Verify one alert evaluation and one notification.
8. Verify recovery behavior after the value returns to normal.

## Recommended pull-request sequence

1. Add schema migration infrastructure and analytics tables.
2. Add analytics domain/contracts and validation.
3. Extract shared SQL field expressions and implement aggregation queries.
4. Add analytics preview API and tests.
5. Add report persistence and report APIs.
6. Add Reports routes, builder, viewer, and table output.
7. Add chart visualizations and aggregated export.
8. Add metric persistence, APIs, evaluation, and metric cards.
9. Add alert persistence, threshold evaluation, and deterministic tests.
10. Add leased background evaluation and in-app notification history.
11. Add external notification adapters and operational hardening.

Each pull request should leave the solution buildable and include tests for its behavior.

## First implementation session

Start with migration infrastructure and the shared analytics model. Do not start with charts or alert scheduling.

Suggested first-session tasks:

1. Add `SchemaMigrations` and a tenant migration runner.
2. Move or register the current tenant schema as migration version 1.
3. Add migration version 2 with `ReportDefinitions`, `MetricDefinitions`, `AlertDefinitions`, `AlertEvaluations`, and `AlertNotifications`.
4. Add analytics domain enums and records.
5. Add request/response contracts for analytics preview.
6. Add validation tests for aggregate/data-type compatibility.
7. Confirm `dotnet build` and all existing tests still pass.

Do not expose report CRUD until the aggregation query can be validated and executed safely.

## Definition of done

The feature set is complete when a tenant user can:

1. Build and save a report over entity records.
2. View the report as a table or supported chart.
3. Save a scalar calculation as a reusable metric.
4. Configure an alert against that metric.
5. See alert state, evaluation history, and in-app notifications.
6. Trust that the same definition returns the same result in reports, metrics, and alerts.

All operations must remain tenant-isolated, metadata-driven, parameterized, cancellable, observable, and covered by unit and SQL integration tests.
