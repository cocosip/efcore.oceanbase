# OceanBase.EntityFrameworkCore

Entity Framework Core 10 provider for OceanBase MySQL and Oracle compatibility modes, using the public
OceanBaseConnector API. The package targets .NET 10 and accepts EF Core 10 patch versions in `[10.0.0,11.0.0)`.
Select the tenant's compatibility mode explicitly when configuring a context:

```csharp
using Microsoft.EntityFrameworkCore;
using OceanBase.EntityFrameworkCore;

optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql);
// For an Oracle-mode tenant, select OceanBaseCompatibilityMode.Oracle.
```

Load the connection string from application configuration or a secret store. The selected mode must match the
tenant. Driver protocol, authentication, connection pooling and transport settings belong to OceanBaseConnector.

This is a development provider. Local tests cover model/type mappings, SQL generation, intercepted query/write
pipelines and package consumption. Real OceanBase query, write, transaction and schema acceptance is still open
for both modes. Passing local tests does not establish server compatibility or release readiness.

General query collection roots support all eight CLR integer types, decimal, Single, Double, nullable
forms and integer-backed enums through scalar parameters or constants and SELECT/UNION ALL. Empty
roots retain their original element type and produce zero-row SQL. Numeric-to-numeric converters keep
their parameter/read hooks. Typing casts follow the provider carrier independently of column facets:
MySQL SIGNED/UNSIGNED, DECIMAL(65,30), FLOAT or DOUBLE; Oracle NUMBER, BINARY_FLOAT or BINARY_DOUBLE.
Non-finite floating inputs reject before execution. Single-parameter JSON-style roots, nonnumeric
carriers and null-converting converters are not supported by this path.
Direct contextually converted element projections support SelectMany, FirstOrDefault and Join,
including nullable/empty roots and Distinct/Take composition. The owning numeric mapping controls
bindings, projected readers and the CLR default fallback. Incompatible requirements for one root or
its scalar-result consumers reject, including different converters with the same store type. Arbitrary
converter/operator composition remains unsupported. Native UNION typing, results and costs are unverified.

Text collection roots support captured arrays/lists and empty inline arrays when the provider-owned mapping
uses a string carrier and does not convert nulls. Model-to-string converters retain parameter and reader
hooks; MySQL empty roots use `CAST(NULL AS CHAR)` and Oracle uses the declared text type. Null-converting,
binary/other carriers and explicit `EF.Parameter(collection)` roots remain unsupported. Oracle empty-string
normalization occurs after raw collection expansion. Native text collation, LOB behavior and UNION results
remain unverified.

Text and temporal `Min/Max` support scalar/selector and nullable forms using native aggregates and
the original mappings/readers. Supported temporal families are DateTime, DateTimeOffset, DateOnly,
TimeOnly and TimeSpan. Filters, grouping and empty/null shaping use EF's relational pipeline.
Null-converting converters, custom/cross-family carriers and Oracle CLOB/NCLOB extrema reject.
Converters use stored-value ordering; native collation, temporal ordering and results remain unverified.

Integer `&`, `|`, `^`, `~`, `<<` and `>>` have per-mode SQL translations, including nullable values and
C# small-integer promotions. Bitwise enum storage must be numeric. Shift counts use C# width masking,
and signed right shifts preserve sign extension. Text/floating/decimal converter carriers are rejected
for these operations. Integer numeric converters operate in stored value space; arbitrary converter
equivalence, overflowing/narrowing casts and single evaluation of volatile operands need separate
contracts. C# expression trees reject `>>>` (CS7053). Native results remain unverified on OceanBase.

Boolean `^`, `==`, `!=`, `!`, AND/OR, Boolean conditions, conditional arms and coalesce normalize
each operand's numeric/text true/false encoding. Nullable logic and configured equality null semantics
remain distinct. Results use the native Boolean mapping even when operands have different converters.
Null-converting, ambiguous and Oracle empty-string encodings are rejected. Valid stored encodings,
native collation/coercion and repeated volatile operand evaluation remain acceptance boundaries.

`Database.GenerateCreateScript()` generates supported relational DDL offline. To create tables in an already
provisioned MySQL database or Oracle schema, explicitly name the target:

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql)
    .EnableOceanBaseSchemaManagement("application_database");
// await context.Database.EnsureCreatedAsync();
```

The current database/schema must match the declared name exactly, including case. `EnsureCreated` returns false
if any ordinary table exists; it does not reconcile or upgrade existing schemas. DDL is not atomic and a failure
can leave partially created objects requiring manual cleanup. Caller transactions are rejected. This option
enables table creation only.

`UseSeeding` and `UseAsyncSeeding` run after successful `EnsureCreated` schema processing, including when it
returns false. Configure the callback matching the sync/async operation; configuring only the opposite callback
fails before schema work. The callback runs in a new owned transaction after a fresh target-scope check. Failure
disposes that transaction but does not undo preceding DDL. No migration lock or automatic retry is supplied;
callbacks must handle concurrent calls and be idempotent. Physical rollback and commit behavior need live tests.

For an application-owned MySQL database, `EnableOceanBaseDatabaseManagement("application_database")`
also enables namespace creation and `EnsureDeleted`. It declares ownership of the **entire database**,
including objects outside the model; deletion drops that database. System database names are rejected.
The target must be closed and select exactly the declared database name. An explicit administration
connection supports supplied targets and native credential, routing and TLS hooks:

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql)
    .EnableOceanBaseDatabaseManagement("application_database")
    .UseOceanBaseManagementConnection(() => new OceanBaseConnector.OceanBaseConnection(administrationConnectionString));
```

Load both strings from configuration/secrets. The factory must return a fresh, closed, independent
connection with no selected MySQL database. The provider disposes it after each namespace operation;
ordinary queries and existing-schema table creation never invoke the factory. Its configuration is
authoritative for administration: the application must select the same tenant as the target and provide
the required privileges, routing and TLS policy. The provider does not infer tenant equivalence from
different user names or callbacks. Returning the target rejects without disposing it. A factory alone
does not grant namespace ownership. Without a factory, the original provider-created string-only path
still rejects native-only configuration changes, hooks and unverifiable hidden credentials.
Oracle can explicitly own an entire user/schema with an independent administrator connection:

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.Oracle)
    .EnableOceanBaseUserManagement("application_user", () => passwordFromSecretStore)
    .UseOceanBaseManagementConnection(() => new OceanBaseConnector.OceanBaseConnection(administrationConnectionString));
```

The closed target must configure that exact user login, optionally with its `@tenant` routing suffix,
and no different initial schema. Pass the user name without `@`/tenant routing to the ownership option.
The administrator must use a different login in the same tenant, with
access to `SYS.DBA_USERS` and the privileges needed to create/drop users and grant CREATE SESSION and
CREATE TABLE. Built-in system users cannot be owned. A successful catalog query establishes whether the
exact user exists; permission/authentication errors and concurrent DDL failures propagate. Existing users
keep their passwords/privileges and follow the existing-schema table-creation profile. `EnsureDeleted`
drops the entire declared user with CASCADE, including objects outside the EF model.

The password callback runs only for user creation. Empty values, double quotes and control characters
are rejected before DDL. Built-in EF command logs redact credential SQL even with sensitive logging enabled.
Application command interceptors/diagnostic listeners are trusted: they can inspect executable SQL and
must log `LogCommandText` instead. Do not log callback/server exception messages containing credentials.
User creation grants only CREATE SESSION and CREATE TABLE. A later grant/table failure can leave a user
requiring manual repair; no automatic deletion, password reset or privilege reconciliation occurs. The
provider clears the target pool before namespace DDL and again after success; other processes/sessions require
application coordination. Oracle tenants themselves are never created or deleted.
DDL is non-atomic: failed table creation can leave a new database or partial objects; no automatic
drop rollback or retry occurs. Actual server syntax, privileges and pool effects need live validation.

Automatic savepoint recovery after cancellation uses an uncancelled cleanup token and preserves the original
save error. Recovery or native disposal can still fail; discard an unusable context/transaction and explicitly
resolve native state. Local command doubles do not establish physical rollback or transaction atomicity.

`ExecuteUpdate` and `ExecuteDelete` have local sync/async coverage for ordinary relational operations.
They execute immediately, return affected rows and bypass the change tracker; applications supply concurrency
predicates and a transaction when several calls must share one. MySQL joined updates use native table-reference
syntax. Single-table acyclic assignments preserve old values through ordering. Cyclic assignments, dependent
joined assignments and derived joined sources use projected original values under SELECT /*+ NO_MERGE */,
joined to the physical target by every primary-key column. This requires a keyed target; native optimizer and
read-consistency behavior still need server validation. Oracle joined/paged updates use correlated
scalar source queries and EXISTS over every physical primary-key column. Filters, nested paging and left joins
are preserved; joined keyless targets reject. Multiple source matches retain the scalar-subquery cardinality
error rather than selecting an arbitrary value. Each setter repeats the source query, which can be expensive.
Direct literal/captured setters use column write bounds; server-computed expression bounds and physical bulk SQL
execution remain unverified. These limits are provider implementation gaps, not demonstrated server defects.

Migrations, reverse engineering and the wider EF feature inventory remain under development.
EF Core 8/9 adaptation is postponed until the EF10 provider stabilizes. Review the repository's EF10
feature checklist and progress documents before choosing a development checkpoint for an application.
