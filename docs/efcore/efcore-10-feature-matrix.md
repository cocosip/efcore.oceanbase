# EF Core 10 Feature Matrix

Updated: 2026-11-18; checked against the current workspace code. This document tracks **feature implementation status** only.
For gaps and development status, see the [Gap Matrix](efcore-10-gap-matrix.md). For test, live-database, package and release status, see the
[Release Checklist](efcore-10-release-checklist.md). Module plans and separate progress logs are no longer maintained.

## Definitions and scope

- This matrix covers the original project inventory's 55 feature groups and preserves every feature ID. The eight former PKG/TST/REL items are now in the release checklist.
- `Implemented`: code or EF pipeline integration exists for the ordinary profiles stated in the row. This does not promise every overload or combination, or indicate a release.
- `Partial`: the feature group has an implementation, but the linked API or strategy details are still missing. `Deferred`: explicitly excluded from the current release scope.
- Code status is recorded independently for each mode. Missing live-database, platform or specification-test evidence does not change implementation status; reproduced driver or server issues are listed separately as B items.
- The Provider owns EF translation, mapping, materialization, DDL and orchestration. The Connector owns protocols, authentication/TLS, pooling, parameter/reader transport and native transactions. The server owns SQL, type and constraint semantics.
- Ordinary direct connections in MySQL and Oracle modes are the current scope. Extended deployments such as Proxy, OB20 and HA do not automatically become Provider features or prerequisites for the current release.
- This matrix describes the project's capabilities; it does not promise translation of the entire .NET BCL. Existing rejection contracts, deferred capabilities and items awaiting confirmation are all identified in the gap matrix.

## Feature groups

| ID | Feature and current implementation scope | MySQL code | Oracle code | Owning source | Gaps/boundaries |
| --- | --- | --- | --- | --- | --- |
| CFG-01 | UseOceanBase string/connection/configuration callbacks, generic and non-generic entry points, and input validation | Implemented | Implemented | [Options][options] | — |
| CFG-02 | Provider invariant, Database.IsOceanBase and service identification | Implemented | Implemented | [Options][options] | — |
| CFG-03 | Model/service/compiled-query caches isolated by compatibility mode, preserving the EF base cache key | Implemented | Implemented | [Cache][cache] | — |
| CFG-04 | EF10 relational service registration, scopes and DatabaseFacade service graph | Implemented | Implemented | [Services][services] | — |
| CON-01 | Connection creation through the public Connector API with ExpectedMode | Implemented | Implemented | [Connection][connection] | — |
| CON-02 | Mode validation before and after opening, conflict rejection and failure cleanup | Implemented | Implemented | [Connection][connection] | — |
| CON-03 | Caller/Provider connection ownership, nested leases and preservation of hidden credentials | Implemented | Implemented | [Connection][connection] | — |
| CON-04 | Sync/async connections, early reader disposal, failure cleanup and connection reuse | Implemented | Implemented | [Reader][reader] | — |
| CON-05 | Timeout/cancellation propagation and resource recovery; no driver cancellation protocol implementation | Implemented | Implemented | [Connection][connection] | — |
| DIA-01 | EF command/failure logging, standard interceptors and parameter redaction by default | Implemented | Implemented | [Logging][logging] | — |
| DIA-02 | DbUpdateException inner errors/entries, concurrency events and unwrapped cancellation | Implemented | Implemented | [Writes][writes] | — |
| MOD-01 | Tables, primary/composite/alternate keys, foreign keys, indexes and relationship conventions | Implemented | Implemented | [Model][conventions] | G-44; SET DEFAULT delete actions are rejected |
| MOD-02 | Required/optional relationships, owned references/collections, shadow FKs and relationship fixup | Implemented | Implemented | [Model][conventions] | — |
| MOD-03 | Identity/default/computed metadata, model triggers, names and namespaces | Partial | Partial | [DDL][schema] | `HasTrigger` is metadata only. See G-26..35/G-43/45 |
| MAP-01 | Boolean, eight signed/unsigned integer types, nullable values and range validation | Implemented | Implemented | [Mappings][mappings] | — |
| MAP-02 | Decimal, Single and Double mappings, parameters, literals and readers | Implemented | Implemented | [Mappings][mappings] | B-01..03; driver/server boundaries |
| MAP-03 | Unicode/plain text, fixed/variable length, bounded/LOB storage and character lengths | Implemented | Implemented | [Mappings][mappings] | B-05/06; server boundaries |
| MAP-04 | Binary/Guid, byte order, RAW/VARBINARY/BLOB and empty-value policy | Implemented | Implemented | [Mappings][mappings] | Empty RAW values are rejected; B-06 |
| MAP-05 | DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan value mappings and precision | Implemented | Implemented | [Temporal mappings][temporalmap] | Method translation is tracked in QRY-07/G-42. See G-50 for MySQL TIMESTAMP and C-02 for time zone scope |
| MAP-06 | Explicit store types, size/precision/scale, Unicode/fixed length and NULL/DBNull | Implemented | Implemented | [Mappings][mappings] | Incompatible or lossy combinations are explicitly rejected |
| MAP-07 | Parameters/reads/writes for ordinary enums, nullable values and numeric/text/temporal converters | Implemented | Implemented | [Mappings][mappings] | Algebraic equivalence is not guaranteed for arbitrary converters; G-25 |
| SQL-01 | Identifier escaping, schema qualification and Oracle aliases/DUAL | Implemented | Implemented | [Identifiers][sqlhelper] | B-07 is a tested server boundary |
| SQL-02 | Named parameters and MySQL positional conversion, repeated binding and parameter leases | Partial | Implemented | [Commands][commands] | B-04/B-09; MySQL currently supports input parameters only |
| QRY-01 | Projection, numeric arithmetic/comparisons/Boolean logic, nullable values, CASE/coalesce, conversions and integer bitwise operations | Implemented | Implemented | [Expressions][sqlvisitor] | Integer bitwise operations on text/floating-point carriers are rejected |
| QRY-02 | Ordering, Distinct, nonnegative Skip/Take/offset-only pagination and empty pages | Implemented | Implemented | [SQL][sqlgenerator] | G-20; negative pagination is outside the contract |
| QRY-03 | Inner/left/cross/lateral joins, correlated scalars/collections, Any/All/Contains and set operations | Partial | Partial | [Queries][roots] | G-20..24; B-08 |
| QRY-04 | Count/LongCount/Sum/Average/Min/Max, GroupBy and grouped first-row/aggregate combinations | Implemented | Implemented | [Aggregates][aggregates] | B-02/03; G-46; LOB extrema are rejected |
| QRY-05 | First/Single/OrDefault, ordinary predicates, cardinality and empty-result materialization | Partial | Partial | [Queries][roots] | G-19 supports explicit scalar defaults. Explicit SingleOrDefault defaults are terminal-only. Entity and structured defaults remain unsupported |
| QRY-06 | String equality/concat, Contains/StartsWith/EndsWith/IndexOf(string/char), Length, Substring, Remove, Replace, Insert, PadLeft/PadRight, parameterless case methods and ordinary/explicit character-set trim | Partial | Partial | [Strings][strings], [IndexOf][indexof], [Explicit trim][trim] | G-17 |
| QRY-07 | Date/time members, calendar/duration methods and DateOnly/TimeOnly conversions/ranges, Math/MathF/decimal, Round/Clamp/extrema | Partial | Partial | [Temporal methods][temporal] | G-12/G-39/G-41/42; B-03; see the detailed support/signature tables below |
| QRY-08 | Include/ThenInclude, single/split graph queries and three tracking/identity-resolution modes | Implemented | Implemented | [Queries][roots] | Broader graph combinations belong to acceptance; C-04 |
| QRY-09 | EF10 named filters, context values and selective/all IgnoreQueryFilters | Implemented | Implemented | [Compilation context][compilation] | — |
| QRY-10 | FromSqlRaw/Interpolated, SqlQueryRaw/SqlQuery, ExecuteSqlRaw/Interpolated and materialization | Implemented | Implemented | [Commands][commands] | B-04; broader error contracts belong to acceptance |
| QRY-11 | Translation errors for unsupported predicates, preserving EF-permitted top-level client projection | Implemented | Implemented | [Expressions][sqlvisitor] | Support is not inferred for unlisted BCL methods |
| UPD-01 | Sync/async INSERT, assigned keys, default-only inserts and ordinary identity retrieval | Implemented | Implemented | [Writes][writes] | G-26/27 |
| UPD-02 | Same-connection generated/default/computed value readback, LAST_INSERT_ID / RETURNING INTO | Partial | Implemented | [Writes][writes] | G-26/27; explicit Oracle PrepareExecute |
| UPD-03 | UPDATE/DELETE, original keys/tokens, composite keys and nullable concurrency values | Implemented | Implemented | [Writes][writes] | G-43; ordinary manually managed tokens are implemented |
| UPD-04 | Matched rows/concurrency conflicts, entries and standard concurrency interceptors/suppression | Implemented | Implemented | [Writes][writes] | MySQL requires UseAffectedRows=false |
| UPD-05 | Dependency ordering, parent/child key propagation, relationship replacement/orphans and graph saves | Partial | Partial | [Batch execution][batch] | G-26..28; ordinary graphs are implemented |
| TXN-01 | Sync/async Begin/Commit/Rollback, UseTransaction and ownership validation | Implemented | Implemented | [Transactions][transactions] | See G-40 for ambient/distributed transactions |
| TXN-02 | Multi-command SaveChanges, generated-value readback and failure/cancellation rollback orchestration | Implemented | Implemented | [Batch execution][batch] | Actual atomicity/interruption evidence belongs to acceptance |
| TXN-03 | Savepoints, SaveChanges within an existing transaction and recoverable failures | Implemented | Implemented | [Transactions][transactions] | Oracle uses a logical release; this is not a server RELEASE |
| SCH-01 | CanConnect/existence checks, distinguishing absence from authentication/permission/network failures | Implemented | Implemented | [Database lifecycle][creator] | — |
| SCH-02 | GenerateCreateScript/EnsureCreated/CreateTables, table/column comments and explicit schema management | Partial | Partial | [DDL][schema] | G-29 accepts trigger metadata. G-31..35/G-43..45. DDL implicitly commits and must not be retried automatically |
| SCH-03 | Explicit namespace creation/deletion, management factories and scope/ownership validation | Implemented | Implemented | [Namespace][namespace] | Custom native connections use an explicit management factory |
| DEF-01 | Migrations/model differ including table/column comments, history/locking, upgrade/rollback/idempotent scripts and CLI/bundles | Partial | Partial | [Migrations][migrations] | G-29 requires explicit trigger SQL. See G-31..35/G-43..45 and DEF-06 for sequences |
| DEF-02 | GetSchema reverse engineering, tables/views/keys/relationships/indexes, design-time services and C# generation | Partial | Partial | [Reverse engineering][scaffold] | MySQL BTREE prefix indexes and DATETIME `ON UPDATE` are supported (G-36/G-37). See G-36/G-38/G-50 and DEF-05 for remaining scope |
| DEF-03 | ExecuteUpdate/Delete, EF10 setter lambdas, joined/paged bulk operations and snapshot rewrites | Partial | Partial | [Bulk][bulk] | G-25; see DEF-04 for JSON |
| DEF-04 | Mapping/query/update for JSON, mapped primitive collections/arrays and complex/JSON-owned types | Deferred | Deferred | [Queries][roots] | D-04; distinct from captured collection parameters |
| DEF-05 | Provider integration for Spatial/XML/native enums/OBJECT, LOB locators and REF CURSOR | Deferred | Deferred | [Mappings][mappings] | D-05; ordinary enum converters/scalar LOBs are implemented |
| DEF-06 | Stored-procedure INSERT/UPDATE/DELETE mappings and sequences/HiLo | Deferred | Deferred | [Writes][writes] | D-06; ordinary SQL/identity support is not deferred |
| DEF-07 | TPH/TPT/TPC, entity/table splitting and many-to-many skip navigations | Partial | Partial | [Inherited generated values][annotations] | G-28; ordinary models/CRUD are implemented |
| DEF-08 | Split-query buffering, ordinary compiled models and lazy proxies/ILazyLoader | Implemented | Implemented | [Compiled model][compiled] | Streaming nested lazy loading is unsupported; see C-01/C-04 for extended capabilities |
| DEF-09 | HasData, sync/async seeding callbacks and retry/recovery orchestration for known outcomes | Partial | Partial | [Retry][retry] | G-40; unknown outcomes, commits and cleanup are not replayed |
| DEF-10 | Full-text/vector/search functions and advanced batching optimizations | Deferred | Deferred | [SQL][sqlgenerator] | D-10 |

## Implemented query API scope

| API family | Implemented scope | Contract boundaries |
| --- | --- | --- |
| Collection parameters/inline roots | Eight integer types, Boolean, Decimal/Single/Double, string, Binary/Guid, five temporal types, nullable values, enums/compatible converters and empty collections | Explicit Constant/MultipleParameters; single JSON parameters/collection properties are unsupported; Oracle contextual CHAR/NCHAR and ordinary BLOB comparisons are rejected |
| Strings | Equality, 2/3/4-string concat, Contains/StartsWith/EndsWith(string/char), Length, both int Substring/Remove overloads, parameterless ToLower/ToUpper and Trim/TrimStart/TrimEnd with no argument, char or char[] | Native character/space/collation semantics; Oracle scalar empty strings are NULL. Char search scalars use contextual receiver mappings/converters. Explicit trim uses a case-sensitive stored-text character set independent of the receiver converter; null/empty arrays select CLR whitespace while no-argument trim retains ordinary spaces. Constants/refreshed arrays/inline char columns are supported; NULL chars propagate, invalid surrogate/custom char carriers reject. Trim/Remove use variable text result mappings and the receiver inverse once. Oracle native CLOB trim can retain empty LOBs; guarded Remove empties are NULL including LOBs. See G-17 for unsupported comparison overloads |
| IndexOf (G-18) | IndexOf(string/char), IndexOf(string/char,int), IndexOf(string/char,int,int) | Native character positions and collation with a zero-based index or -1. NULL inputs or invalid bounds return SQL NULL. A known empty needle returns the valid start position, including Oracle scalar literals and parameters. Scalar needles use contextual receiver mappings and text-backed columns retain their own mappings |
| Replace (G-47) | Replace(string,string), Replace(char,char) | Native literal replacement in stored text. NULL receiver or NULL/empty old token returns SQL NULL. A NULL/empty string replacement deletes matches. NULL char operands propagate. Scalar tokens use plain contextual mappings. Variable national and LOB results retain the receiver converter |
| Insert (G-48) | Insert(int,string) | Zero-based native character index in [0,length]. NULL input or an invalid index returns SQL NULL. Known empty scalar values leave the source intact, including Oracle literals and parameters. Result mappings preserve variable national/LOB text and the receiver converter |
| Padding (G-49) | PadLeft(int), PadLeft(int,char), PadRight(int), PadRight(int,char) | Native character widths without truncation. Negative or NULL width, NULL source and NULL char operands return SQL NULL. The one-argument overloads use ordinary space. Variable national/LOB results retain the receiver converter |
| Temporal members | Date/TimeOfDay, calendar/fractional components, DayOfYear/Week, DateOnly.DayNumber, Ticks, seven TimeSpan.Total* members, signed TimeSpan components and server clocks | DateTime.Kind=Unspecified; MySQL microseconds and an exact TIME range of ±838:59:59; Oracle 100ns; Gregorian calendar |
| Temporal methods | DateOnly.AddDays/AddMonths/AddYears; DateTime.AddMonths/AddYears, AddDays/AddHours/AddMinutes/AddSeconds/AddMilliseconds/AddMicroseconds(double), AddTicks(long), Add(TimeSpan)/Subtract(TimeSpan), Subtract(DateTime); TimeOnly.Add(TimeSpan)/AddHours(double)/AddMinutes(double) | DateTime numeric offsets truncate whole/fraction ticks separately; AddTicks keeps exact Int64 values. Timestamp results promote to DATETIME(6)/TIMESTAMP(7); date differences use exact signed ticks with duration members/composition. TimeOnly wraps at 24 hours, promotes clock precision, accepts full-range scalar/tick durations and truncates the whole binary product with Int64 saturation for numeric additions. MySQL sub-microsecond results return NULL; incompatible duration converters and mixed difference set/membership/binary carriers reject. See G-01..13 and the README for detailed range/converter contracts |
| DateTimeOffset | Calendar/fractional components, Date/TimeOfDay, UtcDateTime/UtcTicks/UtcNow, Subtract(DateTimeOffset), AddTicks(long), Add(TimeSpan) and Subtract(TimeSpan). Oracle also supports DateTime/Ticks/Offset/TotalOffsetMinutes | Arithmetic uses stored precision and propagates SQL NULL. Adjustments preserve the stored numeric offset in Oracle mode. MySQL stores UTC and returns zero offset. Out-of-range results and MySQL sub-microsecond adjustments return SQL NULL. Named-zone/DST support has not been established |
| Math | Abs/Sign/Truncate/Floor/Ceiling, Sqrt/Exp/Log/Log2/Log10/Pow, trigonometric/inverse trigonometric functions, Sinh/Cosh/Tanh/Asinh/Acosh/Atanh, CopySign, Clamp, finite extrema and Least/Greatest | The [Math][math]/[Round][round] translators define exact signatures. Nonfinite parameters and literals are rejected. Acosh returns SQL NULL for SQL NULL and inputs below 1 |
| Round | All five MidpointRounding modes for Decimal/Double/Single; constant digits and refreshed captured digits/rules | Respective digits ranges are 0..28/15/6; row-dependent rules/digits are rejected |
| Results/composition | Tracking/identity resolution, converters/shadow/owned properties, DefaultIfEmpty including explicit scalar defaults, grouped/lateral queries, four set operations and ordinary raw entity/scalar/DML | See G-19 for explicit default scope and the gap matrix for comparer, cross-converter and LOB limitations |

### DateOnly/TimeOnly conversion signatures (G-13)

| Signature | Contract in both modes |
| --- | --- |
| DateOnly.FromDateTime(DateTime) | Extract the stored calendar date, ignoring the clock |
| TimeOnly.FromDateTime(DateTime) | Extract the stored clock with native microsecond/100ns precision |
| TimeOnly.FromTimeSpan(TimeSpan) | Accept only ticks in [0,24h); invalid row values return NULL, without wrapping. Scalars use exact ticks across the full TimeSpan range; MySQL unrepresentable sub-microsecond results return NULL |
| TimeOnly.ToTimeSpan() | Return a nonnegative duration using the native duration carrier |
| DateOnly.ToDateTime(TimeOnly), ToDateTime(TimeOnly, DateTimeKind.Unspecified) | Combine stored date and clock with promoted timestamp precision and Kind.Unspecified; UTC/Local/dynamic Kind are not translated |
| TimeOnly.IsBetween(TimeOnly, TimeOnly) | Inclusive start, exclusive end, midnight wrapping; equal endpoints are empty. Scalar bounds adopt the clock converter with promoted precision; mismatched column converters reject |

These type-changing conversions use stored calendar space, like existing cross-type temporal members;
the new result uses its own built-in mapping, without applying an inverse converter for the input type.
NULL values propagate, and incompatible/null-converting carriers reject. Pure scalar calls may be
evaluated by EF before translation; EF.Parameter keeps a scalar duration in SQL for range guarding.

For exact numeric/text/temporal storage contracts and usage examples, see the [Package README](../../src/OceanBase.EntityFrameworkCore/README.md).
Do not duplicate acceptance pass counts, individual fix histories or the full commit history here; update acceptance evidence only in the release checklist.

[options]: ../../src/OceanBase.EntityFrameworkCore/OceanBaseDbContextOptionsBuilderExtensions.cs
[cache]: ../../src/OceanBase.EntityFrameworkCore/Internal/OceanBaseModelCacheKeyFactory.cs
[services]: ../../src/OceanBase.EntityFrameworkCore/OceanBaseDbContextOptionsBuilderExtensions.cs
[connection]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRelationalConnection.cs
[reader]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRetryReaderInterceptor.cs
[logging]: ../../src/OceanBase.EntityFrameworkCore/Internal/OceanBaseLoggingDefinitions.cs
[writes]: ../../src/OceanBase.EntityFrameworkCore/Update/OceanBaseModificationCommandBatch.cs
[conventions]: ../../src/OceanBase.EntityFrameworkCore/Metadata/Conventions/OceanBaseConventionSetBuilder.cs
[schema]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseSchemaSqlGenerator.cs
[mappings]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseTypeMappingSource.cs
[commands]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRelationalCommandBuilderFactory.cs
[sqlhelper]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseSqlGenerationHelper.cs
[sqlvisitor]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseSqlTranslatingExpressionVisitorFactory.cs
[sqlgenerator]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseQuerySqlGenerator.cs
[roots]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.cs
[aggregates]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseAggregateMethodTranslator.cs
[strings]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseStringTranslator.cs
[indexof]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseStringIndexOfTranslator.cs
[trim]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseExplicitTrimTranslator.cs
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
[math]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseMathTranslator.cs
[round]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseRoundTranslator.cs
