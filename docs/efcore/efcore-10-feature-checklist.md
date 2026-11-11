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

An open row is not automatically waiting for a database. Track the remaining work explicitly:

- **Local implementation or coverage:** remaining collection families/converter projections, wider functions/aggregates,
  wider model/materialization/Include/filter contracts, writes/transactions and schema creation/deletion still
  have locally feasible work. No live connection is needed to continue those implementation and pipeline tests.
- **Live validation:** implemented mappings and SQL still need OceanBase round trips and execution in both
  modes, including server coercion/ranges, collation/padding/LOB behavior, transactions and schema lifecycle.
  Unit tests and intercepted readers do not close these requirements.
- **Other validation:** hosted Windows/Linux evidence applies only to its tested revision; package and
  EF-patch checks have their own recorded boundaries. EF8/EF9 adaptation remains postponed by user choice.

Development resumed at the user's request (2026-11-10) on efcore/10.0. See the
[progress handoff](efcore-10-progress.md) for saved state, remaining work and the exact resume point.

Latest local write checkpoint: Batch5a after6714626 fixes unused NULL original predicate binds while
retaining NULL writes.24 focused graph/composite-concurrency/error/cancellation cases and full Debug,
Release/isolated14 each1986=1474+510+2 pass, no failures/skips. Same graph-update-20261111 nupkg passes
content and both-mode/patch consumers; prior lifecycle package fails the new NULL-token smoke. Package,
Release and consumer DLL identities match. Independent review found and closed an async token-count test
gap; no production findings remain. Transactions/live remain open.

Integer bitwise local checkpoint (Batch3u after42e45a8): BitwiseQueryTests has22 cases and
BasicRelationalTests adds8 intercepted sync/async cases. Both modes cover AND/OR/XOR/complement,
negative/signed and full UInt64 values, small binary/unary promotions, numeric enums/converters,
nullable compensation and refreshed captured binds. MySQL normalizes signed results and integer CAST
syntax; Oracle uses signed64-safe BITAND halves for UInt64 and NUMBER identities for OR/XOR. The
public server source audit is a range precaution, not a reported connector defect or deployed-server
acceptance result. Full Debug/Release and isolated EF10.0.14 Release each pass2217=1529+686+2, zero
failures/skips. Source/test review approves. Shifts, broader cast semantics, volatile single evaluation
and native results remain open. Package validation is recorded in the progress handoff.

Historical local lifecycle checkpoint: Batch6a after0b04af6 fixes failed provider Open/OpenAsync accounting
and physical cleanup using isolated EF10 private-field contracts.36 focused cases and final full
Debug/Release/isolated14 suites pass1962=1474+486+2, no failures/skips. Same final lifecycle nupkg passes
both-mode consumers on10.0.0/10.0.14 and the package-content gate. Raw close failure remains best effort;
the opening exception retains cleanup evidence. Broader lifecycle/transaction and live gates stay open.

Historical model/materialization checkpoint: Batch4a/4b/4c after 02a3e3d adds 58 inherited-provider
characterization cases without changing production code. Full Debug/Release and isolated EF10.0.14 Release
each pass 1926 (1438 unit, 486 intercepted, two project consumers), zero failures/skips. Independent review
found and closed complete-projection and filter-logic/binding assertion gaps; final covering58 pass all three
configurations. See the progress handoff and local plan for evidence. Required runtime rows remain partial;
there is no new package, hosted or live claim for this test/docs-only change.

Historical scalar snapshot: 2026-11-11, `efcore/10.0` floating Round after dbb14ee;
source/patch/package gates and independent source review pass.
The same baseline-built package passes both tested EF patches. This is not a release result.

`dotnet test OceanBase.EntityFrameworkCore.slnx --no-restore -m:1 -nr:false` produced:

The expanded suite passed in Debug and Release (1780 tests per configuration), including floating and decimal Round
with constant digits and midpoint rules, scalar extrema and prior numeric aggregates.

| Layer | Passed | Failed | Evidence boundary |
| --- | --- | --- | --- |
| Unit | 1414 | 0 | Prior coverage plus eight floating Round cases for overloads, rules, boundaries, mappings and fallback. |
| Command pipeline | 364 | 0 | Prior coverage plus four floating Round cases for SQL, current bindings and supplied readers; transactions disabled. |
| Consumer | 2 | 0 | Both-mode provider detection through a project reference, not an isolated packed-package consumer. |
| Packed consumer | Both modes | 0 | Same floating-round-20261111 package passes EF10.0.0/10.0.14 with floating Round and prior checks; README included; no live execution. |
| Later-patch Release suite | 1780 | 0 | Current source passes EF10.0.14 in isolated artifacts; baseline source assets stay EF10.0.0. |
| Hosted Windows/Linux CI | Both jobs succeeded | 0 failed steps | Run 42865290840 at e41981e: restore, Release test, pack and content gate succeeded; later commits require their own hosted evidence. |
| Live MySQL / Oracle | Not established | Not established | No live provider execution recorded for either mode. |
| Relational specification | Not established | Not established | No specification test project yet. |

Historical scalar foundation run (retained within the current full suite):

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

Batch 2d closes the reproduced OceanBaseDecimal-to-Single/Double normalization gap: wide magnitude/precision
and tiny values parse directly at target precision instead of overflowing exact CLR decimal first. Forty-one
unit and eight intercepted cases cover range/scale/culture, direct Single midpoint rounding, converter readers
and generated outputs. Exact integer/decimal and native non-finite paths remain intact. The previous unary
package fails the new smoke; the wide-decimal package passes both selected patches. No new store mapping is implied.

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
- **URS**: [RelationalShapeQueryTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/RelationalShapeQueryTests.cs),
  30 both-mode integral membership/mapping/cache-mode/ordinary join/subquery/quantifier/count cases.
- **UTC**: [TextCollectionQueryTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/TextCollectionQueryTests.cs),
  25 text membership/converter/mapping/current-value/NULL/padding/snapshot/cache/name-reuse cases.
- **URC**: [CollectionRootQueryTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/CollectionRootQueryTests.cs),
  57 captured/nonempty/originally-empty inline integral root, type/mapping/pruning/ordinal/cast/cache/boundary cases.
- **UFC**: [FloorCeilingQueryTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/FloorCeilingQueryTests.cs),
  46 exact method/range/converter/NULL/precedence cases; preserves the earlier MathQueryTests contracts.
- **UTR**: [TranscendentalQueryTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/TranscendentalQueryTests.cs),
  65 exact method/Single normalization/independent converter/NULL/decimal result-reader/clone/boundary cases.
- **UNA**: [NumericAggregateQueryTests](../../tests/OceanBase.EntityFrameworkCore.UnitTests/NumericAggregateQueryTests.cs),
  279 exact numeric aggregate/Single/independent mapping/plugin/fallback/SUM zero/null/literal cases.
- **FP**: [BasicRelationalTests](../../tests/OceanBase.EntityFrameworkCore.FunctionalTests/BasicRelationalTests.cs),
  intercepted execution only.
- **CP**: [ProviderConfigurationTests](../../tests/OceanBase.EntityFrameworkCore.ConsumerTests/ProviderConfigurationTests.cs),
  project reference only.
- **PC**: [Standalone package consumer](../../tests/OceanBase.EntityFrameworkCore.PackageConsumer/Program.cs),
  registration/native floating/explicit Guid/temporal/binary/integer and Boolean model/SQL/paging checks against the
  latest floating-round-20261111 local prerelease package, including floating and decimal Round, native scalar extrema, captured array updates,
  native AVG, neutral Single aggregate results,
  filtered DISTINCT/HAVING/order, twelve exact transcendental predicates,
  neutral Single casts/Log quotient, eight rounding predicates and prior inline-empty roots/ordinal identity,
  changing captured roots/Oracle DUAL per arm, integral/text membership, collection-mode cache separation and prior checks;
  [commands and evidence](efcore-10-query-update-design.md#package-checkpoint-validation).

`Missing` means tests and evidence still need to be added.

## Configuration, connections and diagnostics

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| CFG-01 | Both UseOceanBase overloads preserve options; invalid mode/null/invalid connection inputs fail clearly in both modes. | Required | Done | Done | UO both-mode typed/untyped positive cases, Null_builders/Null_connections/Invalid_modes/Invalid_connection_strings and delayed null-string assignment pass in Debug/Release; facade APIs stay open under CFG-04. |
| CFG-02 | Database.IsOceanBase() and invariant identify this provider through EF's real service graph. | Required | Done | Done | CP UseOceanBase_registers_the_provider passes both modes in Debug/Release; UO confirms invariant and null facade guard. |
| CFG-03 | Model/service-provider and compiled-query caches separate compatibility/translation modes and design-time/runtime models; compatible options reuse services. | Required | Done | Done | UO/URS model/service/key/mode/design/runtime and same-expression default/Constant order regressions pass in Debug/Release and isolated EF10.0.14; same-package PC passes both patches. Full relational base key retained. Positive model retention uses a test-owned cache; explicit eviction preserves keys/services and rebuilds equivalent models. |
| CFG-04 | Required relational services resolve with EF10 lifetimes; normal DatabaseFacade APIs have no missing services. | Required | In progress | In progress | Scoped creator resolves facade connection, timeout, transaction accessor and raw SQL; schema lifecycle remains open. |
| CON-01 | Create OceanBaseConnection through public OceanBaseConnector 0.2.0 APIs and supply ExpectedMode. | Required | Done | Done | UO Relational_connection_uses_connector_and_mode. |
| CON-02 | Reject configured/detected mode conflicts before execution for supplied/created connections; cleanup remains safe after rejection. | Required | In progress | In progress | UO mutation/replacement/cleanup regressions pass; opened/detected-mode execution missing. |
| CON-03 | Caller-owned connections survive context disposal; provider-owned connections close/dispose in sync/async success/error paths. | Required | In progress | In progress | UO closed disposal and ConnectionLifecycleTests failed-open count/ownership/captured-native cleanup pass. Caller-opened/outer leases, cancellation and retry use deterministic physical doubles; native close failure requires best-effort/direct cleanup. Actual opened ownership/disposal and live behavior remain open. |
| CON-04 | Sync/async open, execute, read, close and connection reuse honor mode and connector settings. | Required | In progress | In progress | FP commands plus registered-provider failed-open accounting and physical-state/retry doubles pass; no live lifecycle execution. EF10 private snapshot contracts require patch review. |
| CON-05 | Timeout/cancellation reach connector; pre-cancelled/in-flight query/save operations release resources. | Required | In progress | In progress | CommandExecutionBoundaryTests covers SetCommandTimeout reaching DbCommand, sync/async in-flight cancellation identity/token and retry with EF-visible closed state in both modes. Physical connector close/reuse, wire cancellation, server timeout enforcement and live resource behavior remain open. |
| DIA-01 | Logging/interceptors report commands and failures; parameter values stay protected unless sensitive logging is explicitly enabled. | Required | In progress | In progress | CommandLoggingTests covers both modes/sync/async: command and synthetic failure logs are emitted; runtime-captured values are redacted by default and visible only with EnableSensitiveDataLogging. Connector/server sinks and live policy remain open. |
| DIA-02 | Update failures retain inner connector errors and entries; cancellation is not wrapped as DbUpdateException. | Required | In progress | In progress | GraphUpdateTests retains synthetic connector-error/entry identity and in-flight cancellation/token in both modes sync/async, including MySQL generated readback. Actual connector/network failure and cancellation remain live gates. |

## Model and scalar mappings

Mapping acceptance includes metadata, SQL literals, connector parameter metadata and live round trips.
Test nulls, minimum/maximum values and precision/length boundaries. Unsupported CLR/store combinations must fail
explicitly; silent truncation or lossy fallback is not acceptable.

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| MOD-01 | Relational conventions initialize tables, primary/composite/alternate keys, foreign keys and indexes. | Required | In progress | In progress | ModelMaterializationTests initializes composite primary/alternate keys, required/optional composite FKs, explicit table/column/index names and owned/shadow metadata in both modes; complete model/write/schema acceptance remains open. |
| MOD-02 | Required/optional relationships, owned references/collections and shadow properties preserve model and CRUD behavior. | Required | In progress | In progress | ModelMaterializationTests covers metadata and sync/async intercepted required/optional/owned graph reads, fixup and shadow values under all three tracking modes. Graph CRUD and live behavior remain open. |
| MOD-03 | Generated/default/computed metadata, explicit names and schema settings produce supported SQL or validation errors. | Required | In progress | In progress | Offline DDL validates supported identity/default/computed metadata and rejects unsupported shapes. GeneratedValuesTests pins multi-column default/computed runtime propagation through interception. Wider generation/inheritance and live schema validation remain open. |
| MAP-01 | Boolean and signed/unsigned integers bind/round-trip in documented ranges; predicates use valid Boolean SQL. | Required | In progress | In progress | US/UI all-eight nullable endpoints, decimal/finite-floating fraction rejection, numeric property facets and Boolean false/null binding pass; FP exact key/enum outputs and scalar Boolean cells pass. Full Boolean operators/null SQL and all live evidence remain open. |
| MAP-02 | Decimal precision/scale and float/double round-trip; default decimal storage preserves documented fractional precision. | Required | In progress | In progress | UT/UD decimal declarations/exact write bounds/query transport/clones and alias independence pass; UF/FP native floating and direct wide OceanBaseDecimal normalization/Single midpoint/range/scale/converter paths pass. Decimal-backed storage/REAL promotion and all live coverage remain open. |
| MAP-03 | Unicode/non-Unicode, fixed/variable and bounded/unbounded text honor length and key/index constraints. | Required | In progress | In progress | UT/UX complete supported declarations, character bounds, Unicode, LOB index rejection and FP converted writes pass; BYTE/charset/index/padding/LOB live behavior missing. |
| MAP-04 | Binary/Guid storage and byte order are explicit and round-trip through parameters, literals and generated values. | Required | In progress | In progress | UB/UG declarations/length/facets/null/composition and FP explicit Guid text/binary query/write/generated outputs pass locally; Oracle empty-BLOB transport, padding, large BLOB semantics and all live round trips remain open. |
| MAP-05 | DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan have documented precision, timezone and range semantics. | Required | In progress | In progress | UTM/UTQ five-type mapping/reader/literal/precision/range, actual facet/property and transport-family/DbType/postfix clone validation pass; FP query/write/generated outputs pass. Query input remains lossless; all live calendar/coercion/session evidence remains open. |
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
| SQL-01 | Escape identifiers, schema qualification and embedded delimiters; Oracle table aliases omit AS and table-free SELECT uses DUAL. | Required | In progress | In progress | UH now covers embedded delimiters, schema/Unicode/empty-name escaping and all builder overloads; UQ aliases pass; table-free/live SQL evidence still incomplete. |
| SQL-02 | Parameter names/placeholders follow each dialect. | Required | Done | Done | UH Generates_mode_specific_identifiers_and_parameters. |
| QRY-01 | SELECT/projection, arithmetic/comparison/Boolean predicates and null semantics bind values and materialize nullable/non-null results. | Required | In progress | In progress | UQ/FP cover Boolean/string/scalar handling and integer AND/OR/XOR/complement, signed/unsigned widths, promotions, nullable/enums and bind refresh; MySQL query parameters use positional `?` ordering at the Connector 0.2.0 boundary. Shifts, broader cast/overflow semantics, wider operators and live results remain open. |
| QRY-02 | OrderBy/ThenBy, Distinct, Skip/Take and offset-only queries return correct results, including deterministic paging/empty pages. | Required | In progress | In progress | UQ SQL shape only; live ordering/string/paging missing. |
| QRY-03 | Inner/left joins, correlated subqueries, Any/All and scalar/collection Contains have verified results or explicit exclusions. | Required | In progress | In progress | URS/UTC/URC/FP cover ordinary joins/subqueries/quantifiers, bare integral/text membership, captured int/int? roots and nonempty/originally-empty inline roots. Typed zero-row metadata survives subqueries/pruning, including a bounded sole-ordinal repair. Consistent int-to-long bindings pass; converted Join Value projections can fail earlier EF inference. General text/other carriers and wider shapes remain local work; actual server results remain open. |
| QRY-04 | GroupBy aggregates, Count/LongCount, Sum/Average/Min/Max handle empty and nullable inputs correctly. | Required | In progress | In progress | URS/UNA/FP cover Count/LongCount plus native numeric Sum/Average/Min/Max, all exact numeric/nullable selector families, filtered DISTINCT/HAVING/order, independent promoted Double, neutral Single results, converter-owned SUM zero and supplied empty/null/checked reads. Nonnumeric extrema and real aggregate execution remain open. |
| QRY-05 | First/Single/default variants enforce cardinality; entity/scalar async materialization matches sync. | Required | In progress | In progress | FP scalar reads and ModelMaterializationTests entity/graph sync/async reads; First/Single/default variants assert limits and empty/one/two reader cardinality. Native limits/results and wider materialization cases remain open. |
| QRY-06 | String translations have a supported-overload matrix: equality, Contains, StartsWith/EndsWith, length, substring, case and trim. | Required | In progress | In progress | Exact local equality/concat, unary/Length, search and both Substring arities have unit/FP coverage. Safe long origin/mapping/converter/null cases and computed Oracle NULL needles pass. Live collation/character/space/padding/LOB results and NCLOB capability remain open; native semantics/unsupported overloads are explicit. |
| QRY-07 | Date/time members, numeric functions, conditional/coalesce expressions have a supported-overload matrix. | Required | In progress | In progress | Sixteen temporal parts, including Gregorian DayOfYear/DayOfWeek, 92 exact numeric methods and numeric Least/Greatest have local coverage. Round supports five rules and constant digits for Decimal (0..28), Double (0..15) and Single (0..6). Runtime digits/rules, wider temporal/math coverage and native result behavior remain open. |
| QRY-08 | Include/ThenInclude for reference/collection navigations, tracking/no-tracking and identity resolution preserve related entities. | Required | In progress | In progress | ModelMaterializationTests covers composite/alternate-key collection/reference Include/ThenInclude, absent dependents, owned graphs, fixup and three tracking modes through sync/async intercepted commands. Wider graphs and actual server results remain open; split queries stay separate. |
| QRY-09 | Global query filters compose with parameterized predicates and can be disabled using supported EF APIs. | Required | In progress | In progress | ModelMaterializationTests covers EF10 Tenant/Active named filters, selective/all disable and current context/user-predicate parameters in sync/async commands. Inherited unnamed filters, graph composition and live filtering remain open. |
| QRY-10 | FromSqlRaw/FromSqlInterpolated and ExecuteSqlRaw/ExecuteSqlInterpolated bind values; hostile parameter values cannot change SQL shape. | Required | In progress | In progress | Sync/async intercepted tests cover all four APIs, composed queries, named inputs and NULL. MySQL positional command conversion has offline connector coverage; repeated caller-owned native parameters require distinct instances until a clone contract exists. Live execution remains unverified in both modes. |
| QRY-11 | Unsupported predicates fail with actionable translation errors; preserve EF's allowed top-level client projection evaluation. | Required | In progress | In progress | StringQueryTests/StringFunctionQueryTests/FP cover StringComparison/custom/Concat/culture/Invariant/trim-overload predicates and permitted client projections. Wider function boundaries remain open. |

## Tracked writes, concurrency and transactions

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| UPD-01 | Sync/async INSERT binds values, supports explicit keys/default-only inserts and propagates generated keys. | Required | In progress | In progress | FP generated/default-only inserts now retain MySQL positional bind order in sync/async intercepted tests; server identity generation remains unverified. |
| UPD-02 | Generated/default/computed readback uses same connection; MySQL LAST_INSERT_ID/Oracle RETURNING handle null/converted outputs. | Required | In progress | In progress | GeneratedValuesTests pins composite-key single-identity multi-output defaults/computed values, nullable enum concurrency snapshots, explicit defaults, required NULL and later conversion failure, zero/missing rows. Prior scalar/graph coverage remains; multiple MySQL identities reject before DML. Native generation/transaction/live results remain open. |
| UPD-03 | UPDATE/DELETE bind original keys/tokens, including composite keys and nullable concurrency tokens. | Required | In progress | In progress | GraphUpdateTests covers composite original keys, NULL/non-NULL originals and current NULL writes, full DML/value order and no unused native binds in both modes sync/async; live predicates remain unverified. |
| UPD-04 | Stale update/delete throws DbUpdateConcurrencyException with entries; unchanged matched rows succeed in MySQL. | Required | In progress | In progress | FP zero-row composite update/delete retains failing entry/state; UO rejects affected-row option conflicts. Live matched-row/conflict tests remain open. |
| UPD-05 | Multi-entity saves respect dependency order and propagate parent/child keys; unsupported readback shapes reject before DML. | Required | In progress | In progress | GraphUpdateTests covers three-level insert ordering, generated key/FK propagation, fixup and reverse cascade deletes; BasicRelationalTests rejects MySQL composite keys with two generated identities before DML in sync/async. Wider/unsupported graph shapes, transactional atomicity and live writes remain open. |
| TXN-01 | Begin/commit/rollback and existing transaction enlistment work sync/async; incompatible connections/transactions reject. | Required | In progress | In progress | TransactionTests covers isolation, ownership, commit/rollback, tokens and incompatible connections; registered start failures dispose untracked handles after callback/factory errors and restore leases. Native cleanup failure may leave unusable state; live execution remains open. |
| TXN-02 | SaveChanges is atomic across commands/readback; later failure rolls back earlier writes; cancellation leaves no unintended partial commit. | Required | In progress | In progress | TransactionTests covers multi-batch attachment/commit, MySQL readback failure, cancellation recovery without cancelled cleanup tokens, failed-open outer leases and original-error preservation under cleanup/diagnostic failure. Native rollback/disposal and live atomicity remain open. |
| TXN-03 | Savepoints and SaveChanges inside an existing transaction preserve caller transaction after recoverable failure. | Required | In progress | In progress | TransactionTests covers quoted savepoint SQL, automatic rollback/retry/cancellation, disabled-savepoint policy, caller ownership and rollback/release/diagnostic failures. Oracle release is logical only. Native/server state and recovery remain live gates. |

## Database creation and schema profile

The acceptance target is an isolated test-owned database/schema with ordinary tables, scalar columns,
primary/foreign keys, non-unique/unique indexes and supported identity/default definitions. Offline DDL and
EnableOceanBaseSchemaManagement's existing-schema table-creation profile are locally implemented. Provision the
database/schema externally and declare its exact case-sensitive name; EnsureCreated skips any populated schema.
DDL is not atomic and can leave partial objects; no automatic retry or destructive rollback is attempted.
Namespace creation/deletion and live DDL acceptance remain open. Creating an Oracle tenant is never inferred.

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| SCH-01 | DatabaseFacade services resolve; existence checks distinguish absence from authentication, permission and network failures. | Required | In progress | In progress | Scoped creator/read-only probes/error classification and Batch6a shared failed-open rollback exist. Physical cleanup has deterministic double evidence; live absence/authentication/permission/network and cleanup evidence remain open. Schema DDL is separate. |
| SCH-02 | EnsureCreated/GenerateCreateScript implement schema profile and key generation; existing populated schemas are handled explicitly. | Required | In progress | In progress | Offline scripts and explicitly scoped existing-schema EnsureCreated/CreateTables are implemented; 46 intercepted lifecycle cases plus six option cases cover both modes/sync/async, scope/FK boundaries, existing tables, transactions/callback rejection, cancellation, partial failure and cleanup errors. Namespace creation and native/live DDL remain open. |
| SCH-03 | EnsureDeleted has an explicit ownership/deletion boundary, preserves unrelated tenant objects and fails clearly outside supported profile. | Required | Planned | Planned | Destructive-operation contract missing; tests must use isolated owned fixtures. |

## Packages, compatibility and release evidence

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| PKG-01 | Build net10.0 assets against published OceanBaseConnector 0.2.0 with warnings as errors. | Required | Done | Done | Current solution test command builds assets; not a package-content assertion. |
| PKG-02 | Packed nuspec uses OceanBase.EntityFrameworkCore and EF ranges [10.0.0,11.0.0). | Required | Done | Done | Verify-Package.ps1 passes transaction-recovery-20261111 identity/net10.0/README/ranges; prior negative probes reject as expected. Rerun on every release pack; runtime/live gates are separate. |
| PKG-03 | Isolated packed consumer restores/executes provider API without project/local connector references. | Required | Done | Done | Same transaction-recovery nupkg passes both modes/selected patches, including automatic cancellation recovery with native doubles and all prior schema/transaction smoke; package-only graph and DLL identity inspected; rerun after changes. |
| PKG-04 | Minimum EF10 and selected later available EF10 patch restore/build/test without pinning the EF patch in the provider package; EF11 cannot satisfy dependencies. | Required | Done | Done | Final source suites pass2161 at10.0.0/10.0.14; same baseline-built transaction-recovery nupkg passes both consumers, including prior private-field runtime probes. Ranges rechecked; prior parser rejects11.0.0. Future patches need fresh private-contract evidence. |
| TST-01 | Selected EF relational specification classes pass; exclusions map to checklist IDs and limitations. | Required | Planned | Planned | Specification project/selection absent. |
| TST-02 | Live core query/write/transaction/schema/scalar suites pass in at least one declared supported environment per mode. | Required | Planned | Planned | User will supply access later; User Secrets fixture and authorized schema/cleanup scope required; no live provider execution recorded. |
| REL-01 | Support notes record server/connector/runtime/EF versions, tenant mode, direct/proxy endpoint, transport/options without credentials. | Required | Planned | Planned | Release evidence record missing. |
| REL-02 | Tested efcore/10.0 development checkpoint is reviewed; packaging/release checks run on release revision. | Required | In progress | In progress | Development resumed directly on efcore/10.0 as requested; local numeric aggregate source reviewed and package validated; complete release gates remain open. |

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
