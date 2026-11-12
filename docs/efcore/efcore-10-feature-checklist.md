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

Latest implementation: Batch3ah after `5c42125` on 2026-11-12. Captured `DateTime`, `DateTimeOffset`,
`DateOnly`, `TimeOnly` and `TimeSpan` roots, nullable cells and empty inline roots use the shared VALUES
expansion in both modes where EF10 collection metadata is available. MySQL uses `DATETIME`/`TIME`/`DATE`
casts and Oracle uses `TIMESTAMP`/`DATE` casts; Oracle interval transport remains an explicit boundary. The
source Debug/Release gates pass 3984 tests (1896 unit, 1218 intercepted functional, 868 specification and
2 consumer), zero failures/skips. Native temporal coercion and live execution remain open. The preceding
Boolean checkpoint captured `bool`/`bool?` arrays and lists,
including originally empty inline roots, use the shared VALUES expansion in both modes. Provider-owned
Boolean mappings and non-null-converting Boolean-to-integer/decimal converters retain current values,
duplicates, order, NULL cells, typed zero-row metadata, parameters and readers. MySQL uses SIGNED or the
carrier's unsigned/decimal target; Oracle uses NUMBER with FROM DUAL for each SELECT arm. Focused Boolean
coverage passes 26 unit and 12 intercepted cases; the full source gate passes 3960 tests (1888 unit,
1202 intercepted functional, 868 specification and 2 consumer), zero failures/skips. The
`10.0.0-boolean-roots-20261112` package passes the recorded package gate and both-mode EF10.0.0/10.0.14
consumer checks. These are local SQL-shape, binding, reader and package-consumer results; native Boolean
UNION/result semantics, live OceanBase execution and release acceptance remain open.

Preceding checkpoint: Batch3ae after `b611f0b` on 2026-11-11. Captured string arrays/lists and empty
inline roots use the shared VALUES path with provider text mappings and non-null-converting model-to-string
converters. MySQL uses CHAR casts and Oracle uses the declared text type; Oracle empty-string normalization
occurs after expansion. Final Debug/Release and isolated EF10.0.14 each pass3922=1862+1190+868+2, and the
text-roots package passes both modes/patches and package checks. Native collation, LOB behavior, UNION
coercion and live execution remain open.

Preceding checkpoint: Batch3ad after `cd8d256` on 2026-11-11. String and temporal Min/Max validation
supports provider-owned mappings, nullable forms and non-null-converting converters while rejecting
unsupported carriers, null-converting converters and Oracle CLOB/NCLOB extrema. Final Debug/Release and
isolated EF10.0.14 each pass3910=1858+1182+868+2, with the nonnumeric-extrema package and consumer checks
passing. Native ordering and live execution remain open.

Preceding checkpoint: Batch3ac after f93bc48 on 2026-11-11. Direct contextually converted numeric
element projections now cover SelectMany/FirstOrDefault/Join, empty/nullable roots, forwarded NULL
tests and Distinct/Take. Readers, bindings and encoded CLR defaults retain their owning converters.
Original predicate and scalar-consumer requirements preserve genuine conflicts, including same-store
different converters and shared projection/predicate objects. Final Debug/Release and isolated14 each
pass3764=1764+1130+868+2; native execution, arbitrary converter/operator composition and the overall
inventory remain open.

Preceding checkpoint: Batch3ab after 677b7ef on 2026-11-11. NumericCollectionRootQueryTests and
NumericCollectionRootTests extend general roots to decimal/Single/Double, nullable forms and numeric
converters. Query casts follow provider carriers independently of narrower physical facets. Native
reader hooks, exact bind values, non-finite rejection and typed empty metadata stay intact. Direct
contextually converted element projections remain an explicit EF mapping-inference boundary across
SelectMany/FirstOrDefault/Join; characterization is not implementation of that family. Full validation
and package evidence are recorded in the progress handoff. Remaining text/other carriers, converter
projections and live UNION acceptance keep the Required rows open.

Preceding checkpoint: Batch3aa after a891959 on 2026-11-11. General roots now cover eight integer carriers,
nullable forms/enums and integer converters, including explicitly nullable model/provider mappings.
Standalone parameter roots use a derived EF type-mapping postprocessor; inferred mappings/conflicts stay
authoritative. MySQL casts follow provider signedness; Oracle uses NUMBER. Final default Debug/Release
and isolated EF10.0.14 Release each pass3652=1708+1074+868+2, zero failures/skips. Same integer-roots-r1
package passes both modes/patches/content/package-only graphs/DLL identity. Independent review's nullable
mapping finding is fixed and reverified. Other carriers/converter projections and live UNION gates remain open.

Preceding checkpoint: Batch7f on 2026-11-11. Explicit Oracle user/schema ownership now supports independent
administration, exact catalog probes, minimal grants and CASCADE deletion; EF10 credential SQL logs redact
passwords. Final default Debug/Release and isolated EF10.0.14 Release each pass3590=1670+1050+868+2,
zero failures/skips. The same oracle-user-20261111 package passes both-mode/patch manual consumers,
contents, package-only graphs and baseline DLL identity. Independent review has no unresolved findings.
Oracle runtime syntax/privileges/pools/DDL, remaining local contracts and all live acceptance remain open.

Preceding validation checkpoint: Batch8b on 2026-11-11; development resumed at the user's request. The default
restore audit gate now passes with a test-owned Cryptography.Xml 9.0.23 reference resolving Pkcs 9.0.23;
neither is a provider runtime dependency. Full default Debug/Release each pass3520, zero failures/skips,
without warning overrides. The audit-20261111 package content gate passes. Selected-patch/manual consumer
evidence below remains historical; the full remaining inventory stays open.

Preceding functional checkpoint: Batch7e on 2026-11-11. Explicit owned
management factories support supplied/custom MySQL targets. Final EF10.0.0 Debug/Release and isolated
10.0.14 Release each pass3520=1656 unit+994 functional+868 specification+two consumer tests, zero failures/
skips. Same management-profile-20261111 package passes both modes/patches/content/package-only graphs/
DLL identity. Independent review has no actionable findings; dedicated already-open factory-result and
invalid-result-disposal-failure regressions remain absent. Oracle user/schema lifecycle was not implemented by Batch7e.
This records the historical Batch7e evidence. No live database was executed.

An open row is not automatically waiting for a database. Track the remaining work explicitly:

- **Local implementation or coverage:** remaining collection families/converter projections, wider functions/aggregates,
  wider model/materialization/Include/filter contracts, writes/transactions and schema creation/deletion still
  have locally feasible work. No live connection is needed to continue those implementation and pipeline tests.
- **Live validation:** implemented mappings and SQL still need OceanBase round trips and execution in both
  modes, including server coercion/ranges, collation/padding/LOB behavior, transactions and schema lifecycle.
  Unit tests and intercepted readers do not close these requirements.
- **Other validation:** hosted Windows/Linux evidence applies only to its tested revision; package and
  EF-patch checks have their own recorded boundaries. EF8/EF9 adaptation remains postponed by user choice.

Development resumed at the user's request (2026-11-11) on efcore/10.0. See the
[progress handoff](efcore-10-progress.md) for saved state, remaining work and the exact resume point.

MySQL snapshot checkpoint (Batch9c aftere1ef05e): cycles/dependent joins and derived joined sources
project full original keys and target-encoded values under NO_MERGE, then join the physical target
by every key. Both generators preserve normalized left joins; single-table acyclic ordering remains.
Fourteen new bulk, two transaction and one SQL cases pass in full Debug/Release/isolated14 each
3480=1653+957+868+2, zero failures/skips. Independent review has no actionable findings. Same
bulk-snapshot-20261111 package passes both modes/patches/content/graphs/DLL identity; prior Oracle
package rejects the new cyclic smoke with caught exit1. Native optimizer/read consistency, duplicate
matches, results/costs and wider DEF-03 variants remain open; no driver/server defect demonstrated.

Historical Oracle bulk checkpoint (Batch9b aftereb0c837): joined/paged updates use complete scalar source queries
and EXISTS correlated by every physical primary-key column. Normalized left joins bypass EF's default
UPDATE/FROM fallback.26 new intercepted cases, eight SQL cases and two caller-transaction cases cover
both join sides, nullable Boolean storage, selective filters, composite/converted keys, paging, aliases,
errors and cancellation. Final Debug/Release/isolated14 each3463=1652+941+868+2 pass, zero failures/skips.
Independent review and coverage follow-up have no outstanding findings. Native syntax/results,
cardinality/read consistency/costs and the remaining DEF-03 families are still open.
Same bulk-oracle-20261111 package passes both modes/patches, content, package-only graphs and DLL
identity; prior bulk-dml package rejects the new smoke with caught exit1. Baseline staysEF10.0.0.

Historical bulk DML checkpoint (Batch9a after5f56125): ordinary both-mode sync/async ExecuteUpdate/ExecuteDelete,
MySQL native joined UPDATE syntax, single-table assignment ordering and direct setter write validation
have local coverage. Computed/cross-encoded Boolean setters emit target encodings and preserve NULL;
arbitrary incompatible converter expressions, cyclic/dependent-joined MySQL assignments and Oracle
joined updates reject before opening.48 bulk+four transaction cases pass. Full Debug/Release/isolated14
each3427=1644+913+868+2 pass, zero failures/skips. Same bulk-dml-20261111 package passes both-mode/patch
consumers/content/graphs/DLL identity. Independent review found/fixed wrong Boolean encoding; no
outstanding findings. Native execution and remaining DEF-03 rewrites stay open; no driver/server defect.

EnsureCreated seeding checkpoint (Batch7d afterc1dc62d): matching sync/async callbacks run inside an
owned target transaction after successful schema processing and an in-transaction scope check,
including populated/empty-model false results.46 focused cases cover actual SaveChanges through
stateful doubles, ordering/identity/tokens, mismatch, scope changes after DDL, failures and cleanup.
Full Debug/Release/isolated14 each3375=1644+861+868+2 pass without failures/skips. Same seeding-20261111
package passes both-mode/patch consumers, content, package-only graphs and DLL identity. Independent
review has no outstanding findings. Physical transactions/DDL, namespace seed success and migration
locking/retries remain open. No live execution or connector/server defect was established.

Latest local write checkpoint: Batch5a after6714626 fixes unused NULL original predicate binds while
retaining NULL writes.24 focused graph/composite-concurrency/error/cancellation cases and full Debug,
Release/isolated14 each1986=1474+510+2 pass, no failures/skips. Same graph-update-20261111 nupkg passes
content and both-mode/patch consumers; prior lifecycle package fails the new NULL-token smoke. Package,
Release and consumer DLL identities match. Independent review found and closed an async token-count test
gap; no production findings remain. Transactions/live remain open.

Official specification checkpoint (Batch8a afterd264667): a separate solution project runs complete
inherited relational one-to-many, many-to-one, one-to-one, many-to-many, inheritance and owned model
families against real OceanBase services in both modes.868 cases (866 inherited+two adapter guards)
pass without overrides/exclusions/skips. Full Debug/Release/isolated14 each3329=1644+815+868+2 pass;
matching official package graphs verified and baseline staysEF10.0.0. TST-01 remains In progress for
other families and query/write/runtime specifications. Metadata conformance is not live CRUD/DDL proof.

MySQL namespace local checkpoint (Batch7c after89dd02e): explicit entire-database ownership is separate
from existing-schema table permission. Create/Delete/EnsureCreated/EnsureDeleted use an independent
database-free management connection, bound catalog probes, quoted DDL and target-only pool clearing.
Catalog NULL does not prove absence; only known-outcome DROP error1008 makes EnsureDeleted return false.
Six new option cases and59 intercepted lifecycle cases cover ownership, runtime configuration divergence,
custom native hooks, transactions, validation-before-DDL, sync/async cancellation and error/cleanup paths.
Full Debug/Release/isolated14 each2461=1644+815+2 pass, no failures/skips; source review closed wrong-server
management risk. Batch7e adds an explicit owned management factory for supplied/custom MySQL targets;
Oracle namespace provisioning remains local follow-up work;
native privileges, SQL and pool/session effects remain live gates. Overall SCH rows remain open.
The same database-management-r2 package passes both modes on EF10.0.0/10.0.14, content, package-only
graphs and DLL identity. Previous-package rejection returns handled exit1 without a process crash.

Boolean ordering/text local checkpoint (Batch3z after7235f38):16 unit/16 intercepted cases add exact
CompareTo and ToString identities with independent normalization, signed CASE results and distinct
typed.Value versus boxed-object NULL behavior. Covers inverted/unsigned/nullable mappings, current
captures/global defaults and supplied native int/string/NULL reads. Full Debug/Release/isolated14
each2396=1638+756+2 pass, zero failures/skips; same package passes both modes/patches/content/graphs/
DLL identity. Independent review closed unsigned subtraction and boxed-null issues. QRY-01/QRY-11
stay In progress; parsing/conversion/wider APIs and native evaluation remain open. PackageConsumer
managed failures now return1 with stderr; this manual offline executable is not run by CI.

Boolean methods local checkpoint (Batch3y after6aa90d9):18 unit/16 intercepted cases add exact
framework typed/boxed/nullable Equals, both nullable Boolean defaults and storage HasValue tests.
Fix inherited converter compensation erasing HasValue predicates; preserve Value converters and
custom-method inherited behavior. Global defaults, both null settings and captured values are covered.
Full Debug/Release and isolated14 each2364=1622+740+2 pass, zero failures/skips. Same package passes
both modes/patches/content/graphs/identity; evidence is in the progress handoff. QRY-01/QRY-11 stay
In progress; wider functions, custom-method body semantics and native execution remain open.

Converted Boolean local checkpoint (Batch3x after4ce40a8): BooleanQueryTests adds24 cases and
BasicRelationalTests adds18 cases. Both modes cover mixed/inverted numeric/text ==/!=, NOT,
AND/OR, lifted &/|, predicate/order roots, ternary arms and coalesce with native results. Ordinary
numeric1/0 mappings keep simple SQL. Global Y/N defaults preserve native predicate results, while
stored property/constant/parameter mappings remain converter-owned. Decimal canonical classification
is exact. Full Debug/Release and isolated14 each2330=1604+724+2 pass, zero failures/skips. Independent
review found and closed decimal narrowing and global-predicate normalization issues. Physical results,
collation/coercion, volatile single evaluation, wider Boolean APIs and broader inventory remain open.
QRY-01 stays In progress; package evidence is in the progress handoff.

Boolean XOR local checkpoint (Batch3w after e47892c): BooleanXorQueryTests adds16 cases and
BasicRelationalTests adds12 intercepted sync/async cases. Both modes normalize each operand's mapped
true/false values, preserve nullable XOR through EF null processing and use native Boolean result
mapping even with global default converters. Null/equal/unsupported and Oracle empty encodings reject.
Full Debug/Release and isolated14 each pass2288=1580+706+2, zero failures/skips. Independent review
found and closed a global-converter result bug. Native results/collation/coercion, volatile single
evaluation and broader independently converted Boolean comparisons remain open; QRY-01 stays In progress.
Package evidence is recorded in the progress handoff.

Integer shift local checkpoint (Batch3v after e52a4d3): ShiftQueryTests adds35 cases and
BasicRelationalTests adds8 intercepted sync/async cases. Both modes cover `<<`/`>>`, C# small-integer
promotion, negative/oversized count masking, signed right shift, signed32 wrap and full64-bit results,
independent Int32 count binding, nullable/current captures, nested operations and carrier/marker rejection.
The C# expression-tree compiler rejects `>>>` with CS7053 before EF; this is not a driver/server defect.
Native numeric precision/coercion/results and volatile single evaluation remain unverified; QRY-01
stays In progress. Source/test review approves. Current full/package evidence is in the progress handoff.

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

Database addresses were already supplied by the user. Follow the
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
| MOD-01 | Relational conventions initialize tables, primary/composite/alternate keys, foreign keys and indexes. | Required | In progress | In progress | ModelMaterializationTests covers ordinary metadata. Batch8a adds866 inherited official model tests across six families and both modes, without weakened assertions/exclusions. Complete model/write/schema and native acceptance remain open. |
| MOD-02 | Required/optional relationships, owned references/collections and shadow properties preserve model and CRUD behavior. | Required | In progress | In progress | ModelMaterializationTests covers intercepted graph reads/fixup/shadow values under all tracking modes. Batch8a official relationship/owned model families pass both modes. Wider graph CRUD and native behavior remain open. |
| MOD-03 | Generated/default/computed metadata, explicit names and schema settings produce supported SQL or validation errors. | Required | In progress | In progress | Offline DDL validates supported identity/default/computed metadata and rejects unsupported shapes. GeneratedValuesTests pins multi-column default/computed runtime propagation through interception. Wider generation/inheritance and live schema validation remain open. |
| MAP-01 | Boolean and signed/unsigned integers bind/round-trip in documented ranges; predicates use valid Boolean SQL. | Required | In progress | In progress | US/UI all-eight nullable endpoints, decimal/finite-floating fraction rejection, numeric property facets and Boolean false/null binding pass; FP exact key/enum outputs and scalar Boolean cells pass. Full Boolean operators/null SQL and all live evidence remain open. |
| MAP-02 | Decimal precision/scale and float/double round-trip; default decimal storage preserves documented fractional precision. | Required | In progress | In progress | UT/UD decimal declarations/exact write bounds/query transport/clones and alias independence pass; UF/FP native floating and direct wide OceanBaseDecimal normalization/Single midpoint/range/scale/converter paths pass. Batch3ab adds decimal/Single/Double query roots, extrema/scale-28/subnormals, binding and nullable reader coverage. Decimal-backed floating storage/REAL promotion and all live coverage remain open. |
| MAP-03 | Unicode/non-Unicode, fixed/variable and bounded/unbounded text honor length and key/index constraints. | Required | In progress | In progress | UT/UX complete supported declarations, character bounds, Unicode, LOB index rejection and FP converted writes pass; BYTE/charset/index/padding/LOB live behavior missing. |
| MAP-04 | Binary/Guid storage and byte order are explicit and round-trip through parameters, literals and generated values. | Required | In progress | In progress | UB/UG declarations/length/facets/null/composition and FP explicit Guid text/binary query/write/generated outputs pass locally. Batch3ai adds captured/empty binary collection roots; Batch3ak characterizes captured/empty Guid roots with MySQL text and Oracle RAW carriers, parameter binding and readers. Oracle empty-BLOB transport, Guid byte order/coercion, padding, large BLOB semantics and all live round trips remain open. |
| MAP-05 | DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan have documented precision, timezone and range semantics. | Required | In progress | In progress | UTM/UTQ five-type mapping/reader/literal/precision/range, actual facet/property and transport-family/DbType/postfix clone validation pass; FP query/write/generated outputs pass. Batch3aj adds Oracle interval collection parameters/readers and JSON metadata for TimeOnly/TimeSpan. Query input remains lossless; all live calendar/coercion/session evidence remains open. |
| MAP-06 | Explicit store type, size, precision/scale, Unicode/fixed facets and null/DBNull parameter values are honored or rejected clearly. | Required | In progress | In progress | UT/UD/UTM/UB/UI/UG/UX declarations/facets/clones pass, including direct/property text conflicts and Unicode transport. Batch3aa/3ab cover numeric query collection casts and typed empty values independently of column capacity, including DECIMAL(5,2) contextual binds. Batch3ad validates native text/temporal extrema mappings and reader carriers, with Oracle LOB extrema rejected. Batch3ae adds text roots; Batch3af Boolean roots; Batch3ah temporal roots; Batch3ai binary roots; Batch3aj adds Oracle interval casts, parameters/readers and typed empty SQL. Remaining declarations and live cases missing. |
| MAP-07 | EF value converters, including ordinary enum-to-integer/string conversions, work in query/update paths. | Required | In progress | In progress | US/UI/UD/UN/UTM/UB/UG/UF/UX converter hooks and FP enum/Guid/custom text write bounds/query paths pass. Batch3aa/3ab add numeric roots and infinity rejection. Batch3ac preserves contextual projected-element bindings/readers, same-store/offset converters and encoded defaults; Batch3ad preserves non-null-converting text/temporal extrema converters and rejects null-converting or cross-family carriers. Batch3ae preserves text roots; Batch3af Boolean roots; Batch3ah temporal roots; Batch3ai binary roots; Batch3aj preserves model-to-Oracle-interval converters/readers; Batch3ak covers existing Guid carrier conversion through collection roots. Wider converter/operator compositions and live cases remain. |

### Next mapping acceptance cases

These cases refine existing requirements; they do not add features or establish support. The
[technical baseline](efcore-10-technical-baseline.md#numeric-and-guid-reading-contract) records the current
numeric/Guid design. Complete each case in both modes before closing its parent row.

| Parent IDs | Concrete cases still required |
| --- | --- |
| `MAP-01` | All eight integer CLR types at minimum/maximum and nullable values; native MySQL and decimal/OceanBaseDecimal Oracle reader forms; fractional and overflow rejection. |
| `MAP-01`, `QRY-01` | Boolean true/false/null binding and reading; Boolean columns, comparisons, NOT/AND/OR/XOR, CASE and EXISTS in predicates and projections; SQL three-valued null behavior. |
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
| QRY-01 | SELECT/projection, arithmetic/comparison/Boolean predicates and null semantics bind values and materialize nullable/non-null results. | Required | In progress | In progress | UQ/FP cover Boolean/string/scalar handling, converted Boolean XOR/comparison/logical operators/Equals/CompareTo/ToString/nullable defaults/HasValue/CASE/coalesce and native predicate results under global Boolean defaults, integer AND/OR/XOR/complement and left/right shifts, signed/unsigned widths, promotions, nullable/enums and bind refresh; MySQL query parameters use positional `?` ordering at the Connector 0.2.0 boundary. Wider Boolean method/translator families, cast/overflow semantics, wider operators and live results remain open. |
| QRY-02 | OrderBy/ThenBy, Distinct, Skip/Take and offset-only queries return correct results, including deterministic paging/empty pages. | Required | In progress | In progress | UQ SQL shape only; live ordering/string/paging missing. |
| QRY-03 | Inner/left joins, correlated subqueries, Any/All and scalar/collection Contains have verified results or explicit exclusions. | Required | In progress | In progress | URS/UTC/URC/FP cover ordinary joins/subqueries/quantifiers, bare integral/text membership and numeric general roots/nullable/enums. Batch3ac adds contextual SelectMany/FirstOrDefault/Join projections, forwarded NULL tests, encoded defaults and Distinct/Take. Batch3ae adds captured/empty text roots; Batch3af Boolean roots; Batch3ah DateTime-family roots; Batch3ai binary roots; Batch3aj Oracle interval TimeOnly/TimeSpan roots; Batch3ak characterizes Guid roots with casts, readers and typed zero-row metadata. Original requirements preserve genuine root/result conflicts; explicit EF.Parameter roots, other carriers and native execution remain open. |
| QRY-04 | GroupBy aggregates, Count/LongCount, Sum/Average/Min/Max handle empty and nullable inputs correctly. | Required | In progress | In progress | URS/UNA/FP cover Count/LongCount plus native numeric Sum/Average/Min/Max, all exact numeric/nullable selector families, filtered DISTINCT/HAVING/order, independent promoted Double, neutral Single results, converter-owned SUM zero and supplied empty/null/checked reads. Batch3ad adds string and temporal Min/Max, nullable/all-null/empty/group/HAVING/order and converter/reader boundaries in both modes. Native aggregate ordering/results and real execution remain open. |
| QRY-05 | First/Single/default variants enforce cardinality; entity/scalar async materialization matches sync. | Required | In progress | In progress | FP scalar reads and ModelMaterializationTests entity/graph sync/async reads; First/Single/default variants assert limits and empty/one/two reader cardinality. Native limits/results and wider materialization cases remain open. |
| QRY-06 | String translations have a supported-overload matrix: equality, Contains, StartsWith/EndsWith, length, substring, case and trim. | Required | In progress | In progress | Exact local equality/concat, unary/Length, search and both Substring arities have unit/FP coverage. Safe long origin/mapping/converter/null cases and computed Oracle NULL needles pass. Batch3ae adds provider-text roots; Batch3af Boolean roots; Batch3ah temporal roots; Batch3ai binary roots through the shared VALUES path. Live collation/character/space/padding/LOB results and NCLOB capability remain open; native semantics/unsupported overloads are explicit. |
| QRY-07 | Date/time members, numeric functions, conditional/coalesce expressions have a supported-overload matrix. | Required | In progress | In progress | Sixteen temporal parts, including Gregorian DayOfYear/DayOfWeek, 92 exact numeric methods and numeric Least/Greatest have local coverage. Round supports five rules and constant digits for Decimal (0..28), Double (0..15) and Single (0..6). Runtime digits/rules, wider temporal/math coverage and native result behavior remain open. |
| QRY-08 | Include/ThenInclude for reference/collection navigations, tracking/no-tracking and identity resolution preserve related entities. | Required | In progress | In progress | ModelMaterializationTests covers composite/alternate-key collection/reference Include/ThenInclude, absent dependents, owned graphs, fixup and three tracking modes through sync/async intercepted commands. Wider graphs and actual server results remain open; split queries stay separate. |
| QRY-09 | Global query filters compose with parameterized predicates and can be disabled using supported EF APIs. | Required | In progress | In progress | ModelMaterializationTests covers EF10 Tenant/Active named filters, selective/all disable and current context/user-predicate parameters in sync/async commands. Inherited unnamed filters, graph composition and live filtering remain open. |
| QRY-10 | FromSqlRaw/FromSqlInterpolated and ExecuteSqlRaw/ExecuteSqlInterpolated bind values; hostile parameter values cannot change SQL shape. | Required | In progress | In progress | Sync/async intercepted tests cover all four APIs, composed queries, named inputs and NULL. MySQL positional command conversion has offline connector coverage; repeated caller-owned native parameters require distinct instances until a clone contract exists. Live execution remains unverified in both modes. |
| QRY-11 | Unsupported predicates fail with actionable translation errors; preserve EF's allowed top-level client projection evaluation. | Required | In progress | In progress | StringQueryTests/StringFunctionQueryTests/FP cover StringComparison/custom/Concat/culture/Invariant/trim-overload predicates and permitted client projections. Batch3ae adds text boundaries; Batch3af Boolean boundaries; Batch3ah temporal reader boundaries; Batch3ai binary boundaries including scalar roots, null-converting converters and unknown layouts; Batch3aj closes the Oracle interval collection metadata boundary; Batch3ak covers Guid collection-root translation and empty metadata. Wider function boundaries remain open. |

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
database/schema externally for that profile and declare its exact case-sensitive name. MySQL additionally has
EnableOceanBaseDatabaseManagement's explicitly owned namespace profile. EnsureCreated skips populated schemas.
DDL is not atomic and can leave partial objects; no automatic retry or destructive rollback is attempted.
Explicit management factories support independent credentials/routing/TLS with supplied MySQL targets.
Oracle EnableOceanBaseUserManagement now supplies the explicitly owned user/schema profile, with
independent administration, exact SYS.DBA_USERS probes, minimal grants and redacted credential logs.
Live namespace/DDL acceptance remains open in both modes.
Creating an Oracle tenant is never inferred.

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| SCH-01 | DatabaseFacade services resolve; existence checks distinguish absence from authentication, permission and network failures. | Required | In progress | In progress | Scoped creator/read-only probes/error classification and Batch6a shared failed-open rollback exist. Physical cleanup has deterministic double evidence; live absence/authentication/permission/network and cleanup evidence remain open. Schema DDL is separate. |
| SCH-02 | EnsureCreated/GenerateCreateScript implement schema profile and key generation; existing populated schemas are handled explicitly. | Required | In progress | In progress | Existing-schema tables, owned MySQL databases and Batch7f owned Oracle users are locally implemented. Oracle creation uses a late password factory, redacted command logs and minimal CREATE SESSION/CREATE TABLE grants. Existing users retain credentials/privileges. Shared seeding runs in an owned target transaction with scope rechecks; positive callbacks use stateful doubles. Namespace success through physical seeding, native DDL/privileges and live behavior remain open. |
| SCH-03 | EnsureDeleted has an explicit ownership/deletion boundary, preserves tenant objects outside that boundary and fails clearly outside supported profile. | Required | In progress | In progress | MySQL owns the entire named database; Oracle Batch7f owns the entire quoted user/schema with DROP USER CASCADE. Both require explicit scope and independent management. Oracle uses successful exact SYS.DBA_USERS absence, never MySQL error1008. Local failure/cleanup/cancellation and target preservation pass. The administration factory selects the tenant; equivalence is the application's responsibility. Actual deletion, privileges, concurrent sessions and pool effects need live proof. |

## Packages, compatibility and release evidence

| ID | Requirement and acceptance criteria | Priority | MySQL | Oracle | Existing evidence / gap |
| --- | --- | --- | --- | --- | --- |
| PKG-01 | Build net10.0 assets against published OceanBaseConnector 0.2.0 with warnings as errors. | Required | Done | Done | Current solution test command builds assets; not a package-content assertion. |
| PKG-02 | Packed nuspec uses OceanBase.EntityFrameworkCore and EF ranges [10.0.0,11.0.0). | Required | Done | Done | Verify-Package.ps1 passes `10.0.0-oracle-interval-roots-final-20261112` identity/net10.0/README/ranges and unchanged three runtime dependencies; prior negative probes remain historical evidence. Rerun on every release pack; runtime/live gates are separate. |
| PKG-03 | Isolated packed consumer restores/executes provider API without project/local connector references. | Required | Done | Done | `10.0.0-oracle-interval-roots-final-20261112` passes both modes on EF10.0.0/10.0.14, including Oracle interval collection-root smoke and prior lifecycle/query/mapping checks. Package-only graphs and provider/archive/consumer DLL identity are inspected; rerun after changes. |
| PKG-04 | Minimum EF10 and selected later available EF10 patch restore/build/test without pinning the EF patch in the provider package; EF11 cannot satisfy dependencies. | Required | Done | Done | Source Debug/Release pass 4001 at EF10.0.0; the same Oracle-interval nupkg passes both consumers at EF10.0.0/10.0.14 and ranges remain [10.0.0,11.0.0). Future patches need fresh private-contract evidence. |
| TST-01 | Selected EF relational specification classes pass; exclusions map to checklist IDs and limitations. | Required | In progress | In progress | SpecificationTests now runs full inherited generic relational OneToMany/ManyToOne/OneToOne/ManyToMany/Inheritance/OwnedTypes families against real provider services in both modes:866 inherited+two guard cases pass at EF10.0.0/10.0.14, zero skips/exclusions. Other model families and query/write/runtime specification fixtures remain open. Selected metadata success does not establish deferred runtime features. |
| TST-02 | Live core query/write/transaction/schema/scalar suites pass in at least one declared supported environment per mode. | Required | In progress | In progress | Opt-in `tests/OceanBase.EntityFrameworkCore.LiveTests` loads external User Secrets and builds cleanly. With the supplied credentials, TCP to `172.16.2.45:2881` was reachable; both MySQL and Oracle closed the packet during handshake before authentication or SQL. No SQL/DDL ran. Classify the endpoint/transport failure and rerun before expanding the live matrix. |
| REL-01 | Support notes record server/connector/runtime/EF versions, tenant mode, direct/proxy endpoint, transport/options without credentials. | Required | Planned | Planned | Release evidence record missing. |
| REL-02 | Tested efcore/10.0 development checkpoint is reviewed; packaging/release checks run on release revision. | Required | In progress | In progress | Development continues directly on efcore/10.0; Batch3aj source was reviewed and its package validated. Required live, specification and wider local gates remain open. |

## Visible deferred inventory

These limits remain on the roadmap. They must not disappear from the support matrix simply because they
are difficult to implement. Promoting a deferred feature requires its own design and acceptance evidence.

| ID | Feature | MySQL | Oracle | Promotion criteria |
| --- | --- | --- | --- | --- |
| DEF-01 | Migrations/history table/scripts/idempotent scripts/bundles | Deferred | Deferred | DDL/history/design-time plan; upgrade/downgrade tests. |
| DEF-02 | Reverse engineering/provider design-time services/dotnet ef tooling | Deferred | Deferred | Live schema fixtures; packed tooling consumer tests. |
| DEF-03 | ExecuteUpdate/ExecuteDelete, including EF10 lambda/JSON variants | In progress | In progress | Ordinary bulk operations, Oracle joined/paged scalar/EXISTS rewrites and MySQL cyclic/dependent/derived-source snapshots have local coverage, including physical keys, left joins, nullable Boolean encodings, filters, paging, binding and aliases. Final full3480 pass on both selected patches. Arbitrary cross-converter expressions, wider model/JSON shapes and native optimizer/read consistency/results remain open. |
| DEF-04 | JSON, primitive collections/arrays and complex types, including JSON-owned/complex mappings | Deferred | Deferred | Per-mode mapping/query/update design. |
| DEF-05 | Spatial, XML, native enum types, OBJECT, LOB locators, REF CURSOR | Deferred | Deferred | Connector capability review/live mappings; ordinary enum converters belong to MAP-07. |
| DEF-06 | Stored-procedure modification mappings; provider-specific sequence/HiLo strategies | Deferred | Deferred | Parameter/result/generation contracts; reject unsupported mappings clearly. |
| DEF-07 | TPH/TPT/TPC inheritance, table/entity splitting, many-to-many skip navigations | Deferred | Deferred | Model/query/update specification fixtures. |
| DEF-08 | Split queries, lazy loading, compiled-model/tooling optimizations | Deferred | Deferred | Graph/lifetime/generated-model consumer tests. |
| DEF-09 | EF seeding callbacks, execution-strategy retries, ambient/distributed transactions | In progress | In progress | EnsureCreated callbacks are locally implemented under SCH-02; physical rollback/commit remains unverified. Migrate-time callbacks/locking, execution-strategy retries and ambient/distributed transactions remain unimplemented; no serialization or retry guarantee. |
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
| ExecuteUpdate changes | DEF-03 | Ordinary relational operations and both-mode join/snapshot rewrites have local independent suites; keep native acceptance and wider converter/model/JSON/complex variants open. |
| Seeding/schema lifecycle changes | SCH-02/DEF-09 | Distinguish local EnsureCreated callbacks from native/live behavior and unimplemented Migrate-time seeding/locking. |
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
