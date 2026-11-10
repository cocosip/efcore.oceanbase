# EF Core 10 Provider Feature Checklist

This is the feature inventory and acceptance boundary for `OceanBase.EntityFrameworkCore` 10.x.
It defines the initial relational release, not feature parity with Npgsql or every EF Core API.
MySQL and Oracle modes are required in the first release and are tracked independently.
Generated SQL or intercepted commands do not prove that OceanBase accepts or executes them correctly.

## Status and maintenance rules

| Status | Meaning |
| --- | --- |
| `Done` | All acceptance criteria and required evidence for this row and mode pass. |
| `In progress` | Code or tests exist, but acceptance is incomplete or a regression is open. |
| `Planned` | Required for the initial release; sufficient implementation evidence does not exist yet. |
| `Deferred` | Outside the initial release; do not advertise support. |
| `Blocked` | Work cannot proceed because of a recorded external dependency. Not a pass. |

`Required` rows block release. `Deferred` rows are a visible backlog, not release blockers. Moving a required
feature to deferred changes scope: record the reason and public limitation in the design and release notes.
Do not remove requirements merely to obtain a green checklist.

Stable IDs connect implementation tasks, tests and release notes. Before implementing a row, read the relevant
[official references](official-references.md), record the EF10 contracts and define positive, boundary and
unsupported cases. Add new rows before implementing new behavior.

A `Done` row must record test methods, command/result and tested revision for each mode. Runtime rows also
require live server evidence; pure configuration, metadata and SQL helper contracts can be proved locally.
One scalar type, operator or happy path cannot establish completion of its entire category.

## Current evidence and blockers

Snapshot: 2026-11-10, `dev` development checkpoint based on 844ec7a. The reviewed implementation and this
evidence record are committed together; this is not a release result.

`dotnet test OceanBase.EntityFrameworkCore.slnx --no-restore -m:1 -nr:false` produced:

The same suite also passed with `-c Release` after the generated-key review fix (95 tests per configuration).

| Layer | Passed | Failed | Evidence boundary |
| --- | --- | --- | --- |
| Unit | 72 | 0 | Options, connection rejection/cleanup, string/decimal facets and generated SQL; incomplete scalar coverage. |
| Command pipeline | 22 | 0 | Commands intercepted before network execution; transactions disabled; unsupported MySQL keys reject before DML. |
| Consumer | 1 | 0 | Project reference, not an isolated packed-package consumer. |
| Packed consumer | Both modes | 0 | Separate package restore and executable checks; no ProjectReference or network/database execution. |
| Live MySQL / Oracle | Not established | Not established | No live provider execution recorded for either mode. |
| Relational specification | Not established | Not established | No specification test project yet. |

The previous connection-validation disposal failures are fixed and have sync/async mutation regression coverage.
Closed supplied/provider-owned connection disposal and invalid replacement preservation are also tested locally.
This does not establish opened-connection ownership or live transaction behavior.

Other gaps include large Oracle string LOB query restrictions, remaining scalar/literal semantics and live mapping
round trips, transactions, database creation and package validation. The [query/update design](efcore-10-query-update-design.md)
records the current implementation and its limits.

Existing evidence references used below identify partial coverage, not automatic satisfaction of a row:

- **UO**: [OptionsTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/OptionsTests.cs).
- **UH**: [SqlGenerationHelperTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/SqlGenerationHelperTests.cs).
- **UT**: [TypeMappingTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/TypeMappingTests.cs).
- **UQ**: [SqlGeneratorTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/SqlGeneratorTests.cs).
- **FP**: [BasicRelationalTests](../../tests/OceanBase.EntityFrameworkCore.FunctionalTests/BasicRelationalTests.cs),
  intercepted execution only.
- **CP**: [ProviderConfigurationTests](../../tests/OceanBase.EntityFrameworkCore.ConsumerTests/ProviderConfigurationTests.cs),
  project reference only.
- **PC**: [Standalone package consumer](../../tests/OceanBase.EntityFrameworkCore.PackageConsumer/Program.cs),
  registration/model/paging smoke checks against the local prerelease package at implementation revision 2a02bc8;
  [commands and evidence](efcore-10-query-update-design.md#package-checkpoint-validation).

`Missing` means tests and evidence still need to be added.

## Configuration, connections and diagnostics

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| CFG-01 | Both UseOceanBase overloads preserve options; invalid mode/null/invalid connection inputs fail clearly in both modes. | Required | In progress | In progress | UO tests overloads in different modes; negative coverage missing. |
| CFG-02 | Database.IsOceanBase() and invariant identify this provider through EF's real service graph. | Required | Done | Planned | CP UseOceanBase_registers_the_provider; Oracle case missing. |
| CFG-03 | Model/service-provider caches separate modes and design-time/runtime models; compatible options reuse services. | Required | In progress | In progress | UO Model_cache_key_includes_compatibility_mode; actual cache reuse/separation tests missing. |
| CFG-04 | Required relational services resolve with EF10 lifetimes; normal DatabaseFacade APIs have no missing services. | Required | In progress | In progress | UO/FP exercise part of graph; database creator absent. |
| CON-01 | Create OceanBaseConnection through public OceanBaseConnector 0.2.0 APIs and supply ExpectedMode. | Required | Done | Done | UO Relational_connection_uses_connector_and_mode. |
| CON-02 | Reject configured/detected mode conflicts before execution for supplied/created connections; cleanup remains safe after rejection. | Required | In progress | In progress | UO mutation/replacement/cleanup regressions pass; opened/detected-mode execution missing. |
| CON-03 | Caller-owned connections survive context disposal; provider-owned connections close/dispose in sync/async success/error paths. | Required | In progress | In progress | UO Context_disposes_only_owned_connector_connections covers closed connections; live ownership missing. |
| CON-04 | Sync/async open, execute, read, close and connection reuse honor mode and connector settings. | Required | In progress | In progress | FP suppresses network; live lifecycle tests missing. |
| CON-05 | Timeout/cancellation reach connector; pre-cancelled/in-flight query/save operations release resources. | Required | In progress | In progress | FP covers pre-cancelled save only. |
| DIA-01 | Logging/interceptors report commands and failures; parameter values stay protected unless sensitive logging is explicitly enabled. | Required | In progress | In progress | FP interceptors; logging/event/redaction assertions missing. |
| DIA-02 | Update failures retain inner connector errors and entries; cancellation is not wrapped as DbUpdateException. | Required | In progress | In progress | FP cancellation; connector-error cases missing. |

## Model and scalar mappings

Mapping acceptance includes metadata, SQL literals, connector parameter metadata and live round trips.
Test nulls, minimum/maximum values and precision/length boundaries. Unsupported CLR/store combinations must fail
explicitly; silent truncation or lossy fallback is not acceptable.

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| MOD-01 | Relational conventions initialize tables, primary/composite/alternate keys, foreign keys and indexes. | Required | In progress | In progress | UQ/FP simple models; composite/relationship metadata tests missing. |
| MOD-02 | Required/optional relationships, owned references/collections and shadow properties preserve model and CRUD behavior. | Required | Planned | Planned | Missing. |
| MOD-03 | Generated/default/computed metadata, explicit names and schema settings produce supported SQL or validation errors. | Required | In progress | In progress | FP generated integer key only; complete generation/schema contract missing. |
| MAP-01 | Boolean and signed/unsigned integers bind/round-trip in documented ranges; predicates use valid Boolean SQL. | Required | In progress | In progress | UT int/long metadata; remaining types, boundaries and Boolean execution missing. |
| MAP-02 | Decimal precision/scale and float/double round-trip; default decimal storage preserves documented fractional precision. | Required | In progress | In progress | UT decimal defaults/explicit types/parameter facets/bounds; live decimal and float/double coverage missing. |
| MAP-03 | Unicode/non-Unicode, fixed/variable and bounded/unbounded text honor length and key/index constraints. | Required | In progress | In progress | UT text facets, key sizes and LOB fallback; live charset/index/LOB behavior missing. |
| MAP-04 | Binary/Guid storage and byte order are explicit and round-trip through parameters, literals and generated values. | Required | In progress | In progress | UT binary metadata; Guid conversion and boundaries missing. |
| MAP-05 | DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan have documented precision, timezone and range semantics. | Required | In progress | In progress | UT DateTime metadata only; temporal round trips missing. |
| MAP-06 | Explicit store type, size, precision/scale, Unicode/fixed facets and null/DBNull parameter values are honored or rejected clearly. | Required | In progress | In progress | UT decimal/string explicit types and null/output facets; remaining scalar types missing. |
| MAP-07 | EF value converters, including ordinary enum-to-integer/string conversions, work in query/update paths. | Required | Planned | Planned | Missing; distinct from native enum store types. |

## Queries and materialization

Enumerate supported operators/overloads and expected SQL/results before implementing each family.
Advertising a family does not imply every .NET overload translates. Live fixtures include empty results, nulls,
duplicates and mode-specific string semantics.

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| SQL-01 | Escape identifiers, schema qualification and embedded delimiters; Oracle table aliases omit AS and table-free SELECT uses DUAL. | Required | In progress | In progress | UH reserved name/UQ aliases; embedded delimiters/schema cases missing. |
| SQL-02 | Parameter names/placeholders follow each dialect. | Required | Done | Done | UH Generates_mode_specific_identifiers_and_parameters. |
| QRY-01 | SELECT/projection, arithmetic/comparison/Boolean predicates and null semantics bind values and materialize nullable/non-null results. | Required | In progress | In progress | UQ/FP scalar projection; broader predicates/live results missing. |
| QRY-02 | OrderBy/ThenBy, Distinct, Skip/Take and offset-only queries return correct results, including deterministic paging/empty pages. | Required | In progress | In progress | UQ SQL shape only; live ordering/string/paging missing. |
| QRY-03 | Inner/left joins, correlated subqueries, Any/All and scalar/collection Contains have verified results or explicit exclusions. | Required | In progress | In progress | UQ simple join only; collection/null/empty cases missing. |
| QRY-04 | GroupBy aggregates, Count/LongCount, Sum/Average/Min/Max handle empty and nullable inputs correctly. | Required | In progress | In progress | UQ grouped Count shape only. |
| QRY-05 | First/Single/default variants enforce cardinality; entity/scalar async materialization matches sync. | Required | In progress | In progress | FP ToList scalar only; entity/cardinality cases missing. |
| QRY-06 | String translations have a supported-overload matrix: equality, Contains, StartsWith/EndsWith, length, substring, case and trim. | Required | Planned | Planned | Missing; include wildcard escaping, collation and Oracle empty-string semantics. |
| QRY-07 | Date/time members, numeric functions, conditional/coalesce expressions have a supported-overload matrix. | Required | Planned | Planned | Missing; include precision, nulls and unsupported overloads. |
| QRY-08 | Include/ThenInclude for reference/collection navigations, tracking/no-tracking and identity resolution preserve related entities. | Required | Planned | Planned | Missing; split queries are listed separately below. |
| QRY-09 | Global query filters compose with parameterized predicates and can be disabled using supported EF APIs. | Required | Planned | Planned | Missing; named filters require EF10 review below. |
| QRY-10 | FromSqlRaw/FromSqlInterpolated and ExecuteSqlRaw/ExecuteSqlInterpolated bind values; hostile parameter values cannot change SQL shape. | Required | Planned | Planned | Missing; raw SQL text remains caller-authored. |
| QRY-11 | Unsupported predicates fail with actionable translation errors; preserve EF's allowed top-level client projection evaluation. | Required | Planned | Planned | Missing; do not promise EF forbids all client projection evaluation. |

## Tracked writes, concurrency and transactions

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| UPD-01 | Sync/async INSERT binds values, supports explicit keys/default-only inserts and propagates generated keys. | Required | In progress | In progress | FP generated/default-only inserts; server identity generation unverified. |
| UPD-02 | Generated/default/computed readback uses same connection; MySQL LAST_INSERT_ID/Oracle RETURNING handle null/converted outputs. | Required | In progress | In progress | FP synthetic outputs; live defaults/computed values/transactions missing. |
| UPD-03 | UPDATE/DELETE bind original keys/tokens, including composite keys and nullable concurrency tokens. | Required | In progress | In progress | FP simple int key/token only. |
| UPD-04 | Stale update/delete throws DbUpdateConcurrencyException with entries; unchanged matched rows succeed in MySQL. | Required | In progress | In progress | FP zero-row update and UO affected-row option rejection; live matched-row/conflict tests missing. |
| UPD-05 | Multi-entity saves respect dependency order and propagate parent/child keys; unsupported readback shapes reject before DML. | Required | Planned | Planned | Missing graph/unsupported-shape tests. |
| TXN-01 | Begin/commit/rollback and existing transaction enlistment work sync/async; incompatible connections/transactions reject. | Required | Planned | Planned | Missing; FP disables transactions. |
| TXN-02 | SaveChanges is atomic across commands/readback; later failure rolls back earlier writes; cancellation leaves no unintended partial commit. | Required | Planned | Planned | Missing live failure/rollback fixtures. |
| TXN-03 | Savepoints and SaveChanges inside an existing transaction preserve caller transaction after recoverable failure. | Required | Planned | Planned | Missing connector/server capability and execution tests. |

## Database creation and schema profile

The acceptance target is an isolated test-owned database/schema with ordinary tables, scalar columns,
primary/foreign keys, non-unique/unique indexes and supported identity/default definitions. This is not implemented
support yet. Before SCH-02/SCH-03 implementation, define database/schema naming, ownership and permissions per mode.
Creating an Oracle tenant must not be inferred to be an EF database-creation operation. Document DDL implicit
commit/transaction behavior per mode.

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| SCH-01 | DatabaseFacade services resolve; existence checks distinguish absence from authentication, permission and network failures. | Required | Planned | Planned | Provider database creator absent. |
| SCH-02 | EnsureCreated/GenerateCreateScript implement schema profile and key generation; existing populated schemas are handled explicitly. | Required | Planned | Planned | Mode-specific DDL design/live fixtures missing. |
| SCH-03 | EnsureDeleted has an explicit ownership/deletion boundary, preserves unrelated tenant objects and fails clearly outside supported profile. | Required | Planned | Planned | Destructive-operation contract missing; tests must use isolated owned fixtures. |

## Packages, compatibility and release evidence

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| PKG-01 | Build net10.0 assets against published OceanBaseConnector 0.2.0 with warnings as errors. | Required | Done | Done | Current solution test command builds assets; not a package-content assertion. |
| PKG-02 | Packed nuspec uses OceanBase.EntityFrameworkCore and EF ranges [10.0.0,11.0.0). | Required | In progress | In progress | Checkpoint nuspec/asset assertions passed; repeatable release package-content gate still needed. |
| PKG-03 | Isolated packed consumer restores/executes provider API without project/local connector references. | Required | Done | Done | PC Main passes registration/model/paging checks; restored from packed provider plus cached published dependencies. |
| PKG-04 | Minimum EF10 and selected later available EF10 patch restore/build/test without pinning consumer patch; EF11 cannot satisfy dependencies. | Required | Planned | Planned | Patch-resolution/compatibility matrix missing; a range does not prove binary compatibility. |
| TST-01 | Selected EF relational specification classes pass; exclusions map to checklist IDs and limitations. | Required | Planned | Planned | Specification project/selection absent. |
| TST-02 | Live core query/write/transaction/schema/scalar suites pass in at least one declared supported environment per mode. | Required | Planned | Planned | No live provider execution recorded. |
| REL-01 | Support notes record server/connector/runtime/EF versions, tenant mode, direct/proxy endpoint, transport/options without credentials. | Required | Planned | Planned | Release evidence record missing. |
| REL-02 | Validated dev checkpoint merges to efcore/10.0; packaging/release checks run on release revision. | Required | In progress | In progress | 2a02bc8 synchronized and locally packed on efcore/10.0; complete release gates remain open. |

## Visible deferred inventory

These limits remain on the roadmap. They must not disappear from the support matrix simply because they
are difficult to implement. Promoting a deferred feature requires its own design and acceptance evidence.

| ID | Feature | MySQL | Oracle | Promotion criteria |
| --- | --- | --- | --- | --- |
| DEF-01 | Migrations/history table/scripts/idempotent scripts/bundles | Deferred | Deferred | DDL/history/design-time plan; upgrade/downgrade tests. |
| DEF-02 | Reverse engineering/provider design-time services/dotnet ef tooling | Deferred | Deferred | Live schema fixtures; packed tooling consumer tests. |
| DEF-03 | ExecuteUpdate/ExecuteDelete, including EF10 lambda/JSON variants | Deferred | Deferred | Translation/live affected-row contract. |
| DEF-04 | JSON, primitive collections/arrays and complex types, including JSON-owned/complex mappings | Deferred | Deferred | Per-mode mapping/query/update design. |
| DEF-05 | Spatial, XML, native enum types, OBJECT, LOB locators, REF CURSOR | Deferred | Deferred | Connector capability review/live mappings; ordinary enum converters belong to MAP-07. |
| DEF-06 | Stored-procedure modification mappings; provider-specific sequence/HiLo strategies | Deferred | Deferred | Parameter/result/generation contracts; reject unsupported mappings clearly. |
| DEF-07 | TPH/TPT/TPC inheritance, table/entity splitting, many-to-many skip navigations | Deferred | Deferred | Model/query/update specification fixtures. |
| DEF-08 | Split queries, lazy loading, compiled-model/tooling optimizations | Deferred | Deferred | Graph/lifetime/generated-model consumer tests. |
| DEF-09 | EF seeding callbacks, execution-strategy retries, ambient/distributed transactions | Deferred | Deferred | Callback/DDL lifecycle, retry safety and transaction capability designs respectively. |
| DEF-10 | Provider full-text/vector/search functions, advanced batching optimizations | Deferred | Deferred | Server-version contract and independent tests/benchmarks. |

Ordinary string/byte-array scalar values remain required even when advanced LOB locator APIs are deferred.
Document deferred limitations and reject unsupported mappings/operations clearly. Inheriting an EF implementation
is not proof of support.

## EF Core 10 compatibility review

Review exact EF10 sources and what's-new/breaking-change references before release. This is an audit,
not an automatic feature claim.

| Review item | Checklist destination | Required outcome |
| --- | --- | --- |
| Named query filters | QRY-09 | Supported composition/disable tests or an explicit named-filter limitation. |
| Complex-type/JSON changes | DEF-04 | Stay deferred until a tested mapping profile is promoted. |
| LINQ/operator and parameterized-collection changes | QRY-03/06/07/11 | Enumerate supported translations and negative cases. |
| ExecuteUpdate changes | DEF-03 | Stay deferred until independent acceptance suite exists. |
| Seeding/schema lifecycle changes | SCH-02/DEF-09 | Separate schema support from unverified seeding callbacks. |
| Diagnostics/security changes | DIA-01/02 | Verify events and sensitive-data protection. |

## Definition of complete and development order

For every required ID: document behavior, implement it, pass both-mode evidence at the required layer,
cover negative/boundary cases and update consumer/package impact. Record live environment and tested revision.
An unavailable or skipped required live test leaves its gate open. Missing additional proxy/TLS/HA/compression
or server-version environments limits the support claim; it neither invalidates a tested direct environment
nor establishes a provider defect.

Release requires every `Required` row to be `Done`, no failing required tests, explicit deferred limitations and
release-branch package evidence. Do not derive completion percentages from test counts or unequal feature rows.
Tested development checkpoints may merge from `dev` into `efcore/10.0` before all rows finish; these are not
release candidates.

Implement in this order:

1. Retain CON-02/UPD-04 cleanup regressions; finish opened-connection ownership/mode evidence.
2. Complete MOD/MAP contracts before expanding SQL; close precision and Oracle text gaps first.
3. Complete QRY/UPD together with TXN evidence on both live modes.
4. Define/implement SCH profile; document unsupported operations before exposing them.
5. Finish specification, packed-consumer, patch-compatibility and release records.

The [implementation plan](efcore-10-implementation-plan.md) tracks tasks; this checklist decides acceptance.
For EF8/EF9 and future EF11, adapt the inventory after reviewing that major's contracts, keep stable IDs for
unchanged behavior and collect new evidence. EF10 status never automatically transfers to another branch.
