# EF Core 9 Feature Matrix

This inventory covers the same 55 feature groups as EF Core 10, using stable IDs.
The port starts from `efcore/10.0` and targets `net9.0`. The rows describe the branch's target scope before implementation.
Use the [gap matrix](efcore-9-gap-matrix.md) for closure conditions and the
[release checklist](efcore-9-release-checklist.md) for acceptance evidence.

## Definitions and scope

- **Carry forward** marks existing upstream Provider behavior to preserve in the target branch.
- **Port required** identifies a version-local integration change for the named behavior.
- **Deferred** preserves the baseline scope exclusion.
- The two mode columns describe development status. Inherited SQL signatures, result contracts and mode-specific limits are defined in the shared baseline. Record completed code per mode as it lands.

[Baseline feature and API scope](efcore-10-feature-matrix.md) · [Upstream version references](official-references.md#version-references)

## Feature groups

| ID | Planned feature | MySQL code | Oracle code | Owning source | Version 9 scope and adjustments |
| --- | --- | --- | --- | --- | --- |
| CFG-01 | UseOceanBase string/connection/configuration callbacks, generic and non-generic entry points, and input validation | Carry forward | Carry forward | [Options][options] | Preserve the documented Provider contract on this EF branch. |
| CFG-02 | Provider invariant, Database.IsOceanBase and service identification | Carry forward | Carry forward | [Options][options] | Preserve the documented Provider contract on this EF branch. |
| CFG-03 | Model/service/compiled-query caches isolated by compatibility mode, preserving the EF base cache key | Port required | Port required | [Cache][cache] | Preserve mode isolation in service, model and compiled query caches. Compare EF cache-key and option cloning signatures. [9P-02](efcore-9-gap-matrix.md#porting-gaps) |
| CFG-04 | EF9 relational service registration, scopes and DatabaseFacade service graph | Port required | Port required | [Services][services] | Rebind EF9 relational services and lifetimes. Check design-time discovery and the DatabaseFacade service graph. [9P-02](efcore-9-gap-matrix.md#porting-gaps) |
| CON-01 | Connection creation through the public Connector API with ExpectedMode | Carry forward | Carry forward | [Connection][connection] | Preserve the documented Provider contract on this EF branch. |
| CON-02 | Mode validation before and after opening, conflict rejection and failure cleanup | Carry forward | Carry forward | [Connection][connection] | Preserve the documented Provider contract on this EF branch. |
| CON-03 | Caller/Provider connection ownership, nested leases and preservation of hidden credentials | Carry forward | Carry forward | [Connection][connection] | Preserve the documented Provider contract on this EF branch. |
| CON-04 | Sync/async connections, early reader disposal, failure cleanup and connection reuse | Carry forward | Carry forward | [Reader][reader] | Preserve the documented Provider contract on this EF branch. |
| CON-05 | Timeout/cancellation propagation and resource recovery; no driver cancellation protocol implementation | Carry forward | Carry forward | [Connection][connection] | Preserve the documented Provider contract on this EF branch. |
| DIA-01 | EF command/failure logging, standard interceptors and parameter redaction by default | Carry forward | Carry forward | [Logging][logging] | Preserve the documented Provider contract on this EF branch. |
| DIA-02 | DbUpdateException inner errors/entries, concurrency events and preserved cancellation causes | Carry forward | Carry forward | [Writes][writes] | Direct/pre-send cancellation stays unwrapped. A native in-flight unknown-outcome error can be wrapped by DbUpdateException while retaining the Connector flag and cancellation cause |
| MOD-01 | Tables, primary/composite/alternate keys, foreign keys, indexes and relationship conventions | Port required | Port required | [Model][conventions] | Compare relational convention signatures. Preserve keys, constraints, indexes and relationship DDL. [9P-08](efcore-9-gap-matrix.md#porting-gaps) |
| MOD-02 | Required/optional relationships, owned references/collections, shadow FKs and relationship fixup | Port required | Port required | [Model][conventions] | Reuse owned and shadow-property behavior. Compare complex-property metadata, which has a separate Provider scope. [9P-08](efcore-9-gap-matrix.md#porting-gaps) |
| MOD-03 | Identity/default/computed metadata, model triggers, names and namespaces | Carry forward | Carry forward | [DDL][schema] | `HasTrigger` is metadata only. See G-26..35/G-43/45 |
| MAP-01 | Boolean, eight signed/unsigned integer types, nullable values and range validation | Carry forward | Carry forward | [Mappings][mappings] | Preserve the documented Provider contract on this EF branch. |
| MAP-02 | Decimal, Single and Double mappings, parameters, literals and readers | Carry forward | Carry forward | [Mappings][mappings] | Connector 0.4.0 resolves B-01..03 for the supported native carriers. CLR decimal result bounds remain enforced |
| MAP-03 | Unicode/plain text, fixed/variable length, bounded/LOB storage and character lengths | Carry forward | Carry forward | [Mappings][mappings] | B-05/06. server boundaries |
| MAP-04 | Binary/Guid, byte order, RAW/VARBINARY/BLOB and empty-value policy | Carry forward | Carry forward | [Mappings][mappings] | Empty RAW values are rejected. B-06 |
| MAP-05 | DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan value mappings and precision | Carry forward | Carry forward | [Temporal mappings][temporalmap] | MySQL supports explicit session-local TIMESTAMP storage for DateTime (G-50). Oracle supports explicit TIMESTAMP WITH LOCAL TIME ZONE DateTime storage (C-02). Method translation is tracked in QRY-07/G-42. See C-02 for time zone scope |
| MAP-06 | Explicit store types, size/precision/scale, Unicode/fixed length and NULL/DBNull | Port required | Port required | [Mappings][mappings] | Check mapping constructors, facets and type-mapping inference against EF9. [9P-03](efcore-9-gap-matrix.md#porting-gaps) |
| MAP-07 | Parameters/reads/writes for ordinary enums, nullable values and numeric/text/temporal converters | Carry forward | Carry forward | [Mappings][mappings] | Algebraic equivalence is not guaranteed for arbitrary converters. G-25 |
| SQL-01 | Identifier escaping, schema qualification and Oracle aliases/DUAL | Carry forward | Carry forward | [Identifiers][sqlhelper] | B-07 is a tested server boundary |
| SQL-02 | Named parameters and MySQL positional conversion, repeated binding and parameter leases | Port required | Port required | [Commands][commands] | Adapt raw and generated parameter processing. Preserve repeated positional binding and Oracle names. [9P-03](efcore-9-gap-matrix.md#porting-gaps) |
| QRY-01 | Projection, numeric arithmetic/comparisons/Boolean logic, nullable values, CASE/coalesce, conversions and integer bitwise operations | Port required | Port required | [Expressions][sqlvisitor] | Adapt the EF9 SQL-expression visitor and nullability pipeline. Preserve Boolean and numeric carrier rules. [9P-03](efcore-9-gap-matrix.md#porting-gaps) |
| QRY-02 | Ordering, Distinct, nonnegative Skip/Take/offset-only pagination and empty pages | Port required | Port required | [SQL][sqlgenerator] | Preserve ordering and pagination semantics when adapting SQL generation and SELECT rewrites. [9P-03](efcore-9-gap-matrix.md#porting-gaps) |
| QRY-03 | Inner/left/cross/lateral joins, correlated scalars/collections, Any/All/Contains and set operations | Port required | Port required | [Queries][roots] | Port query roots and collection type mappings. Keep classic `GroupJoin`/`DefaultIfEmpty` left joins. Direct .NET 10 join APIs and EF10 `MultipleParameters` are out of scope. Use EF9 parameterize/constantize options and revalidate signed-integer JSON roots. [9P-03](efcore-9-gap-matrix.md#porting-gaps) / [9P-04](efcore-9-gap-matrix.md#porting-gaps) |
| QRY-04 | Count/LongCount/Sum/Average/Min/Max, GroupBy and grouped first-row/aggregate combinations | Port required | Port required | [Aggregates][aggregates] | Preserve aggregate result conversion and compensation. Recheck EF9 grouping and type inference. [9P-03](efcore-9-gap-matrix.md#porting-gaps) |
| QRY-05 | First/Single/OrDefault, ordinary predicates, cardinality and empty-result materialization | Port required | Port required | [Queries][roots] | Preserve cardinality and explicit scalar-default rewrites using the EF9 shaping pipeline. [9P-03](efcore-9-gap-matrix.md#porting-gaps) |
| QRY-06 | String equality/concat, Contains/StartsWith/EndsWith/IndexOf(string/char), Length, Substring, Remove, Replace, Insert, PadLeft/PadRight, parameterless case methods and ordinary/explicit character-set trim | Port required | Port required | [Strings][strings], [IndexOf][indexof], [Explicit trim][trim] | Retain the shared string signatures and mode-specific SQL. Adapt parameter-dependent Oracle rewrites. [9P-03](efcore-9-gap-matrix.md#porting-gaps) |
| QRY-07 | Date/time members, calendar/duration methods and DateOnly/TimeOnly conversions/ranges, Math/MathF/decimal, Round/Clamp/extrema | Port required | Port required | [Temporal methods][temporal] | Retain supported BCL translators and contextual mappings. Adapt parameterized `Round` and recheck extrema and temporal result carriers. [9P-03](efcore-9-gap-matrix.md#porting-gaps) |
| QRY-08 | Include/ThenInclude, single/split graph queries and three tracking/identity-resolution modes | Port required | Port required | [Queries][roots] | Recheck Includes, split queries and tracking identity resolution against EF9 materialization and buffering services. [9P-09](efcore-9-gap-matrix.md#porting-gaps) |
| QRY-09 | Global filters, context values and disabling all filters | Port required | Port required | [Compilation context][compilation] | Compose an unnamed filter per entity. Preserve context-dependent predicates and `IgnoreQueryFilters()` for all filters. Named filters and selective disabling require EF10. [9P-05](efcore-9-gap-matrix.md#porting-gaps) |
| QRY-10 | FromSqlRaw/Interpolated, SqlQueryRaw/SqlQuery, ExecuteSqlRaw/Interpolated and materialization | Port required | Port required | [Commands][commands] | Preserve entity/scalar raw SQL and parameterized command entry points. Adapt raw parameter expansion. [9P-03](efcore-9-gap-matrix.md#porting-gaps) |
| QRY-11 | Translation errors for unsupported predicates, preserving EF-permitted top-level client projection | Port required | Port required | [Expressions][sqlvisitor] | Preserve translation failure and supported top-level client projection when adopting EF9 expression visitors. [9P-03](efcore-9-gap-matrix.md#porting-gaps) |
| UPD-01 | Sync/async INSERT, assigned keys, default-only inserts and ordinary identity retrieval | Carry forward | Carry forward | [Writes][writes] | G-26/27 |
| UPD-02 | Same-connection generated/default/computed value readback, LAST_INSERT_ID / RETURNING INTO | Port required | Port required | [Writes][writes] | Compare generated-value column modification and propagation contracts. Preserve per-mode readback. [9P-08](efcore-9-gap-matrix.md#porting-gaps) |
| UPD-03 | UPDATE/DELETE, original keys/tokens, composite keys and nullable concurrency values | Carry forward | Carry forward | [Writes][writes] | G-43. ordinary manually managed tokens are implemented |
| UPD-04 | Matched rows/concurrency conflicts, entries and standard concurrency interceptors/suppression | Port required | Port required | [Writes][writes] | Compare EF9 concurrency dispatch and suppression signatures. Preserve matched-row checks and exception entries. [9P-08](efcore-9-gap-matrix.md#porting-gaps) |
| UPD-05 | Dependency ordering, parent/child key propagation, relationship replacement/orphans and graph saves | Carry forward | Carry forward | [Batch execution][batch] | G-26..28. ordinary graphs are implemented |
| TXN-01 | Sync/async Begin/Commit/Rollback, UseTransaction, single-resource ambient/explicit enlistment and ownership validation | Carry forward | Carry forward | [Transactions][transactions] | G-40 retains the distributed/multiple-resource limitation |
| TXN-02 | Multi-command SaveChanges, generated-value readback and failure/cancellation rollback orchestration | Carry forward | Carry forward | [Batch execution][batch] | Actual atomicity/interruption evidence belongs to acceptance |
| TXN-03 | Savepoints, SaveChanges within an existing transaction and recoverable failures | Carry forward | Carry forward | [Transactions][transactions] | Oracle uses a logical release. this is not a server RELEASE |
| SCH-01 | CanConnect/existence checks, distinguishing absence from authentication/permission/network failures | Carry forward | Carry forward | [Database lifecycle][creator] | Preserve the documented Provider contract on this EF branch. |
| SCH-02 | GenerateCreateScript/EnsureCreated/CreateTables, table/column comments and explicit schema management | Port required | Port required | [DDL][schema] | Port EF9 database creation and schema generation hooks. Recheck migration operations, retries and DDL ownership. [9P-07](efcore-9-gap-matrix.md#porting-gaps) |
| SCH-03 | Explicit namespace creation/deletion, management factories and scope/ownership validation | Carry forward | Carry forward | [Namespace][namespace] | Custom native connections use an explicit management factory |
| DEF-01 | Migrations/model differ including table/column comments, history/locking, upgrade/rollback/idempotent scripts and CLI/bundles | Port required | Port required | [Migrations][migrations] | Adapt EF9 migration/model differ and history services. Revalidate forward/reverse scripts, idempotency and design-time bundles. Preserve EF9's migration lock and test pending-model change checks. [9P-07](efcore-9-gap-matrix.md#porting-gaps) |
| DEF-02 | GetSchema reverse engineering, tables/views/keys/relationships/indexes, design-time services and C# generation | Port required | Port required | [Reverse engineering][scaffold] | Adapt EF9 Design services and emitted code for defaults, generated values, indexes and catalog types. [9P-10](efcore-9-gap-matrix.md#porting-gaps) |
| DEF-03 | ExecuteUpdate/Delete, expression-based setters, joined and paged operations | Port required | Port required | [Bulk][bulk] | Use EF9 expression-tree setters. Port joined and paged bulk DML, expression shaping and tags. Action-based setter construction requires EF10. [9P-06](efcore-9-gap-matrix.md#porting-gaps) |
| DEF-04 | Explicit string-to-JSON scalar columns with schema creation, projection, whole-document SaveChanges writes, JSON_VALUE path queries and reverse engineering | Port required | Port required | [Mappings][mappings], [JSON paths][jsonpaths], [Reverse engineering][scaffold] | Retain scalar string/JSON mapping and `OceanBaseJsonValue`. Structured JSON and partial updates remain deferred. [9P-10](efcore-9-gap-matrix.md#porting-gaps) |
| DEF-05 | Provider integration for Spatial/XML/native enums/OBJECT, LOB locators and REF CURSOR | Deferred | Deferred | [Mappings][mappings] | Preserve the deferred scope. |
| DEF-06 | Standalone Int64 model sequence DDL, sequence-backed value allocation and stored-procedure INSERT/UPDATE/DELETE mappings | Port required | Port required | [Sequence DDL][sequence], [HiLo][hilo], [Update SQL generator][update-sql], [Modification batches][modification-batch] | Verify branch-specific EF sequence metadata, TPC allocation and stored-procedure modification/result handling. [9P-08](efcore-9-gap-matrix.md#porting-gaps) |
| DEF-07 | TPH/TPT/TPC, entity/table splitting and many-to-many skip navigations | Port required | Port required | [Inherited generated values][annotations], [HiLo][hilo] | Adapt model and update conventions for TPH/TPT/TPC, splitting and skip navigations. Preserve key allocation and shared rows. [9P-08](efcore-9-gap-matrix.md#porting-gaps) |
| DEF-08 | Split buffering, compiled models, lazy loading, proxies and generated query tooling | Port required | Port required | [Compiled model][compiled], [Query precompilation][precompile] | Regenerate compiled models and proxies with EF9 tools. Preserve lazy loading and split buffering. Track code generation and NativeAOT separately. Query precompilation and NativeAOT are to confirm. [9P-09](efcore-9-gap-matrix.md#porting-gaps) / [9P-11](efcore-9-gap-matrix.md#porting-gaps) |
| DEF-09 | HasData, sync/async seeding callbacks and retry/recovery orchestration for known outcomes | Port required | Port required | [Retry][retry] | Port EF9 seeding and retry integration. Preserve `HasData`, idempotence, owned cleanup and failure outcome handling. Preserve sync and async seeding callbacks. [9P-07](efcore-9-gap-matrix.md#porting-gaps) / [9P-08](efcore-9-gap-matrix.md#porting-gaps) |
| DEF-10 | Full-text/vector/search functions and advanced batching optimizations | Deferred | Deferred | [SQL][sqlgenerator] | Preserve the deferred scope. |

[options]: ../../src/OceanBase.EntityFrameworkCore/OceanBaseDbContextOptionsBuilderExtensions.cs
[cache]: ../../src/OceanBase.EntityFrameworkCore/Internal/OceanBaseModelCacheKeyFactory.cs
[services]: ../../src/OceanBase.EntityFrameworkCore/Internal/OceanBaseOptionsExtension.cs
[connection]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRelationalConnection.cs
[reader]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRetryReaderInterceptor.cs
[logging]: ../../src/OceanBase.EntityFrameworkCore/Internal/OceanBaseLoggingDefinitions.cs
[writes]: ../../src/OceanBase.EntityFrameworkCore/Update/OceanBaseModificationCommandBatch.cs
[conventions]: ../../src/OceanBase.EntityFrameworkCore/Metadata/Conventions/OceanBaseConventionSetBuilder.cs
[schema]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseSchemaSqlGenerator.cs
[sequence]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseSequenceSql.cs
[mappings]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseTypeMappingSource.cs
[commands]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRelationalCommandBuilderFactory.cs
[sqlhelper]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseSqlGenerationHelper.cs
[sqlvisitor]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseSqlTranslatingExpressionVisitorFactory.cs
[sqlgenerator]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseQuerySqlGenerator.cs
[roots]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.cs
[aggregates]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseAggregateMethodTranslator.cs
[strings]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseStringTranslator.cs
[temporal]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseTemporalMethodTranslator.cs
[temporalmap]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseTemporalTypeMapping.cs
[compilation]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseQueryCompilationContextFactory.cs
[batch]: ../../src/OceanBase.EntityFrameworkCore/Update/OceanBaseBatchExecutor.cs
[transactions]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRelationalTransactionFactory.cs
[creator]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseDatabaseCreator.cs
[namespace]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseDatabaseCreator.Namespace.cs
[migrations]: ../../src/OceanBase.EntityFrameworkCore/Migrations/OceanBaseMigrationsSqlGenerator.cs
[scaffold]: ../../src/OceanBase.EntityFrameworkCore/Design/OceanBaseDatabaseModelFactory.cs
[bulk]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseBulkDmlVisitor.cs
[annotations]: ../../src/OceanBase.EntityFrameworkCore/Metadata/OceanBaseRelationalAnnotationProvider.cs
[compiled]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseCompiledModelTypeMappings.cs
[retry]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRetryingExecutionStrategy.cs
