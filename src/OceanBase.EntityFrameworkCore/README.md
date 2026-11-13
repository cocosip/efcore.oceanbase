# OceanBase.EntityFrameworkCore

Entity Framework Core 10 provider for OceanBase MySQL and Oracle compatibility modes, using the public
OceanBaseConnector 0.3.0 API. The package targets .NET 10 and accepts EF Core 10 patch versions in `[10.0.0,11.0.0)`.
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

Enable EF operation retries explicitly for replayable queries and writes:

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql,
    options => options.EnableRetryOnFailure());
```

Defaults are six retries with a maximum 30-second delay for known transient server errors: MySQL 1205/1213,
Oracle 54/60/8177, or SQLSTATE 40001. The optional count/delay/additional server codes apply per context.
Unknown execution outcomes, cancellation, XA, commit-stage errors and failed resource cleanup never replay.
Owned retry transactions explicitly confirm rollback before disposal; uncertain outcomes require application
reconciliation. Put user transactions inside `Database.CreateExecutionStrategy().Execute`/`ExecuteAsync`;
transactions already active before execution reject. Retrying queries buffer results, increasing memory use.
Delegates, callbacks and raw SQL must be replayable; keep automatic SaveChanges transactions enabled.
DDL lifecycle operations and Migrate reject configured or active strategies because DDL commits implicitly;
use a separate context without retries for these operations. Offline script generation remains available.
This local retry policy does not establish native failover or rollback acceptance.

Database reverse engineering uses the driver's public `GetSchema` collections, including its typed column
declarations and tri-state generation/default/update metadata. Scaffolding connections must specify
`ExpectedMode=MySql` or `ExpectedMode=Oracle`; generated `UseOceanBase` code selects that mode explicitly.
Supported scalar columns, keys, ordinary indexes, relationships and keyless views use EF's model/C# generation.
Unavailable behavior metadata produces design-time warnings requiring configuration review before writes or
schema recreation. Known defaults with unavailable SQL use EF's `HasDefaultValue()` marker; unknown identity
is not guessed from a key or sequence-looking default. This is a usable model scaffold, not a lossless DDL
backup. Unsupported types, expression/prefix indexes and known `ON UPDATE` behavior reject explicitly.
Oracle identity mode/options and native catalog/generated-context execution remain unverified.

General query collection roots support all eight CLR integer types, decimal, Single, Double, nullable
forms and integer-backed enums through scalar parameters or constants and SELECT/UNION ALL. Empty
roots retain their original element type and produce zero-row SQL. Numeric-to-numeric converters keep
their parameter/read hooks. Typing casts follow the provider carrier independently of column facets:
MySQL SIGNED/UNSIGNED, DECIMAL(65,30), FLOAT or DOUBLE; Oracle NUMBER, BINARY_FLOAT or BINARY_DOUBLE.
Non-finite floating inputs reject before execution. Explicit `EF.Parameter` roots are supported for integer
and Boolean carriers when the context explicitly selects `Constant` or `MultipleParameters`; the provider
expands them into scalar parameters rather than sending a JSON array. Other single-parameter roots,
non-target carriers and null-converting converters are not supported by this path. Boolean roots preserve
the provider Boolean carrier in MySQL and the decimal Boolean carrier in Oracle.

Explicit temporal `EF.Parameter` arrays/lists for DateTime, DateTimeOffset, DateOnly, TimeOnly and TimeSpan
are admitted when the context explicitly selects `Constant` or `MultipleParameters`. EF10 renders these
explicit roots as scalar `IN` parameters; captured and inline temporal roots retain the provider's typed
`VALUES` path. Current captures rebind, nullable values use the existing temporal readers and empty captures
produce a false predicate. Unconfigured contexts and the single JSON-style collection parameter remain outside
this contract. Native temporal precision, timezone, interval coercion and live results remain unverified.
Direct contextually converted element projections support SelectMany, FirstOrDefault and Join,
including nullable/empty roots and Distinct/Take composition. The owning numeric mapping controls
bindings, projected readers and the CLR default fallback. Incompatible requirements for one root or
its scalar-result consumers reject, including different converters with the same store type. Arbitrary
converter/operator composition remains unsupported. Native UNION typing, results and costs are unverified.

Text collection roots support captured arrays/lists and empty inline arrays when the provider-owned mapping
uses a string carrier and does not convert nulls. Model-to-string converters retain parameter and reader
hooks; MySQL empty roots use `CAST(NULL AS CHAR)` and Oracle uses the declared text type. Explicit
`EF.Parameter(string[])`/`List<string?>` roots are also supported when the context explicitly selects
`Constant` or `MultipleParameters`; the provider expands scalar values rather than sending a JSON array.
Unconfigured contexts, EF's single JSON-style collection parameter, null-converting, binary and other
non-text carriers remain unsupported by this path. Oracle empty-string normalization occurs after raw
collection expansion. Native text collation, LOB behavior and UNION results remain unverified.

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
enables supported table creation and migrations in that existing namespace.

`Database.Migrate()` / `MigrateAsync()` now provide a locally tested migrations core for both modes under
the same schema opt-in. It supports table/column/index/constraint changes, seed data, identity/default/computed
metadata, history, upgrade/downgrade and idempotent scripts. Provision the namespace first; do not mix
EnsureCreated-managed tables with migrations. Caller/ambient/enlisted transactions and automatic retry
strategies reject before migration work because DDL commits implicitly. Inspect partial schema changes before
manual recovery. Configure the matching sync/async seeding callback; the opposite-only callback rejects early.

Runtime migrations serialize through an owner row in `__EFMigrationsHistoryLock`. Cleanup never removes another
owner's row. A process crash or failed cleanup can leave a stale row: verify that no migrator is active before
administrative removal. Generated scripts require external serialization and stop-on-error execution. Oracle
scripts target SQL*Plus-style clients, disable variable substitution, exit on SQL errors and commit the final
history changes; rollback cannot undo DDL. MySQL scripts require autocommit. Raw migration SQL is trusted,
single-statement server input; client directives and multi-statement raw batches are outside this contract.
Native DDL/locking, script-client execution, reverse engineering, sequence/HiLo
and model trigger scaffolding remain unverified or unsupported.

TPT (`UseTptMappingStrategy`, with a table for each mapped type) has a locally tested schema/query/write
workflow in both modes. The root table generates an int/long identity once; intermediate and leaf tables store
that same key as an ordinary primary/foreign key. Assigned/composite keys and client Guid keys retain their
existing generation rules. Initial/evolved migrations and snapshots, hierarchy/OfType queries, derived Includes
and tracked multi-table inserts/updates/deletes have local coverage. Keep automatic transactions enabled for
multi-table saves. As with EF's relational executor, SaveChanges counts physical modification commands in this
profile; one leaf spanning three tables can return 3. Existing generated-key/default limitations still apply,
including MySQL's rejection of server-default key readback. Native joins, key generation, foreign-key enforcement
and rollback remain unverified.

TPC (`UseTpcMappingStrategy`) has a locally tested schema/migrations/query/CRUD profile in both modes.
Concrete tables repeat inherited columns; abstract types have no table. Use application-assigned keys
(`ValueGeneratedNever`, including composite keys) unique across the whole hierarchy, or client-generated
Guid keys without an integer store conversion. Independent table identities, computed keys (even with
ValueGeneratedNever) and store-generated TPC keys are rejected in schema/migration
generation and before the first write batch, including when saving unrelated entities in that model.
Base queries use EF's UNION ALL expansion; OfType, inherited filters, concrete collection Includes,
nullable concurrency and automatic multi-entity transactions have local coverage. No sequence/range allocator
or cross-table uniqueness/polymorphic FK enforcement is supplied. Native UNION typing, FKs and rollback
remain unverified.

Entity splitting (`SplitToTable`) and table splitting (related entities using the same `ToTable`) have a
locally tested schema/migrations/query/CRUD profile in both modes. Only an entity's primary table generates
its identity; required fragment tables receive the same key. EF requires every entity-split fragment row to
exist. A shared row produces one physical insert/update/delete, and an optional dependent whose non-shared
nullable values are all NULL materializes as absent. Map shared concurrency tokens consistently on both
entities; application-managed changes must set both mapped values, while database-generated tokens are read
once and propagated to both. Removing an optional shared dependent clears its columns without deleting the
principal row. Keep automatic transactions enabled for multi-table saves. Native execution/rollback remains
unverified; this profile does not add split queries.

The provider exposes EF design services for migrations authoring. Reference `Microsoft.EntityFrameworkCore.Design`
as a private development dependency in the startup project, and configure the same explicit mode in its host or
`IDesignTimeDbContextFactory`. The existing three-argument `UseOceanBase` overloads remain available; a callback
can select a separate migrations assembly and history table:

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql, options => options
    .MigrationsAssembly("Application.Migrations")
    .MigrationsHistoryTable("__ApplicationHistory"))
    .EnableOceanBaseSchemaManagement("application_database");
```

Omit `MigrationsAssembly` to keep migrations in the context assembly. A history schema, when supplied, must
match the opted-in namespace. Initial/evolved migrations, model snapshots, pending-model checks, forward/reverse/
idempotent scripts, migration listing/removal and framework-dependent Windows bundles have a package-only
verification fixture under `tests/OceanBase.EntityFrameworkCore.ToolingConsumer`. Reverse engineering is not
implemented. `dotnet ef migrations remove --force` may inspect or change a live database; the fixture blocks all
connection opens and tests local file removal only. Bundle verification builds the executable and runs `--help`;
applying a bundle remains a separate native acceptance step.

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
