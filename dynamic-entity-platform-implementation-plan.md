# Dynamic Entity Platform — Implementation Plan

## 1. Objective

Build a multi-tenant dynamic data platform where each tenant can define custom entities and fields, enter data through generated forms, import data from CSV/Excel, view records, filter/sort/paginate data, and export records.

The initial technical stack is:

- Backend: .NET 10 / ASP.NET Core
- Database: SQL Server
- Frontend: React 19
- Data format for dynamic entity records: JSON stored in SQL Server
- Multi-tenancy model: Database-per-tenant
- Entity storage model: Table-per-entity
- Physical table schema: Same generic schema for all entities
- Metadata model: Stored separately from dynamic records
- Storage abstraction: Must allow future merge/split/migration of entity storage without changing business logic

The platform should prioritize:
- schema flexibility
- tenant isolation
- simple application code
- efficient filtering and list rendering
- efficient bulk import/update
- future ability to move an entity between dedicated and shared tables
- future ability to add alternate storage engines

---

## 2. High-Level Architecture

### 2.1 Control Plane

A central Control Database stores tenant and provisioning information.

Suggested tables:

```text
Tenants
TenantStorage
Plans
ProvisioningJobs
```

Example:

```text
TenantStorage
-------------
TenantId
SqlServer
DatabaseName
ConnectionKey
Status
CreatedAt
UpdatedAt
```

Responsibilities:
- resolve tenant database location
- provision tenant databases
- track tenant storage state
- move a tenant to another SQL Server or pool later if needed
- avoid exposing tenant connection details directly to application business logic

### 2.2 Tenant Database

Each tenant has its own SQL Server database.

Example:

```text
Tenant_001
├── EntityDefinitions
├── FieldDefinitions
├── ViewDefinitions
├── ImportJobs
├── EntityStorageMappings
├── FacetDefinitions
├── E_A1B2C3
├── E_D4E5F6
└── E_...
```

Each user-created entity is stored in a dedicated table by default.

Physical table names must use immutable internal IDs, not display names.

Example:

```text
Entity display name: Customer
Entity ID: 01JXYZ...
Physical table: E_01JXYZ...
```

Renaming `Customer` to `Client` must only update metadata.

---

## 3. Storage Model

### 3.1 Entity Table

All entity tables should initially use the same physical schema:

```sql
CREATE TABLE [E_<EntityId>]
(
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [Data] NVARCHAR(MAX) NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL,
    [CreatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIME2(7) NOT NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [Version] ROWVERSION NOT NULL,

    CONSTRAINT [PK_E_<EntityId>] PRIMARY KEY ([Id]),

    CONSTRAINT [CK_E_<EntityId>_Data_IsJson]
        CHECK (ISJSON([Data]) = 1)
);
```

Dynamic fields live inside `Data`.

Example:

```json
{
  "name": "John Smith",
  "status": "Active",
  "age": 30,
  "birthday": "1996-01-10"
}
```

System-level columns such as `Id`, timestamps, auditing fields, and version must remain outside JSON.

### 3.2 Why JSON Is the Source of Truth

Do not create SQL columns for every user-defined field by default.

Benefits:
- adding/removing fields does not require table DDL
- all entities share the same data access implementation
- schema evolution is simpler
- merge/split storage is easier
- an entity can later move to a shared table without changing the application model
- an entity can later move to a different storage provider behind an abstraction

---

## 4. Storage Abstraction

Business logic must never reference physical table names directly.

Create:

```csharp
public interface IEntityStorageResolver
{
    ValueTask<EntityStorageLocation> ResolveAsync(
        Guid tenantId,
        Guid entityId,
        CancellationToken cancellationToken);
}
```

Suggested model:

```csharp
public sealed record EntityStorageLocation(
    string ConnectionKey,
    string DatabaseName,
    string TableName,
    EntityStorageMode Mode,
    bool RequiresEntityPredicate);
```

Storage modes:

```csharp
public enum EntityStorageMode
{
    DedicatedTable,
    SharedTable
}
```

Create:

```csharp
public interface IRecordStore
{
    Task<DynamicRecord?> GetAsync(
        TenantContext tenant,
        EntityDefinition entity,
        Guid recordId,
        CancellationToken cancellationToken);

    Task<PagedResult<DynamicRecord>> QueryAsync(
        TenantContext tenant,
        EntityDefinition entity,
        RecordQuery query,
        CancellationToken cancellationToken);

    Task InsertAsync(...);

    Task UpdateAsync(...);

    Task DeleteAsync(...);

    Task BulkInsertAsync(...);

    Task BulkUpdateAsync(...);
}
```

All business services must depend on `IRecordStore`, not SQL table names.

---

## 5. Metadata Model

### 5.1 EntityDefinition

Suggested fields:

```text
Id
TenantId
Name
DisplayName
Description
SchemaVersion
PrimaryDisplayFieldId
CreatedAt
UpdatedAt
```

### 5.2 FieldDefinition

Suggested fields:

```text
Id
EntityId
Name
DisplayName
DataType
IsRequired
IsUnique
IsFilterable
IsSortable
IsFacetable
IsSearchable
DefaultValueJson
ConfigurationJson
SortOrder
IsActive
CreatedAt
UpdatedAt
```

### 5.3 Supported Data Types

Initial version:

```csharp
public enum FieldDataType
{
    Text,
    LongText,
    Integer,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Email,
    Url,
    Choice,
    MultiChoice,
    Lookup
}
```

Later extensions may include:

```text
Currency
Percentage
File
Image
User
Formula
AutoNumber
```

### 5.4 Field Configuration

Use JSON configuration for type-specific settings.

Example Text:

```json
{
  "minLength": 1,
  "maxLength": 200
}
```

Example Choice:

```json
{
  "allowCustomValues": false
}
```

Example Lookup:

```json
{
  "targetEntityId": "guid",
  "displayFieldId": "guid"
}
```

---

## 6. Dynamic Forms

React must render forms from metadata.

Backend example response:

```json
{
  "entityId": "...",
  "name": "customer",
  "displayName": "Customer",
  "fields": [
    {
      "id": "...",
      "name": "name",
      "displayName": "Name",
      "dataType": "text",
      "required": true
    },
    {
      "id": "...",
      "name": "age",
      "displayName": "Age",
      "dataType": "integer",
      "required": false
    }
  ]
}
```

Frontend should have a field component registry.

Example:

```text
Text        -> TextInput
LongText    -> TextArea
Integer     -> NumberInput
Decimal     -> DecimalInput
Boolean     -> Checkbox
Date        -> DatePicker
DateTime    -> DateTimePicker
Choice      -> Select
MultiChoice -> MultiSelect
Lookup      -> LookupSelect
```

Avoid entity-specific React forms.

---

## 7. Record Validation

All writes must validate data against metadata before persistence.

Validation rules:
- unknown field handling should be configurable
- required fields
- data type validation
- min/max
- max length
- choice value validation
- lookup reference validation
- unique constraint validation
- custom field validation later

Create:

```csharp
public interface IRecordValidator
{
    Task<ValidationResult> ValidateAsync(
        EntityDefinition entity,
        JsonElement data,
        CancellationToken cancellationToken);
}
```

Validation logic must be reused by:
- form save
- CSV import
- Excel import
- API write
- bulk update
- merge/upsert

---

## 8. Query and Filter Engine

### 8.1 Query Request

Support nested AND/OR filters.

Example:

```json
{
  "filter": {
    "logic": "and",
    "conditions": [
      {
        "fieldId": "...",
        "operator": "greaterThan",
        "value": 5000
      },
      {
        "logic": "or",
        "conditions": [
          {
            "fieldId": "...",
            "operator": "equal",
            "value": "IT"
          },
          {
            "fieldId": "...",
            "operator": "equal",
            "value": "Finance"
          }
        ]
      }
    ]
  },
  "sort": [
    {
      "fieldId": "...",
      "direction": "desc"
    }
  ],
  "pageSize": 50,
  "cursor": null
}
```

### 8.2 Operators

Initial operators:

```text
Equal
NotEqual
GreaterThan
GreaterThanOrEqual
LessThan
LessThanOrEqual
Contains
StartsWith
EndsWith
IsNull
IsNotNull
In
NotIn
Between
```

Operators must be validated against field type.

### 8.3 SQL Generation

All query values must use SQL parameters.

Never concatenate untrusted values into SQL.

Field paths must come from trusted metadata.

Example:

```sql
WHERE TRY_CONVERT(BIGINT, JSON_VALUE([Data], '$.age')) > @p0
```

Text example:

```sql
WHERE JSON_VALUE([Data], '$.status') = @p0
```

Use a query AST internally instead of building arbitrary SQL strings throughout the codebase.

---

## 9. Query Performance Strategy

Apply optimization progressively.

### Level 1 — Query JSON directly

Use:

```sql
JSON_VALUE(...)
```

for low-volume or infrequently queried fields.

### Level 2 — Computed Columns + Indexes

For hot filter/sort/facet fields, create computed columns.

Example:

```sql
ALTER TABLE [E_123]
ADD [IDX_Status]
AS JSON_VALUE([Data], '$.status');
```

Then:

```sql
CREATE INDEX [IX_E_123_IDX_Status]
ON [E_123]([IDX_Status]);
```

Use metadata to track generated index fields.

Suggested metadata table:

```text
EntityIndexDefinitions
----------------------
Id
EntityId
FieldId
PhysicalColumnName
IndexName
IndexType
Status
CreatedAt
```

Do not create an index for every field.

### Level 3 — Facet Projection

Only introduce precomputed facet values/counts when direct indexed `GROUP BY` is not sufficient.

---

## 10. Faceted Possible Values

Requirement:

When a user filters a field, only values that currently exist in stored records should be shown.

Example records:

```text
Processing
Completed
Completed
Cancelled
```

Filter dropdown should show:

```text
Processing
Completed
Cancelled
```

If no record has `New`, do not show `New`.

### Phase 1

Use direct aggregation:

```sql
SELECT
    JSON_VALUE([Data], '$.status') AS [Value],
    COUNT_BIG(*) AS [RecordCount]
FROM [E_123]
WHERE JSON_VALUE([Data], '$.status') IS NOT NULL
GROUP BY JSON_VALUE([Data], '$.status')
ORDER BY [Value];
```

### Phase 2

If a computed/indexed field exists:

```sql
SELECT
    [IDX_Status],
    COUNT_BIG(*)
FROM [E_123]
WHERE [IDX_Status] IS NOT NULL
GROUP BY [IDX_Status];
```

### Phase 3

If required by scale, create:

```text
FacetValueCounts
----------------
EntityId
FieldId
Value
RecordCount
```

For normal single-record writes:
- detect changed facetable fields
- decrement old values
- increment new values

For bulk operations:
- never update counters row-by-row
- calculate aggregate deltas

Example:

```text
New        -3200
Processing -1100
Completed  +4300
```

Then update the facet projection once per distinct delta.

For very large imports:
- mark affected facets dirty
- rebuild counts asynchronously or as a post-import stage

---

## 11. Choice vs Facet vs Lookup

Keep these concepts separate.

### Choice

User defines allowed values.

Example:

```text
New
Processing
Completed
```

Store stable IDs or stable codes.

### Facet

Values are discovered from the records that currently exist.

Example:
- only show values that exist in the database
- optionally show counts

### Lookup

Value references a record in another entity.

Store the target record ID.

---

## 12. CSV / Excel Import

### 12.1 Workflow

```text
Upload
  ->
Parse
  ->
Detect columns
  ->
Map source columns to entity fields
  ->
Convert values
  ->
Validate
  ->
Preview
  ->
Commit import
```

### 12.2 Import Mapping

Example:

```csharp
public sealed record ImportColumnMapping(
    string SourceColumn,
    Guid TargetFieldId);
```

### 12.3 Validation Preview

Return:

```json
{
  "totalRows": 1000,
  "validRows": 973,
  "invalidRows": 27,
  "errors": [
    {
      "row": 14,
      "fieldId": "...",
      "message": "Invalid date"
    }
  ]
}
```

### 12.4 Large Imports

Do not insert rows one by one.

Use:
- streaming parser
- batching
- `SqlBulkCopy` where appropriate
- staging tables for merge/upsert workflows

Suggested flow:

```text
CSV/Excel
  ->
Streaming Parser
  ->
Validation Batches
  ->
Staging Table
  ->
Set-based Merge/Insert
  ->
Post-import facet/index work
```

---

## 13. Bulk Update and Merge

### 13.1 General Rule

Never convert a bulk operation into N single-record operations.

Use set-based SQL.

### 13.2 Bulk Update

For facetable fields:
1. determine affected rows
2. capture old values
3. apply update
4. capture new values
5. aggregate field/value deltas
6. update facet counts once per distinct delta

### 13.3 Merge / Upsert

Example semantics:

```text
Match by external key
If record exists -> update
If record does not exist -> insert
```

Use a staging table.

Suggested staging schema:

```text
ImportId
SourceRowNumber
ExternalKey
RecordId
NewData
ValidationStatus
ValidationError
```

For existing records:
- compare old/new values only for affected facetable/indexed fields

For inserted records:
- only increment new facet values

---

## 14. Saved Views

Create:

```text
ViewDefinitions
---------------
Id
EntityId
Name
DefinitionJson
CreatedBy
CreatedAt
UpdatedAt
```

Example view definition:

```json
{
  "columns": ["field-1", "field-2"],
  "filter": {
    "logic": "and",
    "conditions": []
  },
  "sort": [
    {
      "fieldId": "field-1",
      "direction": "asc"
    }
  ],
  "pageSize": 50
}
```

React should render list views generically from the saved view definition.

---

## 15. Pagination

Prefer cursor/keyset pagination over deep OFFSET paging for large tables.

Example cursor inputs:

```text
LastCreatedAt
LastId
```

Example query:

```sql
WHERE
    ([CreatedAt] < @LastCreatedAt)
    OR
    ([CreatedAt] = @LastCreatedAt AND [Id] < @LastId)
ORDER BY [CreatedAt] DESC, [Id] DESC
```

Use OFFSET/FETCH only when acceptable for smaller datasets or user-requested random page navigation.

---

## 16. Concurrency

Use SQL Server `ROWVERSION`.

On update:

```sql
UPDATE ...
SET ...
WHERE Id = @Id
  AND Version = @ExpectedVersion;
```

If affected rows = 0:
- record was changed or deleted
- return a concurrency conflict response

Frontend should show a conflict message and allow refresh/retry.

---

## 17. Transactions

Use transactions for operations that must remain strongly consistent inside one tenant database.

Examples:
- record update + synchronous facet counter update
- metadata change + storage mapping change
- schema/index lifecycle transitions

Large imports should avoid holding one huge transaction unless required.

Prefer batch transactions.

---

## 18. Security

### Tenant Isolation

- determine tenant from authenticated context
- never accept arbitrary database connection information from the client
- resolve database through trusted `TenantStorage`
- never allow cross-tenant entity IDs to resolve silently

### Dynamic SQL

Physical table names and generated computed column names cannot be normal SQL parameters.

Therefore:
- generate physical names internally
- never derive them directly from user input
- validate against storage metadata
- quote identifiers properly

### Authorization

Design for entity-level and record-level permissions later.

Initial hooks:

```csharp
public interface IEntityAuthorizationService
{
    Task<bool> CanReadAsync(...);
    Task<bool> CanWriteAsync(...);
    Task<bool> CanManageSchemaAsync(...);
}
```

---

## 19. Schema Lifecycle

Support:

```text
Create entity
Rename entity
Delete/archive entity

Add field
Rename field
Change display label
Deactivate field
Delete field
Change field configuration
Change field type
```

### Important Rules

Renaming a field should not rewrite record JSON keys unless required.

Prefer immutable internal field IDs and stable storage keys.

Example:

```text
Field ID: F123
Display Name: Customer Name
Storage Key: f_F123
```

JSON:

```json
{
  "f_F123": "John"
}
```

If the user renames `Customer Name` to `Full Name`, JSON remains unchanged.

This avoids rewriting millions of records.

---

## 20. Recommended JSON Key Strategy

Do not use user-visible field names as storage keys.

Bad:

```json
{
  "Customer Name": "John"
}
```

Better:

```json
{
  "f_01JABC": "John"
}
```

Benefits:
- rename is metadata-only
- no collision with reserved names
- no rewrite when display names change
- generated JSON paths are stable

---

## 21. Shared Table Support for the Future

The storage abstraction must support moving small entities into a shared table later.

Shared schema example:

```sql
CREATE TABLE SharedRecords
(
    EntityId UNIQUEIDENTIFIER NOT NULL,
    Id UNIQUEIDENTIFIER NOT NULL,
    Data NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2(7) NOT NULL,
    UpdatedAt DATETIME2(7) NOT NULL,
    Version ROWVERSION NOT NULL,

    CONSTRAINT PK_SharedRecords
        PRIMARY KEY (EntityId, Id)
);
```

EntityStorageMapping:

```text
Entity A -> DedicatedTable -> E_A
Entity B -> DedicatedTable -> E_B
Entity C -> SharedTable    -> SharedRecords
Entity D -> SharedTable    -> SharedRecords
```

Application services must not care which mode is used.

---

## 22. API Design

Suggested endpoints:

```text
POST   /api/entities
GET    /api/entities
GET    /api/entities/{entityId}
PATCH  /api/entities/{entityId}
DELETE /api/entities/{entityId}

POST   /api/entities/{entityId}/fields
PATCH  /api/entities/{entityId}/fields/{fieldId}
DELETE /api/entities/{entityId}/fields/{fieldId}

POST   /api/entities/{entityId}/records
GET    /api/entities/{entityId}/records/{recordId}
PATCH  /api/entities/{entityId}/records/{recordId}
DELETE /api/entities/{entityId}/records/{recordId}

POST   /api/entities/{entityId}/query

GET    /api/entities/{entityId}/fields/{fieldId}/facets

POST   /api/entities/{entityId}/imports
GET    /api/imports/{importId}
POST   /api/imports/{importId}/commit

POST   /api/entities/{entityId}/bulk-update
POST   /api/entities/{entityId}/merge

POST   /api/entities/{entityId}/views
GET    /api/entities/{entityId}/views
PATCH  /api/views/{viewId}
DELETE /api/views/{viewId}
```

---

## 23. Backend Project Structure

Suggested solution:

```text
src/
  DynamicEntity.Api/
  DynamicEntity.Application/
  DynamicEntity.Domain/
  DynamicEntity.Infrastructure/
  DynamicEntity.SqlServer/
  DynamicEntity.Contracts/

tests/
  DynamicEntity.UnitTests/
  DynamicEntity.IntegrationTests/
  DynamicEntity.ApiTests/
```

### Domain

Contains:
- entity definitions
- field definitions
- data types
- filter AST
- storage abstractions
- domain validation concepts

### Application

Contains:
- use cases
- entity services
- record services
- query orchestration
- import workflows
- authorization orchestration

### Infrastructure

Contains:
- tenant resolver
- metadata repositories
- caching
- logging
- background job abstractions

### SqlServer

Contains:
- tenant DB implementation
- SQL query generator
- entity table provisioning
- bulk operations
- computed/index management
- storage migration implementation

---

## 24. React 19 Structure

Suggested structure:

```text
src/
  app/
  api/
  components/
    dynamic-form/
    dynamic-grid/
    filters/
    fields/
    imports/
  features/
    entities/
    records/
    views/
    imports/
  hooks/
  types/
  utilities/
```

Core generic components:

```text
DynamicForm
DynamicField
DynamicGrid
FilterBuilder
SortBuilder
FacetFilter
ImportMapping
ImportPreview
SavedViewSelector
```

Do not create entity-specific UI components.

---

## 25. Frontend State and Data Fetching

Use a standard server-state approach.

Requirements:
- query caching
- request cancellation
- optimistic updates only where safe
- invalidate record list after write
- cache metadata more aggressively than record data

Metadata changes relatively rarely and can use longer cache durations.

Facet values should have short caching if they change frequently.

---

## 26. Observability

Add:
- structured logging
- OpenTelemetry
- request tracing
- SQL duration metrics
- import duration metrics
- query duration per entity
- number of records per entity
- slow query logging
- index/facet rebuild metrics

Useful dimensions:

```text
TenantId
EntityId
Operation
StorageMode
QueryType
```

Avoid logging full record JSON by default.

---

## 27. Performance Testing

Create benchmarks before finalizing advanced optimization.

Test at least:

```text
10K rows/entity
100K rows/entity
1M rows/entity
10M rows/entity
```

Test:
- insert
- update
- bulk insert
- bulk update
- JSON_VALUE filter
- indexed computed column filter
- sorting
- facet GROUP BY
- pagination
- merge/upsert

Important benchmark comparison:

```text
A. JSON_VALUE without computed index
B. JSON + computed/indexed column
C. precomputed facet counts
```

Do not implement C before proving B is insufficient.

---

## 28. Provisioning Workflow

When a tenant is created:

```text
1. Create tenant in Control DB
2. Provision tenant database
3. Apply tenant DB baseline schema
4. Save TenantStorage mapping
5. Mark tenant active
```

When an entity is created:

```text
1. Create EntityDefinition
2. Generate immutable physical table name
3. Create entity table
4. Save EntityStorageMapping
5. Commit metadata
```

Prefer idempotent provisioning.

---

## 29. Deleting an Entity

Do not immediately drop physical tables by default.

Recommended flow:

```text
Entity status -> Archived
  ->
soft-delete / retention period
  ->
background physical cleanup
```

This protects against accidental deletion and allows short-term recovery.

---

## 30. Migration / Merge / Split Strategy

The platform must support:

```text
DedicatedTable -> SharedTable
SharedTable    -> DedicatedTable
Tenant DB A    -> Tenant DB B
```

Recommended migration process:

```text
1. Mark storage mapping as Migrating
2. Create destination
3. Copy records
4. Verify row counts/checksums
5. Switch read mapping
6. Switch write mapping
7. Verify
8. Retire source after retention window
```

For a first implementation, writes may be paused briefly during the final cutover.

Do not implement dual-write until actually required.

---

## 31. Testing Requirements

### Unit Tests

Cover:
- metadata validation
- field type validation
- filter AST validation
- SQL generation
- storage resolver
- stable JSON key generation

### Integration Tests

Use real SQL Server.

Cover:
- entity table creation
- CRUD
- JSON query
- computed index creation
- facet queries
- bulk import
- merge/upsert
- concurrency
- transaction behavior

### End-to-End Tests

Cover:
- create entity
- add fields
- generated form
- create records
- list
- filter
- sort
- facet values
- import
- saved views

---

## 32. Implementation Phases

### Phase 1 — Foundation

Implement:
- solution structure
- authentication tenant context hook
- Control DB
- tenant storage resolver
- tenant database provisioning
- metadata tables
- entity creation
- table-per-entity provisioning

Acceptance criteria:
- a tenant can be created
- a tenant DB is provisioned
- an entity can be created
- its physical table exists

### Phase 2 — Dynamic Records

Implement:
- field definitions
- stable JSON field keys
- generic record model
- record validation
- create/read/update/delete
- optimistic concurrency

Acceptance criteria:
- user can define multiple field types
- records are stored in JSON
- rename field does not rewrite JSON
- concurrency conflict is detected

### Phase 3 — React Dynamic UI

Implement:
- entity designer
- dynamic form renderer
- dynamic list/grid
- record details/editor

Acceptance criteria:
- no entity-specific React form is required
- field metadata drives the UI

### Phase 4 — Query Engine

Implement:
- filter AST
- nested AND/OR
- sorting
- pagination
- parameterized SQL generation

Acceptance criteria:
- filters work for each supported data type
- unsafe values never become SQL identifiers
- list query remains generic

### Phase 5 — Facets and Performance

Implement:
- facet endpoint
- direct GROUP BY
- computed/indexed columns for selected fields
- metadata for entity indexes

Acceptance criteria:
- only existing values appear in facet filter
- indexed filters use generated indexed columns where available

### Phase 6 — Import

Implement:
- CSV upload
- Excel upload
- mapping UI
- validation preview
- batch insert
- staging-table merge/upsert

Acceptance criteria:
- invalid rows are reported
- valid rows can be imported
- large files do not require loading the whole file into memory

### Phase 7 — Bulk Operations

Implement:
- bulk update
- bulk delete
- merge/upsert
- aggregate facet delta strategy if facet projection is enabled

Acceptance criteria:
- no per-record SQL round trip for bulk operations
- updates are set-based

### Phase 8 — Saved Views

Implement:
- saved columns
- filters
- sorts
- user views

Acceptance criteria:
- multiple views can exist for the same entity
- React grid loads state from view definition

### Phase 9 — Advanced Storage Management

Implement only after core product works:
- shared-table mode
- entity storage migration
- move tenant DB
- dedicated storage for very large entities
- optional facet projection
- optional alternate storage provider

---

## 33. Coding Principles for Codex

Codex should follow these constraints:

1. Do not hardcode entity-specific business logic.
2. Keep metadata, record storage, and query generation separated.
3. Use async APIs throughout.
4. Use `CancellationToken` for database and I/O operations.
5. Parameterize all SQL values.
6. Generate physical SQL identifiers only from trusted internal IDs.
7. Never use user display names directly as SQL identifiers.
8. Do not rewrite stored JSON when a field is renamed.
9. Keep dynamic JSON as the source of truth.
10. Treat indexes, computed columns, and facet projections as derived optimizations.
11. Use set-based SQL for bulk operations.
12. Avoid premature distributed architecture.
13. Preserve the storage abstraction so table merge/split can be added later.
14. Write integration tests against SQL Server for all SQL-generation logic.
15. Prefer correctness and maintainability before introducing caching.
16. Do not introduce a generic EAV model unless a concrete requirement proves it necessary.
17. Do not create one SQL column per user-defined field by default.
18. Do not create one index per user-defined field by default.
19. Keep system columns outside JSON.
20. Record all schema/storage transitions through metadata.

---

## 34. Initial Definition of Done

The first production-capable version should support:

- tenant provisioning
- database-per-tenant
- entity creation
- table-per-entity
- dynamic field creation
- JSON record persistence
- dynamic React forms
- record CRUD
- list view
- nested filters
- sorting
- pagination
- facet values from existing records
- CSV import
- Excel import
- validation preview
- bulk insert
- bulk update
- merge/upsert
- saved views
- optimistic concurrency
- basic authorization hooks
- logging/tracing
- automated tests

Advanced storage movement and shared-table mode may remain as extension points in v1, but all core abstractions must be designed so they can be added without rewriting business logic.
