# EF10 Split Query Implementation Plan

**Goal:** Enable both-mode split graph queries with sequential native reader use.
**Architecture:** Reuse EF split coordinators; add effective-behavior buffering and public options.
**Spec:** [split-query design](efcore-10-split-query-design.md). **Base:** 013b964.
**Execution:** Inline, existing checkout. **Tech stack:** .NET/EF10, Connector 0.3.0, xUnit.

## Steps and owning files

1. [x] Add FunctionalTests/SplitQueryRuntimeTests.cs. Enforce one active native reader and reproduce
   AsSplitQuery root/collection failure before changing production. Pin projection/correlation SQL independently.
2. [x] Add UnitTests/SplitQueryOptionsTests.cs for public defaults/invalid values/clones/cache isolation,
   effective AsSplitQuery/AsSingleQuery buffering and preserving retry/precompilation behavior. Record RED.
3. [x] Add Query/OceanBaseQueryCompilationContextFactory.cs and register IQueryCompilationContextFactory
   in Internal/OceanBaseOptionsExtension.cs. Add UseQuerySplittingBehavior in the options builder. No new
   coordinator, driver API or dependency. Make focused tests GREEN.
4. [x] Extend the runtime module with sibling/nested collections, filters/paging/empty graphs, tracking
   modes, overrides and later-reader failure/retry/cancellation/cleanup. Fix only reproduced owning defects.
5. [x] Add PackageConsumer/SplitQuerySmoke.cs and invocation; prove prior retry package RED, then validate
   one baseline-built nupkg on both selected patches/modes. Run full source Debug/Release/isolated patch and
   public runtime/generated-context/migration CLI/bundle compatibility.
6. [x] Independently review the full module, regress/fix actionable findings, update support/checklist/
   roadmap/progress/baseline and commit locally. Leave full EF10 goal active.

## Review focus

- Per-query overrides must decide buffering after normalization, independent of global default and cache reuse.
- Native readers must never overlap, even without retrying, for root/nested/sibling result sets.
- A failed later command must not duplicate relationships or silently change an unknown execution outcome.
- Each split command must bind current filters and use the same deterministic paging/key order.
- Default single-query, retry buffering, precompilation entry points and caller leases must retain semantics.

## Rulings and evidence

- Previous goal turn made progress: retries implemented/reviewed/committed at 013b964, clean worktree.
- Split queries are locally feasible DEF-08 work, plus required Include/filter/materialization contracts.
  The user delegated implementation decisions and asked for complete feature modules; no separate scope
  approval or driver change is required. Native and upstream runtime specification gates remain distinct.
- Initial RED: artifacts/split-query-readers-red.log records all 12 graph cases failing because the root
  native reader is still active at the first child command. split-query-options-red.log records the missing
  public configuration API. Effective-behavior compilation buffering fixes the owning seam; focused options
  (seven) and initial graph cases (12) pass in split-query-options-green.log/split-query-readers-green.log.
- Expanded runtime evidence: split-query-runtime-expanded.log and split-query-review-fix.log each pass
  72 cases, covering both modes, sync/async and all three tracking modes. Fixtures enforce sequential native
  readers; failures test later command replay, original unknown/cancelled errors, reader disposal, caller
  leases and a successful subsequent operation after clearing partial tracking.
- Independent read-only review found no production defect. One P2 verification gap was confirmed: generic
  ORDER BY/ROW_NUMBER assertions could accept wrong SQL with canned rows. The fix pins actual parent Id
  ordering immediately before paging, child ParentId window partition/Id order and row <= 2, parent-child/
  child-note/tag join keys and final correlation order using alias-tolerant independent SQL assertions.
- Ruling: native execution/session/isolation/memory/timing require real server acceptance; preserving the
  experimental factory method does not establish compiled-model/code-generation support. Arbitrary JSON/
  complex collections and upstream runtime store specifications are independent work, not implied by these
  ordinary graph tests. This preserves the full EF10 goal rather than shrinking it.
- Public package RED: artifacts/split-query-old-package-red.log confirms the preceding retry nupkg cannot
  compile the new UseQuerySplittingBehavior graph smoke. SplitQuerySmoke now exercises configured and
  per-query public graph loading, both modes and sync/async, with raw reader closed-state checks.
- Final source matrix: dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug/Release --no-restore -m:1
  -nr:false, plus isolated EF10.0.14 Release using artifacts/retries-ef10.0.14/Directory.Packages.props and
  its separate build outputs, each passes **4930/4930** tests: 2301 unit, 1759 functional, 868 metadata
  specifications and two consumers; zero failures/skips. Logs: artifacts/split-query-final-debug.log,
  split-query-final-release.log and split-query-final-patch.log. Baseline source assets stay EF10.0.0 and
  Connector 0.3.0; no new dependency or driver modification.
- One baseline Release binary supplies artifacts/split-query-package/
  OceanBase.EntityFrameworkCore.10.0.0-split-query-20261113.nupkg. Verify-Package.ps1 passes identity,
  README, net10.0, EF [10.0.0,11.0.0) ranges and Connector 0.3.0. The scratch
  artifacts/verify-split-query-consumers.ps1 and split-query-consumers.log pass all public runtime smokes
  on EF10.0.0/10.0.14 in both modes, including the new split graphs. Package-only graphs and source/runtime
  Provider SHA256 match: FA3FA712ACDED967EFA44487696B8886BDB078D93E38B7FEA447D47E238993E9.
- Full Verify-Tooling.ps1 passes both modes on EF10.0.0/10.0.14 for the same nupkg: generated contexts,
  compiled migration snapshots/evolution, model/list/script/removal checks and four Windows bundle
  build/help checks. Logs: artifacts/split-query-tooling-10.0.0.log and split-query-tooling-10.0.14.log.
  Workspaces: artifacts/tooling-10.0.0-a723fc7e74fb41a58950112186524286 and
  artifacts/tooling-10.0.14-0b25ccd6b4e84e1d92ff48dfbf0c3856. Package-only runtime/Design dependency graphs
  and source/archive/runtime/tooling DLL identity are verified against the SHA256 above.
- No unchanged live probe repeat, SQL/DDL execution, driver change, push, tag or publication. Local
  split-query acceptance is complete; native gates, independent DEF-08 families and the full goal stay open.
