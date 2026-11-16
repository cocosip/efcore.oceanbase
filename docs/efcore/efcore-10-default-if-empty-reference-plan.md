# EF10 DefaultIfEmpty reference and row-presence semantics

**Base:** `b0bdae0` (collection continuation: `79006f3`). **IDs:** QRY-01/03/05/08/11, MAP-07, PKG-03/04.
**Status:** Local profile implemented/tested/reviewed; native and broader runtime acceptance remain open.
This is the continuation of the scalar default module, not a new feature family.

## Ownership and contract

EF owns LINQ translation, CLR projection construction and missing-row defaults. Consume only
OceanBaseConnector 0.3.0 public command/parameter/reader APIs. No driver, protocol, authentication,
TLS, pooling, native transaction or codec changes. Both modes are OceanBase modes; reuse the
existing SELECT, LEFT JOIN and CASE dialect contracts and existing lateral version prerequisites.
No new native capability is inferred from an upstream Oracle/MySQL implementation.

An explicit constant projected inside the source distinguishes missing rows from present rows,
including present rows whose projected values are all NULL. Keep entity identity/tracking with EF.
Do not substitute a projected nullable value for row presence. Preserve user coalesce and converters.

| Profile | Contract |
| --- | --- |
| Anonymous object / DTO / constructed mapped CLR type | Missing row yields null; present all-NULL members still construct an object |
| Boxed required / nullable value | Missing row yields null; present zero remains boxed zero; nullable NULL remains null |
| Required scalar read of nullable value | Missing row yields CLR default; present NULL must still fail materialization |
| Composition | Whole-reference null predicates, member predicates/projections/order, correlated paging and current parameters retain server translation |
| Nested / mixed entity projection | Mark the outer projection; retain EF entity shapers and tracking |
| Failure | Dispose readers/release leases on materialization error and allow subsequent query recovery |

## Execution

1. Add SQL and supplied-reader RED regressions against the unchanged provider, covering both modes
   and sync/async. Supplied results prove shaping, not native SQL evaluation.
2. Extend the existing shared default helper and translator seams; use ordinary EF expression APIs,
   no reflection into EF internals, driver bypass or SQL evaluator.
3. Run focused/connected checks, Debug/Release, isolated EF10.0.14 Release and package consumers.
4. Obtain independent code review, resolve findings, update support boundaries and local-commit.

Native evaluation and exact deployment versions remain unverified. Do not repeat unchanged endpoint
opening failures. No live DDL/writes, push/publication, EF8/9 or Deferred features.

### Nested collection runtime continuation (base `79006f3`)

Use the existing `DefaultIfEmptyRuntimeTests` supplied-reader fixture to verify the collection
bindings preserved by this module. EF owns collection grouping, missing-parent defaults, identity
resolution and reader cleanup; all transport remains with Connector 0.3.0 public APIs. This is the
same single-query default-result profile, not a new collection loader or driver feature.

- [x] Verify missing outer reference, present empty/populated collections and multiple parent groups.
- [x] Verify nullable/converted scalar children, entity identity scope and current query parameters.
- [x] Verify materialization/command failure, command cancellation, reader cleanup and later query recovery;
  retain explicit user construction after defaulting.
- [x] Fix only reproduced provider defects with RED/green regressions; existing behavior needs no
  duplicate implementation. Review independently, run full local baseline/patch gates, document exact
  package provenance (reuse only if production stays unchanged), and local-commit.

Supplied rows must follow the generated projection ordinals, with explicit SQL-shape assertions.
They prove EF shaping and grouping, not native join/CASE/order evaluation. Native SQL, split-query
combinations, arbitrary CLR collection projections and real deployment versions remain unverified.

`DefaultIfEmptyRuntimeTests.Nested_*` adds 32 both-mode sync/async cases; the complete default runtime
profile now passes 104/104. Scalar parent key zero, present NULL child values and converted offsets
are distinguished from missing parent/child keys. A repeated child is shared only with tracking or
identity resolution; plain no-tracking creates separate instances. Parent page size is a query
constant, while captured predicates and child limits rebind between executions. Explicit post-default
construction retains a non-null outer object even when its parent ID and collection are null.

No production change was necessary. A temporary mutation removing the existing
`CollectionResultExpression` traversal made all 32 new cases fail; restoring it returned the full
default profile to 104/104 (`collection-binding-mutation.log`, `collection-default-profile.log`).
Earlier intentional layout-inspection failures, a fixed outer `Take` expectation and the
Oracle-mode Boolean CASE assertion were test development, not new provider defects.
Independent read-only review found no material issue. It did not run tests or native queries.
Command cancellation occurs before a reader is created; the invalid second child value exercises
partial-collection materialization failure and reader disposal. Cancellation during reader iteration
is outside this evidence, as are physical driver cleanup and native SQL execution.

Final continuation gates on `79006f3` production source plus these tests:

| Gate | Result |
| --- | --- |
| EF10.0.0 Debug / Release; isolated EF10.0.14 Release | Each 6408 pass: Unit 2509, Functional 2359, Specification 1538, ConsumerTests 2; 16 old mapped skips |
| Original package content replay | `10.0.0-default-reference-20261116.1` passes; no new package |
| Original public packed consumers | Both modes at EF/Proxies10.0.0 and10.0.14 pass; package-only graphs and original archive/consumer DLL hashes match |

Run `artifacts/default-if-empty/run-collection-gates.ps1` for the three full source gates. Logs are
`collection-full-debug.log`, `collection-full-release.log`, `collection-full-compat.log`.
The patch runtime deps explicitly resolve EF/Relational10.0.14 and Connector0.3.0.
Run `tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1` with the original
archive and expected version, then `artifacts/default-if-empty/verify-collection-consumers.ps1`;
logs are `collection-package-content.log` and `collection-consumers.log`.
The replay pins original archive provider-DLL SHA256 `703D578BF6FB00AF88501FFE1F180A8CC6B382F68DAD61AC227AB2815C0F716C`.
This remains the preceding precommit build; the current rebuilt source DLL is not claimed to match
that archive's revision stamp/hash. The new collection cases run against rebuilt source, not the packed
consumer; packed consumers repeat their existing public smokes. No production or dependency diff,
new CLI/bundle/hosted/Linux/native evidence, native attempt, or Required-row promotion.

## Implementation and review

The existing default helper snapshots the shaper's actual CLR types and projection bindings. Reference
construction, boxing and required nullable SQL expressions project a typed constant marker inside a
derived source. The outer conditional returns the CLR default only if that marker is NULL. Restore
nullable projection metadata for constructor/client-index bindings as well as member bindings; the
baseline EF implementation makes only member projections nullable. Remove only EF-added compensation,
preserving inner user COALESCE, converter metadata and structural entity projections.

Exact EF navigation-expansion source is decompiled in
`artifacts/default-if-empty/source/NavigationExpandingExpressionVisitor.cs`. Its snapshot retains mixed
entity/New shapes and reconstructs them in an extra SELECT after DefaultIfEmpty. The preprocessor's
reference boxing/cast-back rewrite hides that shape until EF translation, retaining the entire NULL
reference. TranslateCast folds only an exact original-type boxing conversion. Explicit user SELECT
construction after defaulting remains a new object, covered by a separate assertion in the mixed-entity
runtime case. No private EF reflection or method dispatch is introduced.

Member/null predicates over default-reference conditionals translate through ordinary CASE/IS NULL.
When this newly allows EF's first SELECT projection-binding pass to succeed over an old client list,
the dictionary ReplaceProjection overload does not clear that list. Capture the final same-select
member bindings, clear stale clients and restore the dictionary; any current-select index binding
prevents this reset. The original failure and final shape are recorded in `reference-final-shape.log`.

Independent read-only review confirmed the projection-list cause and reset, then identified nullable
arithmetic (`Value.Value + 1`) as a gap in column-only presence detection. Conservatively retain a
marker for required non-column SQL expressions; direct and computed nullable reads, missing defaults,
present-NULL failures, coalesce and recovery now have regressions. Final review of preprocessing,
cast folding, registration and helper changes found no remaining material defect. The reviewer did
not execute tests or native queries; execution evidence belongs to the primary agent.

Initial unchanged-provider RED: six new SQL cases fail; the first sixteen supplied-reader cases fail
twelve/pass four. Later mixed entity, constructor nullable-metadata and stale-client-projection failures
are recorded in `reference-*-debug.log` and intermediate logs. Incorrect reader ordinals and alias
expectations discovered while extending fixtures are fixture corrections, not Provider defects.
Final focused profiles: 30 default SQL cases plus 16 prior correlation cases; 72 default runtime cases.

Final source review also reproduced collection binding loss: a New projection containing a correlated
list had its unvisited client slot replaced by the presence marker. Two SQL compilation cases fail
before traversing CollectionResultExpression/IncludeExpression and pass afterward
(`reference-nested-binding-red.log`, `reference-nested-binding-green.log`). Preserve these existing
slots; the same-select binding identity guard excludes nested query scopes. Independent follow-up
review confirms the fix and records no remaining material issue. This proves compilation/binding
preservation, not nested collection runtime/native acceptance. Full gates and a new unique package
version are refreshed after this final source change; earlier archive hashes are superseded.

## Original reference checkpoint verification record

Logs and scripts live under `artifacts/default-if-empty/`, with `reference-` filenames distinguishing
this module from the preceding scalar checkpoint. Run `run-reference-gates.ps1` for Debug/Release,
isolated EF10.0.14 and separately excluded offline LiveTests. Baseline full Debug passes 6376 tests
(Unit 2509, Functional 2327, Specification 1538, ConsumerTests 2), with 16 existing mapped skips.
Final gates:

| Gate | Result |
| --- | --- |
| EF10.0.0 Debug and Release | Each 6376 pass, 16 old mapped skips |
| Isolated EF10.0.14 Release | Same component totals and skips |
| Excluded read-only fixture offline filter | Ten pass per patch, no skips/opening/secrets |
| New package content | `10.0.0-default-reference-20261116.1` passes |
| Public packed consumers | Both modes at EF/Proxies10.0.0 and10.0.14 pass, including added reference/boxed/required-NULL smokes |
| Graph and DLL provenance | Package-only graphs; source/archive/both consumers share SHA256 `703D578BF6FB00AF88501FFE1F180A8CC6B382F68DAD61AC227AB2815C0F716C` |

The isolated patch reuses `artifacts/default-if-empty/compat/Directory.Packages.props` and
`compat-build`, with exact patch pins and the existing isolated package cache; baseline assets remain
EF10.0.0. Exact commands are in `run-reference-gates.ps1`. Final logs:
`reference-full-debug.log`, `reference-full-release.log`, `reference-full-compat.log`,
`reference-live-offline.log`, `reference-live-offline-compat.log`.

Pack with `dotnet pack src/OceanBase.EntityFrameworkCore -c Release --no-build --no-restore
-p:PackageVersion=10.0.0-default-reference-20261116.1 -o artifacts/default-if-empty/reference/package`.
Run `Verify-Package.ps1` with that archive/version, then
`artifacts/default-if-empty/verify-reference-consumers.ps1`. Logs: `reference-pack.log`,
`reference-package-content.log`, `reference-consumers.log`. The archive DLL is the tested precommit
build on base `b0bdae0` plus this module; a postcommit rebuild can have a different revision stamp/hash.
The package README describes the tested local profile and native limits. No new CLI/bundle/Linux/
hosted/native evidence is claimed; no driver dependency or required-status change.

Supplied readers prove CLR shaping, binding and cleanup; SQL assertions prove translation structure.
They do not execute joins/defaults/CASE inside OceanBase. The read-only native fixture gains reference,
boxing and required nullable assertions; these assertions are added, not natively executed. Arbitrary
CLR client projections and all server-side evaluation/precision/coercion remain outside this evidence.
The Required matrix remains 53 rows per mode, 9 Done / 44 In progress; the overall goal stays active.
