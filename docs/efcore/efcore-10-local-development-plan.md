# EF Core 10 Local Development Plan

**Goal:** Implement every locally developable EF10 feature, in reviewed/tested commits, while postponing real
database integration and EF8/EF9 adaptation as explicitly requested on 2026-11-10.

**Architecture:** Continue on efcore/10.0 with HasData schema bootstrap following lazy-loading integration, ordinary compiled models, split queries, retries, TPC,
many-to-many verification, Connector 0.3.0 reverse engineering, splitting, TPT and migrations core/tooling.
Reuse the existing EF relational services, connector 0.3.0
public APIs and per-mode mappings/SQL. Separate local implementation evidence from server acceptance.

**Spec:** efcore-10-feature-checklist.md, efcore-10-technical-baseline.md, efcore-10-query-update-design.md.
**Tech Stack:** C#, .NET 10, EF Core/Relational [10.0.0,11.0.0), xUnit, OceanBaseConnector 0.3.0.

The compact execution board is [efcore-10-roadmap.md](efcore-10-roadmap.md); this file remains the detailed
batch ledger and acceptance notes.

**Updated:** 2026-11-13, HasData schema bootstrap. Development is paused at the user's request after this
module's verification, documentation and local commit. Resume only when requested; keep one complete module
active and review it before committing. EF8/EF9 remains postponed; live execution is opt-in and
currently blocked before authentication by the supplied endpoint/transport behavior.
The full goal remains incomplete.

## Execution order and release gates

Use this section to choose the next task; the feature checklist remains the detailed acceptance authority.
Keep one feature module active at a time: finish its implementation, verification, review fixes, evidence
and local commit before starting another. A status request does not authorize switching modules.

1. **Completed locally: migrations core and tooling.** The core is committed at `a8cbccf`; the
   [tooling execution plan](efcore-10-migrations-tooling-plan.md) records the complete configuration/discovery/
   scaffolding/snapshot/script/removal/bundle workflow, package matrix, review and feature commit.
2. **Complete runtime/specification modules.** Add the next relational query/write specification fixtures,
   then resolve the exposed MOD/QRY graph/materialization and UPD/TXN/CON/DIA write/recovery gaps as complete
   features. QRY/MAP overload work belongs inside those modules, rather than a new sequence of admission-only
   commits. Keep intercepted evidence separate from native result or transaction claims.
3. **Finish schema and specification coverage.** Complete the remaining SCH ownership/DDL/error paths and
   add the next TST-01 relational fixture families with explicit exclusions mapped to checklist IDs.
4. **Resolve the live gate.** Classify the supplied endpoint's pre-auth packet closure, then run the
   non-destructive scalar/query/write/transaction/schema matrix in both modes. Do not run DDL until the
   test-owned cleanup scope is recorded.
5. **Prepare release evidence.** Record REL-01 environment/support details without credentials, rerun the
   full Debug/Release suites, package content gate and package consumer at EF10.0.0/10.0.14, review the
   release revision, and only then consider a release branch/tag. EF8/EF9, push and publication remain out
   of the current development sequence.

### HasData schema bootstrap module and pause (2026-11-13)

**IDs:** SCH-02, DEF-01/09, MAP-07, PKG-03/04. The [design](efcore-10-model-data-design.md) and
[execution record](efcore-10-model-data-plan.md) enable HasData in scripts/CreateTables/EnsureCreated using
EF dependency-ordered seed operations and provider migration SQL. Excluded tables are filtered; DDL runs
before an owned seed transaction, followed by existing separate callbacks. Twenty-two focused cases cover
both modes/sync/async, conversions/owned rows, existing schema skip, cancellation/rollback and leases.
Independent review's combined rollback/disposal failure is fixed with four RED/green cases preserving the
primary error and caller-open connection. Old invalid-plan tests now reject sequences rather than HasData.
Final Debug/Release/isolated patch suites each pass 5028 tests, zero failures/skips; one baseline-built package
passes both-mode runtime/generated-context/compiled-model/full CLI checks on both patches and four Windows
bundle build/help checks. Exact graphs/hashes and artifacts are in the execution record. Native DDL/transactions,
identity behavior and partial-schema recovery remain acceptance boundaries; Connector 0.3.0 needs no change.

Pause after this module's local commit. On resume, choose one remaining coherent module from the inventory
below, finish tests/review/docs/commit, then move on. Wider local coverage and native/release gates remain
unfinished; do not start EF8/9, repeat unchanged live probes, push or publish during this handoff.

### Lazy-loading integration module (2026-11-13)

**IDs:** DEF-08, QRY-08, CFG-03, CON-04/05, PKG-03/04. The [design](efcore-10-lazy-loading-design.md) and
[execution record](efcore-10-lazy-loading-plan.md) verify official proxies/injected ILazyLoader through
existing provider queries/writes. No provider algorithm was missing; Proxies is optional and test/consumer
references align with the selected EF patch. Sixty-four runtime cases cover collection/reference graphs,
tracking/filters/loaded state, lifecycle/recovery and nullable-concurrency writes. Independent review's
injected-reference and empty-target gaps are fixed, with actual predicate-to-value checks in public smokes.
Full baseline Debug/Release/isolated patch suites each pass 5007 tests, zero failures/skips. One baseline-
built package passes content and both-mode public consumers on both patches; graphs/hashes match. No new
CLI/bundle/native run is claimed. Streaming nested loads, proxy/compiled-model combinations, change-tracking
proxies, NativeAOT/precompiled optimization, upstream runtime fixtures and native acceptance remain open.

### Ordinary compiled-model module (2026-11-13)

**IDs:** DEF-08, CFG-03, MAP-01..07, PKG-02..04. The [design](efcore-10-compiled-model-design.md) and
[execution record](efcore-10-compiled-model-plan.md) cover actual optimize/generated C#/UseModel for both
modes, six mapping families, converter/facet/sentinel/write parity and wrong-mode rejection. Independent
review's unhinted-converter physical mapping regression is fixed with RED/green coverage. Thirteen unit
cases and package-only compiled graph/write smokes pass. Full Debug/Release/isolated patch suites each
pass 4943 tests, zero failures/skips. The final r2 baseline-built nupkg passes content, both-mode runtime,
generated-context/optimized-model/full migration CLI consumers on both patches and four bundle build/help
checks; source/archive/consumer hashes and package graphs match. Lazy loading, NativeAOT/precompiled
optimization, native/performance acceptance and upstream runtime fixtures remain open. Connector 0.3.0
requires no change. This closes the ordinary compiled-model local module, not the full EF10 goal.

### Split-query graph loading module (2026-11-13)

**IDs:** DEF-08, QRY-01/05/08/09, CON-04/05, CFG-02/03, PKG-03/04. The
[design](efcore-10-split-query-design.md) and [execution record](efcore-10-split-query-plan.md) add public
splitting options and effective-behavior buffering, reusing EF graph coordinators and existing reader
ownership. Seven unit and 72 runtime cases pass; the initial one-active-reader regression failed all 12
graph cases before implementation. Independent review's SQL ordering/window/correlation verification gap
is fixed. Full Debug/Release/isolated patch suites each pass 4930 tests, zero failures/skips. One
baseline-built nupkg passes content, both-mode runtime/generated-context/full CLI consumers on both
patches and four bundle build/help checks, with matching graphs/DLL hashes. Native
graph/session/isolation acceptance, lazy loading, NativeAOT/precompiled optimization and upstream runtime specifications stay
open; this module does not finish the full goal. Connector 0.3.0 needs no changes.

### Execution retries module (2026-11-13)

**IDs:** DEF-09, CON-04/05, QRY-01/08, UPD-01..05, TXN-01..03, SCH-01/02, DEF-01, PKG-03/04.
The [design](efcore-10-retries-design.md) and [execution record](efcore-10-retries-plan.md) add opt-in EF
operation retries, known-outcome policy, confirmed rollback for owned transactions and early DDL rejection.
Independent review's startup/reader/close replay gaps are fixed with RED/green regressions. Seventy-nine
unit and 68 runtime cases cover options/classification, query buffering, save/bulk replay and error safety.
Debug/Release/isolated patch suites each pass 4851 tests with zero failures/skips. One baseline-built nupkg
passes content and both-mode runtime consumers on EF10.0.0/10.0.14. Connector 0.3.0 requires no change.
Generated-context/full migration CLI and four bundle build/help checks pass for the same package too.
Native outcomes/failover, ambient/distributed support, upstream runtime specifications and the overall goal stay open.

### TPC inheritance module (2026-11-13)

**IDs:** DEF-07, MOD-01/03, QRY-01/05/08/09, UPD-01..05, TXN-02, SCH-01/02, PKG-03/04.
The [design](efcore-10-tpc-design.md) and [execution record](efcore-10-tpc-plan.md) implement concrete tables,
assigned/composite/client Guid keys, inherited schema evolution, UNION/filter/Include/materialization and
tracked concrete CRUD/concurrency/transaction cleanup. The batch factory rejects unsupported model key
strategies before any write, including mixed saves. Eighteen schema and 58 runtime cases pass; independent
review's computed/Never and converted-Guid bypasses are fixed with regressions. The previous package fails
the expanded public consumer specifically at its blanket TPC rejection. Native validation, sequence keys,
upstream runtime specification fixtures and the overall EF10 goal remain open.
Final source Debug/Release and isolated EF10.0.14 each pass 4704 tests, zero failures/skips. Both independent
review findings have regression coverage and the follow-up review finds no remaining blockers.
The same TPC r2 nupkg passes content, both-mode runtime/generated-context and full CLI consumers on both
patches, including compiled snapshots, inherited evolution, scripts/removal and four bundle build/help checks.

### Many-to-many runtime contract module (2026-11-13)

**IDs:** DEF-07, MOD-01..03, QRY-03/08/09, UPD-01..05, TXN-02/03, SCH-01/02, PKG-03/04.
The [design](efcore-10-many-to-many-design.md) and [execution record](efcore-10-many-to-many-plan.md) group
ordinary implicit/explicit join schema evolution, Include/filter/materialization, tracked link mutations,
generated keys, payload/nullable concurrency and transaction failure/retry. Four schema and 62 runtime cases
pass; expanded consumers pass the existing scaffolding r2 binary on both patches/modes. Existing EF/provider
services already implement the tested behavior, so no production algorithm or new package is added. Native
acceptance, upstream runtime specification fixtures, wider variants and the overall goal remain open.
Final Debug/Release and isolated EF10.0.14 each pass 4628, zero failures/skips. Independent review findings
are fixed with exact filter predicates, explicit join conditions, renamed join columns, FK targets/cascades
and supporting index assertions; explicit join Include covers all tracking modes.

### Connector 0.3.0 reverse-engineering module (2026-11-13)

**IDs:** DEF-02, CFG-04, CON-01, MAP-01..07, PKG-01..04. The [design](efcore-10-scaffolding-design.md)
and [execution record](efcore-10-scaffolding-plan.md) cover public GetSchema metadata through compiled
generated DbContexts, context-free mappings, warnings, ownership and package tooling in both modes.
Fifty-eight focused cases and 4562 tests per Debug/Release/selected-patch suite pass. The same nupkg passes
both-patch runtime, generated-context and full CLI/bundle checks. Independent review's Oracle TIMESTAMP/CHAR
dispatch finding is fixed. Driver 0.3.0 meets the agreed profile; unknown behavior warns rather than blocking
all scaffolding. Native catalogs, lossless DDL and the overall EF10 goal remain open.

### Entity/table splitting module (2026-11-13)

**IDs:** DEF-07, MOD-01..03, QRY-01/05/08, UPD-01..05, TXN-02, SCH-01/02. The
[design](efcore-10-splitting-design.md) and [execution record](efcore-10-splitting-plan.md) group physical key
generation, shared-column propagation, schema/model differences, query/materialization, CRUD and toolchain
coverage into one feature. Twelve new schema and 52 runtime cases pass; full Debug/Release/selected-patch
suites each pass 4504. Native behavior, TPC, wider graph/specification modules and the overall goal remain open.

### TPT inheritance module (2026-11-13)

**IDs:** DEF-07, MOD-01/03, QRY-01/05/08/09, UPD-01..05, SCH-01/02. The
[TPT design](efcore-10-tpt-design.md) and [plan](efcore-10-tpt-plan.md) cover one coherent schema/query/write/
tooling workflow. Physical identity generation belongs only to the declaring root table. Final schema/runtime
cases pass 14/40; Debug/Release/isolated EF10.0.14 each pass 4441, and the same TPT nupkg passes both-mode,
both-patch runtime/CLI consumers and four bundle build/help checks. Independent source review finds no blockers.
Native server behavior, TPC/entity splitting and the overall EF10 goal remain open.

### Migrations tooling module (2026-11-13)

**IDs:** DEF-01/02, CFG-04, PKG-03/04. The module preserves existing UseOceanBase APIs, adds migration
assembly/history callbacks and discoverable EF design services, and verifies the complete package-only CLI
authoring workflow. EF10.0.0 Debug/Release and isolated EF10.0.14 Release each pass 4388 tests; the same
`10.0.0-tooling-r2-20261113` package passes ordinary runtime and full CLI consumers in both modes/patches,
including four Windows bundle build/help checks. Independent source review finds no blockers. The
[execution plan](efcore-10-migrations-tooling-plan.md) records RED/GREEN, commands and detailed evidence.
Native migration/bundle/script acceptance and reverse engineering remain open. The overall goal is incomplete.

### Migrations core module (2026-11-13)

**IDs:** DEF-01, DEF-09, SCH-01/02, MOD-03, REL-02. The module owns model differences through native command
generation, history, explicit locking, migration application/reversion and scripts. Its design/plan record
unsupported cases, RED/GREEN evidence, package/patch gates and independent review. Native DDL/script execution
and the wider EF10 goal remain incomplete; local tooling/bundles are covered by the next module above.
No EF8/EF9, push, tag or publication is implied.

### Batch3az brief: explicit temporal parameter roots

**IDs:** QRY-03/11, MAP-05/06/07. **Spec:** Explicit temporal parameter-root contract in
`efcore-10-query-update-design.md`. Continue from Batch3ay; no live database or EF8/EF9 work.

- [x] Add RED/GREEN unit coverage for `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` and `TimeSpan`
  arrays/lists, nullable cells, capture rebinding, empty roots and unsupported collection modes.
- [x] Extend the existing primitive collection admission check with `IsSupportedTemporal` only; keep binary
  and Guid explicit roots outside this slice and preserve the JSON-style parameter rejection boundary.
- [x] Add intercepted sync/async coverage for MySQL/Oracle temporal readers, current parameter values and
  Oracle interval values. Explicit EF parameters use EF's scalar `IN` form; captured roots retain `VALUES`.
- [x] Run aggregate full Debug/Release, isolated EF10.0.14, package-content and both EF10 patch consumers
  on the combined current revision; see the migrations module plan for exact evidence.

Focused coverage passes 15 unit cases and 36 intercepted sync/async cases. Native temporal coercion and live
OceanBase execution remain separate gates.

### Batch3ay brief: explicit Boolean parameter roots

**IDs:** QRY-03/06/11, MAP-06/07. **Spec:** Explicit Boolean parameter-root contract in
`efcore-10-query-update-design.md`. Continue from Batch3ax; no live database or EF8/EF9 work.

- [x] Add RED/GREEN unit coverage for explicitly configured `EF.Parameter(bool[])`, nullable Boolean arrays
  and `List<bool?>`, including rebinding, NULL/empty roots and the unconfigured/single-parameter boundaries.
- [x] Extend the existing primitive collection visitor to accept provider-owned Boolean mappings only when the
  context explicitly selects `Constant` or `MultipleParameters`; keep temporal, binary and Guid explicit roots
  outside this slice.
- [x] Add intercepted sync/async coverage for MySQL Boolean and Oracle decimal carriers, current parameter
  values, nullable readers and a non-null-converting Boolean-to-integer converter.
- [x] Run focused/full Debug/Release, isolated EF10.0.14 Release, package content and both EF10 patch
  consumers. Native Boolean UNION coercion, result semantics and live execution remain separate gates.

Batch3ay implementation adds one provider gate and no public API or dependency. Focused Boolean coverage passes
30 unit cases and 28 intercepted sync/async cases. Debug, Release and isolated EF10.0.14 Release each pass
4,281 source tests (2,061 unit, 1,350 functional, 868 specification and 2 consumer tests), with zero
failures/skips. Package `10.0.0-boolean-parameter-r4-20261113` passes `Verify-Package.ps1` and the standalone
consumer on EF10.0.0 and EF10.0.14 in both modes. Native Boolean UNION/result behavior and live OceanBase
execution remain open.

### Batch3ax brief: explicit text parameter roots

**IDs:** QRY-03/06/11, MAP-06/07. **Spec:** Explicit text parameter-root contract in
`efcore-10-query-update-design.md`. Continue from Batch3aw; no live database or EF8/EF9 work.

- [x] Add RED/GREEN unit coverage for explicitly configured `EF.Parameter` string arrays/lists, NULL and
  empty roots, Oracle empty-string normalization, model-to-string converters and both compatibility modes.
- [x] Extend the existing primitive collection visitor to accept provider-owned text mappings only when the
  context explicitly selects `Constant` or `MultipleParameters`; keep Boolean, temporal, binary and Guid
  explicit roots outside this slice.
- [x] Add intercepted sync/async coverage for current parameter values, nullable readers and converted
  provider values, then add packed-consumer smoke for both EF10 patches and both modes.
- [x] Run full Debug/Release, isolated EF10.0.14 Release, package content and both EF10.0.0/EF10.0.14
  consumers. Native collation, UNION coercion and live results remain separate gates.

Batch3ax evidence: 33 focused text unit cases and 24 intercepted functional cases pass. Debug, Release and
isolated EF10.0.14 Release each pass 4261 source tests (2057 unit, 1334 functional, 868 specification and
2 consumer tests). Package `10.0.0-text-parameter-r5-20261113` passes `Verify-Package.ps1` and standalone
consumers on EF10.0.0/10.0.14 in both modes from a clean EF10.0.0 baseline build. The live MySQL and Oracle
probes reach TCP but close during the pre-authentication handshake, so no SQL result is claimed. No production
dependency or public API was added.

### Batch3aw brief: explicit numeric parameter roots

**IDs:** QRY-03/11, MAP-02/06/07. **Spec:** Explicit numeric parameter-root contract in
efcore-10-query-update-design.md. Continue from Batch3av; no live database or EF8/EF9 work.

- [x] Add RED/GREEN unit coverage for explicit decimal, Single and Double arrays/lists, nullable roots,
  rebinding, duplicate values, NULL cells, empty roots and the unsupported single-parameter mode.
- [x] Extend the existing parameter-root mapping gate from integer to numeric carriers with no new translator,
  dependency or mapping abstraction.
- [x] Add intercepted sync/async coverage for both MySQL/Oracle modes, both relational-null settings and both
  configured collection policies; verify current DbTypes, reader materialization and empty predicates.
- [x] Run full Debug/Release, isolated EF10.0.14 Release, package content and both EF10 patch consumers;
  native numeric `IN` results and live OceanBase execution remain separate gates.

Batch3aw evidence: focused coverage passes 64 unit and 84 intercepted functional cases. Debug, Release and
isolated EF10.0.14 Release each pass 4241 source tests (2053 unit, 1318 functional, 868 specification and
2 consumer tests). Package `10.0.0-numeric-parameter-20261113` passes Verify-Package.ps1 and standalone
consumers on EF10.0.0/10.0.14 in both modes. The explicit EF `Parameter` collection mode and non-finite
floating values remain explicit rejection boundaries; native and live results remain open.

### Batch3av brief: inverse-hyperbolic translations

**IDs:** QRY-07/11, MAP-02/07. **Spec:** Transcendental numeric local contract in
efcore-10-query-update-design.md. Continue from Batch3au; no live database or EF8/EF9 work.

- [x] Add RED/GREEN coverage for Math/MathF Asinh and Atanh across both modes, numeric carriers, converters,
  nullable shaping, Single normalization, decimal readers and unsupported mappings.
- [x] Reuse the existing transcendental translator to emit ASINH and ATANH without a new service or dependency.
- [x] Add intercepted sync/async and packed-consumer dialect smoke, then run full Debug/Release, isolated
  EF10.0.14, package content and both EF10 patch consumers. Native domain/results remain separate gates.

Batch3av evidence: focused coverage passes 169 unit and eight intercepted functional cases. Debug/Release and
isolated EF10.0.14 Release each pass 4201 source tests (2045 unit, 1286 functional, 868 specification and
2 consumer tests). Package 10.0.0-asinh-atanh-20261113 passes Verify-Package.ps1 and standalone consumers
on EF10.0.0/10.0.14 in both modes. Native inverse-hyperbolic results and live execution remain open.

## Batch3as brief: scalar runtime Round parameters

**IDs:** QRY-07/11, MAP-02/07. **Spec:** Exact decimal and binary Round contracts in
`efcore-10-query-update-design.md`. Continue from Batch3ar; no EF8/EF9 work.

- [x] Add RED coverage for captured `digits` and `MidpointRounding` values, omitted defaults, invalid runtime
  values and row-dependent arguments in both compatibility modes.
- [x] Resolve scalar parameters in the EF10 parameter-based SQL processor for each execution, disable stale
  SQL-shape caching, and reuse the existing constant decimal/binary translation without changing native SQL
  formulas or adding a service/dependency.
- [x] Add sync/async intercepted coverage that executes one compiled query with changed digits/rules in both
  modes; run the focused and full Debug gates. Native rounding and live execution remain separate gates.

Batch3as evidence: 124 focused Round unit cases and 14 intercepted functional cases pass. Debug, Release and
isolated EF10.0.14 Release each pass 4127 tests with zero failures/skips. Package
`10.0.0-runtime-round-20261113` passes content verification and standalone consumers on EF10.0.0/10.0.14
in both modes. The live MySQL and Oracle probes still close during the pre-auth handshake before SQL.

Current binary collection-root evidence is the Batch3ai committed slice: 3994 source tests, the
`10.0.0-binary-roots-final-20261112` package, package consumers on EF10.0.0/10.0.14, and local review.
It does not close native binary, live, specification, or other required release gates.

## Batch3ar brief: paging and ordering pipeline evidence

**IDs:** QRY-02. **Spec:** Paging and ordering local contract in efcore-10-query-update-design.md. Continue
from Batch3aq; no EF8/EF9 work.

- [x] Add real EF sync/async intercepted coverage for ordered `Distinct` plus `Skip`/`Take`, empty pages and
  offset-only queries in MySQL and Oracle.
- [x] Assert materialized reader rows, execution count and mode-specific `LIMIT`/`OFFSET`/`FETCH` SQL while
  retaining the existing SQL generator and no new service or dependency.
- [x] Run focused/full Debug/Release, isolated EF10.0.14, package content and both EF10 patch consumers;
  native ordering, collation, paging and server results remain separate gates.

Batch3ar evidence: the focused four-case matrix, full Debug/Release suites and isolated EF10.0.14 Release each
pass 4117 tests (2005 unit, 1246 functional, 868 specification and 2 consumer tests). Package
`10.0.0-paging-20261113` passes content verification and the standalone consumer on EF10.0.0/10.0.14 in both
modes. Oracle parameter suffixes are intentionally matched by shape because EF10.0.14 renames the second
generated parameter; native ordering, collation, paging and server results remain open.

## Batch3ak brief: Guid collection roots

**IDs:** QRY-03/11, MAP-04/07. **Spec:** Guid collection-root contract in
efcore-10-query-update-design.md. Continue from Batch3aj; no live database or EF8/EF9 work.

- [x] Add RED/characterization unit coverage for captured and originally empty `Guid?` roots in both modes,
  asserting the default MySQL text and Oracle RAW carriers and typed zero-row metadata.
- [x] Add sync/async intercepted functional coverage for Guid parameter binding and reader conversion in both
  modes, including NULL cells and the empty transition.
- [x] Run the full Debug/Release, package and consumer gates after the documentation-only contract slice;
  4007 tests pass in each configuration, package content passes, and EF10.0.0/10.0.14 consumers pass.
  Native Guid equality, byte order, CHAR/RAW coercion and live execution remain separate gates.

## Batch3al brief: numeric Clamp translation

**IDs:** QRY-07/11, MAP-02/07. **Spec:** Numeric Clamp local contract in
efcore-10-query-update-design.md. Continue from Batch3ak; no live database or EF8/EF9 work.

- [x] Add RED coverage for all supported `Math.Clamp`/`decimal.Clamp` numeric overloads in filtering queries,
  including both modes and statically reversed constant bounds.
- [x] Add a provider translator that emits searched CASE expressions, retains the input mapping/converter and
  rejects unsupported/null-converting carriers before fallback.
- [x] Add sync/async intercepted integer and decimal tests for captured bound rebinding and result readers.
- [x] Run the full Debug/Release and selected EF10.0.14 suites (4021 each), package content and both EF10.0.0/
  EF10.0.14 consumers; native range/overflow and live CASE results remain separate acceptance gates.

## Global constraints

- Develop and commit on efcore/10.0. Do not push, publish, tag or synchronize other branches implicitly.
- Live tests are explicit opt-in only; keep credentials in User Secrets, run non-destructive probes first and do not perform destructive database tests until the cleanup scope is recorded.
- EF8/EF9 work remains postponed. Local passing evidence never completes a required live gate.
- Preserve every Required feature ID. Track local coverage separately instead of weakening acceptance criteria.
- For each batch: specify behavior, add tests (RED for missing behavior), implement, run full tests, review,
  fix confirmed issues, update progress/checklist and commit. Existing correct behavior gets characterization tests.
- Use independent reviews for each batch. Workers must not spawn additional workers or commit unreviewed code.
- Keep docs portable; no credentials or machine-specific paths. No new dependency for a small helper.

## Work inventory and order

This is an execution ledger, not a replacement support matrix. A checked local task can still have open live gates.
Do not mark the goal complete while any locally developable item below remains unresolved.

| Batch | Feature IDs / owning code | Local completion evidence |
| --- | --- | --- |
| 1. Configuration and SQL boundaries | CFG-01/02/03, SQL-01/02; OptionsTests, ConsumerTests, SqlGenerationHelperTests | Both modes/overloads; null/invalid options; deferred connection configuration; real cache separation/reuse and design-time keys; identifier escaping/schema and both StringBuilder paths. |
| 2. Remaining scalar contracts | MAP-01..07; Storage mappings and focused unit/pipeline tests | Oracle decimal aliases; remaining explicit numeric forms; complete converter/null/clone/input/output boundaries and documented unsupported combinations. Environment-dependent ranges/byte capacity remain separate. |
| 3. Query translator services | QRY-01/03/04/06/07/11; Query translators and SQL generator | Supported string/date/numeric overload tables, concat/arithmetic/null semantics, negative translation tests, collection membership (numeric/Boolean/text/temporal/binary/Oracle interval), correlated subqueries and aggregates. Other collection carriers and wider converter/operator compositions remain open. |
| 4. Model and materialization | MOD-01/02/03, QRY-02/05/08/09; conventions, validator, model/query tests | Composite/alternate keys, relationships/owned/shadow, cardinality, tracking/identity resolution, Include, filters and generation validation through real EF pipelines. |
| 5. Tracked writes and diagnostics | UPD-01..05, DIA-01/02, CON-05; Update batch, command tests | Composite/nullable concurrency, graphs, generated/default/computed output shapes, failures/entries/inner errors, cancellation/timeout and protected parameter logging. |
| 6. Transactions and lifecycle | TXN-01..03, CON-02..05; relational connection/transaction services | Local API/service wiring and deterministic command/transaction doubles for ownership, enlistment, atomic failure behavior and savepoints; server semantics explicitly pending. |
| 7. Database/schema services | SCH-01..03, CFG-04; database creator and DDL services | Define ownership/name/permission boundaries, mode-specific SQL and exception classification, existence/create/delete/schema scripts using deterministic doubles, no real DDL. |
| 8. Specification and package gates | TST-01, PKG-01..04, REL-01/02 | Selected EF relational specification fixtures locally; documented exclusions; repeatable nupkg identity/content/range gate, README, expanded package consumers and selected patch matrix. |
| 9. Deferred inventory audit | DEF-01..10 | Revisit every documented deferred family after Required local implementation stabilizes. Implement locally feasible families under explicit per-mode contracts and tests; record precise architectural/connector/server blockers for any unimplementable family. Do not treat a Deferred label alone as evidence it cannot be developed. |
| 10. Completion audit | All IDs | Compare docs/public API/source/tests/package evidence. Every locally feasible feature is implemented/reviewed/committed; only specifically evidenced database or external blockers may remain. |

## Batch3ad brief: text and temporal aggregate extrema

**IDs:** QRY-04/11, MAP-06/07. **Spec:** text/temporal extrema contract in
efcore-10-query-update-design.md. Continue from cd8d256; no live database or EF8/9 work.

- [x] Add NonnumericAggregateQueryTests for the eleven string/native/nullable temporal types,
  exact Min/Max scalar/selector identities, original mapping/readers/converters, filtered DISTINCT,
  grouping/HAVING/order, unsupported identities and resolved-service negative mappings. Observe RED
  for missing null-converter/carrier boundaries; characterize inherited correct SQL separately.
- [x] In OceanBaseAggregateMethodTranslator, validate only owned nonnumeric Min/Max model families
  before inherited fallback. Keep native string/temporal mappings and supported converters; explicitly
  reject null-converting, unsupported cross-family/custom carriers and Oracle LOBs. Reuse the inherited
  translator's native function construction; no new services, casts, public APIs or dependencies.
- [x] Add dedicated NonnumericAggregateTests with deterministic sync/async command doubles for
  current filters, native string/temporal readers, converters, all-null/empty/group shapes and failures
  before commands. Extend the existing package consumer with both-mode text/temporal aggregate smoke.
- [x] Run focused/full Debug/Release, a fresh EF10.0.14 snapshot and one baseline-built package at
  both patches/modes, contents/dependency/graph/hash gates and negative old-package smoke where useful.
  Independently review, fix confirmed findings with regressions, update evidence and commit locally.

Review focus: no duplicated inherited aggregate logic, exact identities/plugin priority, provider
ordering rather than CLR comparer claims, no Oracle LOB truncation, full temporal reader/converter
ownership, empty/all-null cardinality and no dependency on live credentials. The full goal stays active.

## Batch3am brief: DateTime.Date translation

**IDs:** QRY-07/11, MAP-05/07. **Spec:** DateTime.Date contract in
efcore-10-query-update-design.md. Continue from Batch3al; no live database or EF8/EF9 work.

- [x] Add RED unit coverage for MySQL `DATE(input)` and Oracle `TRUNC(input)`, date-valued result mappings,
  nullable composition, value-preserving DateTime converters and unsupported carriers.
- [x] Implement the existing temporal member translator path without adding a service or dependency.
- [x] Add four intercepted sync/async functional cases for parameter binding, SQL shape and DateTime readers.
- [x] Run full Debug/Release (4029 each), package content and both-mode EF10.0.0/EF10.0.14 consumers. The
  isolated EF10.0.14 source-suite rerun is blocked by NuGet repository-signature TLS restore failure; native date
  semantics and live execution remain separate gates.

## Batch3an brief: Log2 translation

**IDs:** QRY-07/11, MAP-02/07. **Spec:** Transcendental numeric local contract in
efcore-10-query-update-design.md. Continue from Batch3am; no live database or EF8/EF9 work.

- [x] Extend the existing Math/MathF transcendental contract with `Log2` unit coverage for both modes,
  numeric carriers, converters, nullable shaping and unsupported boundaries.
- [x] Translate MySQL `LOG2(input)` and Oracle `LOG(2, input)` through the existing provider-carrier and
  Single-normalization path; do not add a new service or dependency.
- [x] Add intercepted sync/async predicate coverage and packed-consumer dialect smoke, then run the full
  Debug/Release, package and selected EF10 patch gates. Native logarithm domain/results remain open.

Batch3an evidence: focused transcendental coverage passes 73 unit and 8 intercepted functional cases. Full
Debug/Release each pass 4037 tests (1925 unit, 1242 functional, 868 specification and 2 consumers), with zero
failures/skips. Package `10.0.0-log2-20261112` passes the content gate and standalone consumers in both modes on
EF10.0.0/10.0.14. These are local translation, binding, shaping and package-consumption checks; native Log2
domain/results and live execution remain open.

## Batch3ao brief: trigonometric translations

**IDs:** QRY-07/11, MAP-02/07. **Spec:** Transcendental numeric local contract in
efcore-10-query-update-design.md. Continue from Batch3an; no live database or EF8/EF9 work.

- [x] Add RED/GREEN coverage for `Math`/`MathF` Sin, Cos and Tan across both modes, numeric carriers,
  converters, nullable shaping and unsupported boundaries through the existing transcendental suite.
- [x] Translate the six exact overloads through the existing native-function and Single-normalization path;
  do not add a new service or dependency.
- [x] Add intercepted predicate coverage and packed-consumer smoke, then run full Debug/Release, package
  content and both EF10 patch consumers. Native trigonometric results remain open.

Batch3ao evidence: focused transcendental coverage passes 97 unit and 8 intercepted functional cases. Full
Debug/Release each pass 4061 tests (1949 unit, 1242 functional, 868 specification and 2 consumers), zero
failures/skips. Package `10.0.0-trig-20261112` passes content and both-mode EF10.0.0/10.0.14 consumers. These
checks establish local translation, binding, shaping and package consumption only; native results and live
execution remain open.

## Batch3aq brief: hyperbolic translations

**IDs:** QRY-07/11, MAP-02/07. **Spec:** Hyperbolic numeric local contract in
efcore-10-query-update-design.md. Continue from Batch3ap; no EF8/EF9 work.

- [x] Add RED/GREEN coverage for `Math`/`MathF` Sinh, Cosh and Tanh across both modes, numeric carriers,
  converters, nullable shaping and unsupported boundaries through the existing transcendental suite.
- [x] Translate the six exact overloads through the existing native-function and Single-normalization path;
  preserve the existing decimal reader and do not add a new service or dependency.
- [x] Add intercepted sync/async predicate coverage and packed-consumer dialect smoke, then run full
  Debug/Release, isolated EF10.0.14, package content and both EF10 patch consumers. Native results,
  overflow/precision behavior and live execution remain open.

Batch3aq evidence: focused transcendental coverage passes 153 unit and 8 intercepted functional cases.
Debug, Release and isolated EF10.0.14 Release each pass 4117 tests (2005 unit, 1242 functional, 868
specification and 2 consumers), zero failures/skips. Package `10.0.0-hyperbolic-20261113` passes content
verification and both-mode consumers on EF10.0.0/10.0.14. The provider remains unverified against native
OceanBase because both live probes close during pre-auth handshake.

## Batch3ap brief: inverse-trigonometric translations

**IDs:** QRY-07/11, MAP-02/07. **Spec:** Transcendental numeric local contract in
efcore-10-query-update-design.md. Continue from Batch3ao; no EF8/EF9 work.

- [x] Add RED/GREEN coverage for `Math`/`MathF` Asin, Acos, Atan and Atan2 across both modes, numeric
  carriers, converters, nullable shaping and unsupported boundaries through the existing transcendental suite.
- [x] Translate the eight exact overloads through the existing native-function and Single-normalization path;
  preserve `Atan2(y, x)` argument order and do not add a new service or dependency.
- [x] Add intercepted predicate coverage, null/composition coverage and packed-consumer dialect smoke, then
  run full Debug/Release, isolated EF10.0.14, package content and both EF10 patch consumers. Native inverse-
  trigonometric results remain open.

Batch3ap evidence: focused transcendental coverage passes 129 unit and 8 intercepted functional cases. Debug,
Release and isolated EF10.0.14 Release each pass 4093 tests (1981 unit, 1242 functional, 868 specification and
2 consumers), zero failures/skips. Package `10.0.0-inverse-trig-final-20261112` passes content verification
and both-mode consumers on EF10.0.0/10.0.14; provider and package hashes are recorded in the progress handoff.
The provider remains unverified against native OceanBase because both live probes close during pre-auth handshake.

Batch3ad evidence: the new nonnumeric aggregate unit suite passes 94 cases (373 with the existing numeric
aggregate focus) and the dedicated intercepted functional suite passes 52. EF10.0.0 Debug/Release and isolated EF10.0.14 Release
each pass 3910 tests (1858 unit + 1182 functional + 868 specification + 2 consumers), zero failures/skips.
Package `10.0.0-nonnumeric-extrema-20261111` passes both-mode consumer, content, dependency and identity
checks; independent source review found no actionable issue. Native server extrema semantics and live
database execution remain open.

## Batch3ae brief: general text collection roots

**IDs:** QRY-03/06/11, MAP-06/07. **Spec:** general text collection-root contract in
efcore-10-query-update-design.md. Continue from b611f0b; no live database or EF8/9 work.

- [x] Add RED coverage for captured string arrays/lists and empty inline string roots in both modes;
  preserve the existing scalar text membership and explicit `EF.Parameter(collection)` boundaries.
- [x] Reuse the existing VALUES expansion and SQL renderer for provider-owned text mappings,
  non-null-converting model-to-string converters and Oracle empty-string normalization. Keep numeric
  behavior unchanged; reject null-converting, binary/other carriers and unknown layouts before commands.
- [x] Add sync/async intercepted pipeline coverage for current values, duplicates/NULL/empty rows,
  converter-owned parameters/readers and typed zero-row SQL. Extend the packed consumer with both-mode
  text-root smoke.
- [x] Run full Debug/Release, a fresh EF10.0.14 snapshot and one baseline-built package at both patches/
  modes, then complete independent code review, update evidence and commit locally. Final evidence is the
  3922-test gate, the `10.0.0-text-roots-20261111` package, both patch consumers and the checkpoint above.

Native text collation, LOB behavior, UNION coercion and live server results remain outside this local batch.

## Batch3af brief: Boolean collection roots

**IDs:** QRY-03/06/11, MAP-06/07. **Spec:** Boolean collection-root contract in
efcore-10-query-update-design.md. Continue from `776a9a6`; no live database or EF8/9 work.

- [x] Add RED/GREEN unit coverage for captured and inline `bool`/`bool?` arrays, lists, empty roots,
  converter mappings, Constant/MultipleParameters policies and both compatibility modes.
- [x] Reuse the general VALUES expansion and retain provider Boolean mappings/readers. Generate MySQL
  SIGNED/unsigned/decimal carrier casts and Oracle NUMBER casts with per-arm DUAL; keep null-converting,
  non-Boolean, unsupported carrier/layout and explicit `EF.Parameter(collection)` boundaries.
- [x] Add synchronous/asynchronous intercepted binding and reader coverage plus both-mode package-consumer
  smoke. Run baseline Debug/Release and isolated EF10.0.14 Release gates, then update the checklist and
  progress checkpoint before committing locally.

Final evidence is the 3960-test source gate (1888 unit, 1202 intercepted functional, 868 specification and
2 consumer tests), the Boolean focused 26/12 tests, and package consumers on EF10.0.0/10.0.14. Native
Boolean UNION/result semantics and live OceanBase execution remain open.

## Batch3ai brief: binary collection roots

**IDs:** QRY-03/11, MAP-06/07. **Spec:** binary collection-root contract in
efcore-10-query-update-design.md. Continue from the Batch3ah temporal-root checkpoint; no live database or
EF8/EF9 work.

- [x] Add RED/GREEN unit coverage for captured and inline `byte[][]`/`byte[]?[]` roots, originally empty
  roots, provider binary mappings, non-null-converting converters and both compatibility modes.
- [x] Reuse the general VALUES expansion and retain binary parameters/readers. Generate MySQL `BINARY` and
  Oracle mapped `RAW(n)`/`BLOB` empty-root casts; preserve NULL cells and typed zero-row metadata.
  Keep scalar `byte[]`, `EF.Parameter(collection)`, null-converting converters, unknown layouts and
  Oracle non-null empty binary transport as explicit boundaries.
- [x] Add sync/async intercepted bindings/readers and both-mode package-consumer binary smoke. Run full
  Debug/Release, package contents and EF10.0.0/10.0.14 consumers, then review the module and update the
  checklist/progress before committing.

Final evidence is the 3994-test source gate (1902 unit, 1222 intercepted functional, 868 specification and
2 consumer tests), focused binary 6/4 cases, package
`10.0.0-binary-roots-final-20261112`, and both patch consumers. Native binary equality, LOB coercion,
UNION behavior, Oracle empty-BLOB transport and live OceanBase execution remain open.

## Batch3aj brief: Oracle interval collection roots

**IDs:** QRY-03/11, MAP-05/06/07. **Spec:** temporal collection-root contract in
efcore-10-query-update-design.md. Continue from Batch3ai; no live database or EF8/EF9 work.

- [x] Add RED/GREEN SQL-shape tests for Oracle `TimeOnly` and `TimeSpan` captured/NULL roots and an
  interval mapping JSON round-trip. Keep MySQL temporal roots as controls.
- [x] Add EF10 JSON metadata for `OceanBaseIntervalDayToSecond`, letting the existing model-to-interval
  converters compose without changing parameter or reader conversion code.
- [x] Add sync/async intercepted Oracle reader/bind coverage, including `DbType.Object` and
  `OceanBaseDbType.IntervalDayToSecond`. Native interval coercion and live results remain separate gates.

Focused evidence adds 3 unit and 4 functional cases. Debug/Release each pass 4001 source tests; package
`10.0.0-oracle-interval-roots-final-20261112` passes the content gate and both-mode consumers on EF10.0.0/10.0.14.
Independent review is complete; native interval coercion and live results remain open.

## Batch3ac brief: contextual numeric collection projections

**IDs:** QRY-03/11, MAP-06/07. **Spec:** contextual numeric projection contract in
efcore-10-query-update-design.md. Continue from f93bc48; no live execution or EF8/9.

- [x] Replace the numeric projection exclusion characterization with positive captured/inline/empty
  SelectMany/FirstOrDefault/Join tests. Add integer-offset, cross-numeric nullable and same-store
  converters, subquery forwarding, native defaults and genuine incompatible-context tests. Observe RED.
- [x] Add a focused normalization pass before EF's type-mapping scanner in the existing query
  postprocessor. Track originally untyped numeric VALUES and direct column forwarding; remove only
  premature default projection mappings, preserve contextual mapping identity and reject genuine conflicts.
  Remap to the owner rather than leave untyped projected columns; early Distinct supplies its comparer.
- [x] Add intercepted sync/async projected collection reader/bind tests and package consumer smoke.
  Cover duplicates/NULL/empty/rebinding and distinct converters over the same capture. Retain explicit
  casts, default mapping and unsupported carrier boundaries; do not alter driver/read mappings.
- [x] Full Debug/Release, isolated EF10.0.14, one baseline package at both patches/modes and content/
  graph/hash checks. Independent review, repair findings with regressions, update evidence and commit.

Review found two reproduced defects: shared column objects could erase real predicate mappings, and
scalar-result comparisons could mix converted provider values with native literals/properties. Preserve
original occurrence requirements before remapping and reject incompatible scalar consumers. Dedicated
shared/distinct-object and nullable/nonnullable scalar regressions cover both modes. Direct integer
FirstOrDefault also maps its COALESCE default literal/result; nullable forwarding NULL tests are carrier
agnostic. Intercepted decimal tests retain GetDecimal; OceanBaseDecimal normalization belongs to the
existing integer/floating reader paths. Scalar NULL comparisons have separate RED evidence and preserve
inner operator requirements. Final source re-review reproduces all repaired cases with no open findings.
Full Debug/Release and fresh isolated14 each pass3764=1764+1130+868+2, zero failures/skips. The same
converted-roots package passes both modes/patches/content/graphs/baseline DLL identity; preceding numeric
package fails the new smoke with caught exit1. Evidence/hashes are recorded in the progress handoff.
Final evidence re-review confirms counts, hashes, package graphs and corrected current handoff wording.
The local module is ready for commit; the overall EF10 goal stays active.

## Batch3ab brief: decimal and floating collection roots

**IDs:** QRY-03/11, MAP-06/07. **Spec:** numeric collection-root contract in
efcore-10-query-update-design.md. Continue from 677b7ef in this checkout, no live execution.
Use the writing-plans/executing-plans and TDD workflows for this existing work inventory.

- [x] Add failing NumericCollectionRootQueryTests and intercepted NumericCollectionRootTests for
  decimal/Single/Double, nullable and numeric converters, both modes/null settings/policies, exact
  empty/nonempty rows and inline ordinals. Include extrema, scale-28, subnormals and rebinding.
- [x] Extend the shared carrier guard in OceanBaseCollectionParameterExpandingVisitor, reuse it in
  OceanBaseQueryTranslationPostprocessorFactory, and expand GenerateCollectionCast in the SQL generator.
  Keep inferred conflicts authoritative, mapping/converter identity and reader hooks intact. Update
  older rejection tests only for numeric families newly supported by this contract.
- [x] Verify sync/async native bindings and result conversion, narrow physical facets, invalid inputs
  and null-converting/nonnumeric rejection before commands. Add package-consumer numeric root smoke.
- [x] Run full Debug/Release, isolated EF10.0.14, one baseline-built package on both patches/modes,
  archive/dependency/graph/hash gates. Independently review, fix findings with regressions, update
  progress/checklist/README and commit locally. Overall EF10 and live acceptance remain incomplete.

Verified: all three source configurations pass3728=1748+1110+868+2 with0fail/skip. The same final
numeric-roots-r1 package passes both modes/patches, contents/dependency ranges, package-only graphs
and DLL identity; the preceding integer package fails the new smoke with caught exit1. Review's
broader contextual-projection exclusion and stale package-evidence notes are corrected, with no
production findings. Ruling: preserve EF's authoritative conflict checks and record directly projected
contextually converted elements as the next implementation family, rather than bypassing the checks.

## Batch3aa brief: complete integer collection-root carriers

**Feature IDs:** QRY-03/11, MAP-06/07. Continue after the reviewed Oracle lifecycle commit a891959.
Expand the existing general VALUES-root pipeline to all eight signed/unsigned CLR integer types,
nullable forms and integer-backed enums. Retain one snapshot per capture, exact duplicate/order
preservation, current-value binding, mapping/converter identity and the original empty-inline type.
Use MySQL SIGNED/UNSIGNED according to the actual provider carrier; Oracle NUMBER is the lossless
integer query cast. Never use a property's narrower physical declaration as the query cast.

Ruling: widen the existing integral path rather than introduce a second collection translator. This
preserves the already tested parameter/null-processing and zero-row metadata seams. Integer-to-integer
converters participate; text/decimal/floating carriers and null-converting converters require separate
contracts. Direct converted Join projections can still encounter EF's earlier mapping-inference conflict.

Ruling: standalone projected parameter roots also need a derived type-mapping postprocessor. New
native root tests reproduce EF10's UnreachableException when no comparison supplies inference.
Prefer contextual inference, then existing element mapping/default original CLR mapping; keep the
base conflict check. This fixes a demonstrated required query path rather than weakening the tests.

1. Add failing both-mode SQL cases for arrays/lists, native/nullable extrema, duplicate/null/empty
   transitions, originally empty/nonempty inline roots, joins and ordinal access under both null settings
   and Constant/MultipleParameters/default modes. Characterize explicit single-parameter rejection.
2. Widen the shared validation and carrier casts, retaining early rejection and immutable metadata.
3. Add sync/async intercepted commands for exact parameter types/values, native reader conversion,
   unsigned boundaries, converter ownership and zero-row bodies. Supplied rows do not prove SQL results.
4. Run focused and full Debug/Release suites, isolated EF10.0.14 and the same baseline-built package
   consumers. Independent review, fix regressions, update evidence and commit locally. No live execution.

Verified checkpoint: final Debug/Release and isolated EF10.0.14 Release each pass3652=1708+1074+868+2,
zero failures/skips. Focused95 unit/24 functional cover the wider path. Review's nullable-model/provider
converter defect has separate captured/inline RED evidence, a two-guard fix and independently verified
re-review. The same baseline-built integer-roots-r1 package passes both modes/patches, content, package-only
graphs and Release/archive/consumer DLL identity. Previous Oracle package rejects the new root smoke with
caught exit1. Native execution and remaining carriers/projections stay open; overall goal remains active.

## Batch8b brief: restore the default dependency audit gate

**Feature IDs:** PKG-01/02, TST-01, REL-02. The ordinary solution restore currently fails because the
official EF10.0.0 specification package brings vulnerable Cryptography.Xml/Pkcs 9.0.0 test dependencies.
Keep warnings-as-errors and NuGet audit enabled. Override the test dependency with the available 9.0.23
servicing patch, using central version management and a reference owned by SpecificationTests. Prefer
one direct Xml reference if its dependency closes Pkcs as well; do not change provider runtime dependencies.

- [x] Capture default restore failure with NU1902/NU1903 before editing dependencies.
- [x] Add the centrally versioned test-only Xml reference; restore without warning overrides and inspect
  the resolved Xml/Pkcs versions and absence of these libraries from the provider dependency graph.
- [x] Run full Debug and Release solution tests under the default warning policy. Pack the provider and
  run Verify-Package.ps1 to prove the runtime dependency boundary remains unchanged.
- [x] Independently review the diff and dependency graphs, resolve findings, update checkpoint evidence
  and commit. Then continue the Oracle namespace lifecycle contract; this gate does not complete EF10.

## Batch7f brief: Oracle owned user/schema lifecycle

**Spec:** Oracle lifecycle section in efcore-10-technical-baseline.md. **IDs:** SCH-01/02/03, DIA-01, CFG-04.
Implement in place on efcore/10.0, reusing the current creator, management factory and EF10 LogCommandText.

- [x] Add failing typed/untyped options and sync/async functional cases for explicit Oracle user management,
  exact scope, SYS.DBA_USERS binding, CREATE/GRANT/DROP order, absence and password log redaction.
- [x] Extend options/clones and shared namespace orchestration; isolate only Oracle SQL/password handling
  in OceanBaseDatabaseCreator.Oracle.cs. Preserve every MySQL guard and ownership contract.
- [x] Cover invalid credentials/scopes/modes, callback timing, model prevalidation, failures/cleanup,
  cancellation and target preservation. Run focused tests and then full Debug/Release and selected-patch
  checks; validate the packed API/dependency boundary. Intercepted execution is not native acceptance.
- [x] Independently review the completed module, resolve findings, update checklist/README/checkpoint and
  commit only after the tests and review pass. Continue the remaining EF10 inventory afterward.

## Batch4a/4b/4c brief: relational model, graph materialization and filters

Continue Batch 4 on the existing efcore/10.0 checkout. Reuse EF's relational conventions and
materializers; add provider behavior only for a reproduced failure. No database access or new dependency.

- Batch4a (MOD-01/02/03): characterize composite/alternate keys, required/optional composite foreign
  keys, explicit index/column/table names, owned reference/collection and shadow-property metadata through
  the initialized provider model. These checks do not establish DDL or graph-save support.
- Batch4b (QRY-05/08): feed deterministic readers through real sync/async EF commands for reference and
  collection Include/ThenInclude, absent optional dependents, repeated rows, tracking/no-tracking and
  identity resolution. Pin SQL joins/projection order and inspect object identity, fixup and tracker state.
  Cover entity cardinality separately; supplied rows do not establish server filtering/order/cardinality.
- Batch4c (QRY-09): characterize EF10 named global-filter composition, selective/all disable and fresh
  context/captured-predicate parameters through real sync/async provider commands. Synthetic scalar rows
  prove materialization only; assert filter SQL and bound values independently of those rows.
- Run focused characterization first. Confirmed provider defects require a failing regression before
  implementation. Run full Debug/Release and isolated EF10.0.14 Release, independent read-only review,
  update evidence and commit. No production/package rebuild is needed if only tests/docs change.
- Keep Required rows partial until the remaining local and live acceptance criteria are satisfied.

## Batch6a brief: failed-open ownership and accounting

CON-03/04 and SCH-01: fix the documented failed-open cleanup gap at the shared provider connection,
so queries, writes and read-only database probes use one policy. Validate the configured mode before
capturing state. A failed open restores the entry EF open count/ownership flag and closes a physical
connection only when it was not already open at entry. Preserve an outer EF lease and caller-opened
connections. Async cleanup must not reuse a cancelled opening token.

Exact EF10 RelationalConnection source/assemblies show ConnectionOpened callbacks execute before
_openedInternally/_openedCount are updated; ambient handling executes after both. Public Close cannot
distinguish these phases and may decrement an outer lease. There is no protected accounting snapshot API.
Use two narrowly isolated .NET UnsafeAccessor field contracts for this EF10-only repair, with explicit
tests on EF10.0.0 and10.0.14. Review these private contracts on every supported EF patch/major upgrade;
do not access Connector internals or add an alternate transport. Cleanup uses public DbConnection close
directly, avoiding reentrant EF closing callbacks after an opening callback has already failed.
If raw close itself fails, retain the original opening exception (including cancellation), attach the
cleanup exception as Exception.Data["OceanBase.OpenCleanupError"], and still restore accounting. Physical
cleanup is then best effort and the captured connection can remain open; do not report that as successful
cleanup. The entry DbConnection reference is captured before opening so callback reentrancy cannot make
cleanup close a replacement connection.

First reproduce ambient-warning count leakage through the real provider. Then cover both failure phases,
pre-open failure, nested leases, caller-opened state, cancellation and retry with deterministic EF/native
connection doubles. Test the real registered provider path as well as the physical-state double; doubles
do not prove Connector/server behavior. Run full Debug/Release/isolated14, same-package consumers at both
patches/modes, independent review, update evidence and commit. Broader transaction/lifecycle/live work
remains open; EF8/EF9 are postponed.

## Batch6b brief: transactions, savepoints and batch ownership

TXN-01..03 / CON-03: continue in the existing authorized efcore/10.0 checkout. Reuse EF10's transaction
implementation for begin/commit/rollback, enlistment and command attachment. A test-only relational connection
with deterministic native connection/transaction/command doubles exercises the actual provider batch executor;
it does not establish native connector state or server rollback. Keep the standalone live test program postponed
at the user's explicit request on 2026-11-11.

- Pin owned versus borrowed transaction disposal, same-connection validation, isolation and async tokens.
- Pin automatic transaction selection for multiple batches and MySQL generated-value follow-up reads, failure
  before commit, disposal, and caller-owned transaction savepoint/rollback/release with retry.
- RED: Oracle releases must not send RELEASE SAVEPOINT. Register a mode-specific transaction factory and
  override only Oracle release as a logical no-op, leaving creation and rollback SQL with EF's quoted helper.
  Preserve transaction diagnostics/interceptors for the logical release; check async cancellation before it.
  Oracle cannot physically discard an individual savepoint; it survives until replaced or transaction end.
- The connector 0.2.0 public savepoint API rejects Oracle Release and restricts names, including EF's internal
  __EFSavePoint. Retain EF's command-based create/rollback rather than changing driver API contracts. This is
  a provider integration difference, not evidence of a driver defect. Live native state remains unverified.
- Run focused RED/GREEN, full Debug/Release/isolated EF10.0.14, same-package both-mode/patch consumers after
  production changes, independent read-only review, docs and a local commit. No network database execution.

## Batch6c brief: save cancellation and cleanup recovery

TXN-02/03 / CON-03: continue from16b1d92, using the existing transaction doubles. Reproduce a cancelled
second write in a caller transaction: EF10's inherited BatchExecutor reuses the cancelled token for rollback
and release, so recovery may never reach the native command. Automatic cleanup must use CancellationToken.None;
public explicit transaction methods must still honor their supplied cancellation tokens.

- Preserve the caller transaction, original cancellation identity and tracked states. Attempt rollback only to a
  successfully created automatic savepoint on an open connection. Do not automatically commit, retry or own the
  caller transaction. AutoSavepointsEnabled=false remains an explicit user choice.
- Retain EF's batching/auto-transaction/diagnostic policies. If regression confirms no protected recovery seam,
  specialize ExecuteAsync in a version-local BatchExecutor. Six additional sync regressions justify also owning
  Execute for failed-open leases and error-preserving cleanup. Acquire and close only this operation's lease.
  Cleanup failures must not replace the original error; record
  recovery failures on that error and keep normal EF rollback/release diagnostics. Successful savepoint-release
  failures retain EF's warning policy. Owned-transaction cleanup uses its uncancelled DisposeAsync contract.
- Cover both modes, async tokens, successful recovery/retry, recovery failure, pre-open failure/outer leases,
  savepoints-disabled and owned cancellation. No native rollback or live atomicity claim. Run full baseline/patch
  suites, same-package consumers, independent review, docs and local commit. No standalone/live program.
- Transaction-start failures must restore entry accounting and dispose a newly allocated, not-yet-tracked native
  transaction after a Started callback/factory error. Reuse the shared open-state snapshot; retain EF10 logger,
  protected native begin calls and factory. Interceptors replacing a transaction retain ownership of the replaced
  handle as in EF. A native disposal failure is best effort, may leave unusable context/transaction state, and
  does not establish rollback. Keep original errors and attach secondary cleanup details.

## Batch7a brief: offline relational create scripts

SCH-02 / MOD-03: implement Database.GenerateCreateScript from the design-time relational model, without
opening a connection. Preserve existing namespace ownership guards on all mutating schema methods until the
next lifecycle slice defines an explicit per-mode opt-in profile. Do not repurpose EnsureDeleted as model-table
deletion under an undocumented default.

- Generate complete ordinary tables/columns, nullable facets, primary/alternate keys, indexes, check constraints
  and foreign keys. Emit FKs after tables, so cyclic graphs are supported. Respect excluded tables and views.
- Reuse the provider's SQL helper and resolved store mappings for quoting/types/default literals. MySQL integer
  identity uses AUTO_INCREMENT; Oracle uses GENERATED BY DEFAULT AS IDENTITY. These are generated dialect
  contracts, not proof of server/version support. Validate incompatible generation before returning a script.
- Explicit default SQL and computed SQL are trusted model expressions. MySQL computed columns have the mapped
  type and STORED/VIRTUAL; Oracle virtual expressions infer their type. Oracle stored computed columns,
  unsupported index filters/collations, model sequences and unimplemented HasData inserts fail explicitly.
- Ruling after independent review: preserve MySQL computed-column collations; explicitly reject TPT/TPC and
  entity splitting until table-specific identity propagation is designed under DEF-07. Silently marking dependent
  table keys as new identities is incorrect. Both findings have focused RED/GREEN fixtures; no server claim.
- Add offline positive/negative model fixtures, full Debug/Release/isolated14, same-package consumers,
  independent read-only review and local commit. No live schema operation or standalone test program.

## Batch7b brief: explicit existing-schema table creation

SCH-02 / CFG-04: add EnableOceanBaseSchemaManagement(databaseOrSchema) on typed/untyped options builders.
The immutable scope is per context (not a singleton/model-cache input); clone it across all existing UseOceanBase
and relational option calls. Do not log connection strings or the scope. The first execution profile is table
creation inside an already provisioned MySQL database or Oracle schema. Namespace creation/deletion remain
guarded until the next slice; Oracle tenant creation is never inferred.

- Before opening, require opt-in, reject active/enlisted/ambient transactions and configured unsupported seeding
  callbacks, validate the entire DDL plan and reject created-table/FK-principal schemas outside the declared scope.
- Hold one EF connection lease while checking SELECT DATABASE() / SYS_CONTEXT CURRENT_SCHEMA against the exact
  declared scope. EnsureCreated returns false when any ordinary table exists; otherwise executes individual
  validated DDL commands. Explicit CreateTables executes the same plan without the emptiness shortcut.
- Async checks pre-cancellation and forwards the caller token to opening/probes/every DDL. Preserve the original
  DDL/cancellation exception and balance the lease on failure. Attach a final close failure to the original
  exception as Data["OceanBase.SchemaCleanupError"]; successful-operation close errors propagate normally.
  Existing caller/outer connection ownership remains.
- DDL is not transactionally atomic. Never auto-delete partially created objects after a failure. No retry/schema
  reconciliation is claimed; user cleanup may be needed. Scope options do not yet enable EnsureDeleted.
- Add deterministic both-mode sync/async connection/command fixtures and option-clone tests, RED/GREEN, full
  suites, same-package consumer smoke, independent review, docs and a local commit. No actual DB access.

## Batch7c brief: explicitly owned MySQL database namespaces

SCH-01/02/03, CFG-04: add typed/untyped EnableOceanBaseDatabaseManagement(databaseName), separate
from the existing-schema opt-in. It declares ownership of the entire named MySQL database, including
objects outside the EF model. Preserve immutable per-context options/clones and singleton sharing;
switching back to EnableOceanBaseSchemaManagement disables namespace management. Reject system database
names. Oracle user/schema namespace provisioning/deletion remains a separate locally developable slice;
do not infer tenant creation or attribute this unimplemented profile to connector/server limitations.

- Require the current configured MySQL database name to match the declared scope exactly, a configured
  connection string, a closed target physical connection and no active/enlisted/ambient transaction.
  Verify provider-created identity and agreement of canonical EF/native configuration; reject runtime
  supplied connections and native routing/credential/TLS hooks before any management open.
  Validate the complete table plan before EnsureCreated can create a namespace. Supplied/native custom
  connections require a future explicit management-connection contract; do not copy private credentials.
- Clone EF connection dependencies/options into an independent provider connection with Database empty;
  retain mode, interceptors, timeout and public connector APIs, without changing the target connection.
  Query information_schema.schemata with a bound name and verify its returned spelling exactly. Do not
  treat authorization/network/catalog errors as absence. Quote namespace DDL with the SQL helper.
- Create/Delete execute CREATE/DROP DATABASE directly. Catalog NULL cannot prove absence because
  visibility depends on privileges; EnsureDeleted still attempts DROP and returns false only for
  known-outcome MySQL error1008. Direct Delete preserves that error. EnsureCreated creates a missing namespace and tables,
  returns false for existing populated schemas, and handles an empty model without invented tables.
  Clear only the target connector pool around namespace mutation; never clear unrelated pools.
- Async propagates tokens and checks pre-cancellation. Dispose only the owned management connection;
  preserve primary errors and attach cleanup failures. DDL is non-atomic, has no auto-retry/rollback and
  may leave a created namespace or partial tables after failure. Never auto-drop on create failure.
- Test option boundaries, exact names/quoting, master-versus-target connection identity, lifecycle paths,
  transactions/cancellation/failures/cleanup, both sync/async, and retained Oracle guards. Full baseline
  Debug/Release/isolated14, same-package/content/graph/hash gates, independent review, docs/local commit.
  No database access, live test application, EF8/9, push or publication.

- Batch7c review ruling: EF can cache an old connection string while the actual native target has
  changed server/credentials; runtime SetDbConnection bypasses options-level supplied guards. Fourteen
  RED regression cases prove this risk. Track provider-created native identity, reject custom hooks and
  compare both strings through the same public-mode/concurrency validator. Preserve supported EF
  SetConnectionString. Hidden secrets cannot prove equivalence and are conservatively rejected.
- Batch7c source gates:59 intercepted lifecycle and12 option cases pass. Full Debug/Release and isolated
  EF10.0.14 Release each2461=1644+815+2 pass, zero failures/skips. Independent source re-review approves
  with no open findings. Same database-management-r2 package passes both modes/patches, archive content,
  package-only graphs and DLL identity. Preceding package returns handled exit1 at the missing new API.
  Final documentation/consumer review approves after explicit packaged CREATE TABLE/INDEX assertions
  and a table-only namespace note correction. Same unchanged package reruns pass both patches/modes;
  provider DLL identity remains unchanged. Local slice is ready for commit; overall goal stays active.

## Batch7e brief: explicit management connections (Oracle lifecycle prerequisite)

- Add typed/untyped `UseOceanBaseManagementConnection(Func<OceanBaseConnection>)` options, independent of
  namespace ownership. A factory alone never authorizes creation/deletion and is never invoked for queries,
  table-only operations, invalid models/scopes, active caller transactions or pre-cancelled calls.
- Each namespace operation requests a fresh, closed, independent native connection. The provider owns its
  disposal; returning the target connection rejects without disposing it. Factory exceptions retain identity.
  Invalid independent results are disposed; cleanup errors attach to the original validation error.
- The factory explicitly selects the privileged endpoint/tenant, credentials and routing/TLS policy; the
  application must point it at the same tenant as the owned target. Do not clone or erase native callbacks.
  Validate native mode, reject a selected MySQL database, and retain exact target scope checks. The existing
  provider-created string-only fallback retains all prior configuration-equivalence/hook guards.
- Reuse normal EF connection/command diagnostics, interceptors, timeout and cancellation for this profile.
  Preserve primary operation errors and owned disposal on success/open/command failures. No DDL retry.
- Keep factories/credentials out of service-provider cache identity, debug information and option log fragments.
  Copy factories through relational option clones without invoking them or granting ownership implicitly.
- Scope: options/public extensions, database creator management construction, unit/functional tests, manual
  packed consumer and English support/evidence docs. No native SQL/password DDL or new dependency is needed.
- Verify missing-API RED, lifecycle/invalid-result/ownership/error/cancellation behavior, full Debug/Release/
  isolated EF10.0.14 and same-package consumers at both patches/modes; independent review and local commit.
  Oracle user/schema provisioning/deletion is the next implementation, not completed by this prerequisite.
  No real database, standalone live program, EF8/9, push or publication.

## Batch7d brief: EnsureCreated seeding callbacks

SCH-02 / DEF-09: support EF10 UseSeeding/UseAsyncSeeding after successful EnsureCreated for both
existing-schema modes and the owned MySQL namespace profile. Match the official EF10.0.0
RelationalDatabaseCreator callback/context/operationsPerformed/owned-transaction contract; keep the
existing explicit scope and caller-transaction guards. The source contract was inspected from the
published EF10.0.0 assembly (artifacts/seeding-ef-source/).

- Validate matching sync/async callback availability before DDL; when only the opposite delegate exists,
  throw CoreStrings.MissingSeeder without opening. Do not run callbacks from CreateTables/Create/Delete.
- Run the selected callback once after successful schema work, including populated-schema false and
  empty models. Begin a new owned transaction on the target connection; recheck actual scope inside it
  before invoking application code. Commit only after the callback returns successfully. Pass async
  cancellation to begin/probe/callback/commit, and never retry callback or DDL automatically.
- Dispose the owned transaction on all paths. Preserve callback/commit/scope errors over disposal;
  attach a secondary failure as OceanBase.SeedingCleanupError. Native disposal/implicit rollback can
  fail and remain live gates. A seed failure does not roll back already executed non-atomic DDL.
- Add both-mode sync/async intercepted real SaveChanges coverage, return flags/order/transaction
  identity, wrong delegates, failures/cancellation/cleanup, empty models/scope guards and retained
  namespace behavior. Replace prior unsupported-seeding tests with the new precise contract.
- Full Debug/Release/isolated14 and same-package/content/graph/hash gates, independent review, docs and
  local commit. No migration lock, retry strategy, distributed transaction or live acceptance claim.

Execution afterc1dc62d:46 new cases pass, including callback SaveChanges, wrong delegate prevalidation,
false flags, transactional scope changes/probe failure, cancellation after return and cleanup. Initial
full-suite provider-cache warning was traced to new test replacement combinations and fixed with
fixture-only cache isolation. Full Debug/Release/isolated14 each3375=1644+861+868+2 pass, zero skips.
Same seeding-20261111 package passes both modes/patches/content/graphs/DLL identity; prior package
rejects the new callback smoke with handled exit1. Reviewer approved source/tests/docs with no
outstanding findings. Evidence artifacts/seeding-*.log; provider/package SHA-256 recorded in progress.
No live execution; namespace-positive callback and physical rollback/commit remain unproven.

## Batch9a brief: bulk relational update/delete

DEF-03: promote ordinary ExecuteUpdate/ExecuteDelete from the deferred inventory into active development.
Reuse EF10's relational setter translation, query filters, command execution, affected-row and tracking
contracts. Source: official execute-insert-update-delete documentation and published EF10.0.0
QuerySqlGenerator/RelationalQueryableMethodTranslatingExpressionVisitor (artifacts/bulk-dml-ef-source/).

- Characterize both-mode sync/async filtered/unfiltered operations, multiple/captured/expression setters,
  nullable and converted fields, named filters, hostile inputs, result counts and unchanged tracked state.
- Implement MySQL UPDATE table-reference/join SQL, not the base generator's UPDATE/FROM syntax. Preserve
  original-row values across dependent assignments by topological ordering. Reject dependency cycles
  until snapshot-based simultaneous assignment is implemented; never silently apply sequential values.
- Keep Oracle ordinary single-table UPDATE/DELETE; reject joined UPDATE/pushed-down update shapes before
  any open until a native rewrite is implemented. Retain EF's delete predicate/subquery translation.
- Apply existing column write validation to direct literal/captured setter values before opening; keep
  predicate operands independent of column bounds. Server-computed expression bounds remain live work.
- Cover current captures across cached queries, cancellation/errors, command transaction attachment and
  no implicit transaction/concurrency/tracker updates. Do not invent tracked SaveChanges semantics.
- Run full Debug/Release/isolated14, packed consumers, independent review, docs and local commit.
  This is the initial bulk implementation stage: Oracle joined updates, cyclic MySQL assignments,
  complete advanced/model/paging shapes and live execution remain on DEF-03 until separately implemented.

Execution after5f56125:48 bulk and four caller-transaction cases pass. Initial30 exposed20 failures;
additional RED cases cover joined assignment dependencies and the reviewer's computed/cross-encoded
Boolean storage finding. Target-aware CASE now preserves nullable Boolean encodings; arbitrary
incompatible converter server expressions reject before open. Restore root statement tags lost by
EF10 VisitChildren reconstruction. Full Debug/Release/isolated14 each3427=1644+913+868+2 pass with
zero failures/skips. Same bulk-dml-20261111 package passes both modes/patches/content/graphs/hash;
prior seeding package fails new smoke with handled exit1. Independent final source/test review has
no outstanding findings. See progress for package identity/hash and artifacts/bulk-dml-*.log evidence.
Oracle join rewrites, MySQL snapshot assignments, wider converter/model shapes and live tests remain
open; this stage does not complete DEF-03 or the overall goal. No driver/server defect demonstrated.

## Batch9b brief: Oracle joined and pushed-down bulk updates

DEF-03: replace the initial joined Oracle UPDATE rejection with correlated scalar-subquery SET values
and an EXISTS target filter. Reuse EF10's complete translated source query, including join predicates,
query filters and nested paging. Keep the native single-table path unchanged.

- Inspect published EF10.0.0 QuerySqlGenerator/relational bulk translation and pinned OceanBase Oracle
  UPDATE/scalar-subquery references. Use one source SELECT per setter and one EXISTS; correlate every
  physical primary-key column to a distinct outer target alias. Never drop source filters or paging.
- Require a physical table primary key for joined correlation and reject unsupported outer modifiers
  before opening. Retain quoted table/schema/key/column names and avoid aliases from every nested scope.
- Keep target encoding/write validation, original-row assignment semantics, tags/current binds,
  cancellation/command timeout and caller transaction attachment. Key assignments remain permitted:
  correlation is a WHERE condition on the original row, not a merge ON-clause restriction.
- Cover inner/left/self joins, target on either join side, composite/converted/renamed keys, nullable
  values, multiple/cyclic setters, Boolean encoding, named filters, order/skip/take/zero-row sources,
  alias collisions and keyless early rejection. Duplicate source matches retain scalar-subquery
  cardinality errors instead of choosing a value silently; actual server behavior remains a live gate.
- Run both selected patches/full configurations, same-package checks and independent review, update
  support/evidence docs, commit locally. MySQL snapshot assignments and the wider DEF-03 inventory stay open.

## Batch9c brief: MySQL original-value snapshot assignments

DEF-03: replace cycle/dependent-join rejection with an update of a physical target joined by every
primary-key column to a non-merged derived source. Project each complete, target-encoded setter once
alongside provider-owned key/value column aliases. Keep the complete original source/predicate,
including nested paging. Use the documented OceanBase SELECT /*+ NO_MERGE */ hint; never update the
derived table itself. Ordinary acyclic single-table assignments retain their existing ordered path.
Derived joined sources use the snapshot conservatively because projected aliases can hide target reads.

- References: pinned OceanBase docs commit2a5ae3a23a51729d065cec716508eb26cb064106, MySQL UPDATE
  and NO_MERGE query-transformation contracts; public MySQL parser grammar already inspected.
  NO_MERGE forbids view merging; native evaluation/materialization/read consistency remains a live gate.
- Require a physical primary key; reject unnormalized outer operators rather than dropping them.
  Use fresh target/source aliases outside all nested source/setter scopes and quote identifiers.
- Keep encoded Boolean/null semantics, captured write bounds, exact positional binding order,
  query filters/tags/paging, affected rows, cancellation/errors and caller-owned transaction behavior.
  Multiple matching source rows retain native MySQL joined-update semantics; do not invent ordering.
- Both generators accept normalized left joins directly, avoiding EF's anonymous-setter fallback failure.
- Cover cycles, dependent joins, converted nullable Boolean swaps, key writes, cached captures,
  paging, keyless rejection and aliases. Update the old rejection tests and package smoke.
- Run full selected configurations, same-package gates and independent review, update docs and commit.
  This closes these local DEF-03 gaps, not advanced bulk variants or physical server acceptance.

## Batch8a brief: official relational model specification fixtures

TST-01 / MOD-01/02/03 / DEF-07: consume the official EF10 relational specification package in a
separate solution test project. Reuse the published ModelBuilderTest wrappers and relational bases;
configure the real OceanBase provider, conventions, validator, type mapper and logging in both modes.
This implements the existing Batch8 requirement, without creating the postponed live-test application.

- Add the specification package to central versions with the same EF10 range; retain the provider's
  EF10.0.0 baseline. Restore new test dependencies to a workspace-local package directory where needed.
- Add a small RelationalTestHelpers adapter using public provider option/service extension contracts.
  Run the complete inherited one-to-many, many-to-one, one-to-one, many-to-many, inheritance and owned
  model families in MySQL and Oracle modes. Keep upstream assertions and inherited skips visible.
- First run the fixtures to expose actual gaps; fix reproduced provider defects with covering RED to
  GREEN tests. Do not weaken inherited assertions or mark failures skipped to obtain a green result.
  Any genuine profile-specific exclusion needs an exact test name, reason and checklist mapping.
- Run the new project and existing full Debug/Release suites plus isolated EF10.0.14. Confirm package
  dependency versions and zero network/database calls in fixture execution. Review source/config/docs
  independently, then commit locally. Repack runtime assets only if production code changes.
- Model metadata success is not runtime query/write/DDL support for inheritance, splitting or skip
  navigations; those remaining contracts and live evidence stay open. Live access was already supplied
  according to the user; fixture/configuration wiring remains our work, not a missing-user-input gate.

- Batch8a initial fixture run passes868=866 inherited+two adapter guards, zero failures/skips. Six
  families per mode:90 one-to-many,59 many-to-one,142 one-to-one,36 many-to-many,31 inheritance,
  75 owned. No provider assertion overrides/exclusions. Both mode services/mappings and sync/async
  network rejection have direct adapter checks. Full Debug/Release/isolated14 each3329=1644+815+868+2
  pass; matching official package graphs verified and baseline remains10.0.0. No production behavior
  or runtime dependencies changed; no new pack/consumer rerun is needed for this test-only slice.

## Batch5a brief: tracked graph writes and composite concurrency

UPD-01..05 and DIA-02: exercise real EF SaveChanges in both modes, sync/async, with commands intercepted
before network execution. Add dependent-first three-level graphs and verify principal-before-dependent
inserts, generated-key propagation, relationship fixup and dependent-before-principal deletes. Characterize
composite-key updates/deletes with NULL/non-NULL original concurrency tokens, current versus original
values, zero-row conflicts, exception entries and connector/cancellation identity.

Pin full generated DML and parameter-to-placeholder values independently of supplied reader/output values.
Synthetic readback must follow the command's projected columns. Preserve the existing single-row batches;
do not add graph ordering code if EF already orders correctly. Automatic transactions are explicitly disabled
in this command fixture, so these tests do not establish SaveChanges atomicity or rollback. Test missing
behavior RED before implementation; run full Debug/Release/isolated14 and independent review. Repack and
repeat both-mode/patch consumers if production changes. Wider generation/readback, unsupported graph shapes,
transaction and live gates remain separate required inventory.

## Batch5b brief: command timeout and in-flight cancellation boundaries

CON-05: characterize EF command execution with a real provider query and deterministic command interceptors.
For both compatibility modes and sync/async paths, `Database.SetCommandTimeout` must reach the created
`DbCommand`, an in-flight `OperationCanceledException` must retain its identity/token, async interception must
receive the `ToListAsync` token, and a failed query must leave the EF-visible connection state closed while the
command pipeline can be retried in sync/async paths. The test fixture suppresses network access and supplies rows only on the retry; it does not establish connector
wire cancellation, server timeout enforcement, or transaction/resource behavior on a live server. Fix only a
reproduced provider defect, then run full Debug/Release/isolated14, independent review, update evidence and
commit. DIA logging/redaction and transaction gates remain separate slices.

## Batch5c brief: command diagnostics and parameter redaction

DIA-01: exercise EF's real command logging and interceptor pipeline in both modes and sync/async paths.
Successful commands must be reported, failures must retain the original exception in the log, and captured
parameter values must remain redacted by default while `EnableSensitiveDataLogging` explicitly permits them.
Use runtime-captured query values so EF creates parameters; compile-time constants may be rendered as SQL
literals and are outside this redaction assertion. The network-suppressed reader supplies a deterministic row
or throws a synthetic failure. This establishes EF/provider logging boundaries only; connector/server logs,
structured redaction sinks, and production policy remain separate. Run focused and full suites, independent
review and commit; no package rebuild when production code is unchanged.

## Batch5d brief: generated readback shape rejection

UPD-02/UPD-05: add a real EF SaveChanges regression for a MySQL composite key with two generated identity
columns. The provider's single-identity `LAST_INSERT_ID()` readback contract must reject the shape before
executing DML in both sync and async paths, with the existing actionable `NotSupportedException`. This is a
test of the current provider boundary; it does not add a second identity retrieval strategy or establish Oracle
multi-output/live generation behavior. Run full Debug/Release/isolated14, independent review and commit; no
package rebuild is needed when production code is unchanged.

## Batch5e brief: multi-column default/computed readback

UPD-02/05 / MOD-03: continue from9d1f4d2. Pin insert/update generated identity, nullable/non-null computed
values, on-add defaults and converted concurrency outputs together in real EF SaveChanges. Cover composite-key
MySQL readback with exactly one identity and explicit/default-sentinel writes. Supply deterministic values by
the actual projected/RETURNING column names, but assert the SQL/bind/output sets independently.

- Verify generated values and original concurrency snapshots after successful insert/update; on-add defaults do
  not overwrite explicit values or refresh on update. NULL outputs must be accepted for nullable properties and
  rejected for required properties rather than silently becoming CLR defaults.
- Failed later conversion/readback/affected-row checks retain entries, temporary keys and pending changes; a
  failed update must not accept new originals. Never claim synthetic readback executes database expressions.
- Characterize already-correct behavior; reproduce defects RED before fixes. Run baseline/selected-patch suites,
  independent review, same-package smoke if production changes, update evidence and locally commit. No live DB
  or standalone functional test program. Transaction/physical generation acceptance remains separate.

## Batch 1 brief

**Files:** OptionsTests.cs; SqlGenerationHelperTests.cs; ConsumerTests/ProviderConfigurationTests.cs;
production options/cache/helper code only if a demonstrated defect needs fixing.

**Behavior:** Explicit modes are preserved for both generic/untyped overloads; invalid modes/null builders/null
connections reject. A null connection string may configure services/models and be filled later using standard
EF SetConnectionString; connecting without a string must fail clearly without network access. Different connection
strings in one mode reuse compatible EF services/models; MySQL and Oracle never share a model/service graph.
Design-time and runtime cache keys differ. Identifier escaping must double embedded per-mode delimiters, qualify
schema and preserve empty/Unicode names consistently in return-string and StringBuilder overloads. Consumers
identify the provider in both modes. Do not claim full CFG-04 until database creator services exist.

1. Read the connected production APIs and existing tests; enumerate positive/negative/cache/escaping cases.
2. Add characterization tests for existing contracts. If a bug appears, isolate it with a failing test before fixing.
3. Run the focused tests, then full Debug/Release suites. Record counts, commands and remaining gaps.
4. Independent read-only review; address confirmed issues with regression tests. Update feature evidence and commit.

## Review focus

- Mode-specific singleton factories must not capture a different context's mode through cache reuse.
- Connection strings must not leak into cache/log/debug evidence or demand network access during model building.
- EF parsed facets/converters must not bypass the provider's validation or alter query operand semantics.
- SQL null/cardinality/string/temporal semantics must not inherit SQL Server-only syntax.
- Local transaction/schema doubles prove client control flow, not connector or server execution.

## Batch 2a brief: Oracle fixed-point aliases

**Files:** Storage/OceanBaseDecimalTypeMapping.cs, Storage/OceanBaseIntegerTypeMapping.cs;
UnitTests/DecimalMappingTests.cs and ExplicitIntegerMappingTests.cs; FunctionalTests/BasicRelationalTests.cs.
The updated decimal contract in the technical baseline is authoritative.

1. RED: Oracle bare DECIMAL and NUMERIC expose precision 38, scale zero, preserve trimmed declarations,
   output metadata and composed converters/clones; reject fractional current decimal writes. Bare NUMBER
   still accepts fractional decimals and leaves precision/scale unspecified. Query/InputOutput inputs through
   fixed aliases retain value inference even when fractional. Configured precision/scale regenerate explicit SQL.
   Integer mappings using bare DECIMAL/NUMERIC expose precision 38, zero scale, while configured precision
   overrides the alias default by generating an explicit declaration. Test bool/enum converter paths as applicable.
2. Implement the smallest shared default-selection fixes; no transport fork, aliases outside existing scope,
   CLR floating-point expansion or dependency changes in this sub-batch.
3. Add intercepted EF sync/async query/write tests for decimal aliases, including converted current values,
   pre-command rounding rejection, lossless query operands and unqualified NUMBER independence.
4. Run focused RED/GREEN and full Debug/Release; independent review and commit with progress/evidence.
   Later selected-patch/package verification must run after the production change before closing package evidence.

## Batch 2b brief: temporal facet and clone validation

**Files:** Storage/OceanBaseTypeMappingSource.cs, Storage/OceanBaseTemporalTypeMapping.cs;
UnitTests/TemporalMappingTests.cs and TemporalQueryMappingTests.cs as needed. Preserve existing query/write
transport and converters; no new temporal types, server ranges or dependencies.

1. RED: both modes and all five temporal CLR types reject size and fixed-length=true facets through direct
   resolution and model properties. Clones reject incompatible/cross-mode store declarations, suffixes,
   precision conflicts and size/fixed facets rather than producing mismatched SQL/DbType/converter metadata.
   Include a converter-composed mapping and Oracle DATE/interval/zoned representations in clone boundaries.
   Distinguish EF's parsed declaration Size from actual length configuration; property GetMaxLength must reject
   even when it equals parsed precision. Limit clone declarations to the current transport family, so DATE,
   unzoned/zoned timestamps and intervals cannot retain stale DbType or converter semantics after a family change.
2. Add the smallest validation consistent with the existing temporal declaration parser. Keep legal unchanged
   clones, converter composition and precision changes working; generated declaration, Precision and output
   Scale/leading metadata must agree. Retain wide Input/InputOutput query precision after precision reduction.
3. Focused RED/GREEN, full Debug/Release and independent review. Record actual failing checks and exact counts,
   update MAP-05/06/07 evidence and commit. Live temporal/session/calendar acceptance remains open.

## Batch 2c brief: exact integer normalization and scalar gaps

**Files:** Storage/OceanBaseNumericConversion.cs; UnitTests/ScalarMappingTests.cs;
FunctionalTests/BasicRelationalTests.cs. ExplicitIntegerMappingTests.cs and OceanBaseTypeMappingSource.cs only
if a property-facet regression actually fails. Keep the existing type profile and public API.

1. RED: finite Single/Double fractions must not round into any of the eight integer CLR types, including
   converter-backed readers and generated outputs. Use the real mapping reader path and intercepted EF output
   propagation. Preserve exact integral conversions, checked range/NaN/infinity failures and floating CLR readers.
2. Fix the shared normalization boundary, retaining decimal/OceanBaseDecimal behavior and avoiding a second
   generated-output conversion path. Characterize missing nullable integer endpoints, Boolean false/null and
   Guid.Empty cells using existing helpers instead of duplicating the established scalar suite.
3. Probe explicit Oracle integer property precision conflicts through actual model/property mapping. If already
   rejected, retain only useful characterization; if not, record RED and fix at the shared validation boundary.
4. Focused RED/GREEN, full Debug/Release, independent review and scoped commit. Repeat current package/selected
   patch evidence after the scalar production changes before completing Batch 2 locally.

## Batch 3a brief: string concatenation and query boundaries

**Files:** Query/OceanBaseQuerySqlGenerator.cs; scoped query/parameter processor code only when regression
evidence requires it; UnitTests/SqlGeneratorTests.cs (or a focused StringQueryTests.cs);
FunctionalTests/BasicRelationalTests.cs. Follow the concatenation contract in the query/update design.

1. RED: C# string addition and fixed Concat(2/3/4 strings) use native syntax, numeric addition remains `+`.
   Cover nested/constant/parameter/nullable columns, both modes, predicates/projections and independent contexts.
2. Fix SQL generation while retaining MySQL null protection. Probe Oracle nullable projections and null-sensitive
   predicates against the documented native empty/NULL result rule; fix incorrect nullable metadata or reject a
   specific unsupported shape explicitly. A SQL-shape pass must not hide a known nullability optimization bug.
   RED includes concat IS NULL/IS NOT NULL, one/both nullable inputs, concat comparisons/NOT, nested Concat,
   COALESCE to a nonempty fallback and to empty, and the same query with nonempty/empty/null captured parameters,
   under both null-semantics settings. Correct this before EF's nullability optimization; changing a binary
   expression's nullable flag alone is insufficient because EF may decompose its null test as operand OR.
3. Characterize equality/inequality/string Equals, default/relational null semantics, negative StringComparison
   and custom predicates, and allowed top-level client projections. Reuse EF behavior where it is correct.
4. Add sync/async intercepted parameter/materialization evidence, full Debug/Release, independent review and
   scoped commit. QRY-06 stays partial; later search/member/substring/case/trim slices still need their own matrix.

## Batch 3b brief: numeric division and remainder

**Files:** Query/OceanBaseQuerySqlGenerator.cs; a focused UnitTests/ArithmeticQueryTests.cs;
FunctionalTests/BasicRelationalTests.cs. The arithmetic contract in the query/update design is authoritative.
Do not replace the expression factory or general query pipeline for a SQL-rendering correction.

1. RED: mapped integer division emits MySQL DIV and Oracle TRUNC(a / b), while Oracle remainder emits MOD(a,b).
   Include signed int/long, promoted small integers, uint/ulong, nullable expressions, negative operands,
   captured divisors and a value-preserving numeric converter. Decimal/Single/Double and explicit fractional
   casts retain ordinary division, even when Oracle storage is NUMBER. Preserve numeric +,-,* and unary minus.
2. Implement the smallest generator correction. Preserve grouping for a/(b+c), a/(b/c), nested remainder,
   multiplication around division and predicate comparisons. Keep inferred mappings/null propagation;
   do not use store declaration names as an integer classifier. No bitwise or Math/Random expansion here.
3. Add sync/async intercepted queries for parameter binding and nullable/non-null materialization in both modes.
   Supply synthetic results honestly: these prove no server arithmetic or predicate-membership results.
4. Focused RED/GREEN, full Debug/Release, independent review, selected-patch/package checks, documentation
   and scoped commit. QRY-01 remains partial until wider operators and live acceptance pass.

## Batch 3c brief: unary string functions and character length

**Files:** Query method/member translator providers and focused string translators; Internal/OceanBaseOptionsExtension.cs;
UnitTests/StringFunctionQueryTests.cs; FunctionalTests/BasicRelationalTests.cs. Follow the planned unary string
contract in the query/update design and pinned research in artifacts/local-development/query-functions-design.md.

1. RED: exact parameterless ToLower/ToUpper/Trim/TrimStart/TrimEnd and string.Length translate in predicates,
   projections and order/group expressions with mapped columns. Cover nullable inputs, empty/null constants
   and captured inputs, nested concat, CASE/coalesce, function IS NULL/NOT NULL and both null-semantics options.
   Oracle all-space trim can produce NULL even from a required column; preserve result null tests.
2. Register derived RelationalMethodCallTranslatorProvider/RelationalMemberTranslatorProvider with standard
   AddTranslators, preserving plugins/model DbFunctions and opposite-mode cache separation. Reuse the SQL
   expression factory/default mapping APIs; no new dependency or general query visitor replacement.
3. Use exact overload/member matching and conservative nullable metadata. Character length means database
   characters; trim means SQL blank space; casing means database rules. Reject unsupported culture/Invariant,
   trim-char/char-array and unrelated same-name members in predicates; preserve allowed client projections.
   Check bounded/fixed/national text and a connected string converter without discarding conversion metadata.
4. Add both-mode sync/async intercepted parameter/read checks with synthetic string/nullable int results.
   Focused RED/GREEN, full Debug/Release, independent review, patch/package evidence and scoped commit.
   Search/substring, exact UTF-16/Unicode-whitespace guarantees and server results remain separate work.

## Batch 2d follow-up brief: wide exact decimal to floating normalization

Run after the active Batch 3c worker has stopped. This is a confirmed remaining local scalar gap, not a
database integration task. Requirements are the planned normalization paragraph in the technical baseline.
The package-only reproduction is artifacts/local-development/floating-decimal-probe/ (six genuine failures
against the arithmetic package after correcting an initial probe-only namespace compilation ambiguity).

**Files:** Storage/OceanBaseNumericConversion.cs; UnitTests/FloatingPointMappingTests.cs;
FunctionalTests/BasicRelationalTests.cs. Do not add new store mappings, converters, dependencies or numeric types.

1. RED through actual mapping-reader/generated-output paths: OceanBaseDecimal 1E30, a 38-digit fraction and
   1E-40 target both float/double without CLR decimal overflow. Include positive/negative/zero values, normal,
   subnormal/underflow and target overflow boundaries, extreme scales and a converter-composed reader.
2. Fix the shared conversion boundary using public connector APIs/BCL. Round directly to the requested IEEE
   target; avoid a float-via-double double-rounding path. Finite conversion to infinity rejects. Preserve exact
   integer/fraction/range and decimal semantics, existing native non-finite reader behavior and narrowing checks.
   Include an exact Single midpoint regression: 1 + 2^-24 + 10^-60 must round to the next float above 1,
   whereas converting through Double first can incorrectly round to 1. Assert the expected target bits/value.
3. Add intercepted sync/async floating reads and generated outputs with synthetic wrapper values, reusing
   existing helpers. These prove shared normalization rather than new wire/server type claims.
4. Focused RED/GREEN, full Debug/Release, independent review, patch/package gates, progress and scoped commit.
   Check the actual connector 0.2.0 nuspec source revision rather than relying on connector repository HEAD.

## Batch 3d brief: string search with safe empty-needle handling

Run after Batch 3c and the confirmed Batch 2d normalization follow-up. **Files:** existing string method
translator/provider, scoped Oracle string processor/generator only if required, focused StringSearchQueryTests.cs,
and FunctionalTests/BasicRelationalTests.cs. The planned search contract in query/update design is authoritative.

1. RED exact Contains(string)/StartsWith(string)/EndsWith(string) over mapped nullable/required columns,
   constants, parameters and column needles; wildcard-like characters are ordinary data. Include nonempty,
   empty/null, long/repeated needles, negatives, bool projections and default/relational nulls.
2. Reuse existing translator services and fixed SQL function names. MySQL dynamic empty branch uses character
   length. Oracle known constant/captured empty must become source IS NOT NULL before normalization erases
   its identity; a null needle produces false. Expand any private marker before base nullability, preserving normal
   DbFunction behavior and raw caller values. Oracle column/computed empty needles already collapsed to NULL
   produce false. Guard source/needle and return a total Boolean for all three operations: NULL input is false,
   NOT is true, under both null settings. Bound nullable suffix equality with guards/CASE so EF both-NULL
   compensation cannot invent a suffix match; too-long Oracle suffix NULL also yields false.
3. Test changing one cached query between nonempty/empty/null, composition with concat/unary functions/CASE/
   COALESCE, both-mode context isolation and unsupported char/comparison overloads. No LIKE escaping helper,
   user-derived SQL fragments or general pipeline replacement. Retain conservative function nullability.
4. Add sync/async intercepted binding/materialization/parameter-change cases, focused RED/GREEN, full
   Debug/Release, independent review, patch/package gates, docs and scoped commit. Server collation/padding/
   substring results remain live gates; exact CLR exception/ordinal semantics are not promised.

## Batch 3e brief: Substring and index adjustment

**Files:** Query/OceanBaseStringTranslator.cs; UnitTests/StringSubstringQueryTests.cs;
FunctionalTests/BasicRelationalTests.cs, plus obsolete substring rejection assertions only if present.
Follow the planned Substring contract in query/update design; retain the current converter convention.

1. RED exact Substring(int) and Substring(int,int) through mapped predicates/projections in both modes/null
   settings. Cover constants/captures/columns, starts 0/-1/-2/int.MinValue/int.MaxValue, lengths -1/0/MaxValue,
   nullable results, CASE/coalesce/concat/unary/search composition and unsupported signatures/nontext mappings.
2. Use long-result origin adjustment before any constant evaluation. Assert Type/mapping and positive
   2147483648 at the maximum; preserve raw captured/column mapping. No new cast rendering or generic visitor.
3. Native range/character semantics apply uniformly. Preserve receiver result mapping/converter and positive
   upper-bound Size across fixed/national/LOB families, including lowercase declarations. NCLOB native behavior
   is an unverified extension, not established capability. Preserve independent function NULL production.
4. Add sync/async intercepted parameter/result-conversion cases in both modes. Carry the Batch 3d review minor:
   directly cover Oracle computed CASE/COALESCE needle collapsing to NULL, positive/NOT/projected searches.
5. Focused RED/GREEN, full Debug/Release, independent code/docs review, selected-patch/same-package gates
   and scoped commit. Server contents, invalid-range outcomes, characters, padding/LOB and hosted execution
   for new source remain distinct acceptance gates. No live work or EF8/9 adaptation.

## Batch 3f brief: ordinary temporal members

**Files:** Query/OceanBaseTemporalMemberTranslator.cs (new), OceanBaseMemberTranslatorProvider.cs,
OceanBaseQuerySqlGenerator.cs; UnitTests/TemporalMemberQueryTests.cs and FunctionalTests/BasicRelationalTests.cs.
The planned ordinary temporal-member contract in query/update design is authoritative.

1. RED the twelve exact DateTime/DateOnly/TimeOnly members in both modes/null settings, mapped predicates,
   projections/order/group and composition. Preserve plugin precedence/scoped services/opposite-mode isolation.
2. Use MySQL native functions and a narrow Oracle builtin marker with fixed allowlisted unit fragment and
   visited operand, rendered as EXTRACT(unit FROM input). Only Oracle DATE clock parts cast to TIMESTAMP(7).
   SECOND extracts as decimal then one-argument TRUNC; final result mapping is int without source converter.
3. Validate existing temporal carriers/families without stripping built-in/custom temporal conversions.
   Reject unrelated string/binary/numeric/zoned carriers and unsupported members. Preserve nullable function
   result tests and captured parameter precision; no SQL grammar interpolation or general visitor replacement.
4. Add both-mode sync/async intercepted Int32 comparison/nullable reader/Oracle NUMBER wrapper checks,
   with controlled temporal inputs where present. No synthetic server extraction, clock/cast or calendar claim.
5. Focused RED/GREEN, full Debug/Release, independent code/docs review, selected-patch/package gates and
   scoped commit. Clocks/offset/fractional/Add/duration members and broader numeric queries remain separate.

## Batch 3g brief: numeric Abs, Sign and Truncate

**Files:** Query/OceanBaseMathTranslator.cs (new), OceanBaseMethodCallTranslatorProvider.cs,
UnitTests/MathQueryTests.cs (new) and FunctionalTests/BasicRelationalTests.cs. Root owns docs/package consumer.
The planned numeric contract in query/update design defines the exact twenty-two methods and native limits.

1. Genuine RED for every exact signature in mapped predicates/order/projections, both modes/null settings,
   nullable composition and result NULL tests. Cover negative fractions, signed/decimal endpoints, zero and
   finite wide floating values. Top-level client evaluation and synthetic results cannot prove translation
   or server arithmetic.
2. Reuse strict numeric mapping validation as a probe, preserving operand mappings/converters and full query
   ranges. Reject nonnumeric/null-converting carriers; permit accepted numeric BOOL/BOOLEAN aliases. Pin
   SIGN's fresh int mapping and TRUNCATE's independent int zero. Preserve plugin/DbFunction/mode precedence.
3. Add intercepted sync/async captures/readers in both modes, including numeric wrappers, converters and wide
   floats. Native ABS/SIGN/TRUNCATE/TRUNC domain, overflow and result metadata remain live acceptance gates.
4. Focused GREEN, full Debug/Release, independent code/docs review, isolated later-patch and same-package
   consumer/content gates, then scoped commit. No new dependency, generator, visitor or public configuration.

## Batch 3h brief: integral membership and ordinary relational shapes

**Files:** Storage/OceanBaseIntegerTypeMapping.cs only for a reproduced metadata repair;
UnitTests/RelationalShapeQueryTests.cs (new) and FunctionalTests/BasicRelationalTests.cs.
The planned relational-shape contract in query/update design defines this bounded QRY-03/04 slice.

1. Characterize ordinary inner/left equijoins, composite numeric keys, ON/WHERE placement, correlated/root/
   projected Any/All, ordered nullable scalar FirstOrDefault and queryable scalar Contains in both modes.
2. Reproduce and repair captured int/int? array/list mapping for bare Contains, reusing EF's element JSON
   metadata and scalar parameter expansion. Preserve converters/readers/ranges and distinguish default,
   Constant, context Parameter fallback and unsupported explicit EF.Parameter(collection).
3. Cover empty/null/all-null/duplicate collections, nullable items/negation/projections, both null settings,
   repeated changed captures and padding bucket boundaries without mutating caller arrays or stale SQL.
4. Characterize Count/LongCount grouped/ungrouped/predicate/HAVING/distinct/nullable cases. Add sync/async
   intercepted current parameter/nullable-right/int/long/overflow reads, clearly labeled as supplied results.
5. Focused/full tests, independent code/docs review, selected-patch and same-package gates, then scoped commit.
   Text lists, general VALUES, APPLY, SUM/AVG/Min/Max promotion and actual server results stay separate work.

## Batch 3i brief: scalar text collection membership

**Files:** Storage/OceanBaseStringTypeMapping.cs, Query/OceanBaseCollectionParameterExpandingVisitor.cs (new),
OceanBaseParameterBasedSqlProcessorFactory.cs and existing Oracle string visitor only as needed;
UnitTests/TextCollectionQueryTests.cs (new) and FunctionalTests/BasicRelationalTests.cs.
The planned text-collection contract in query/update design is authoritative.

1. Reproduce missing string collection mapping and Oracle converted-empty expansion; reuse the built-in
   string JSON reader/writer while preserving mapping clone/converter/parameter hooks.
2. Expand only Oracle provider-text IN parameter collections before existing scalar normalization/base NULL
   compensation. Preserve raw values and per-mapping bindings, execution-local state, safe generated names,
   EF bucket policy and cache disabling. MySQL keeps inherited scalar expansion.
3. Cover inline/captured arrays/lists, NULL/empty/nonempty/duplicates and converted-empty elements, both NULL
   settings, changed captures, padding boundaries, name collisions and same capture under two converters.
4. Add sync/async binding/Boolean/nullable reader checks with supplied results; full tests, independent review,
   selected-patch/same-package gates and commit. General VALUES/explicit parameter transport and native
   collation/LOB/charset/actual membership remain separate work and acceptance gates.

## Batch3j brief: captured integral collection roots

**Files:** existing collection-parameter visitor/processor as needed, OceanBaseQuerySqlGenerator.cs,
UnitTests/CollectionRootQueryTests.cs (new) and FunctionalTests/BasicRelationalTests.cs.
The planned captured-root contract in query/update design defines this bounded follow-up.

1. Reproduce general captured int/int? root failures through real EF queries, then expand VALUES before
   the base IntTypeMapping cast. Reuse execution-local snapshots/names/mappings without membership padding.
2. Render SELECT/UNION ALL SELECT with Oracle DUAL per arm, quoted aliases, retained/pruned ordinals and
   legal lossless signed integer typing. Empty captured roots have typed zero rows, not a fake NULL row.
3. Cover arrays/lists, native int/int-to-long converters, null/empty/duplicates/all-null/current captures,
   filter/project/order/composed membership/count/any/join shapes, both modes/null settings and policy modes.
4. Add intercepted sync/async binding/checked reader cases, focused/full tests, independent review and
   selected-patch/same-package gates. Originally empty inline/general text/other carriers stay explicit
   local follow-ups; native coercion/cardinality/order/results remain live gates. Not dispatched yet.

## Batch3k brief: originally empty inline integral roots

**Files:** derived relational queryable-visitor/postprocessor factories only if the demonstrated metadata
gap requires them; minimal options registration and existing VALUES annotation/generator integration;
CollectionRootQueryTests.cs and FunctionalTests/BasicRelationalTests.cs.
The inline-empty local contract in query/update design and local metadata research define this slice.

1. Record the existing real EF empty-inline Count/Any/Select/join failures, then retain original int/int?
   type/mapping before projection/pruning. Preserve contextual inference and its conflict rejection.
2. Use narrow inherited hooks/factories, retain the derived visitor in nested subqueries, and carry immutable
   empty metadata through pruning/alias rewriting without copying Process or guessing Count's element type.
3. Reuse legal typed zero-row rendering; verify nested/alias/ordinal/converted/unsupported-family boundaries
   with real EF and sync/async supplied readers. Keep current captured/nonempty/membership regressions.
4. Focused/full tests, independent review, isolated patch/same-package gates and scoped commit. General
   text/other families and native database results remain separate. Local implementation and source review
   complete: focused57 unit/eight intercepted, Debug/Release/isolated14 Release1281 and same-package
   consumers on both patches/modes pass. Source/docs/consumer reviews pass; native and wider feature gates remain open.

## Batch3l brief: exact Floor/Ceiling translations

**Files:** existing OceanBaseMathTranslator.cs, new FloorCeilingQueryTests.cs, the two now-supported
MathQueryTests negative-list entries and intercepted BasicRelationalTests.cs cases. The numeric local contract in query/update design and numeric-next
research define the eight exact methods, full finite ranges and converter/native-result boundaries.

1. RED exact methods in real EF queries; add Oracle FLOOR/CEIL and MySQL TRUNCATE plus guarded CASE
   without BIGINT narrowing. Reuse validation; retain original mappings and fresh syntax constants.
2. Verify null settings, stable nested expressions, finite/subnormal/decimal extremes, same/cross-carrier
   converters and checked readers. Keep volatile single-evaluation and native result behavior explicit.
3. Focused/full checks, independent review, isolated patch/same-package gates and scoped local commit.
   Local implementation/source review complete: focused46 unit/12 intercepted, Debug/Release/isolated14
   Release1339 and same-package consumers pass both modes/patches. Round/transcendental functions remain separate.

## Batch3m brief: exact transcendental numeric methods

Implement all twelve exact Math/MathF Sqrt, Exp, Log (one/two arguments), Log10 and Pow identities using
the existing numeric translator. Both modes use native SQRT/EXP/LN/POWER; two-argument Log uses MySQL
LN(x)/LN(base) and Oracle LOG(base,x), and Oracle Log10 uses an independent raw provider ten. Preserve
each operand mapping/converter; primary x owns the result. Native functions retain independent nullable
metadata. No finite-domain clipping or fabricated CLR NaN/exception policy is introduced.

Normalize actual Single-provider results in SQL after the complete operation with neutral FLOAT or
BINARY_FLOAT casts; retain native double results. A result-only decimal mapping clone uses GetValue and
checked numeric conversion because Connector0.2.0 GetDecimal rejects boxed native float/double values.
The clone retains the converter/facets; ordinary decimal readers and write validation remain owners.
Binary-to-decimal conversion may round at materialization and does not establish decimal SQL arithmetic.

Cover exact dispatch, independent mixed converters, NULL semantics, cast/composition boundaries and
sync/async supplied-reader materialization. Root owns package-only consumers on both selected EF patches.
Native execution/typing/precision/domain errors remain live gates. Detailed brief/evidence:
artifacts/local-development/batch3m/. Local source/patch/package gates and independent source review pass;
broader local work remains open.

## Batch3n brief: numeric aggregates

Implement native SUM/AVG/MIN/MAX through a derived aggregate translator provider, preserving plugin
priority. Own exact Sum/Average numeric/nullable selector identities and generic numeric Min/Max across
the existing eleven primitive numeric families. Keep filtered CASE/DISTINCT ordering, inherited cardinality
shapers and current Count/LongCount behavior. Invalid owned mappings fail explicitly rather than falling
through to EF's default aggregate translator.

AVG retains the mapped input without per-row conversion. Model int/long Average uses an independent
converter-free Double result; other aggregates retain the source converter/result convention. Native SUM
keeps its mapping so EF's COALESCE uses converted model zero. Normalize final Single-provider results
outside that SUM/COALESCE with fixed FLOAT/BINARY_FLOAT targets, including physically wider declarations.
Narrowly widen the existing private marker to numeric aggregate model types; preserve nonnumeric rejection.
Reuse the result-only decimal reader without SQL decimal narrowing or physical write-bound clipping.

Baseline package SQL and consumer RED reproduce integer AVG pre-casts and wide-declaration Single result
casts. Unit/pipeline tests will cover both modes/null settings, selected numeric/nullable families, mixed
converters, empty/null shaping, filtering/distinct, current parameters and checked reads. Native execution/
metadata/accumulation/rounding remain live gates. Detailed brief/provenance/evidence:
artifacts/local-development/batch3n/. Local source/patch/package gates and independent source review pass.
The overall local EF10 goal remains active.

## Batch3o brief: scalar numeric extrema

Translate exact Math/MathF/decimal Min/Max and numeric EF.Functions Least/Greatest through the relational
SQL visitor. Preserve nested calls, existing operand mappings, operand order and the first result converter.
Use native LEAST/GREATEST for two or more arguments and identity for one. Both row-dependent inline
arguments and extracted numeric constant arrays are supported. Snapshot these arrays in the ordinary query
cache key so a caller can change a capture between executions without reusing old SQL. Do not promise
refresh within an explicit compiled delegate or concurrent mutation during query compilation.

Reuse the fixed Single result cast, result-only decimal reader and checked numeric mappings. Native
NULL behavior and Oracle first-argument coercion remain server semantics; no CLR floating-point parity
claim. Baseline package SQL probe confirms the missing translations and both EF array shapes. Brief and
evidence: artifacts/local-development/batch3o/.

## Batch3p brief: exact decimal Round

Add all eight exact decimal `Math.Round` and `decimal.Round` overloads with constant digits and
midpoint rules. Use the existing method translator provider, exact decimal truncation and remainder
tests, and a result-only decimal reader that preserves the input mapping and converter. Exercise
both modes, ties, directed modes, 0 and 28 digits, invalid/runtime arguments and command shaping.
Floating point overloads and runtime argument semantics remain separate local work.
Batch brief and local evidence: `artifacts/local-development/batch3p/`.

## Batch3q brief: binary floating Round

Add the four exact Round signatures on each of Math (Double), MathF (Single), Double and Single.
Use matching binary mappings, constant rule/digit validation, .NET scaling limits and integral guards,
with neutral Single casts at binary precision boundaries. Preserve the original result reader and
converter. See `artifacts/local-development/batch3q/` for decompiled runtime reference and local
evidence. Runtime arguments and actual engine calculation remain separate acceptance work.

## Batch3r brief: Gregorian calendar members

QRY-07/QRY-11: extend the existing temporal member translator with `DateTime` and `DateOnly`
`DayOfYear` and `DayOfWeek`. Reuse the native carrier checks and leave unrelated date/time members
outside this slice. Pin the per-mode SQL and nullable/converted behavior in `TemporalMemberQueryTests`,
exercise enum projection and filtering through `BasicRelationalTests`, then run the full suite and the
packed consumer. Review the mappings, SQL and live limitations before committing.

## Batch3s brief: raw SQL command boundaries

QRY-10/DIA-01: characterize inherited FromSqlRaw/FromSqlInterpolated and
ExecuteSqlRaw/ExecuteSqlInterpolated through the real EF command builder and connector parameters.
Add sync/async, both-mode tests to BasicRelationalTests with hostile text, NULL, explicit connector
parameters, repeat execution and composed query materialization. Fix only proven provider defects.
Raw SQL identifiers/text remain caller-owned; interpolating identifiers is not supported. Exercise
packed SQL translation, run complete suites, review, update QRY-10 evidence and commit. Native SQL
execution, server parameter metadata and database effects remain live gates.

The initial command tests expose CFG-04/SCH-01's missing database creator. Implement its read-only
connection/Exists/HasTables service using IRelationalConnection and IRawSqlCommandBuilder, with
MySQL unknown-database classification and propagated Oracle/auth/permission/network errors from Exists.
CanConnect follows EF's boolean probe contract while preserving cancellation. DDL methods explicitly
reject before opening a connection until Batch 7 supplies the schema creation/ownership contract;
SCH-02/03 are still Required and Planned. Add facade and cancellation/error tests before registration.

## Batch3t brief: MySQL positional command boundary

QRY-01/03/06/07/10, UPD-01/02/03, CFG-04: keep EF's named SQL templates, but supply a
provider relational command builder/factory that converts the final MySQL DbCommand to positional
SQL and orders bound values by marker occurrence. Oracle commands remain named. Preserve EF's
original LogCommandText, command timeout/transaction, cancellation, interceptors and reader cleanup.
Parse quotes/comments without treating marker-like text as binds; reject ambiguous SQL_MODE-dependent
backslash quoting, executable comments with binds, missing/unused/mixed binds and repeated streams
before execution. Recreate repeated mapped parameters through EF rather than copying native facets.
Connector 0.2.0 has no public parameter clone; repeated caller-owned native parameters require an
explicit supported duplication contract, not inference from Scale's ambiguous zero getter.

1. Add focused failing tests for order/repetition, quote/comment skipping and malformed/ambiguous SQL.
2. Implement parser and command factory in Storage; register the factory before core services.
3. Add real EF query/raw/write tests asserting final positional command text and ordered values;
   verify the connector 0.2.0 binding contract directly offline. Update changed actual-command assertions.
4. Run full Debug/Release, isolated EF10.0.14 Release and packed consumers; independent source review,
   update remaining boundaries, then commit. No database, EF8/EF9 or publication work.

## Batch3u brief: integer bitwise operators

QRY-01: implement `&`, `|`, `^`, `~` for all eight mapped integer families, actual C# promotions,
nullable operands and numeric enum storage in the existing SQL generator. Preserve EF's Boolean
AND/OR/NOT and null processing. Integer numeric converters retain stored-space operations, as in the
arithmetic contract; reject text/floating/decimal carriers instead of emitting native integer SQL.

- MySQL: native binary operators with signed-result casts; unsigned complement is masked to its CLR
  width below64 bits. Fully group operands and results independently of provider-neutral precedence.
- Oracle: BITAND for AND; NUMBER arithmetic identities for OR/XOR and complement. Split UInt64 AND
  into two32-bit halves: do not rely on Oracle-compatible128-bit BITAND. The audited public server
  source uses signed64-bit conversion, independently of the wider NUMBER storage range.
- Write BitwiseQueryTests first, observe missing/incorrect SQL, then implement at the shared generator.
  Exercise all operators in actual predicates and projections, promoted small integers, negative/extreme
  literals, enum/numeric converters, nullable and nested expressions, both null settings and mode order.
- Add intercepted sync/async command/reader coverage and package-consumer assertions. Run full baseline
  Debug/Release and isolated10.0.14 Release, package content/identity/consumer gates, independent review,
  update design/checklist/progress and commit locally. No database or standalone test-program work.
- Shifts require count masking and signed-width rules and remain a separate local slice. Native results,
  physical BITAND range, SQL coercion and repeated volatile operand evaluation remain explicit live/
  semantic boundaries; this batch does not finish QRY-01 or the overall goal.

## Batch3v brief: integer shifts

QRY-01: implement LINQ `<<` and `>>` for all eight integer property types after C# promotion,
nullable values/counts, captured counts and integer storage converters. Count uses its own Int32
mapping independently of the left result mapping; mask it with31 (Int32/UInt32) or63 (Int64/UInt64),
including negative and oversized counts. Reject non-integer storage carriers for either operand.

- EF10 SqlBinaryExpression does not accept shifts. Translate the original binary operands to two
  strict internal function markers in the existing relational visitor and render them in a focused
  QuerySqlGenerator partial file. Retain EF function null propagation and the left result mapping.
- MySQL: native unsigned64 shifts; truncate left32-bit results and normalize signed results. Signed
  right shift uses complemented-negative/native-logical shifts to preserve sign extension exactly.
- Oracle: right shift uses FLOOR(value / POWER(2, maskedCount)); left shift first reduces modulo
  2^(width-count), normalizes the negative remainder, then multiplies by2^count. Rotate the completed
  unsigned result around2^(width-1) to restore signed results. Keep intermediates below2^65; do not
  multiply a full64-bit operand by2^63 and rely on NUMBER rounding. POWER uses numeric NUMBER literals.
- Add ShiftQueryTests first, reproduce missing predicate translation, then implement. Cover all promoted
  result families, count boundaries/current parameters, signed extremes, nested operands, nullable/null
  settings, carrier rejection and unchanged ordinary arithmetic/bitwise/Boolean paths. Add intercepted
  sync/async readers/binds plus package consumer assertions. Run full baseline Debug/Release and isolated
  EF10.0.14 Release, same-package consumers/content/identity gates, independent review, docs and local commit.
- Native execution/coercion and repeated volatile operand evaluation remain unverified. Investigate the
  C# expression-tree boundary for `>>>`; do not advertise a translation the compiler cannot represent.
  No live DB, standalone testing application, EF8/EF9, push or publication work.

## Batch3w brief: Boolean exclusive OR

QRY-01: translate bool/bool? XOR in predicates, scalar projections and nested expressions in both
modes. Retain the ExclusiveOr node through EF null processing: rewriting to nullable NotEqual early
would introduce incorrect null compensation. Map each operand independently and use the default
Boolean result mapping, never a converted operand's storage mapping.

- Normalize each operand using its mapped true/false literals in a NULL-preserving CASE, then compare
  the normalized values with <> at SQL generation time. Oracle classifies Boolean XOR as a search
  condition and uses its existing three-valued CASE conversion for scalar contexts.
- Cover native and mixed numeric/text Boolean converters, nullable captures/transitions, both null
  settings, predicates/projections/NOT/CASE/nesting and integer XOR controls. Reject null-converting,
  nonnumeric/nontext carriers and null/equal (Oracle empty) true/false representations.
- Add failing query regressions before implementation, sync/async intercepted readers and packed
  consumers; run baseline Debug/Release and isolated14, independent review, update docs and commit.
  Repeated operand evaluation and native collation/coercion/results remain explicit boundaries.
  No standalone live-test application, database access, EF8/EF9, push or publication work.

## Batch3x brief: converted Boolean comparisons and logical operations

QRY-01: complete ==/!=, NOT, AND/OR (including lifted &/|), Boolean conditions and conditional
tests when operands use independent numeric/text encodings. Numeric/text storage equality is not
logical Boolean equality; MySQL also cannot use a Y/N column directly as a truth condition.

- Normalize noncanonical Boolean scalar operands to native 0/1/NULL before EF null processing, using
  a strict internal single-argument function rendered with the existing CASE normalization. Canonical
  numeric 1/0 operands keep their ordinary SQL. Preserve nullable equality compensation, lifted &/|
  truth tables and each operand's original mapping/converter. Results use the native Boolean mapping.
- Normalize predicate and conditional-test roots during translation. Oracle scalar predicate CASE
  output uses native Boolean constants independently of global DefaultTypeMapping<bool> converters.
  Existing XOR and integer bitwise paths must remain covered. No public function/API is added.
- Tests first: mixed/inverted/native/nullable fields, captures and global Boolean defaults, predicates/
  projections/NOT/CASE/nesting, both null settings, real binds/readers and negative storage cases.
  Run full Debug/Release/isolated14, same-package consumers and independent review, docs and commit.
  Keep valid encodings/native collation/coercion and repeated volatile evaluation as explicit boundaries.
  Broader global Boolean defaults on unrelated translator families remain separately audited local work.

## Batch3y brief: Boolean equality methods and nullable defaults

QRY-01/QRY-11: bool.Equals(bool/object), Boolean/nullable operands of object.Equals(object,object),
Nullable<bool>.Equals(object) and Nullable.Equals<bool> must use the same independently normalized
Boolean comparison as ==. Remove only boxing conversions; other types/custom methods keep inherited
translation or unsupported behavior. Preserve the configured equality null semantics.

- Add both Nullable<bool>.GetValueOrDefault overloads using normalized operands and native Boolean
  COALESCE result mapping; the no-argument overload supplies native false. EF's numeric-only default
  translator currently does not handle Boolean. Retain .Value converters; .HasValue tests storage NULL.
- Match exact framework methods, not names. Leave genuinely dynamic object operands to existing EF
  inference; do not reinterpret arbitrary converted objects as Boolean. Normalization must precede
  inherited equality/null processing, preserve current captured values and avoid new SQL functions.
- Tests first, mixed numeric/text/inverted/nullable fields, constants/boxing/null/custom controls,
  both null settings, global defaults and sync/async readers. Full baseline Debug/Release/isolated14,
  package consumers/content/identity, independent review, docs and local commit remain required.
  Boolean comparison/string/conversion methods beyond these exact identities remain further local work;
  no live testing application, database access, EF8/EF9, push or publication work.

## Batch3z brief: Boolean ordering and string representations

QRY-01/QRY-11: exact bool.CompareTo(bool/object) with statically Boolean arguments normalizes each
operand and compares native 0/1 carriers in a signed CASE to produce -1/0/1. This avoids comparing converter storage
order (including inverted encodings) and preserves SQL NULL propagation from nullable.Value inputs.
An object-null argument yields1 for non-null receivers; dynamic/wrong-type object arguments cannot
gain translation. Exact bool.ToString(), bool.ToString(IFormatProvider), nullable Boolean ToString
and statically Boolean boxed object.ToString output True/False independently of stored encodings.
The framework ignores the format provider; no general formatting API is added. Nullable ToString
returns empty on NULL in MySQL; Oracle represents empty as NULL under its existing string contract.

- Reuse BooleanOperand/native mappings and the existing visitor; no new public API/function/dependency.
- Tests first for native/mixed/inverted/nullable/boxed/current captures, both null settings, global
  defaults and native int/string readers. Preserve client projection and inherited custom identities.
- Full baseline Debug/Release, isolated14 and same-package both-mode/patch/content/graph/hash checks,
  independent review, docs and local commit. Parsing/TryParse/Convert overloads remain local audit work;
  no native DB/test application, EF8/9, push or publication in this batch.

## Execution record

- Batch7e final after82dd469: typed/untyped explicit management factories support independent MySQL
  administration credentials/hooks and supplied targets without cloning native private state. Exact native
  instance is EF-owned; mistaken target returns preserve target ownership. Existing scope/transaction/
  model/default-path guards remain. Options clones retain factories without service identity/log exposure.
  Four missing-API RED cases precede implementation. Final Debug/Release/isolated14 each3520=
  1656+994+868+2 pass, zero failures/skips; focused management96/options15 pass. Independent review
  reports no actionable findings, with dedicated already-open/invalid-candidate-disposal-failure cases
  still absent. Same management-profile-20261111 package passes both modes/patches/content/graphs/
  DLL identity (0440D389798E84374260CF8A030F2A20975CBC2C4B34D6E6E0BA9C63018A9E4A).
  Evidence: artifacts/management-profile-*.log and verify-management-profile-package.ps1.
  Oracle namespace lifecycle/password-DDL contract remains the next implementation. No live evidence,
  demonstrated connector/server defect, EF8/9, standalone live program, push or publication. Paused;
  finish the local checkpoint commit and await the user's resume request.
- Batch9c final aftere1ef05e: MySQL cycles/dependent joins and derived joined sources use complete
  encoded setter/key projections under NO_MERGE, joined by every original physical key to the real
  target. Single-table acyclic ordering remains. Both modes accept normalized left joins directly.
  Four initial MySQL RED cases and later left-join/paging regressions identify the missing branches.
  Fourteen new bulk+two transaction+one SQL cases cover bind order/current values/write bounds,
  Boolean NULL encodings, keys/paging/aliases, failures/cancellation/zero/tracker and transaction
  ownership. Final Debug/Release/isolated14 each3480=1653+957+868+2 pass, zero failures/skips.
  Source review reports no actionable findings; README's stale unsupported text is corrected.
  Same bulk-snapshot-20261111 package passes both modes/patches/content/graphs/hash; prior Oracle
  package fails the cyclic smoke with caught exit1. Baseline staysEF10.0.0. Native optimizer/read
  consistency/duplicate-match/results/costs remain live gates, not demonstrated driver/server defects.
  Broader converters/model/JSON bulk variants and the remaining local inventory stay open. No DB,
  standalone live program, EF8/9, push or publication. Overall goal active.
- Batch9b final aftereb0c837: Oracle joined/paged updates now use complete scalar source queries and
  EXISTS correlated by every physical primary-key column. Normalized left joins bypass EF's
  UPDATE/FROM fallback; native single-table behavior remains.26 intercepted, eight SQL and two
  caller-transaction cases cover keys/filters/paging/aliases, both join sides/nullable targets,
  nullable Boolean encoding, errors/cancellation and unchanged tracker/transaction ownership.
  Initial16 failed; focused76 functional+eight SQL pass. Full Debug/Release/isolated14 each
  3463=1652+941+868+2 pass, zero failures/skips. Independent review reports no production findings;
  four extra cases close its nullable-target/Boolean/selective-filter coverage suggestions.
  Same bulk-oracle-20261111 nupkg passes both modes/patches/content/graphs/DLL identity; prior
  bulk-dml package returns caught NotSupportedException/exit1. Baseline staysEF10.0.0. Native
  syntax/read consistency/cardinality/costs remain live gates. Next: MySQL snapshot assignments
  and the remaining local inventory. No driver/server defect, live tests, EF8/9, push or publication.
- Batch3z final:16 added unit/16 intercepted cases; full Debug/Release/isolated14 each
  2396=1638+756+2 pass without failures/skips. Same boolean-order-text-20261111 nupkg passes both
  modes/patches/content/package-only graphs and DLL identity; prior package rejects ToString predicate.
  Baseline assets remain10.0.0. Independent fix review approves; native execution and broader inventory
  remain open. User reports Consumer crash popups from old-package negative probes: catch managed
  failures at Main, stderr+exit1, preserve success0. Explicit old-package exit1/no-unhandled assertion
  and both-patch/both-mode positive reruns pass without rebuilding/changing the provider package.
  Document manual PackageConsumer versus CI archive checks and ordinary ConsumerTests; CI is unchanged.
- Batch3z fix round1 ruling: independent review finds canonical unsigned0/1 subtraction can underflow
  in MySQL and boxed nullable object-null must compare below a Boolean. Six RED regressions reproduce
  unsafe subtraction SQL and NULL instead of1 for a null capture. Use signed CASE constants with
  leading typed null guards, then normalized equality/order. A NULL receiver or typed.Value argument
  propagates NULL; object-overload NULL argument returns1. Later EF equality compensation may still
  appear, but preceding null branches determine the result; assert their order instead of banning
  compensation text. No connector/server defect asserted without native execution.
- Batch3y final:18 added unit/16 intercepted cases; full Debug/Release/isolated EF10.0.14 each
 2364=1622+740+2 pass with no failures/skips. Same baseline-built boolean-method-20261111 nupkg passes
 both modes/patches/content/package-only graphs and DLL identity; baseline assets remain10.0.0.
 Previous Boolean package fails the new method-normalization consumer. Independent source and final
 docs/consumer review approve; one stale log pointer corrected. No connector/server defect established.
 Native execution/coercion/collation/volatile single evaluation, wider Boolean APIs and remaining
 Required/deferred inventory stay open. Overall goal active; no live tests, EF8/9, push or publication.
- Batch3y rulings: .HasValue RED2 exposes inherited converter compensation changing nullable storage
  into a truth comparison under unary IS NOT NULL, then erasing the filter. Emit binary != typed NULL
  until that pass completes; EF's normal null processor restores the direct storage null test.
  Nullable instance Equals arrives as System.Object.Equals(object), so include that exact identity
  with statically Boolean operands. The first false-predicate MySQL assertion was over-specific:
  WHERE0 is valid there; Oracle keeps WHERE0=1. Actual custom Equals RED2 proves inherited name-based
  translation; preserve it as the brief requires, characterize absence of new normalization, and
  explicitly document that arbitrary method bodies are not interpreted. Keep CustomEquals predicate
  rejection/top-level client controls separately. No DbFunction/plugin API is intercepted or disabled.
- Batch3x after4ce40a8: independently converted Boolean comparisons/logical operators, predicate/order
  roots, ternary and coalesce normalize to native Boolean carriers before EF null processing. Oracle
  scalar predicate CASE uses native constants even with global Boolean defaults. Initial RED18/20;
  focused24 new unit+18 new functional cases pass, with existing XOR controls. Review fix round1
  resolves exact decimal canonical classification and global-default native-predicate misclassification;
  near-one mutation RED2/global predicate RED1 and expanded global reader checks cover both. Scoped
  re-review reports no remaining actionable findings. Final Debug/Release/isolated14 each2330=1604+724+2,
  zero failures/skips. Same boolean-20261111 package passes both modes/patches/content/package-only
  graphs and DLL identity; prior Boolean-XOR package fails logical normalization. Baseline assets
  stay10.0.0. Native results/coercion/collation and repeated volatile evaluation remain open, as do
  additional Boolean methods/translator families and broader inventory. Evidence: artifacts/boolean-*.log,
  boolean-package/ and boolean-compat/. Overall goal active; no connector/server defect established.
- Batch3w after e47892c: bool/bool? XOR retains EF null propagation, independently mapped operand
  normalization and native result mapping; Oracle uses existing three-valued search-condition conversion.
  Initial query RED16, null-carrier RED2 and review-found global-default-converter reader RED4 were
  corrected. Focused16 unit+12 intercepted and full Debug/Release/isolated14 each2288=1580+706+2 pass,
  zero failures/skips. Scoped independent re-review approves without remaining important findings.
  Same baseline-built boolean-xor-20261111-r1 package passes both modes/patches, content, package-only
  graphs and DLL identity; previous shift package fails the new normalization assertion. Baseline
  assets stay10.0.0. EF may simplify XOR IS NULL to operand null tests; preserve that valid optimization.
  Model-free result mapping prevents global Y/N defaults from reading numeric SQL results as text.
  Native results/collation/coercion, repeated volatile evaluation and broader converted Boolean
  comparisons remain open. Evidence: artifacts/boolean-xor-*.log, boolean-xor-package/ and
  boolean-xor-compat/. Overall goal active; no standalone live program, EF8/EF9, push or publication.
- Batch3v after e52a4d3: LINQ shifts via strict internal functions, original promotions/left result
  mapping and independent Int32 count mapping. MySQL native shifts normalize width/sign; Oracle
  NUMBER formulas reduce before multiplying. Initial missing-translation RED32; exact signed32 CAST
  grouping RED1 corrected. Focused35 unit+8 intercepted and full Debug/Release/isolated14 each
  2260=1564+694+2 pass, zero failures/skips. Independent source/test/consumer review approves.
  Same baseline-built shift-20261111 package passes both modes/patches, content, package-only graphs
  and Release/package/consumer SHA equality; prior bitwise package fails the new shift predicate.
  Baseline assets stay10.0.0. Package evidence: artifacts/shift-package-final.log and shift-package/.
  Ruling: retain EF's NULL argument within the marker/formula rather than require whole-projection
  constant folding — actual cached-count transitions inline NULL, remove binds and preserve nullable
  reading; no production change was required for the initial over-specific SELECT NULL assertion.
  Ruling: C# expression trees reject >>> with CS7053 in the .NET10 compiler probe. Record this language
  boundary without inventing a connector/server limitation or adding an unsolicited public API.
  Source audit: Oracle ObNumber POWER uses integer-exponent repeated squaring, not the double/exp-ln
  path for these counts; actual native evaluation remains a live gate. Evidence: artifacts/shift-*.log,
  shift-research/ and shift-compat/. Overall goal active; Boolean XOR and broader inventory remain local.
- Batch3u after42e45a8: integer AND/OR/XOR/complement, MySQL signed/unsigned width normalization,
  Oracle signed64-safe UInt64 halves, numeric enum complement and original binary promotions. Initial
  RED14/18; promotion RED2 and unrelated-cast-control RED2 corrected. Focused22 unit+8 intercepted pass;
  full Debug/Release and isolated14 each2217=1529+686+2, zero failures/skips. Independent source review
  approves. Same baseline-built bitwise-20261111 package passes both modes/patches, content, package-only
  graphs and Release/package/consumer SHA equality; prior transaction-recovery package fails the new
  signed-result consumer assertion. Baseline assets remain10.0.0. Evidence: artifacts/bitwise-*.log,
  bitwise-research/, bitwise-compat/ and bitwise-package/.
  Ruling: preserve integer operand promotions in the existing relational visitor — EF removes them
  before factory inference, causing wrong narrow result mapping for e.g. byte OR256 and byte XOR-1.
  Ruling: legal MySQL SIGNED/UNSIGNED integer CAST spelling also applies outside bitwise; ordinary
  explicit converted-property casts remain allowed. Prior collection tests pinned illegal BIGINT/INT
  CAST targets; replace their SQL spellings while retaining both cast nodes and all identity assertions.
  Narrowing/overflow/signedness semantics remain separate; source review approves the test correction.
  No database, connector defect, standalone program, EF8/EF9 or publication claim. Overall goal active.
- Batch7a after8a4ad6a: offline GenerateCreateScript and27 schema fixtures. Full Debug/Release/isolated14
  final2 each2077=1501+574+2 pass, zero failures/skips. Same schema-script-20261111 package passes both
  modes/patches/content/package-only graphs and SHA-256 equality; preceding transactions package fails at
  expected unsupported script boundary. Baseline assets remain10.0.0. Independent review's computed-column
  collation omission and TPT/split duplicate-identity findings are fixed with RED/GREEN cases; re-review approves.
  Evidence: artifacts/schema-script-*.log, schema-script-compat/, schema-script-package/. Mutating namespace
  ownership/lifecycle, native DDL and full runtime relationship/generation evidence remain open. Next: Batch7b.
- Batch7b after ed11c8b: explicit existing-schema EnsureCreated/CreateTables and immutable target options.
  Initial missing-API/behavior RED, then eight scope/cleanup RED failures; final46 lifecycle+six option cases pass.
  Full Debug/Release/isolated14 each2129=1507+620+2 pass, zero failures/skips; later async-seeder coverage gets
  covering reruns. Same schema-lifecycle-20261111 package passes content, both modes/patches, package-only graph
  and verified Release/package/consumer SHA equality; source assets stay10.0.0. Independent source review
  approves without blocking findings. Evidence: artifacts/schema-lifecycle-*.log, -compat/, -package/.
  Ruling: reject explicit cross-scope FK principals even when excluded from migrations — table creation adds
  constraints referencing them, so they belong to the declared target boundary; supporting cross-scope references
  later needs a deliberate contract. Trusted SQL model expressions/interceptors remain application-owned.
  Minor (deferred): explicit-enlistment and stateful physical-session/caller-opened lifecycle fixtures; active EF
  and both seeding callback variants are now covered. Physical/native/live DDL and namespace deletion remain open.
- Batch7b committed as16b1d92 after source/consumer/docs review and baseline/patch/package gates; no push.
- Batch6c after16b1d92: registered batch executor and transaction-start failure cleanup. Initial RED2 cancellation,
  broader RED8, sync RED6, registered-start RED4 and review Started-callback RED4; final70 transaction cases pass.
  Debug/Release/isolated14 each2161=1507+652+2 pass, zero failures/skips. Same transaction-recovery-20261111 package
  passes both modes/patches, content, package-only graph and Release/package/consumer SHA equality; preceding
  schema-lifecycle package fails the new recovery smoke. Source assets stay10.0.0. Independent source/fix review
  approves. Evidence: artifacts/transaction-recovery-*.log, -compat/, -package/.
  Ruling: specialize both EF10 execution loops — sync REDs prove masking/outer-lease defects too; no protected
  recovery seam exists. This adds a version-local maintenance obligation across EF upgrades.
  Ruling: capture the native start candidate through callbacks/factory — account restoration alone cannot clean
  a Started-callback failure on a caller-opened session. Replacing interceptors retain EF's original ownership.
  Native disposal/diagnostic failure can still leave a disposed wrapper installed/lease retained; discard unusable
  context/transaction state and explicitly clean native resources. No physical rollback guarantee.
  Minor (deferred): transaction conflicts are checked before opening; repeating the guard after opening would
  additionally cover unsupported transaction-state mutation from reentrant connection interception.
- Batch6c committed as9d1f4d2 after source/fix/consumer/docs reviews and complete source/patch/package gates.
- Batch5e after9d1f4d2:26 GeneratedValuesTests characterize composite-key single-identity multi-column default/
  computed/concurrency outputs, explicit default overrides, NULLs, conversion/zero-row/missing-row failure state.
  No provider defect was reproduced and no production change is needed. Initial test failures were fixture
  parsing, EntityEntry-wrapper equality and named-parameter ordering assumptions, all corrected.
  Independent review found Oracle output values followed parameter collection order instead of INTO binds.
  A temporary reversed-bind mutation passed26 with the old fixture and fails8 after named-bind/uniqueness/set
  validation; restored production source passes26. Scoped re-review approves without open findings.
  Full final Debug/Release/isolated14 each2187=1507+678+2 pass, zero failures/skips. No new package required for
  test/docs-only changes; previous package evidence stays historical. Rebuilt provider informational-version
  commit metadata changes from16b1d92 to9d1f4d2, so the new binary SHA is not asserted unchanged. Baseline assets
  stay10.0.0. Evidence: artifacts/generated-values-*-final.log and -mutant-before/after.log. No live/atomicity claim.

- Batch6b after456902f: TransactionTests adds38 cases for both-mode sync/async transaction ownership,
  isolation/commit/rollback, different-connection rejection, savepoints, multi-batch failures/retry and MySQL
  generated readback enlistment/failure. Four corrected RED cases reproduce Oracle RELEASE SAVEPOINT SQL;
  the registered transaction factory now treats Oracle release as a logical no-op with cancellation and
  diagnostics/interceptors, leaving other transaction behavior inherited. Focused38 and full Debug/Release/
  isolated EF10.0.14 each2050=1474+574+2 pass, zero failures/skips. Same baseline-built transactions-20261111
  package passes content/both-mode consumers at both patches, package-only graph and identical provider hashes.
  Baseline assets stay10.0.0. The preceding graph-update package fails the new Oracle smoke when base
  release tries to create a native command with the synthetic borrowed transaction, confirming the no-command
  boundary (not a server failure). Independent read-only review approves without findings. Evidence:
  artifacts/transactions-*.log, artifacts/transactions-compat/, artifacts/transactions-package/.
  No native/server rollback, wire cancellation, live DDL or standalone live program claim. Required TXN rows
  remain partial for native state/recovery/cancellation and live acceptance. EF8/EF9, push/publication excluded.

- Batch5b: CommandExecutionBoundaryTests adds eight both-mode timeout/cancellation/retry cases. `SetCommandTimeout(17)`
  reaches the intercepted command; sync/async in-flight cancellation preserves the original exception and token,
  async receives the caller token, and a failed query can execute again with the EF-visible connection state closed
  between calls. Focused Release passes8/8; no production change. Final Debug/Release/isolated14 each pass
  1994=1474+518+2, zero failures/skips. Independent review found and closed sync-retry coverage and physical
  lifecycle overclaiming; evidence now stays at EF-visible/interceptor boundaries. No package rebuild was needed.
  Connector wire cancellation, server timeout enforcement, transactions and logging remain open.

- Batch5c: CommandLoggingTests adds 16 both-mode sync/async success/failure cases. Runtime-captured parameter
  values are absent from default EF command/error logs and appear only with EnableSensitiveDataLogging; command
  SQL and synthetic failure text are reported. Initial RED exposed compile-time constant inlining, corrected by
  using captured variables. Focused Release passes16/16; final Debug/Release/isolated14 each pass2010=1474+534+2,
  zero failures/skips. Independent review found and closed async callback/event-text assertions; no production
  change or package rebuild is needed. This is EF logging/interceptor evidence only; connector/server logging
  and live policy stay open.

- Batch5d: BasicRelationalTests adds two MySQL sync/async cases for a composite key with two generated identity
  columns. Both reject before command interception with the documented single-identity readback limitation.
  Focused Release passes2/2; full Debug/Release and isolated EF10.0.14 Release each pass2012=1474+536+2,
  zero failures/skips. Independent read-only review found no code/test defects; corrected its stale suite-status
  documentation finding. Evidence: artifacts/generated-readback-{debug,release,patch}-final.log. No production or
  package change; Oracle multi-output and live generation remain open.

- Batch5a after6714626: graph dependency ordering/key propagation/reverse deletes, composite NULL/non-NULL
  original tokens and current NULL writes, zero-row conflict entries, synthetic connector and in-flight
  cancellation identity have24 focused cases. Original16 tests reproduced four MySQL unused NULL predicate
  bindings; omit only those binds in the shared batch producer, retaining NULL write inputs. Full Debug,
  Release and isolated14 each1986=1474+510+2, zero failures/skips. Same graph-update-20261111 nupkg passes
  content and both-mode/patch consumers; prior lifecycle package fails new NULL-token smoke as expected.
  Package/baseline/consumer DLL identities and package-only dependency graphs pass; baseline assets stay10.0.0.
  Logs artifacts/graph-update-*.log; isolated build/props artifacts/graph-update-compat/; packed consumers
  artifacts/graph-update-package/. Independent review found and closed one async token-count assertion gap
  with RED/GREEN focused evidence; no production findings remain. Transactions are disabled in this command
  fixture; no atomicity/live claim. Wider graph/readback, logging, transaction/schema/specification and
  remaining local inventory stay open. No EF8/EF9, database connection, push or publication.

- Batch6a: shared provider failed-open snapshot/rollback fixes the documented opening-callback and ambient
  warning phases, preserves outer/caller leases and retries, and closes the entry native instance rather
  than a callback replacement. Original cancellation survives a native close failure via attached cleanup
  evidence. Two private EF10 fields use isolated UnsafeAccessor contracts, reviewed against exact EF10
  behavior and tested on both selected patches. RED8 registered-provider failures, focused GREEN36; review
  callback-target mutation RED2 then GREEN. Final Debug/Release/isolated14 each1962=1474+486+2, no failures
  or skips. Same baseline-built connection-lifecycle-final-20261111 package passes content gate and both
  mode/patch consumers, including the packed failed-open probe. Logs artifacts/connection-lifecycle-*.log;
  isolated props/build artifacts/connection-lifecycle-compat/; package artifacts/connection-lifecycle-package/.
  Independent final source/test/docs/evidence review approves, zero findings. Native close failure remains
  best effort with explicit direct-cleanup boundary. The wider local inventory,
  transaction semantics and live acceptance remain open; no EF8/EF9, push or publication.

- Batch4a/4b/4c: 58 model/materialization/filter characterization cases pass without a production
  change. Both-mode metadata, composite/alternate relationships, owned/shadow reads, Include/ThenInclude,
  tracking/fixup/identity resolution, cardinality/limits and named-filter disable/current binding are covered.
  Full Debug/Release and isolated EF10.0.14 Release each pass 1926 (1438+486+2), zero failures/skips.
  Independent review identified incomplete joined-column and filter predicate/binding assertions; both
  are corrected and scoped re-review approves, zero findings. Final covering 58 pass in all three
  configurations. Logs: artifacts/model-materialization-*.log; isolated build/props:
  artifacts/model-materialization-compat/. Baseline assets stay10.0.0. No production change, repack or
  fresh hosted/live claim. Corrected stale SCH-01 creator-absent status; its cleanup/live gates stay open.
  Next: remaining model/generation validation and unnamed/graph filters, then write/diagnostic/transaction
  inventory. Finishing these local slices does not finish the full goal; EF8/EF9/live work stays postponed.

- Batch3s: four raw query cases passed initially; twelve execute cases exposed missing creator DI.
  After registration, prefixed raw parameters exposed doubled SQL prefixes and Oracle scalar tests
  exposed a connector-number conversion gap. Both fixed. Final Debug/Release/isolated EF10.0.14 Release
  1840 each, no failures/skips; same-package consumers and contents pass. Independent review fixed
  Oracle metadata scope. Connector v0.2.0 review identified MySQL positional binding as the next
  local priority, spanning generated queries, raw SQL and tracked writes. DDL/live remain open.

- Batch3t: positional MySQL command factory/parser covers query, raw, tracked write and generated-key
  order/repetition, quote/comment rejection, mixed/missing binds and connector client-prepared fallback.
  Initial focused run: 11 failing/two passing cases. Final Debug/Release/isolated EF10.0.14 Release 1868 each, zero failures/skips;
  package content and EF10.0.0/10.0.14 consumers pass. Caller-owned repeated streams remain explicitly unsupported.
  Independent review fixes have RED/GREEN regressions for reader open balancing and initialization interceptors.

- Batch3r extends the existing temporal translator with four Gregorian calendar members. Twelve RED
  translation cases; final Debug/Release/isolated EF10.0.14 Release 1792 each, zero failures/skips.
  Package `calendar-final-20261111` passes both modes/patches and package-content verification.
  Independent source review findings addressed; engine arithmetic/result metadata remain live gates.

- Batch3q adds 16 floating Round signatures with constant syntax arguments, precision guards and
  Single normalization. Debug, Release and isolated EF Core 10.0.14 each pass 1780 tests. The same
  `floating-round-20261111` package passes both modes and both tested EF patches. Runtime digits and
  rules remain local work; native precision, casts and computation remain live gates.

- Batch3p translates eight exact decimal Round overloads, supports five constant rules and digits
  0..28, and keeps input mappings and checked decimal result shaping. Debug, Release and isolated
  EF Core 10.0.14 each pass 1768; the same `decimal-round-20261111` package passes both modes and
  patches, including Round predicates that the previous package rejects. Native decimal result
  behavior remains a live gate; floating point Round and runtime arguments remain local work.

- Batch3o adds scalar numeric extrema translation, preserves nested operands and result mapping, and
  snapshots extracted numeric extrema arrays for ordinary query cache keys. Focused tests pass 43 unit and
  six intercepted cases, including same-hash mutation and reversion. Debug, Release and isolated 10.0.14
  Release each pass 1760 tests (1400 unit, 358 intercepted, two project consumers); the same baseline-built
  `extrema-20261111` package passes both modes and EF patches. Content, graph, DLL and baseline asset checks
  pass. Independent source review passed. Native computation remains a live gate. See `artifacts/local-development/batch3o/`.

- Batch3n native numeric aggregates: initial8 native-AVG/wide-Single RED failures and a focused Oracle
  synthetic-zero literal RED become279 numeric unit/16 intercepted passes. New aggregate service preserves
  plugins/Count/LongCount, native AVG inputs, independent promoted Double, mapped SUM zero and result
  converters; a narrow marker allowance retains EF's Int32-typed coalesce. Oracle Single zero literals
  normalize safely without InfinityF. Final Debug/Release and isolated14 Release1711=1357+352+2 pass, no
  skips. Same baseline-built aggregates package passes both modes/patches, content/graph/DLL/baseline
  identity gates. Independent source spec/quality review passes without findings. Evidence:
  artifacts/local-development/batch3n/. Real server
  accumulation/metadata/empty/group execution remain live gates; wider local query/model/write/transaction/
  schema/specification/deferred audit remains. Overall EF10 goal active, no push or EF8/9 work.

- Batch3m local transcendental slice:54 expected missing-dispatch RED failures become65 focused unit and12
  intercepted passes. Exact twelve Math/MathF identities preserve independent operands and x-owned result
  converters; neutral Single SQL normalization follows the completed operation. Result-only decimal
  GetValue clones retain ordinary decimal readers and clone/parameter behavior. A focused marker-shape
  RED/GREEN correction precedes final full Debug/Release and isolated14 Release1416=1078+336+2, no skips.
  Same baseline-built transcendental package passes both modes/patches; content, package-only graph and
  DLL/baseline identity pass. Independent source spec/quality review passes without findings. Evidence:
  artifacts/local-development/batch3m/. Native metadata/calculation/precision/domain errors remain live
  gates. Round, wider queries/aggregates/model/write/transaction/schema work remains local; full goal active.

- Baseline: 57fd44a, clean efcore/10.0. Prior verified suite: 563 (420 unit, 142 intercepted, one consumer).
- Batch 1: local configuration/cache/helper scope complete; focused 66, full Debug/Release 606 each, independent
  review passed. No production change required. Standard DatabaseFacade.GetDbConnection/SetConnectionString
  failed because IRelationalDatabaseCreator is missing (10 initial failures); move these regressions to Batch 7
  under CFG-04/SCH-01. IRelationalConnection tests prove only the lower-level delayed-assignment contract.
- Environment follow-up: a2bbe5f adds repository-owned LF/CRLF rules; attributes/normalization and review passed.
- Batch 2a: local Oracle fixed-point alias scope complete. Unit RED 10/97 failures and pipeline RED 6/8 failures
  became 97/97 and 8/8 passes. Full Debug/Release and isolated EF10.0.14 Release each pass 627 (475+150+2).
  Independent review passed; the same alias-20261110 nupkg passes both modes on EF10.0.0/10.0.14, with package
  identity/assets/ranges and package-only graph inspected. Expanded consumer fails against the old text package.
  Evidence: artifacts/local-development/batch2a/. No server connection or release publication.
- Batch 2b local scope complete: actual facets/property metadata and complete transport-family clones validated,
  while legal precision/converter clones preserve wide query inputs. Final focused 131 and Debug/Release 706 pass.
- Batch 2b fix round 1: initial focused/full checks pass, but independent review found public Clone DbType and
  StoreTypePostfix overrides may bypass transport/declaration consistency. Add RED regressions and reject unsafe
  overrides. Both findings are addressed and independently re-reviewed. True postfix regression uses mappingInfo;
  a postfix-only argument is an EF no-op and does not justify another public override. Final isolated EF10.0.14
  Release passes 706. The same baseline-built temporal2-20261110 package passes both modes at EF10.0.0/10.0.14;
  README, identity, assets, bounded ranges and package-only consumer graph inspected. The initial temporal probe
  predates review fixes and is not current completion evidence. No publication or database connection occurred.
- Batch 2c local scope complete: finite floating fractions reject integer reads/generated keys and enums;
  four confirmed Oracle property precision conflicts now reject through the shared numeric property check.
  Focused 116 unit and ten pipeline pass; Debug/Release and isolated EF10.0.14 Release each pass 752 (590+160+2).
  Independent review passed. Same baseline-built numeric-20261110 nupkg passes both modes on both selected patches;
  previous temporal2 package fails the new reader smoke. Package identity/assets/README/ranges and package-only
  graph inspected. Evidence: artifacts/local-development/batch2c/. No live connection/publication.
- Scalar audit note: connector 0.2.0 nuspec revision 4734781a7028aea990af7a832ca7d251a2055556
  OceanBaseDataReader.GetDecimal/GetFieldValue already handles OceanBaseDecimal.ToDecimal; reuse that reader
  path rather than inventing a second transport. Byte/session/calendar/LOB/wire gates remain external evidence.
- Batch 3a local concat/null-boundary scope complete: initial SQL RED 12/14 failures and converter RED 2/2
  failures became 33 focused passes. Debug/Release and isolated EF10.0.14 Release pass 785 (611+172+2).
  Independent review passed. Same baseline-built concat-20261110 package passes both modes on EF10.0.0/10.0.14;
  previous numeric package fails the new syntax check. Package-content gate and package-only graph pass.
  Oracle provider-string parameters disable command-SQL caching for native empty/null correctness; pure
  converters preserve raw binding values and null-converting string converters reject explicitly.
  Evidence: artifacts/local-development/batch3a/. Batch 3b arithmetic and wider string/query work remain open.
- Batch 3b local arithmetic scope complete: unit RED 9/12 and intercepted RED 4/4 failures become 12+4 focused
  passes. Debug/Release and isolated EF10.0.14 Release pass 801 (623+176+2). Independent review passed.
  Same baseline-built arithmetic-20261110 package passes both modes on EF10.0.0/10.0.14; preceding concat
  package fails the arithmetic smoke. Package-content gate and package-only graph pass. Server arithmetic,
  predicate membership/zero-divisor/overflow gates remain open. Evidence: artifacts/local-development/batch3b/.
  Next local slice is Batch 3c unary string functions; search/substring/date/numeric/wider query work remains.
- Numeric research exposed a local floating-reader defect: the existing shared conversion always calls exact
  OceanBaseDecimal.ToDecimal before selecting float/double. A package-only probe against arithmetic-20261110
  reproduces six overflows for representable target values (1E30, 38-digit fraction, 1E-40). The published
  connector revision confirms invariant bounded scientific ToString is available. Queue Batch 2d after 3c;
  this historical reproduction is resolved by Batch 2d below.
- Batch 3c unary functions/Length local scope complete: focused 24 unit + eight intercepted pass after RED,
  including four lowercase/whitespace Oracle LOB return-family failures. Debug/Release and isolated EF10.0.14
  Release pass 833 (647+184+2). Independent review passed. Same baseline-built unary-20261111 package passes
  both modes on EF10.0.0/10.0.14; the previous arithmetic package fails the new unary predicate. Repeatable
  package-content gate and package-only graph pass. Evidence: artifacts/local-development/batch3c/.
  Native casing/SQL-space trim/database-character semantics and converter/live limits are documented.
  Batch 2d normalization follows below, then Batch 3d search; the full goal remains active.
- Batch 2d local scope complete: shared OceanBaseDecimal normalization parses directly at Single/Double
  precision, rejects finite-to-infinity conversion and preserves exact/native paths. RED 29 unit + eight
  intercepted failures; final Debug/Release and isolated EF10.0.14 Release pass 882 (688+192+2). Independent
  spec/quality review passed. Same baseline-built wide-decimal-20261111 package passes both modes on
  EF10.0.0/10.0.14; previous unary package fails the new Single midpoint smoke. Content gate and package-only
  graph pass. Evidence: artifacts/local-development/batch2d/. No new storage/live/platform claim.
  Next is Batch 3d search; the overall goal remains active.
- Batch 2d committed as 3bb365a after code/docs approval and one docs example correction; no push.
- Ruling: Batch 3d searches return a total Boolean, false for NULL receiver/provider needle and true when
  negated, in both null settings. Known empty is receiver IS NOT NULL; Oracle stored/computed empty has
  already become NULL. Explicit guards/CASE prevent nullable suffix equality compensation from matching
  NULLs. This follows common query predicate handling without promising CLR exceptions; changing the policy
  later would change observable query results and require regression updates.
- Batch 3d local search scope complete: exact string Contains/StartsWith/EndsWith use native functions and
  guarded total Booleans; Oracle provider-empty captures resolve before normalization. Initial translation
  RED 2/2 plus converted-empty and unrelated-method NRE regressions became 24 focused query and eight
  intercepted passes. Debug/Release and isolated EF10.0.14 Release pass 914 (712+200+2). Independent code
  review passed. Same baseline-built search-20261111 package passes both modes on EF10.0.0/10.0.14;
  previous wide-decimal package fails the new method translations. Content and package-only graph pass.
  Evidence: artifacts/local-development/batch3d/. Server search/collation/padding/character/LOB and current
  hosted execution remain unverified. Next is Batch 3e Substring; the full goal remains active.
- Batch 3d minor (deferred to 3e): add a direct Oracle computed-needle CASE/COALESCE-to-NULL regression.
  Nullable column needles and composed sources already pass; independent review approved with this
  non-blocking coverage suggestion. Carry it into the next string-composition slice and final audit.
- Batch 3d committed as e41981e after code/docs approval; current local source/package checks pass 914.
  Batch 3e starts next using the documented native substring/index/result-mapping contract. No push.
- Batch 3e local Substring scope complete: exact two arities use safe long origin adjustment and preserve
  result family/converter metadata. Genuine translation RED 8/10; final focused 12 unit (including two carried
  computed Oracle NULL needle cases) and eight intercepted pass. Debug/Release and isolated EF10.0.14 Release
  pass 934 (724+208+2). Independent spec/quality review passed. Same baseline-built substring-20261111 package
  passes both modes on EF10.0.0/10.0.14, including both arities/positive max-start; preceding search package
  fails missing Substring. Content gate and package-only graph pass. Evidence: artifacts/local-development/batch3e/.
  Batch 3d computed-needle review minor is closed. Native NCLOB/range/character/padding/LOB and current hosted
  gates remain open. Next is Batch 3f ordinary temporal parts; the complete local EF10 goal remains active.
- Batch 3e committed as 4e78da1 after code/docs approval, 934 local tests and both selected package patches.
  Batch 3f ordinary temporal members starts next; no push/publication or EF8/9 work.
- Read-only CI follow-up: run 42865290840 at e41981e has successful Windows/Linux jobs and all four
  restore/test/pack/content gates. Metadata is artifacts/local-development/ci-e41981e.json; this covers
  the search checkpoint and preceding changes, not later source. No run dispatch or push was performed.
- Batch 3f fix round 1/5: all three coverage findings addressed; captured DateOnly/TimeOnly carrier/precision,
  numeric/binary negatives and representable temporal endpoints added without production changes. Scoped
  independent re-review approved. Final Debug/Release and isolated EF10.0.14 Release pass 963 (745+216+2).
- Batch 3f local ordinary temporal parts complete: twelve exact members, Oracle DATE clock cast and decimal
  SECOND then TRUNC, converter-free int results and conservative NULL metadata. Focused 21 unit/eight
  intercepted pass. Same baseline-built temporal-parts-20261111 package passes both modes on EF10.0.0/10.0.14;
  prior Substring package fails the new member predicate. Content gate and package-only graph pass.
  Evidence: artifacts/local-development/batch3f/. Native extraction/casts/calendar/interval and current hosted
  execution remain open. Next is Batch 3g numeric Abs/Sign/Truncate; the complete local EF10 goal stays active.
- Batch 3f committed as 9f595ed after final 963-pass suites, code/fix/docs approval and package gates. No push.
  Batch 3g starts from this checkpoint using the exact twenty-two-method numeric contract.
- Batch 3g local numeric scope complete: exact 22 Abs/Sign/Truncate signatures, strict mapping validation,
  compatible source/result converters, independent SIGN/precision int and full existing finite ranges.
  Genuine RED 88 missing translations; focused GREEN 108 unit+12 intercepted. Full Debug/Release and isolated
  EF10.0.14 Release each pass 1083 (853+228+2). Independent spec/quality review approved without findings.
  Same baseline-built numeric-core-20261111 nupkg passes both modes on EF10.0.0/10.0.14; preceding temporal
  package fails the new numeric predicate. Content gate and package-only graph pass; baseline assets stay
  EF10.0.0. Evidence: artifacts/local-development/batch3g/. Native arithmetic/domain/overflow/result families
  and current hosted execution remain open. Next is Batch 3h integral membership/ordinary relational shapes;
  wider local work remains active. Development resumed after the user's pause; no push or publication.
- Batch 3g committed as a98e27b after code/docs approval and full source/patch/package gates; no push.
- Batch 3h ruling: initialize matching built-in JSON reader/writer metadata for all existing closed generic
  integer-mapper carriers (eight integers, bool and decimal), preserving clones/converters. An int-only special
  case would miss int properties converted to long; the same constructor also owns Boolean transport. This
  enables EF collection mapping metadata, not a claim of mapped JSON/one-parameter collection SQL support.
- Batch 3h additional RED: Oracle null collection normalization rewrites a parameter envelope into a scalar
  NULL; exclude ElementTypeMapping envelopes from the scalar string visitor. A separate four-case same-query
  mode-order RED confirms shipped EF's relational compiled-query key omits ParameterizedCollectionMode.
  Ruling: add the current collection mode around the full base key in a scoped derived generator, registered
  through the existing provider builder; retain service-provider hash/sharing. This fixes a demonstrated
  local cache defect without copying EF's key or isolating service graphs unnecessarily.
- Batch 3h initial Debug/Release pass 1153 and independent code review approves, but isolated EF10.0.14
  Release exposes one existing Oracle model-reference cache assertion failure (1152 other passes). Fix round1
  investigates exact service/key/cache state before any correction; no package/commit acceptance yet.
- Batch 3h fix round1/5: controlled same-capacity pressure reproduces identity failure with equal keys and
  shared services, proving unconditional model retention is an invalid fixture assumption. The exact natural
  failure timing remains unproven. Test-owned retained cache preserves every positive assertion; two explicit
  eviction cases verify equivalent rebuilding/shared keys/services. No production cache change. Scoped review
  approves (one addressed, zero open). Final Debug/Release and isolated EF10.0.14 Release pass 1155 (885+268+2).
- Batch 3h local slice complete: integral scalar IN metadata, Oracle envelope exclusion and complete-base-key
  collection-mode partitioning; inherited joins/subqueries/quantifiers/counts receive characterization only.
  Focused 30 unit/40 intercepted pass. Same membership-20261111 nupkg passes both modes on EF10.0.0/10.0.14,
  while the preceding numeric package fails the new captured collection mapping. Content/package-only graph
  pass; baseline assets remain EF10.0.0. Evidence: artifacts/local-development/batch3h/. QRY-03/04 stay partial;
  actual server results/current hosted execution remain open. Next is Batch3i scalar text membership.
- Batch3h committed as 1b14513 after production/fix/docs approval, 1155-pass source gates and both selected
  same-package consumers. Final staged check removed only trailing blank lines from the new test file;
  no behavioral change or extra suite rerun. Clean efcore/10.0 before Batch3i; no push or publication.
- Batch3i started from 1b14513 with a fresh implementer and exclusive provider-build ownership. Previous
  membership package fails the new string collection consumer before SQL generation (missing element
  mapping). Metadata-only repair then exposes Oracle converted-empty expansion, so this is local missing
  behavior rather than a live-environment blocker. Default NULL compensation and relational UNKNOWN remain
  distinct contracts. Final acceptance is pending; no current-source package or completion claimed.
- Batch3i source gates: 25 focused unit/20 intercepted pass; full Debug/Release and isolated EF10.0.14
  Release pass 1200 (910+288+2), zero failures/skips. Exact metadata repair and Oracle pre-expansion retain
  raw values/mappings/converters, per-execution state, collision-safe names and padding before normalization.
  Same baseline-built text-membership-20261111 package passes both modes on EF10.0.0/10.0.14, including changed
  captures and all-provider-null Oracle compensation. Content/package-only graph pass; baseline assets stay
  EF10.0.0. Evidence: artifacts/local-development/batch3i/. Independent source spec/quality review approves
  without findings; final docs/consumer review and commit acceptance remain pending.
- Batch3i consumer fix round1/5: docs pass, but nonempty captures need a positive comparison-arm assertion
  after empty/all-provider-null transitions. Original implementer owns the consumer-only correction and
  covering same-package checks. Production source stays approved; no repeated full suite or repack required.
- Batch3i fix round1/5 complete (one addressed, zero open): comparison-arm assertion now names SearchText,
  rejects stale false/IS NULL-only predicates and preserves MySQL empty versus Oracle NULL semantics. Same
  unchanged package passes both patches/modes; provider DLL/package hashes unchanged. Scoped independent
  spec/quality re-review approves with no new findings. Local slice ready for commit; next is planned Batch3j.
- Batch3i committed as c7f1bb0 after all code/docs/consumer reviews and covering checks. Clean efcore/10.0,
  ahead5, no push/publication. Batch3j starts next under the captured-integral-root contract; runtime-null
  zero-row handling does not replace upstream LINQ null-source argument validation.
- Batch3j ruling: preserve proven upstream conflicting-mapping rejection for a collection Value used with
  native and converted properties, or directly projected by Join while its key infers an int-to-long mapping.
  The error occurs in EF's type-mapping postprocessor before the owned runtime seams. Keep exact negative
  tests, native join positives and consistently inferred converted membership/reader positives. No new
  compile-time service in this bounded slice; broader converted-root projection stays local follow-up work.
- Batch3j initial gates pass40 unit/16 intercepted, full Debug/Release and isolated EF10.0.14 Release1256
  (950+304+2), and same integral-roots package consumers pass both modes/patches. Independent review passes
  spec but requests a nested synthetic-wrapper/explicit-CAST regression. Fix round1 is test-only, confined
  to the existing renderer cases and covering checks; production code and package stay unchanged.
- Batch3j fix round1/5 complete (one addressed, zero open): existing renderer cases now enter the outer
  identity wrapper around an explicit long-to-int CAST and retain both targets/node/mapping identities.
  Covering2/2 pass in baseline Debug/Release and isolated14 Release; production/assets/centralprops unchanged.
  Scoped independent spec/quality re-review approves. No full suite/pack/consumer repeat for this test-only fix.
- Batch3j local slice source/patch/package gates pass1256; same integral-roots-20261111 package passes both
  modes/patches and content/package-only graph, baseline assets stay10.0.0. QRY-03 stays partial. Native UNION
  coercion/cardinality/order/limits and current hosted execution remain open. Next is planned Batch3k.
- Batch3j committed as cccdd04 after source/test/docs/consumer approvals. Consumer-only fix round2 requires
  exact UNION separator counts in both modes; unchanged package reruns pass both patches, provider/package
  hashes unchanged. Clean efcore/10.0 ahead6, no push/publication. Continue originally empty inline roots.
- Batch3k RED12 confirms missing inline-empty metadata; delegated inline/subquery visitor and postprocessor
  retain original CLR type, contextual mapping/converter and immutable prototypes. A real ordinal-only empty
  ElementAt expression reproduces EF pruning's body-Value/ORDER-BY-_ord mismatch (RED2). Ruling: repair only
  our annotated zero-row roots using actual surviving references/original identity; retain general pruning.
- Batch3k focused57 unit/eight intercepted pass. Full Debug/Release and isolated14 Release each1281=967+312+2,
  zero failures/skips; one later newline assertion portability edit reruns only the covering eight cases.
  Same inline-empty-20261111 package/content/graph consumers pass both modes/patches; baseline assets stay10.0.0.
  Independent source review passes with no actionable findings. QRY-03 remains partial; SetTables internal API and precompiled
  annotation quoting/native execution have explicit evidence limits. Next planned numeric slice is Batch3l.
- Batch3k committed as9287fab after source/docs/consumer approvals and doc-only stale-status fix round1.
  All11 intended files including new factories staged/whitespace checked. Clean efcore/10.0 ahead7; no push.
- Batch3l RED44fail/two pass confirms missing exact dispatch. Extend existing translator by20 lines for eight
  Floor/Ceiling identities: Oracle FLOOR/CEIL, MySQL guarded TRUNCATE/CASE with independent raw syntax constants.
  Nullable numeric carrier self-review adds RED1fail/one pass, then unwraps only fresh one's boxed carrier;
  original mapping/converter/declared nullable ProviderClrType remain intact. No ordinary numeric input cap.
- Batch3l focused46 unit/12 intercepted, full Debug/Release and isolated14 Release1339=1013+324+2 pass with
  zero failures/skips. Same baseline-built floor-ceiling package passes both modes/patches, content and
  package-only graph; baseline assets stay10.0.0. Independent source review passes with no actionable finding.
  Native TRUNCATE/comparison/arithmetic/CASE typing, finite/subnormal/decimal calculation and volatile
  single evaluation remain explicit live/semantic limits. Wider math/temporal/aggregate/collection and later
  model/write/transaction/schema inventory remain locally developable; overall goal active.
- Line-ending recheck: git ls-files --eol reports no CRLF/mixed index text. Existing working CRLF files remain
  safe under the repository attributes; do not reset local edits or change global core.autocrlf to refresh them.
- Remaining Batch 2 scalar work and Batches 3–10 pending. The overall goal remains active.
- Batch 8 package README slice: use the provider project's README as package-owned documentation and declare
  PackageReadmeFile. Verify archive metadata/content with no-build packing of an already verified isolated
  assembly, independently of the active scalar worker; this does not complete the broader package/release gates.
  Archive metadata/exact content and SHA-256 equality with the verified assembly pass; pack no longer reports a
  missing README. Evidence: artifacts/local-development/package-readme/. Independent review passed. The isolated
  probe has exact EF10.0.14 dependencies; it does not replace bounded-range alias package/consumer evidence.
- Batch 8 package content gate: add PackageConsumer/Verify-Package.ps1 using only PowerShell/.NET ZIP/XML APIs.
  Inputs are an existing nupkg and expected version. Assert provider identity, net10.0 assembly/framework,
  README metadata/nonempty content and exactly the three expected dependency ranges. Verify a valid current
  package plus wrong-version, missing-README and exact-patch probes fail for the intended reason. This read-only
  gate neither builds nor executes a provider or changes the independent source/consumer/live gates.
  At introduction, the numeric package passed; wrong-version, old missing-README and exact-patch probes failed at their intended
  checks on Windows PowerShell 7. Independent spec/quality review passed; Linux execution unverified.
- Cross-platform CI follow-up: add a Windows/Linux GitHub Actions matrix for restore, Release tests, pack and
  the existing package-content gate, using PowerShell 7 on both hosts. It performs no database access or
  publication. YAML parsing/structure and independent spec/quality review pass; committed as 5feef06.
  Hosted execution and Linux runtime remain unverified until an actual workflow run succeeds.
- Hosted CI follow-up (read-only): run 42854480450 at db676ff has successful ubuntu-latest/windows-latest jobs
  and successful restore/test/pack/content steps. Run/job metadata saved in artifacts/local-development/ci-db676ff.json;
  log-archive download was denied HTTP403 (admin rights), so no fresh per-test count is asserted. This evidence
  applies to db676ff only. No push or workflow dispatch was performed.
