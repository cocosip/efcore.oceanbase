# EF10 grouped aggregate result composition

**Base:** `0aa2601`. **IDs:** QRY-03/04/05/11, MAP-07.
**Status:** Local module complete, 2026-11-16; native acceptance remains open.

## Scope and ownership

Complete the grouped-result composition evidence missing from the existing scalar aggregate profiles:
nullable/composite/converted keys, aggregate values alongside ordered group elements, first-in-group
results, paging/current parameters, empty results and materialization failure/recovery. Keep existing
numeric aggregate semantics; do not add aggregate overloads or a new grouping engine.

EF owns grouping translation, SQL projection/joins and CLR shaping/identity. Connector 0.3.0 owns
public command/parameter/reader transport. OceanBase owns GROUP BY, joins, ordering and aggregate SQL
evaluation. Both modes use existing OceanBase SQL contracts; no upstream Oracle/MySQL feature inference.

## Execution

- [x] Add `GroupingRuntimeTests` using the existing supplied-reader pattern and TestConnection. Verify
  exact projection layouts before supplying rows. Cover both modes and sync/async, nullable and
  converted keys, aggregate/element composition and key correlation, first-in-group, group paging,
  current parameters, empty/failure/recovery and rejection before opening unsupported predicates.
- [x] Fix only reproduced provider defects after tracing the owning seam; existing behavior needs no
  duplicate implementation. Grouping and aggregation of supplied rows must never masquerade as SQL evaluation.
- [x] Independently review the complete module. Run focused/connected checks and full Debug/Release/
  isolated EF10.0.14 gates; verify package content/public consumers, with a unique package if production changes.
- [x] Update checklist, design, progress/workboard and locally commit after verification; revision is recorded in git log.

## Defect and implementation

EF10 ApplyGrouping correlates the grouped key with its cloned element source using equality. For a
nullable single key, bounded first-in-group projection becomes a ROW_NUMBER/LEFT JOIN whose root
equality bypasses full null compensation in SqlNullabilityProcessor.ProcessJoinPredicate. Consequently
the NULL group can retain its aggregate but lose its first entity. Composite equality only happened
to receive compensation under default null semantics; UseRelationalNulls also needs explicit group matching.

The provider's TranslateGroupBy override requests the base grouping shaper before the result projection.
It replaces only nullable column equalities against the owning SELECT's grouping keys with
`left = right OR (left IS NULL AND right IS NULL)` in the grouping enumerable predicate. Cloned source
filters have different aliases. Ordinary user joins and nonnullable keys retain their equality contracts.
Both GroupBy result-selector and subsequent Select forms go through the normal EF Select translator;
element selectors, mapping, tracking and aggregates remain EF-owned.

The explicit nullable correlation can prevent EF's equality extraction and preserve LATERAL, including
nullable composite-key collections. This deliberately uses the existing OceanBase lateral contract:
the V4.3.5 reference resolver permits cluster versions >=4.3.1, or >=4.2.2 and <4.3.0. See the pinned
OceanBase documentation/parser/resolver sources and SQL syntax in the
[correlated-query module](efcore-10-correlated-queries-plan.md#owning-sources). No upstream Oracle or
MySQL compatibility inference, server-version detection or new driver API is introduced. Actual query
plans/performance and the deployed server version are unverified.

Owning EF sources inspected: cached EF10.0.0 and 10.0.14
RelationalQueryableMethodTranslatingExpressionVisitor.TranslateGroupBy/TranslateSelect,
SelectExpression.ApplyGrouping/AddJoin/VisitChildren, and SqlNullabilityProcessor.ProcessJoinPredicate.
The mutable grouping SELECT is visited in place, retaining its projection/shaper identity.

## Tests, review and final gates

Artifacts: `artifacts/grouped-results/`. Tested revision: `0aa2601` plus this module's source/tests/docs;
the local commit is recorded in git history. No push or publication.

| Check | Result and evidence |
| --- | --- |
| Meaningful SQL RED | 8 nullable-key failures / 8 ordinary-join/nonnullable controls pass, before the fix; `unit-red.log` |
| Final grouping SQL | 24 cases pass; 16 connected CorrelatedQueryTests also pass, 40 total in `unit-green.log` |
| Grouping runtime | 64/64 pass in `runtime-green.log`: both modes/sync/async, nullable/composite/converted keys, ordered elements/first entity, all tracking modes, both null configurations/result forms, current filtered/HAVING/paging bindings, empty/error/cancel/value failure and recovery, pre-open rejection |
| Debug / Release at EF10.0.0 | Each Unit2533 + Functional2457 + Specification1538 + ConsumerTests2 = 6530 pass; 16 existing mapped skips; `full-debug.log`, `full-release.log` |
| Isolated EF10.0.14 Release | Same 6530 pass / 16 existing skips, Connector0.3.0; `full-compat.log` |
| New package | `10.0.0-grouped-results-20261116.1`, contents pass in `package-content.log` |
| Public packed consumers | EF/Proxies10.0.0 and10.0.14, both modes pass including GroupingSmoke and prior consumers; exact package-only graphs and source/archive/consumer hashes verified in `consumers.log` |
| Independent read-only review | No material issue in provider/unit/runtime logic. Review finds a new consumer namespace mismatch; compiler reproduces it, namespace is aligned to sibling smokes and final consumer gates pass |

The initial runtime paging fixture incorrectly expected an already-constructed Skip/Take call to
refresh after changing local integers. Reconstructing the query through the same factory verifies the
cache/current bindings without misclassifying fixture assumptions as a provider RED. Supplied rows
also keep converted keys and HAVING counts consistent with their current inputs.

Runtime rows establish EF projection/shaping/converter/identity and cleanup behavior only. SQL
assertions independently verify NULL correlation and selected columns; the double does not evaluate
GROUP BY, aggregates, joins or paging. Cancellation is an injected command exception, not physical
read cancellation/token propagation. The preceding enumeration module owns that separate local profile.

Source commands are recorded in `artifacts/grouped-results/run-gates.ps1`: solution Debug/Release,
then Release using `artifacts/default-if-empty/compat-build` and its pinned Directory.Packages.props.
Pack the already-tested baseline Release binary with:

```powershell
dotnet pack src/OceanBase.EntityFrameworkCore/OceanBase.EntityFrameworkCore.csproj -c Release --no-build --no-restore -p:PackageVersion=10.0.0-grouped-results-20261116.1 -o artifacts/grouped-results/package
```

Run PackageConsumer/Verify-Package.ps1 with this archive and ExpectedVersion, then
`artifacts/grouped-results/verify-consumers.ps1`. The script copies public consumer sources into isolated
scratch projects, uses the new package and existing offline dependency feed, checks EF/Relational/Connector
versions and absence of project references, and compares DLL hashes. PackageVersion does not rebuild or
restamp the tested Release assembly. The baseline source DLL, archive DLL and both consumer DLLs have SHA256:

`69E865DCEA039B76C72C25A269008EDF0A0074E8E1CB7CAB00F329E2A0D67D5A`

The isolated patch source suite separately rebuilds against EF10.0.14; packed consumers load the same
baseline-built archive on both patches. No new CLI/bundle, hosted/Linux, native or performance result.
Required status remains 9 Done / 44 In progress per mode; this completes the local module, not EF10 release.
Stop at this checkpoint as requested; do not start another module.

Final GroupBy returning IGrouping objects without an aggregate is outside this module. Native key
equality/collation, ordering, SQL NULL semantics and aggregate evaluation are unverified; no live
DDL/writes, unchanged failed endpoint retry, driver edits, push/publication, EF8/9 or Deferred expansion.
