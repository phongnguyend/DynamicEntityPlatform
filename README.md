# Dynamic Entity Platform

A metadata-driven, multi-tenant data platform.

## Capabilities

- .NET 10 layered backend and React 19 + TypeScript frontend
- control database and database-per-tenant provisioning
- table-per-entity storage with immutable GUID-derived physical names
- entity and field lifecycle management without JSON rewrites on rename
- generated forms and grids for all initial field types
- metadata-driven validation, unique checks, and lookup-reference checks
- generic JSON record CRUD with SQL `ROWVERSION` concurrency
- nested AND/OR filters, typed operators, sorting, and pagination cursors
- existing-value facets with counts
- opt-in computed columns and indexes for hot fields
- streamed CSV/XLSX parsing, batched staging, validation preview, and set-based commit
- set-based bulk patch/delete and staged merge/upsert
- saved views and streaming CSV export
- tenant-scoped reports with grouped analytics, date buckets, live preview, five visualization modes, and aggregated CSV export
- reusable scalar metrics with presentation-only number, percentage, currency, and duration formatting
- leased scheduled alerts with thresholds, cooldowns, recovery behavior, evaluation history, and queued in-app, email, and webhook notifications
- versioned tenant schema migrations applied to both new and existing active tenants
- tenant-scoped authorization seam, Problem Details errors, structured logging, and OpenTelemetry ASP.NET/SQL instrumentation
- unit tests and an opt-in real-SQL integration test

Dynamic JSON remains the source of truth. Display names never become SQL identifiers,
and all filter values are SQL parameters.

Reports, metrics, and alerts use the same metadata-validated aggregation engine. Analytics
queries are parameterized, cancellable, row-limited, and use the configurable
`SqlServer:AnalyticsCommandTimeoutSeconds` timeout.

When an alert fires or recovers, evaluation records the in-app notification and enqueues one pending
row per configured action. A separate background service delivers those rows so a slow SMTP host or
webhook never delays evaluation, retrying with bounded exponential backoff (five attempts, 30s to
30m) and recording status, attempts, and the last error in the alert history. Each delivery carries
the notification id as `X-DynamicEntity-Idempotency-Key`, because a retry re-sends to endpoints that
already accepted the payload. Webhooks are HTTPS-only, never follow redirects, and are refused when
the host resolves to a non-public address.

Configure the channels under `AlertNotifications:Email` (SMTP `Host`, `Port`, `EnableSsl`,
`FromAddress`, and optional `UserName`/`Password`) and `AlertNotifications:Webhook`
(`TimeoutSeconds`, optional `SigningSecret` for the `X-DynamicEntity-Signature` HMAC-SHA256 header,
and `AllowPrivateNetworks` for trusted internal endpoints only). Keep `Password` and `SigningSecret`
in user secrets, environment variables, or a key vault — never in `appsettings.json`. An email action
on an alert with no SMTP host configured is marked failed with that reason in the alert history.

## Prerequisites

- .NET SDK 10
- Node.js 24+
- SQL Server reachable by the API identity
- A pre-created control database and one pre-created database per tenant
- SQL permission to create and update tables, indexes, and data in those databases (database creation permission is not required)

The development configuration in
[`appsettings.json`](backend/DynamicEntity.Api/appsettings.json) uses the default SQL Server
LocalDB instance, `(localdb)\\MSSQLLocalDB`, with Windows authentication. Override `SqlServer` with development
settings, user secrets, or environment variables for another instance. Never commit
production credentials.

Example environment-variable overrides:

```powershell
$env:SqlServer__ControlDatabaseConnectionString = 'Server=(localdb)\MSSQLLocalDB;Database=DynamicEntityControl;Integrated Security=true;TrustServerCertificate=true'
```

## Build and test

```powershell
dotnet restore backend/DynamicEntity.slnx
dotnet build backend/DynamicEntity.slnx --no-restore
dotnet test backend/DynamicEntity.slnx --no-build --no-restore

Set-Location frontend
npm install
npm run build
```

The SQL integration test runs when `DYNAMIC_ENTITY_TEST_SQL` contains a connection
string for an existing administrative database such as `tempdb`. It creates and removes databases whose
names contain test-generated GUIDs.

```powershell
$env:DYNAMIC_ENTITY_TEST_SQL = 'Server=localhost;Database=tempdb;Integrated Security=true;TrustServerCertificate=true'
dotnet test backend/DynamicEntity.IntegrationTests
```

CI runs the same integration test against a disposable SQL Server service.

## SQL definitions for metrics and reports

The metric and report editors offer **Builder** and **SQL** modes. Existing definitions
continue to use Builder. Switching modes preserves the Builder settings and SQL draft;
save the definition to persist the selected mode. **Generate from Builder** explicitly
replaces the SQL draft with the Builder's virtual SQL and parameter defaults.

SQL mode accepts one read-only SQL Server `SELECT`, optionally preceded by non-recursive
CTEs. Use entity **names** (not display names) as tables and field **names** as columns:

```sql
SELECT c.[firstName], COUNT(*) AS [total]
FROM [customer] AS c
WHERE c.[firstName] = @firstName
GROUP BY c.[firstName]
```

The schema browser inserts virtual names at the editor cursor. `Id`, `CreatedAt`, and
`UpdatedAt` are also available on each virtual table. Joins, derived queries, subqueries,
grouping, unions, expressions, and window functions are supported. Give every output
column a unique alias. Qualify ambiguous columns with table aliases. CTEs may reference
earlier CTEs; recursive and forward references are rejected.

Parameter references use `@name` in SQL. The editor detects references outside comments
and quoted text and exposes their type and optional default. Supported types are Text,
Integer, Decimal, Boolean, Date, DateTime, and Guid. Missing values are allowed in SQL
validation/preview, but execution requires a supplied value or default. Values are bound
as SQL parameters, never interpolated. Use `SYSUTCDATETIME()` for the current UTC time or
`CAST(SYSUTCDATETIME() AS date)` for today's UTC date.

**Validate SQL** checks syntax, virtual names, output aliases and database compilation
without running the query. **View SQL** shows the original virtual SQL, compiled physical
SQL, and parameter values. **Run preview** (metrics) or **Preview** (reports) evaluates
the definition. SQL metrics must return exactly one row and one column. Reports support
1–1000 rows, mark truncated results, and infer numeric output columns as measures. Use
the measure-column setting to choose chart measures explicitly; other outputs become
dimensions. Existing CSV exports, runtime prompts, dashboard parameter bindings and
alert evaluations support SQL definitions.

Compilation uses Microsoft's ScriptDom parser. Virtual tables become derived tables
that expose mapped fields and enforce shared-table entity predicates. Physical table
names, schema/database qualifiers, writes, multiple statements, procedures, dynamic SQL,
table/query hints, external table sources and user-defined functions are rejected.
Built-in scalar and aggregate functions are allowlisted; unsupported constructs return
validation errors. Queries use the configured analytics timeout and a server-side row cap.

Tenant migration 11 adds metric SQL storage and creates the login-less `VirtualSqlReader`
database principal with SELECT permission and denied writes. SQL validation and execution
impersonate this principal. The migration identity needs permission to create users and
grant permissions; the runtime identity needs permission to impersonate `VirtualSqlReader`
(database owners already have this). Restart the API to apply the migration before using
the new definitions. Reports store SQL settings within their existing definition JSON.

## Formatting

Formatting follows `AGENTS.md`: control-flow bodies use braces and multiple lines,
C# braces start on a new line, and adjacent C# properties have a blank line between them.

From the repository root:

```powershell
dotnet format backend/DynamicEntity.slnx --diagnostics IDE0011 --severity info --no-restore
npm --prefix frontend run format
```

To check without modifying source files:

```powershell
dotnet format backend/DynamicEntity.slnx --diagnostics IDE0011 --severity info --no-restore --verify-no-changes
npm --prefix frontend run format:check
```

Property spacing is maintained when writing and reviewing C# code.
Generated files and build output are excluded from formatting.

## Run locally

Terminal 1:

```powershell
dotnet run --project backend/DynamicEntity.Api --urls http://localhost:5261
```

Terminal 2:

```powershell
Set-Location frontend
npm run dev
```

Open `http://localhost:5173`. Vite proxies `/api` to the backend. The start screen
lists existing tenants so you can select and open an active one, or register a new tenant
with a connection string for an existing database. The application applies its tables
and schema migrations but does not create that database. The last opened tenant is
remembered in browser local storage. Use **Manage tenants** in the sidebar to list,
create, update, disable, or switch tenants; connection strings are write-only in the API.

To edit a field, open its entity and use **Manage fields**. Select **Edit**, change
its names, sort order, behavior flags, or JSON configuration, and select **Save field**.
A field's data type is immutable.

To add a SQL index, find an eligible field under **Manage fields**, select
**Create index**, and confirm the operation. Indexed fields are labeled **Indexed**;
`LongText` and `MultiChoice` fields do not support scalar computed indexes.
Use **Remove index** beside an indexed field to drop its SQL index and computed column.

## API conventions

- Pass the authenticated tenant as `X-Tenant-Id` during development.
- Record input can use a field machine name, GUID, or returned `storageKey`.
- Record output uses immutable storage keys.
- Patch and delete operations require the base64 `version` returned by the API.
- Delete archives entity metadata; it does not immediately drop physical tables.
- Import files may be `.csv` or `.xlsx`; multi-choice cells use semicolon-delimited values.

## View SQL preview

All SQL previews (views, reports, and metrics) offer **Virtual names** and **Database
names**. Virtual SQL uses the entity and field machine names as a logical table and
columns; it is for understanding the query, not direct database execution. Database SQL
shows the actual physical table, JSON paths, computed index columns, and storage
predicates. Each representation displays its matching parameters.

Use **View SQL** in the views list to inspect a saved view's record query, or in view
details to inspect the current definition, including unsaved filter and sort changes.
The popup shows SQL and bound parameters without executing the query. Column visibility
and color are display settings and do not affect SQL. The preview uses
`POST /api/entities/{entityId}/query/sql` with the same body as the record query endpoint.

## Report and metric runtime parameters

Report and metric filter values support fixed literals, named runtime inputs, and server-generated
`today`/`now` values. Choose the value source in the report or metric filter editor. Running a
report or metric with named parameters opens a popup asking for values, including on dashboard
cards. Text is preserved exactly as entered; numeric and boolean inputs are converted
according to the filtered field's type. No JSON quoting is needed. Values are resolved
on every evaluation without changing the saved filter.

Use **View SQL** in the report builder or add/edit metric form to inspect generated SQL and bound
parameters for the current form, including unsaved changes, without saving or executing
the report or metric. Runtime parameter values are optional: the query appears immediately,
with missing values marked as unbound (or their defaults applied). Expand **Parameter
values (optional)** and select **Update SQL preview** to supply values. Automatic `today`/`now` values resolve when
the preview is generated.

In a filter condition, use `"value": { "parameter": "minimum" }` for a required input,
or `"value": { "parameter": "minimum", "defaultValue": 0 }` for an optional input.
Defaults can be configured through **Edit JSON**. Names are case-sensitive and must
start with a letter, followed by letters, numbers or underscores (up to 64 characters).
Supply inputs to `POST /api/entities/{entityId}/metrics/{metricId}/evaluate`:

```json
{ "parameters": { "minimum": 100 } }
```

The preview endpoint accepts the same `parameters` object alongside the metric definition.
Values must match the filtered field type; missing required or invalid values return HTTP 400.
Existing evaluation calls without a request body remain supported.

Reports accept the same body at `POST /api/entities/{entityId}/reports/{reportId}/run`
and `POST /api/entities/{entityId}/reports/{reportId}/export`. The report list prompts
for values before CSV export. Report preview and SQL preview accept `parameters`
alongside the analytics query at `POST /api/entities/{entityId}/reports/preview`
and `POST /api/entities/{entityId}/reports/sql`, respectively.

Use `"value": { "system": "today" }` for the UTC date (or UTC midnight for a date-time
field), or `"value": { "system": "now" }` for the current UTC timestamp on a date-time
field. Nested filters and individual bounds of `Between`/`In` arrays support references.
Selecting a parameterized metric in the alert editor displays **Metric parameters**.
Supply required values before saving; optional inputs can use the metric's defaults.
Values are stored per alert and reused by scheduled evaluations and **Test now**.
Automatic values are resolved on every evaluation. The alert API accepts a `parameters`
object on create/update. Tenant migration 10 adds storage for these values.

## Production integration points

`AllowAllEntityAuthorizationService` is deliberately a development policy. Replace
it with the application's identity/permission implementation before exposing the API.
OpenTelemetry instrumentation is registered without a forced exporter so deployment
can select its normal collector/exporter configuration.

Shared-table storage and cross-database entity movement remain extension paths behind
the storage interfaces, as intended for post-v1 advanced storage management.
