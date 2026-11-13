# EF10 Execution Strategy Retries Implementation Plan

**Goal:** Deliver opt-in known-outcome server retries through EF10, with early DDL replay rejection.
**Architecture:** Reuse ExecutionStrategy and the relational options factory; no command-level replay loop.
**Tech Stack:** .NET/EF10, Connector 0.3.0, xUnit. **Base:** 3d58c0f.
**Spec:** [retry design](efcore-10-retries-design.md). **Execution:** Inline in the existing checkout.

## Scope and steps

1. [x] Add UnitTests/ExecutionStrategyTests.cs for public configuration, invalid values, cloned/isolated
   settings, mode-specific codes, SQLSTATE, additional-code immutability, unknown/XA/cancellation exclusions,
   default non-retry behavior, limits, nested execution and buffering cache keys. Record RED before production.
2. [x] Add Storage/OceanBaseRetryingExecutionStrategy.cs and EnableRetryOnFailure to
   Infrastructure/OceanBaseDbContextOptionsBuilder.cs. Use WithExecutionStrategyFactory and EF's retry engine.
3. [x] Add in-strategy Migrate and schema/namespace lifecycle RED cases; update OceanBaseMigrator.Validate,
   OceanBaseDatabaseCreator.ValidateTableCreation and namespace management to reject configured/current
   execution strategies before any work. Preserve default lifecycle behavior and offline script generation.
4. [x] Add FunctionalTests/ExecutionStrategyRuntimeTests.cs for query/buffering, generated and assigned-key
   saves, transaction failure/recovery, bulk DML, sync/async cancellation/exhaustion, caller transactions and
   custom public strategies. Fix only reproduced owning defects, preserving error/entry/cleanup behavior.
   Extend Storage/OceanBaseRelationalTransactionFactory.cs for confirmed rollback before disposing an
   unfinished owned retry transaction; preserve ordinary non-retry and Oracle logical-release behavior.
   Update BatchExecutor/connection cleanup catches to propagate a replay veto through EF wrapper exceptions.
5. [x] Add PackageConsumer/RetrySmoke.cs and Program invocation; run against the preceding package for RED,
   then one baseline-built package on both modes/patches. Run full source Debug/Release and isolated patch,
   package content/public runtime and existing complete CLI/bundle compatibility checks.
6. [x] Independently review the whole module; fix valid findings with regressions, update support/progress/
   checklist/roadmap/baseline docs, verify staged whitespace/scope and commit locally. Keep full EF10 goal active.

## Review focus

- Unknown execution outcomes must never be replayed, even when configured as an additional server code.
- Current/nested strategies must not bypass DDL guards or multiply budgets.
- Reader failure must not leak a partial result or an open/dirty lease into the next attempt.
- Save retries must roll back/dispose their automatic transaction and discard temporary generated values.
- Shared singleton services/compiled query caches must not capture another context's retry configuration.

## Evidence and rulings

- Previous goal turn made concrete progress: TPC was implemented/reviewed/committed at 3d58c0f; working tree
  is clean before this module. DEF-09 execution-strategy retries are still an actual unimplemented feature.
- Connector public unknown-outcome metadata is sufficient; no driver work or new capability is requested.
- Strategy defaults are conservative server errors, not an assertion of live retry/failover acceptance.
- Builder check confirmed factory lifetime and base buffering cache separation. It exposed suppressed
  rollback failures in Connector transaction disposal: explicitly confirm rollback before disposing owned
  retry transactions, veto any cleanup failure, and propagate the veto through wrappers before EF unwraps.
  Include user strategy delegates as well as automatic saves; no driver contract change is required.
- Initial RED: artifacts/retries-options-red.log confirms the missing public API; runtime RED records
  16/32 failures for owned rollback, cleanup veto and in-strategy DDL rejection. Options/classification
  tests pass 79 cases. Ordinary non-retry Oracle rollback/disposal regression reproduced two failures
  before restricting stored rollback-error propagation to retry transactions.
- Independent review reproduced three P1 safety gaps: a started native transaction disposed before EF
  ownership; buffered-reader disposal replacing an unknown read error; post-write connection close errors
  permitting bulk increment replay. RED logs: retries-start-red.log (4), retries-reader-red.log (4) and
  retries-close-red.log (4). Fix startup veto, preserve buffered read errors through a retry-only native
  reader wrapper, and mark Close/CloseAsync exceptions unsafe. Reviewed focused tests pass 56 cases
  before adding exhaustion/cancelled-save coverage. No driver change is needed.
- Ruling: all commit-stage errors veto replay because EF combines physical commit and post-commit
  callbacks in the same method; cost is conservative rejection of some otherwise retryable commit errors.
  User delegates keep C# disposal exception precedence. Arbitrary raw DDL, external side effects and
  application replacements bypassing provider services remain application-owned; native outcomes,
  transient codes and failover require server acceptance. These boundaries do not reduce the EF10 goal.
- Public package RED: artifacts/retries-old-package-red.log proves the prior TPC r2 nupkg cannot compile
  the new EnableRetryOnFailure/strategy smoke. Package fixture exercises both modes and sync/async owned
  transaction rollback/commit, unknown outcomes and active-strategy DDL rejection without a native server.
- Patch restore initially failed at remote NuGet signature TLS and a protected global Connector cache.
  Restored successfully from existing local feeds into artifacts/retries-packages; no credential or
  dependency change was needed. Isolated patch props/build live under artifacts/retries-ef10.0.14.
- Final independent-review fixes: startup native-handle veto (4 RED -> GREEN), buffered-reader cleanup
  protection (4 RED -> GREEN), post-write close veto (4 RED -> GREEN). Commit callback/ordinary disposal
  checks and exhaustion/cancelled-save cases also pass. No deferred minor finding was reported.
- Final Debug/Release at EF10.0.0 and isolated EF10.0.14 Release each pass **4851/4851** tests: 2294 unit,
  1687 functional, 868 inherited metadata specifications and two consumers, zero failures/skips. Logs:
  artifacts/retries-final-debug.log, retries-final-release.log and retries-final-patch.log. Baseline source
  assets remain EF10.0.0/Connector 0.3.0; isolated assets resolve EF10.0.14/Connector 0.3.0.
- One baseline Release binary supplies artifacts/retries-package/
  OceanBase.EntityFrameworkCore.10.0.0-retries-20261113.nupkg. Verify-Package.ps1 passes content, README,
  net10.0, EF [10.0.0,11.0.0) ranges and Connector 0.3.0. artifacts/verify-retries-consumers.ps1 and
  retries-consumers.log pass both-mode public runtime smokes on EF10.0.0/10.0.14, including the new retry
  API/owned rollback. Both graphs are package-only and match the source/archive Provider DLL hash.
- Full Verify-Tooling.ps1 passes both modes on both patches, including public-GetSchema generated contexts,
  compiled snapshots/evolution, pending-model/list/script/removal checks and four Windows bundle build/help
  checks. Logs: artifacts/retries-tooling-10.0.0.log and retries-tooling-10.0.14.log. Workspaces:
  artifacts/tooling-10.0.0-43676ea67a654fd880a5b44feab70609 and
  artifacts/tooling-10.0.14-161b5403ab0f4ab38ebe068318348e6d. Package-only graphs and DLL identity verified.
- Source/archive/runtime/tooling Provider SHA256:
  EDBB81E08FFEFF28FBB2F59AB7BF25E468CA42C7E4C60CFF80FE5AC13A2A4A48.
  No native probe repeat, SQL/DDL, driver change, push, tag or publication. The full EF10 goal stays active.
