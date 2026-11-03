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
Scheduled alerts resolve automatic values and saved defaults; they cannot supply interactive
inputs, so required parameters without defaults cause an evaluation error.

## Production integration points

`AllowAllEntityAuthorizationService` is deliberately a development policy. Replace
it with the application's identity/permission implementation before exposing the API.
OpenTelemetry instrumentation is registered without a forced exporter so deployment
can select its normal collector/exporter configuration.

Shared-table storage and cross-database entity movement remain extension paths behind
the storage interfaces, as intended for post-v1 advanced storage management.
