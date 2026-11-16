# EF10 SaveChanges concurrency diagnostics and interception

**Base:** `2deb9ee`

**IDs:** `UPD-02/04`, `DIA-01/02`, `TXN-02/03`, `TST-01`

**Status:** Implemented, locally verified and independently reviewed; native acceptance remains open.

## Contract and scope

The custom modification batch previously threw directly for unexpected affected-row counts and a
missing MySQL generated-value row. Unlike EF10 AffectedCountModificationCommandBatch, it did not call
the update logger's OptimisticConcurrencyException/Async. DbContext's concurrency catch does not perform that
dispatch either. Consequently diagnostic listeners and SaveChangesInterceptor.ThrowingConcurrencyException
missed these conflicts, and interceptors could not suppress them. The fix belongs at the shared conflict boundary, not
DbContext, SQL generation or connector code.

For INSERT/UPDATE/DELETE, any affected-row count other than one creates the existing exception with
the failing command's entries and dispatches the standard update diagnostic once. Missing generated
rows follow the same contract. Preserve the exception instance when suppression is absent. Invoke
the matching sync/async interceptor and pass the caller cancellation token on async dispatch. Normal
success, command errors and cancellation do not become concurrency conflicts.

Suppression accepts the caller's decision and skips the failed command's readback/output propagation;
subsequent batches continue. EF owns SaveChanges return counts, AcceptAllChanges and transaction/
savepoint outcome. Suppression does not prove a database mutation or supply missing generated values.
Tests use assigned keys plus computed outputs to check snapshots safely. A generated-identity insert
with no returned key receives no permanent key: SaveChanges(false) retains Added/temporary state;
default acceptance clears EF's temporary flag and marks the entry Unchanged with its unset CLR key.
Applications suppressing such conflicts own reconciliation before accepting changes; the provider must
not invent a key or silently detach an entry. This feature does not add retry or conflict-resolution policy.

Use the standard core ConcurrencyExceptionEventData factory for this nonquery batch: no live DML reader
or retained native command exists at its affected-count boundary. The public ISaveChangesInterceptor
contract and official core SaveChangesInterceptionTestBase accept this payload. Do not fabricate a
RelationalConcurrencyExceptionEventData or promise reader/command correlation fields. Keep that
difference explicit even for the separate MySQL readback conflict.

Owning source contracts: installed EF10.0.0 AffectedCountModificationCommandBatch,
CoreLoggerExtensions, DbContext and SaveChangesInterceptionTestBase; verify 10.0.14 compatibility.
Official guidance: [interceptors](https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors),
[optimistic concurrency](https://learn.microsoft.com/ef/core/saving/concurrency),
[transactions](https://learn.microsoft.com/ef/core/saving/transactions).

## Files and execution

1. [x] Add SaveChangesInterceptionTests.cs with real provider contexts and intercepted ADO command
   outcomes. Cover all DML states, both modes, sync/async, wrong row counts, observe/suppress,
   AcceptAllChanges, missing readback, ordinary success/error/cancellation, interceptor composition
   and failure/cancellation inside callbacks. Observe RED before production changes.
2. [x] Route all four direct conflict throw sites in OceanBaseModificationCommandBatch.cs through
   the standard update diagnostics. Return from the conflicted command after suppression. Reuse
   the dependency's UpdateLogger; add no service, dependency or public option.
3. [x] Extend connected transaction fixtures to verify that propagated conflicts dispose the owned
   uncommitted transaction or roll back the caller savepoint, while
   suppressed conflicts can complete an owned transaction/caller savepoint workflow. Verify following
   batches and readback/output handling. Run focused then full Debug/Release and isolated patch gates.
4. [x] Add a packed consumer concurrency-interception smoke, build/verify one baseline package on both
   EF patches and modes, request independent review, update owning docs, diff-check and commit locally.

## Review focus

Every conflict is dispatched once; the exact exception/context/entries survive without suppression.
Async callbacks receive the actual cancellation token and never call the sync hook. Suppression must
not read or propagate stale/missing generated outputs or silently abort later batches. Standard
SaveChanges counting/AcceptAllChanges and transaction handling remain observable. Diagnostic or
interceptor errors must retain the existing wrapping/cancellation rules. Validate public core payload
claims, explicit interception evidence limits, callback ordering and absence of invented native results.

## Evidence

Tested revision: base `2deb9ee` plus this module's final diff; local commit recorded in git log.
Artifacts: artifacts/save-concurrency-interception/.

| Gate | Command / result |
| --- | --- |
| RED | Initial 36 SaveChangesInterceptionTests: 28 failures / 8 passes before the production fix (`red.log`). |
| Focused | Release filter `SaveChangesInterceptionTests\|Concurrency_suppression_controls`: 52/52 pass (`green.log`); 48 interception cases plus four transaction cases. |
| Connected | Release filter `SaveChangesInterceptionTests\|GeneratedValuesTests\|GraphUpdateTests\|TransactionTests`: 186/186 pass (`connected.log`). |
| Baseline full | `dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug/Release --no-restore -m:1 -nr:false`: each passes Unit 2,421, Functional 2,007, Specification 1,538, ConsumerTests 2 (5,968 total), with 16 existing mapped skips (`full-debug.log`, `full-release.log`). |
| Patch full | Isolated EF10.0.14 Release runs the same solution with `--artifacts-path artifacts/save-concurrency-interception/compat-build` and `-p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/save-concurrency-interception/compat/Directory.Packages.props`: same totals (`ef10.0.14-release.log`). Separate restore pins EF packages only; Connector stays 0.3.0 and baseline EF10.0.0 assets remain intact. |
| Package | Baseline Release `dotnet pack --no-build --no-restore -p:PackageVersion=10.0.0-save-concurrency-interception-20261116`; Verify-Package.ps1 content/range/dependency gate passes (`pack.log`, `package-content.log`). |
| Packed runtime | `verify-consumers.ps1` restores only packages and runs all public smokes, including the new concurrency hook, on EF10.0.0/10.0.14 in both modes (`consumers.log`). Exact EF/provider/Connector graphs and absence of project references are checked. |
| Binary provenance | Source Release, archive and both consumer provider DLL SHA256 match: `CAF82CED2B5CD9E7D967FA81F1587B82F588EE1E38590CCE4476A47F7B2A7C68` (`archive-hash.log`, `consumers.log`). |
| Review | Independent read-only review of production, connected executor, tests, package smoke and exact EF10 contracts: no actionable findings. No builds were delegated. |

Fixture corrections: owned transaction failure is asserted as disposal while active, not an explicit
Rollback call; successful SaveChanges(false) leaves generated values in EF's sidecar rather than the
CLR property. Identity acceptance follows the verified EF10 InternalEntryBase.AcceptChanges contract,
not an assumed temporary-key exception. These corrections do not change production behavior.

No fresh migration CLI/bundle or hosted/Linux run: this module changes the tracked-write conflict
boundary only. Native database atomicity, server conflicts/readback, broader official store-backed
runtime fixtures and live acceptance remain open. Required statuses remain unchanged. No push,
publication, live connection or destructive SQL.
