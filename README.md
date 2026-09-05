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
- leased scheduled alerts with thresholds, cooldowns, recovery behavior, in-app notifications, and evaluation history
- versioned tenant schema migrations applied to both new and existing active tenants
- tenant-scoped authorization seam, Problem Details errors, structured logging, and OpenTelemetry ASP.NET/SQL instrumentation
- unit tests and an opt-in real-SQL integration test

Dynamic JSON remains the source of truth. Display names never become SQL identifiers,
and all filter values are SQL parameters.

Reports, metrics, and alerts use the same metadata-validated aggregation engine. Analytics
queries are parameterized, cancellable, row-limited, and use the configurable
`SqlServer:AnalyticsCommandTimeoutSeconds` timeout. External email and webhook alert
delivery are intentionally deferred until the in-app notification workflow is validated
in production.

## Prerequisites

- .NET SDK 10
- Node.js 24+
- SQL Server reachable by the API identity
- SQL permission to create the control and tenant databases

The development configuration in
[`appsettings.json`](backend/DynamicEntity.Api/appsettings.json) uses the default SQL Server
LocalDB instance, `(localdb)\\MSSQLLocalDB`, with Windows authentication. Override `SqlServer` with development
settings, user secrets, or environment variables for another instance. Never commit
production credentials.

Example environment-variable overrides:

```powershell
$env:SqlServer__ControlDatabaseConnectionString = 'Server=(localdb)\MSSQLLocalDB;Database=DynamicEntityControl;Integrated Security=true;TrustServerCertificate=true'
$env:SqlServer__TenantServerConnectionString = 'Server=(localdb)\MSSQLLocalDB;Database=DynamicEntityControl;Integrated Security=true;TrustServerCertificate=true'
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
dotnet run --project backend/DynamicEntity.Api --urls http://localhost:5000
```

Terminal 2:

```powershell
Set-Location frontend
npm run dev
```

Open `http://localhost:5173`. Vite proxies `/api` to the backend. The start screen
lists existing tenants so you can select and open an active one, or create a new tenant.
The last opened tenant is remembered in browser local storage; use **Change tenant**
in the sidebar to return to the selector. The API does not connect to SQL during
startup; provisioning begins when tenants are first listed or created.

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

Example requests are available in
[`DynamicEntity.Api.http`](backend/DynamicEntity.Api/DynamicEntity.Api.http).

## Production integration points

`AllowAllEntityAuthorizationService` is deliberately a development policy. Replace
it with the application's identity/permission implementation before exposing the API.
OpenTelemetry instrumentation is registered without a forced exporter so deployment
can select its normal collector/exporter configuration.

Shared-table storage and cross-database entity movement remain extension paths behind
the storage interfaces, as intended for post-v1 advanced storage management.
