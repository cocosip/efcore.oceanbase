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

## Before starting a feature

Use this checklist as the single inventory. Keep implementation details in the linked design/technical baseline
and execution steps in the implementation plan; do not maintain a second, conflicting support list.

Before writing production code, expand the affected row in its design document using this record:

| Field | Required content |
| --- | --- |
| Feature IDs and scope | Exact APIs, CLR/store types, operators and overloads included; explicit exclusions. |
| Version contracts | Official documentation and exact EF/connector source version; affected services and lifetimes. |
| MySQL / Oracle behavior | Storage, SQL and observable results per mode, including any deliberate difference. |
| Acceptance cases | Normal, null/empty, boundary and unsupported inputs; expected result or exception for each. |
| Verification | Named tests at unit, real EF pipeline, live and package layers as applicable. |
| Evidence | Mode, tested revision, command, result and environment; distinguish planned tests from passing tests. |
| Remaining gaps | Unimplemented cases, failing tests, missing environments and their release impact. |

An aggregate row stays open until every included case meets its acceptance criteria. A passing mapping metadata
test cannot substitute for reader conversion, parameter binding or a required live round trip. A new regression
reopens the affected status even when an older checkpoint passed. Before merging, update this inventory and the
affected design together. Before release, compare the public API/support notes against required and deferred rows
so that an undocumented feature is not accidentally advertised.

## Current evidence and blockers

Development resumed at the user's request (2026-11-10) on efcore/10.0. See the
[progress handoff](efcore-10-progress.md) for saved state, remaining work and the exact resume point.

Snapshot: 2026-11-10, `efcore/10.0` text declaration/write slice, continuing from 20a2f18.
The same baseline-built package passes both tested EF patches. This is not a release result.

`dotnet test OceanBase.EntityFrameworkCore.slnx --no-restore -m:1 -nr:false` produced:

The expanded suite passed in Debug and Release (563 tests per configuration).

| Layer | Passed | Failed | Evidence boundary |
| --- | --- | --- | --- |
| Unit | 420 | 0 | Prior mapping coverage plus text declarations/facet/property/clone validation, Unicode and converted character bounds; full explicit-type/live coverage incomplete. |
| Command pipeline | 142 | 0 | Prior intercepted coverage plus text query/materialization, converted write rejection and original-value predicates; transactions disabled. |
| Consumer | 1 | 0 | Project reference, not an isolated packed-package consumer. |
| Packed consumer | Both modes | 0 | Same text-20261110 package passes EF10.0.0/10.0.14, including invalid declaration/UTF-16 rejection and lossless national text query binding; no live execution. |
| Later-patch Release suite | 563 | 0 | Current text source compiled/tested against EF10.0.14 in isolated artifacts; separate from baseline-package compatibility. |
| Live MySQL / Oracle | Not established | Not established | No live provider execution recorded for either mode. |
| Relational specification | Not established | Not established | No specification test project yet. |

Focused scalar run:

`dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj --no-restore -m:1 -nr:false --filter FullyQualifiedName~ScalarMappingTests`

Initial RED result: 24 tests, 2 passed, 22 failed, none skipped. After implementation and additional exact-number
and converter-composition cases, all 28 scalar tests pass. Oracle SQL coverage adds eight cases; the initial
projection tests failed before the search-condition processor was implemented. Full Debug/Release results above
include both suites. MAP-01/MAP-03/MAP-04/MAP-06/MAP-07 and QRY-01 remain `In progress`, because their wider
acceptance criteria and live gates are not complete. The previous 95-test checkpoint remains historical evidence.

Independent review found generated Oracle output values bypassed exact integer reading: fractional decimals could
round and OceanBaseDecimal outputs failed conversion. Output propagation now shares numeric normalization and
checked integral conversion with the reader path. Ten sync/async cases cover exact decimal/OceanBaseDecimal
keys, fractional/overflow rejection and generated Boolean converter outputs. These are intercepted results,
not evidence that an OceanBase server executes the generated SQL.

The previous connection-validation disposal failures are fixed and have sync/async mutation regression coverage.
Closed supplied/provider-owned connection disposal and invalid replacement preservation are also tested locally.
This does not establish opened-connection ownership or live transaction behavior.

Temporal coverage adds 42 unit cases and eight intercepted sync/async query/write/generated-output cases.
The [temporal contract](efcore-10-technical-baseline.md#temporal-mapping-contract) defines all five CLR types,
precision, timezone and duration rules. Mapping-clone regressions cover changing precision without retaining a
stale interval converter or SQL declaration. MAP-05/MAP-06/MAP-07 and UPD-02 remain open: live round trips, server
ranges/session semantics and remaining explicit-type/converter/output cases are not established.

Binary mapping follows the [binary contract](efcore-10-technical-baseline.md#binary-mapping-contract).
The focused suite initially failed 37 of 41 cases; explicit key-default coverage adds a separate RED-to-GREEN
regression. Four review regressions exposed whitespace-sensitive capacity/LOB-key classification; declaration
normalization now fixes them. The focused binary suite passes 46 cases. Intercepted tests add both-mode sync/async
query/write/custom conversion, pre-command length
rejection and synthetic generated binary/Guid output coverage. Oracle empty non-null inputs currently reject
explicitly; empty-BLOB support remains a required open acceptance case, not a deferred feature.

Explicit integer and Boolean storage follows the [integer contract](efcore-10-technical-baseline.md#explicit-integer-and-boolean-storage-contract).
All 48 focused cases initially failed; the implementation now preserves native widths/unsigned aliases, Oracle
zero-scale precision, parameter metadata and converter/clone behavior. Eight intercepted query/write cases cover
explicit columns and pre-command bounds rejection. Independent review exposed query operands incorrectly inheriting
write bounds; four RED-to-GREEN regressions cover arithmetic, CASE, captured parameters and binary comparisons.
Physical bounds now run only for converted current values of written columns in SaveChanges.

Explicit Guid declarations follow the [Guid contract](efcore-10-technical-baseline.md#explicit-guid-storage-contract).
The 33 initial cases all failed before implementation; native text/binary storage now selects matching converters
and parameter metadata, with conflicting facets rejected. Six property-path regressions exposed and fixed EF parsed-length precedence; two connected string-length checks
and eight intercepted
sync/async query/write/generated-output cases pass. Live Guid byte order, server padding and comparisons remain open.

Native binary floating-point storage follows the [floating contract](efcore-10-technical-baseline.md#binary-floating-point-storage-contract).
All 37 initial cases failed before implementation; 44 focused cases now cover declarations, finite inputs,
reader width conversion, limits/subnormal client transport, literals, facets, precision clones and composition.
Twenty-two intercepted cases cover alternate-width query/write and generated outputs, including converted bounds
and finite overflow rejection. Independent review found a clone size facet erased during precision changes;
a RED-to-GREEN regression now preserves it for validation. Live rounding/subnormal/negative-zero/arithmetic,
remaining decimal-backed floating declarations and session-dependent REAL remain required open cases.

Temporal query transport is independent of physical write precision. Ten new UTQ cases initially failed; input
parameters, input-output values and literals now retain transport precision and Oracle interval leading width,
while output-only metadata retains the column declaration. Eight intercepted sync/async cases exercise query
comparands and reject lossy writes before DML. Oracle DateTime DATE comparands promote to timestamp transport;
DateOnly and MySQL native transport limits retain their existing semantics. Independent review found no concrete
defect. Live comparison, server coercion and session behavior remain open.

The user will supply live database access later. Follow the
[live-test credentials and ownership contract](../repository-guidelines.md#live-test-credentials-and-ownership):
use .NET User Secrets for local connection strings and confirm the test-owned schema/cleanup scope before DDL.
The live fixture and secret loading are still to be implemented; credentials are not needed for local tests.

Other gaps include large Oracle string LOB query restrictions, remaining scalar/literal semantics and live mapping
round trips, transactions, database creation and package validation. The [query/update design](efcore-10-query-update-design.md)
records the current implementation and its limits.

Decimal declaration/query/write validation adds 36 UD and 12 FP cases. Of the initial 31 unit cases, 27
failed before implementation; review added two failing direct bare-UNSIGNED clone regressions before their fix.
Queries retain value-inferred precision/scale; only actual converted writes check physical capacity and rounding.
Output-only metadata retains declared facets. The expanded package consumer failed on f645608 and passes on
3365ad9 at both tested EF patches. Unqualified Oracle alias defaults and live server behavior remain open.

National text adds eight UN cases, all initially RED, and four FP sync/async cases. Native Oracle NChar/NVarchar2
metadata and widened input Size survive Guid and enum-to-string converters; generic bindings remain on ordinary
text. The expanded consumer fails on 3365ad9 and passes on d46ae9d at both tested patches. Charset byte limits,
LOBs, surrogate/empty/NULL/padding and server round trips remain required open evidence.

Existing evidence references used below identify partial coverage, not automatic satisfaction of a row:

- **UX**: [StringMappingTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/StringMappingTests.cs),
  37 declarations/direct-property facets/LOB index/clone/converter/Unicode/character-bound cases. Initially 29 failed;
  all pass after implementation. Sixteen additional FP cases cover sync/async text query/materialization,
  converted write bounds, null/empty inputs and predicate-only original values in both modes.
- **UO**: [OptionsTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/OptionsTests.cs).
- **UH**: [SqlGenerationHelperTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/SqlGenerationHelperTests.cs).
- **UT**: [TypeMappingTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/TypeMappingTests.cs).
- **US**: [ScalarMappingTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/ScalarMappingTests.cs),
  28 passing integer range/reader, Boolean parameter/reader, Guid, literal and converter cases.
- **UTM**: [TemporalMappingTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/TemporalMappingTests.cs),
  42 passing temporal mapping, literal, precision/range, wrapper and clone cases.
- **UTQ**: [TemporalQueryMappingTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/TemporalQueryMappingTests.cs),
  10 passing narrow-column query transport, DATE timestamp promotion and input/output metadata cases.
- **UI**: [ExplicitIntegerMappingTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/ExplicitIntegerMappingTests.cs),
  48 passing explicit integer/Boolean declaration, physical write-bound, precision/clone and enum cases.
- **UF**: [FloatingPointMappingTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/FloatingPointMappingTests.cs),
  44 passing native float/double declaration/facet/clone/reader/parameter/literal/composition and client-boundary cases.
- **UD**: [DecimalMappingTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/DecimalMappingTests.cs),
  36 declaration/facet/property/clone/query transport and exact converted-write cases, including extreme Oracle scales.
- **UN**: [StringParameterMappingTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/StringParameterMappingTests.cs),
  eight native national/generic text bindings, query length, null/output and converter-composition cases.
- **UG**: [GuidMappingTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/GuidMappingTests.cs),
  41 passing explicit Guid conversion/declaration/facet/null/composition and connected text-length cases.
- **UB**: [BinaryMappingTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/BinaryMappingTests.cs),
  binary declaration, size/fixed/key, truncation, null/output, clone/converter and Oracle literal/input boundaries.
- **UQ**: [SqlGeneratorTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/SqlGeneratorTests.cs).
- **FP**: [BasicRelationalTests](../../tests/OceanBase.EntityFrameworkCore.FunctionalTests/BasicRelationalTests.cs),
  intercepted execution only.
- **CP**: [ProviderConfigurationTests](../../tests/OceanBase.EntityFrameworkCore.ConsumerTests/ProviderConfigurationTests.cs),
  project reference only.
- **PC**: [Standalone package consumer](../../tests/OceanBase.EntityFrameworkCore.PackageConsumer/Program.cs),
  registration/native floating/explicit Guid/temporal/binary/integer and Boolean model/SQL/paging checks against the
   latest text-20261110 local prerelease package;
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
| MAP-01 | Boolean and signed/unsigned integers bind/round-trip in documented ranges; predicates use valid Boolean SQL. | Required | In progress | In progress | US/UI integer reader/storage/write bounds and explicit Boolean binding pass; UQ Oracle conditions/projections and FP converters pass; full Boolean/live matrix missing. |
| MAP-02 | Decimal precision/scale and float/double round-trip; default decimal storage preserves documented fractional precision. | Required | In progress | In progress | UT/UD decimal defaults/declarations/exact write bounds/query transport/clones and UF native float/double reader widths/literals/finite inputs pass; FP decimal/floating query/write paths pass; aliases, decimal-backed float/REAL and all live coverage remain open. |
| MAP-03 | Unicode/non-Unicode, fixed/variable and bounded/unbounded text honor length and key/index constraints. | Required | In progress | In progress | UT/UX complete supported declarations, character bounds, Unicode, LOB index rejection and FP converted writes pass; BYTE/charset/index/padding/LOB live behavior missing. |
| MAP-04 | Binary/Guid storage and byte order are explicit and round-trip through parameters, literals and generated values. | Required | In progress | In progress | UB/UG declarations/length/facets/null/composition and FP explicit Guid text/binary query/write/generated outputs pass locally; Oracle empty-BLOB transport, padding, large BLOB semantics and all live round trips remain open. |
| MAP-05 | DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan have documented precision, timezone and range semantics. | Required | In progress | In progress | UTM/UTQ five-type mapping/reader/literal/precision/range/cloning, lossless query transport and DML-only precision rejection pass; FP query/write/generated temporal outputs pass; live round trips/coercion/session semantics missing. |
| MAP-06 | Explicit store type, size, precision/scale, Unicode/fixed facets and null/DBNull parameter values are honored or rejected clearly. | Required | In progress | In progress | UT/UD/UTM/UB/UI/UG/UX declarations/facets/clones pass, including direct/property text conflicts and Unicode transport; remaining declarations and live cases missing. |
| MAP-07 | EF value converters, including ordinary enum-to-integer/string conversions, work in query/update paths. | Required | In progress | In progress | US/UI/UD/UN/UTM/UB/UG/UF/UX converter hooks and FP enum/Guid/custom text write bounds/query paths pass; wider custom and live cases missing. |

### Next mapping acceptance cases

These cases refine existing requirements; they do not add features or establish support. The
[technical baseline](efcore-10-technical-baseline.md#numeric-and-guid-reading-contract) records the current
numeric/Guid design. Complete each case in both modes before closing its parent row.

| Parent IDs | Concrete cases still required |
| --- | --- |
| `MAP-01` | All eight integer CLR types at minimum/maximum and nullable values; native MySQL and decimal/OceanBaseDecimal Oracle reader forms; fractional and overflow rejection. |
| `MAP-01`, `QRY-01` | Boolean true/false/null binding and reading; Boolean columns, comparisons, NOT/AND/OR, CASE and EXISTS in predicates and projections; SQL three-valued null behavior. |
| `MAP-03`, `MAP-06` | Empty/null text, quotes, backslashes, control and non-ASCII characters through parameters and literals; MySQL backslash-mode independence; Oracle national literals and empty-string behavior. |
| `MAP-04` | Empty/null/bounded binary; Guid.Empty and a known UUID through parameters, literals and materialization; fixed string/RAW size and documented byte order. |
| `MAP-06`, `MAP-07` | Explicit type/facets, mapping clones and converter composition preserve provider reader/parameter hooks; test real EF query and tracked-write paths. |
| `MAP-05`, `MAP-06` | Run live temporal query comparands/CASE/original-value predicates independently of stored column precision. UTQ/FP prove the local input/output and write-validation boundary; server coercion remains unverified. |
| `MAP-05`, `MAP-06`, `MAP-07`, `UPD-02` | Follow the technical baseline's temporal contract: five CLR types per mode, fractional precision, timezone rules, signed/ranged durations, exact provider wrappers, nulls, literals, readers and generated outputs. |

The focused US/UTM tests cover only part of this table. Decimal/float, temporal, model and live mapping requirements
remain open in MAP-02/MAP-05 and the other parent rows; this table must not replace the full inventory.

## Queries and materialization

Enumerate supported operators/overloads and expected SQL/results before implementing each family.
Advertising a family does not imply every .NET overload translates. Live fixtures include empty results, nulls,
duplicates and mode-specific string semantics.

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| SQL-01 | Escape identifiers, schema qualification and embedded delimiters; Oracle table aliases omit AS and table-free SELECT uses DUAL. | Required | In progress | In progress | UH reserved name/UQ aliases; embedded delimiters/schema cases missing. |
| SQL-02 | Parameter names/placeholders follow each dialect. | Required | Done | Done | UH Generates_mode_specific_identifiers_and_parameters. |
| QRY-01 | SELECT/projection, arithmetic/comparison/Boolean predicates and null semantics bind values and materialize nullable/non-null results. | Required | In progress | In progress | UQ Oracle Boolean predicates/CASE/EXISTS/LIKE/nullable NOT and FP scalar conversions pass; FP integer arithmetic/CASE/large comparand and binary comparison regressions pass; full null/operator/live results missing. |
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
| UPD-02 | Generated/default/computed readback uses same connection; MySQL LAST_INSERT_ID/Oracle RETURNING handle null/converted outputs. | Required | In progress | In progress | FP synthetic exact/overflow/fractional numeric, converted Boolean, Oracle timestamp/zoned and both-mode binary/Guid outputs; null/multiple-output and live defaults/computed/transaction cases missing. |
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
| PKG-03 | Isolated packed consumer restores/executes provider API without project/local connector references. | Required | Done | Done | PC text-20261110 passes both modes on both selected patches, including declaration/UTF-16 rejection and national binding; rerun after provider changes. |
| PKG-04 | Minimum EF10 and selected later available EF10 patch restore/build/test without pinning consumer patch; EF11 cannot satisfy dependencies. | Required | Done | Done | Source Release suite 563 passes at 10.0.14; same baseline-built text package passes EF10.0.0/10.0.14; nuspec ranges rechecked, prior range parser rejects 11.0.0; rerun after changes. |
| TST-01 | Selected EF relational specification classes pass; exclusions map to checklist IDs and limitations. | Required | Planned | Planned | Specification project/selection absent. |
| TST-02 | Live core query/write/transaction/schema/scalar suites pass in at least one declared supported environment per mode. | Required | Planned | Planned | User will supply access later; User Secrets fixture and authorized schema/cleanup scope required; no live provider execution recorded. |
| REL-01 | Support notes record server/connector/runtime/EF versions, tenant mode, direct/proxy endpoint, transport/options without credentials. | Required | Planned | Planned | Release evidence record missing. |
| REL-02 | Tested efcore/10.0 development checkpoint is reviewed; packaging/release checks run on release revision. | Required | In progress | In progress | Development resumed directly on efcore/10.0 as requested; local text package validated; complete release gates remain open. |

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
Tested development checkpoints may be committed on `efcore/10.0` before all rows finish; these are not
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
