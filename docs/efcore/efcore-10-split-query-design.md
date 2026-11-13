# EF10 Split Query Graph Loading

**Date:** 2026-11-13. **Base:** 013b964. **IDs:** DEF-08, QRY-01/05/08/09, CON-04/05,
CFG-02/03, PKG-03/04. Continue inline on efcore/10.0 under the existing full-development authorization.

## Intent and approach

Enable ordinary split-query graph loading in both modes, reusing EF10's collection coordinators, identity
resolution, filtering and per-query SingleQuery/SplitQuery overrides. Add public
OceanBaseDbContextOptionsBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior), preserving null/default
behavior and existing option clones. Reject invalid enum values during configuration.

EF's generic RelationalQueryCompilationContext only buffers when retrying. OceanBase does not have a
multiple-active-results contract on one connection. Supply a provider compilation-context factory whose
context reports IsBuffering when base.IsBuffering or QuerySplittingBehavior is SplitQuery. Determine the
effective query behavior dynamically, after AsSplitQuery/AsSingleQuery normalization. Preserve EF10's
precompilation factory entry point without claiming compiled-model/tooling support.

The alternatives were a second provider graph coordinator or command-level reader draining. Both duplicate
EF responsibilities and cannot preserve all shaping/retry semantics as directly as the compilation seam.
No new dependency, driver change or independent command replay loop is needed.

## Acceptance and ownership

Verify root/two sibling/nested collection graphs, empty roots/collections, deterministic ordering/paging,
filtered Includes and current tenant parameters in every command. Pin command projections/key correlation
separately from fixture data. Exercise sync/async and tracking/no-tracking/identity resolution, context-wide
options, per-query overrides and option/cache isolation. Reuse existing relational/native test doubles;
enforce one active native reader so a fake transport cannot conceal the buffering requirement.

Reader failures/cancellation must dispose every result and release leases. With opt-in retrying, the failing
buffered command may replay before exposing its rows; unknown outcomes never replay. Test later-command
failure, original error identity and subsequent operation safety. User transactions and resource ownership
retain existing EF semantics. No application-level snapshot isolation is introduced by splitting: separate
commands can observe concurrent database changes; applications choose an appropriate native isolation level.

Split results consume memory and incur additional round trips. Existing single-query defaults remain; explicit
SingleQuery overrides must avoid forced split buffering unless retrying independently requires it. Do not
claim lazy loading, compiled models, arbitrary JSON/complex/collection shapes or all upstream runtime
specification families from this module. DEF-08 stays partial until those independent families are developed.

## Evidence boundary

First reproduce one-active-reader failures with AsSplitQuery before production changes and missing API
configuration tests. Then implement only the owning compilation/configuration seams. Run whole source
Debug/Release and isolated EF10.0.14, a public package-only graph smoke (old nupkg RED), both-mode runtime/
generated-context/migration CLI and bundle compatibility on one baseline-built binary. Independently review
the module, fix confirmed issues with regressions, update acceptance/progress/support docs and commit locally.
Native SQL/results, consistency, memory/timing and reader/session reuse still require controlled OceanBase
acceptance. Do not repeat unchanged failing live probes or turn missing native evidence into a driver defect.

References: exact EF10 RelationalQueryCompilationContext/Factory and ShaperProcessingExpressionVisitor
buffer/split coordinator contracts, cached under artifacts/split-query-contract and bulk-dml-ef-source.
