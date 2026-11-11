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
Use a closed provider-created connection configured by connection string, with exactly the declared
database name. Runtime supplied connections, native routing/credential/TLS hooks or native-only string
changes are rejected before management opens. Hidden credentials prevent configuration verification
and are rejected as well. Oracle user/schema provisioning and deletion remain unimplemented.
DDL is non-atomic: failed table creation can leave a new database or partial objects; no automatic
drop rollback or retry occurs. Actual server syntax, privileges and pool effects need live validation.

Automatic savepoint recovery after cancellation uses an uncancelled cleanup token and preserves the original
save error. Recovery or native disposal can still fail; discard an unusable context/transaction and explicitly
resolve native state. Local command doubles do not establish physical rollback or transaction atomicity.

`ExecuteUpdate` and `ExecuteDelete` have local sync/async coverage for ordinary relational operations.
They execute immediately, return affected rows and bypass the change tracker; applications supply concurrency
predicates and a transaction when several calls must share one. MySQL joined updates use native table-reference
syntax. Single-table dependent assignments preserve old values through ordering; cyclic assignments and dependent
joined assignments still require an unimplemented snapshot rewrite. Oracle joined/paged updates use correlated
scalar source queries and EXISTS over every physical primary-key column. Filters, nested paging and left joins
are preserved; joined keyless targets reject. Multiple source matches retain the scalar-subquery cardinality
error rather than selecting an arbitrary value. Each setter repeats the source query, which can be expensive.
Direct literal/captured setters use column write bounds; server-computed expression bounds and physical bulk SQL
execution remain unverified. These limits are provider implementation gaps, not demonstrated server defects.

Migrations, reverse engineering and the wider EF feature inventory remain under development.
EF Core 8/9 adaptation is postponed until the EF10 provider stabilizes. Review the repository's EF10
feature checklist and progress documents before choosing a development checkpoint for an application.
