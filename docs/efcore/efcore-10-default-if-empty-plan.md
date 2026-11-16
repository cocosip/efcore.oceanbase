# EF10 empty-source and outer-join default semantics

**Base:** `1df3779`. **IDs:** `QRY-01/03/05/11`, `MAP-07`, `CON-04/05`, `PKG-03/04`.
**Goal:** Close the local DefaultIfEmpty result-shape gap left by the correlated-query module.
**Architecture:** Retain EF10's DefaultIfEmpty translation, nullable shaping and COALESCE construction;
test the complete default-value profile through OceanBase's existing dialect and command pipeline.
Change Provider production code only for a reproduced defect, with a RED regression first.
**Spec:** `efcore-10-feature-checklist.md` and `efcore-10-query-update-design.md`.
**Stack:** net10.0, EF `[10.0.0,11.0.0)`, OceanBaseConnector 0.3.0.
**Status:** Local scalar profile implemented/tested/reviewed; native and broader reference semantics remain open.

This is the historical scalar checkpoint. The later
[reference continuation](efcore-10-default-if-empty-reference-plan.md) closes its tested reference,
boxing and nullable-required projection gaps; the patch-specific expectations below describe this
earlier revision rather than the current implementation.

## Reproduced defects and implementation

EF10's SQL expressions erase CLR Nullable<T>. SelectExpression.MakeProjectionNullable therefore adds
COALESCE(value, default(T)) even when the shaper's actual result is Nullable<T>. Capture nullable
projection bindings before the operation, then remove only newly inserted compensation by expression
identity; retain user-authored COALESCE, nonnullable conversion shapers and structural entity defaults.

EF10 also lifts scalar DefaultIfEmpty out of SelectMany to a synthetic internal method. It computes
COALESCE inside a right source that may still return zero rows, so no value is available when the outer
join supplies NULL. Keep EF's real default-row translation for nonentity SelectMany sources by adding
a true Where barrier after DefaultIfEmpty at the translator seam (after normalization). The base
correlation finder does not lift through Where; its SQL translator removes the true predicate. Thus a
one-row dummy LEFT JOIN creates the row inside the lateral source before compensation. Mapped entity
sources keep the existing path; constructing a new object of a mapped CLR type is still a projection
and must use the barrier. No reflection/private-method dispatch, SQL parser or new driver API.

The owning EF10.0.0 visitor was decompiled from the installed baseline package into
`artifacts/default-if-empty/source/RelationalQueryableMethodTranslatingExpressionVisitor.cs`.
SelectExpression source and related EF join construction are also cached in
`artifacts/set-operations/source/SelectExpression.cs`. Both EF patches must test the private-contract
dependency; a future EF patch still needs fresh evidence.

Initial fixture corrections: join-constant rendering was over-specified; use the existing true join
predicate without assuming its textual spelling. C# params collection expressions needed explicit
nullable row arrays, and the bad numeric reader string raises FormatException through the existing
mapping. None of those fixture errors is counted as a Provider defect.

With the pre-review 20-unit/40-runtime fixtures and the factory restored temporarily to HEAD, unit tests
fail 12/pass 8 and runtime tests fail 20/pass 20 (`unit-red-final.log`, `functional-red-final.log`).
The factory is restored in finally. Initial narrower SQL regressions also independently expose both
defects (`unit-red.log`, `lateral-default-red.log`).

## Contract and approach

Owning layer: EF query translation, mapping and shaping only. Commands use existing Connector public
DbConnection/CreateCommand, DbCommand.Parameters/ExecuteReader[Async], and DbDataReader APIs. No protocol,
native reader conversion, connection pooling, transport, authentication or driver implementation.
Both targets are OceanBase modes; lateral prerequisites remain those of the preceding module.

Use EF's existing machinery, rather than introduce a second default-value translator or evaluate SQL
inside a fake store. Unit assertions inspect the actual SQL tree output. Intercepted readers supply
explicit SQL result cells, independently of assertions about SQL predicates/default compensation;
they prove shaping/binding/cleanup, never the server's empty-source or join evaluation.

The module covers parameterless Queryable.DefaultIfEmpty:

| Shape | Required local behavior |
| --- | --- |
| Standalone scalar | Nonnullable integral/Boolean defaults have SQL compensation; nullable/text retain NULL |
| Converted scalar | Model default is encoded by the original value converter; no raw numeric zero shortcut |
| Entity | NULL key shapes a missing entity; existing rows retain identity/tracking rules |
| Correlated scalar | Empty per-parent sources preserve the parent and the scalar's default; paging stays inside the right source |
| Correlated anonymous projection | Characterize the selected EF patch's member policy; do not promise whole-reference NULL semantics |
| Composition | Predicates/ordering/aggregates operate on the compensated value; current captured values rebind |
| Unsupported | Explicit-default overload and local predicates reject before opening, with no fallback predicate evaluation |
| Failure | Command/cancellation/reader errors release reader and Provider-owned lease; subsequent query recovers |

Native evaluation/version/type coercion remains an open gate. Extend the existing read-only fixture
with literal expected default results and offline SQL checks; do not retry unchanged opening failures.
No schema objects, DDL, data writes, server-version inference, EF8/9, or Deferred features.

## Explicit remaining semantics

The module's scalar profile is ordinary unboxed stored values and SQL expressions over them. It is
not a claim to implement every CLR client projection. EF10.0.0 compensates required anonymous/DTO
members, whereas EF10.0.14 tests the reference shaper's nullability and omits that compensation;
tests pin each selected patch and include the .14 required-NULL materialization exception. Preserve
these EF decisions; there is no new whole-reference presence-marker shaper here. Direct anonymous/DTO
default(NULL), arbitrary boxing and explicit nullable-to-required `.Value` exception preservation
remain unresolved and do not necessarily reject before execution. A speculative boxed-rejection test
showed no early error and was removed as an incorrect fixture assumption, not counted as a passing
boundary or a fixed defect. These limits keep the relevant Required rows In progress.

## Independent review

Read-only review identified two material issues: computed COALESCE could run after NULL extension,
and a newly constructed mapped CLR type could be mistaken for an entity shaper. Four SQL cases fail
before the fixes (`review-red.log`); computed scalar pushdown and explicit object-projection detection
address them. The numeric/reference coalesce profile adds placement assertions. Existing correlation
tests now expect the real scalar default-row source and generated aliases, retaining pagination,
outer-reference and join assertions. Review did not run tests or native queries; execution results
below are the parent's evidence. Boxing and nullable-to-required semantic boundaries are explicit
remaining work, not evidence of complete reference-object support.

## Review focus

- Missing row versus a present row whose nullable scalar is NULL.
- A model-to-store offset converter whose encoded model default is not literal zero.
- Boolean default compensation surviving OceanBase Oracle predicate/value rewriting.
- Per-parent paging/default placement and exact SQL parameter occurrence order.
- Anonymous member defaults versus whole-entity NULL shaping; no fabricated native evidence.

## Execution

- [x] Add `DefaultIfEmptyQueryTests.cs`: standalone/default/converter/entity/correlated/composed SQL and
  explicit-default rejection. Run UnitTests with `--filter FullyQualifiedName~DefaultIfEmptyQueryTests`.
- [x] Add `DefaultIfEmptyRuntimeTests.cs`: both modes sync/async, supplied NULL/zero/nonzero rows,
  converted defaults, entity tracking/identity, current bindings, errors/cancellation/recovery and
  rejection-before-open. Run the focused FunctionalTests filter. If tests expose a Provider defect,
  record its actual RED cause and fix the owning seam, retaining the rest of EF's implementation.
- [x] Add a packed public API smoke and read-only native assertions/offline SQL checks. Verify baseline
  and isolated EF10.0.14 without changing default assets; no new native attempt.
- [x] Run full Debug/Release/isolated patch, new package content and both-patch/mode package consumers.
- [x] Obtain one independent code review, fix material findings, update current docs/matrix, diff-check
  and commit locally. No push/publication. Keep 53 Required rows and their actual evidence status.

The existing user-directed inline module workflow controls execution; this is one module on the
authorized efcore/10.0 branch. Artifacts/evidence go in `artifacts/default-if-empty/`; record commands,
test outcomes, review and rulings here before committing. No extra implementation worker or worktree.

## Final verification and package provenance

| Gate | Result |
| --- | --- |
| EF10.0.0 Debug | Unit2503 + Functional2295 + Specification1538 + ConsumerTests2 = 6338 pass; 16 existing mapped skips |
| EF10.0.0 Release | Same; zero failures |
| Isolated EF10.0.14 Release | Same; zero failures |
| Excluded LiveTests offline filter | Eight pass per patch; no skips/connection/secrets |
| Focused module | 24 unit + 40 runtime cases; prior 16 correlation cases also pass with corrected SQL expectations |
| New package | `10.0.0-default-if-empty-20261116`, content verification passes |
| Packed public APIs | Both modes at both EF patches pass, including DefaultIfEmpty smoke and prior profiles |
| Graph and DLL identity | Package-only graphs; source/archive/both consumer copies SHA256 `FC9BADAB006B9C311D3FEA6CBF66EDE84A3445FFC2538184B4B3B419B8A4E6E9` |

Baseline commands: `dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1 -nr:false`
and the same at Release. Logs: `full-debug.log`, `full-release.log`. The isolated patch adds
`--artifacts-path artifacts/default-if-empty/compat-build` and
`-p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/default-if-empty/compat/Directory.Packages.props`.
Its exact props pin EF/Relational/Design/Proxies/specification and UserSecrets to 10.0.14; baseline assets
remain 10.0.0. Restore from `artifacts/min-number-feed` with
`--packages artifacts/datetime-calendar-add/compat-packages`; restore the excluded LiveTests separately.
Use `--filter FullyQualifiedName~ReadOnlyQueryProfileTests` for the eight offline checks at each patch.
Logs: `full-compat.log`, `restore-compat.log`, `live-offline.log`, `live-offline-compat.log`.

Pack baseline Release using `dotnet pack src/OceanBase.EntityFrameworkCore -c Release --no-build
--no-restore -p:PackageVersion=10.0.0-default-if-empty-20261116 -o artifacts/default-if-empty/package`.
Run `tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1` with the new archive as
PackagePath and the same ExpectedVersion. `artifacts/default-if-empty/verify-consumers.ps1` copies
public consumer sources, restores package-only graphs and runs both patches/modes, checking DLL hashes.
Logs: `pack.log`, `package-content.log`, `consumers.log`. The archive uses base `1df3779` plus this
module's precommit source; its hash records that revision stamp and can differ after a later rebuild.

Independent review's two material findings are addressed with the four-case RED (`review-red.log`)
and green focused/full suites. No remaining finding in the declared scalar profile. Explicit unresolved
reference/boxing/nullable-to-required semantics above stay visible and keep the Required rows open.
No new CLI/bundle/Linux/hosted/native result, connection retry, DDL/data writes, push or publication.
Required remains 53 rows per mode: 9 Done / 44 In progress. The overall goal is active.
