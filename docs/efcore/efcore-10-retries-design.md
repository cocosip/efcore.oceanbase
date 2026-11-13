# EF10 Opt-in Execution Strategy Retries

**Date:** 2026-11-13. **Base:** 3d58c0f. **IDs:** DEF-09, CON-04/05, QRY-01/08,
UPD-01..05, TXN-01..03, SCH-01/02, DEF-01, PKG-03/04.

## Intent and approach

Provide EF's normal replay mechanism for known retryable server failures, explicitly enabled per context.
Default contexts keep the existing non-retrying strategy. Reuse ExecutionStrategy backoff, retry limits,
cancellation, transaction restrictions, exception unwrapping, query buffering and diagnostics rather than
introducing a second command replay loop. Continue inline in the user's existing checkout; do not edit the
driver or add dependencies. This is the next locally feasible part of DEF-09, not native failover acceptance.

Considered approaches: reconnect/replay individual provider commands cannot preserve a multi-command save's
transaction boundary; a generic retry of all network/timeouts violates Connector's unknown-outcome contract.
Use EF's operation-level strategy with a conservative server-error policy. This implementation choice follows
the existing project architecture and the user's authorization to progress complete local modules.

## Public contract and policy

Add OceanBaseDbContextOptionsBuilder.EnableRetryOnFailure(int maxRetryCount = 6,
TimeSpan? maxRetryDelay = null, IEnumerable<int>? additionalErrorCodes = null). Null delay means 30 seconds.
Validate non-negative count/delay and reject additional code zero (client-local); copy caller collections.
Use RelationalOptionsExtension.WithExecutionStrategyFactory; existing option clones and custom EF service
replacement keep their ordinary semantics. Expose OceanBaseRetryingExecutionStrategy in the Storage namespace,
with the same configuration parameters after ExecutionStrategyDependencies, for standard EF customization.

Retry only OceanBaseException with a nonzero ServerErrorCode, known execution outcome and no XA return code.
Default transient server codes: MySQL 1205 (lock wait timeout), 1213 (deadlock); Oracle 54 (resource busy),
60 (deadlock), 8177 (cannot serialize). SQLSTATE 40001 (serialization failure) also qualifies in either mode.
Additional codes extend the server set but can never override unknown outcome, XA or cancellation exclusion.
Server cancellation (MySQL 1317/3024, Oracle 1013, SQLSTATE 70100/57014) also remains non-retryable,
even if explicitly supplied as an additional code.
Authentication/permissions, uniqueness, syntax, missing objects, generic IOException/TimeoutException and
unclassified client-local errors do not retry. A false unknown flag alone never implies retryability.

Connector 0.3.0's public IsExecutionOutcomeUnknown is authoritative: sent SQL may have executed, and a lost
COMMIT/ROLLBACK acknowledgement cannot be retried or treated as rollback. Do not provide automatic unknown
outcome verification/replay, reconnect or transaction migration. Applications reconcile such failures using
their own business keys/new connection. Raw SQL/app delegates must be replayable; non-database side effects,
nontransactional objects and AutoTransactionBehavior.Never can invalidate replay atomicity. Keep ordinary
automatic save transactions enabled. No distributed transaction recovery is supplied.

User-created transactions must be created inside the execution-strategy delegate; EF rejects already active
caller/ambient/enlisted transactions before replay. Nested operations reuse the outer strategy rather than
multiplying retry budgets. Retrying queries buffer results, so a failure during reading can replay the whole
query before any result is delivered; document the memory cost.

Connector transaction Dispose suppresses rollback failures, so disposal alone cannot certify replay safety.
Provider-owned EF transactions running under a retrying strategy must explicitly Rollback/RollbackAsync
(CancellationToken.None) before disposing if Commit/Rollback has not completed. This applies both to automatic
SaveChanges transactions and owned transactions created inside a user strategy delegate. Keep caller-owned
native transaction ownership unchanged. Reuse the existing relational transaction factory/Oracle release
subclass, tracking completed transaction ends and preserving explicit rollback errors across disposal.
Any rollback/disposal/lease cleanup failure vetoes retry. Preserve the primary failure and entries; propagate
an internal veto marker down its InnerException chain because EF unwraps DbUpdateException before classification.
Do not replay while EF still has an active transaction after failed cleanup. This is not physical rollback
acceptance; deterministic tests verify explicit calls and failure propagation.

Commit-stage failures never replay automatically, including confirmed server codes: EF's commit method also
runs post-commit callbacks, so a thrown exception cannot establish that physical commit failed. Failed startup
after a native transaction handle exists also vetoes replay; the EF wrapper has not yet taken ownership and
native disposal cannot certify rollback. Normal non-retry transaction disposal retains its existing behavior.

An injected command interceptor wraps readers only for configured/current execution strategies. It preserves
read/getter/NextResult failures if native disposal fails and marks cleanup failures non-retryable. This closes
EF's buffered-reader finally path, which otherwise replaces an unknown read outcome with a cleanup error.
Provider Close/CloseAsync also veto exceptions, including post-close callbacks after successful bulk writes.
Automatic saves retain their primary write exception and entries, attaching transaction cleanup errors in Data.
Application using/await-using delegates follow C# disposal exception precedence; failed cleanup surfaces and
prevents retry. Application interceptor/service replacements must preserve these safety boundaries, and any
non-database side effects in callbacks remain the application's responsibility.

## DDL and lifecycle boundary

Migrate/MigrateAsync already reject retrying strategies because DDL commits implicitly. Also reject when
ExecutionStrategy.Current is active: RetriesOnFailure becomes false on a different strategy instance while an
outer strategy executes, so testing that property alone is insufficient. Apply the same early rejection to table/schema/database/user creation
and deletion, before opening connections, invoking management/password factories or seeding callbacks.
GenerateCreateScript and migration script generation remain usable because they do not execute DDL.
Database.ExecuteSqlRaw follows EF's explicit strategy semantics; arbitrary raw DDL is application-owned.

## Evidence and limits

Before implementation, add failing option/factory/classification tests and in-strategy migration/DDL guards.
Verify real EF query/read buffering, SaveChanges, bulk DML, exhaustion, cancellation, nested execution,
caller transaction policy, original exception/entry identity and safe state for a subsequent operation.
Use both modes and sync/async. Deterministic failures carry real Connector exception metadata via the test
constructor seam; native doubles must not be represented as physical rollback or server failover evidence.
Check retry configuration isolation and EF compiled-query buffering cache keys. Add a package-only public
consumer and prove the old package fails the new feature before packing one baseline binary for both patches.
Run full source Debug/Release and isolated EF10.0.14, packed runtime/CLI compatibility, independent module
review, fix findings, update current support/progress docs and commit locally. No push/publication implied.

Native transient-code behavior, rollback/commit outcomes, failover and timing still need controlled server
acceptance. Existing read-only live probes failed during connection opening and have no changed conditions;
do not repeat them or request driver changes for that unchanged evidence.

References: Connector 0.3.0 OceanBaseException and docs/failure-handling.md; exact EF10 ExecutionStrategy,
RelationalExecutionStrategyFactory and relational query-cache/buffering contracts; MySQL ER_LOCK_WAIT_TIMEOUT /
ER_LOCK_DEADLOCK and Oracle ORA-00054 / ORA-00060 / ORA-08177 server error meanings.
