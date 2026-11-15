# EF Core 10 timeout and cancellation module

**Status:** Completed local module; native/live gates remain open

**Scope:** `CON-05`: command timeout propagation, pre-cancelled query/save short-circuiting, in-flight
cancellation identity and resource release for both compatibility modes. Native wire cancellation, server
timeout enforcement and pool behavior remain live gates. No `OceanBaseConnector` source or dependency change
is in scope.

## Required behavior

- `Database.SetCommandTimeout` reaches every created provider command.
- A pre-cancelled async query or save does not open a connection or execute a command, and leaves the
  EF-visible connection closed.
- An in-flight sync/async cancellation preserves the original `OperationCanceledException` and token,
  disposes readers/commands, and leaves the context reusable for a later operation.
- MySQL and Oracle follow the same EF cancellation contract; mode-specific SQL and parameter mapping remain
  unchanged.

## Test-first acceptance

1. Add focused registered-provider tests before production edits for pre-cancelled query/save and existing
   timeout/in-flight paths. Assert command counts, cancellation tokens, reader disposal, connection state and
   retry behavior independently.
2. Run focused Release tests. If the existing EF/provider path passes, record characterization evidence and
   avoid duplicate production code. If a defect appears, preserve RED, fix the shared boundary minimally,
   then rerun GREEN.
3. Run the full Release Unit, Functional, Specification and Consumer suites, then package content, both EF10
   patch consumers and both-patch tooling with `-SkipBundle` only when runtime packs are unavailable.
4. Review that intercepted cancellation is not described as wire/server evidence and that cleanup errors do
   not replace the primary cancellation.

## Evidence boundary

Local tests prove EF command timeout assignment, cancellation identity, pre-cancel short-circuiting and
EF-visible cleanup. They do not prove connector wire cancellation, server-side timeout enforcement, native
pool release or live OceanBase behavior. Keep those states `Blocked-live` until a real handshake and the
authorized non-destructive matrix succeed.

## Recorded evidence

- New `Pre_cancelled_query_does_not_open_or_execute` and
  `Pre_cancelled_save_does_not_open_or_execute` cases pass 4 focused combinations across MySQL and Oracle;
  existing timeout and in-flight cancellation cases remain green.
- Release source matrix: Unit 2349, Functional 1877, Specification 868 and Consumer 2 passed, with zero
  failures/skips. The existing implementation already satisfied the local contract; no production RED/GREEN
  fix or Connector change was needed.
- Package `10.0.0-timeout-cancellation-20261115` passed `Verify-Package.ps1` and PackageConsumer on
  EF10.0.0/10.0.14 in both modes. ToolingConsumer passed catalog, compiled-model and migration/script/remove
  checks on both patches with `-SkipBundle`; bundle remains blocked by missing EF10.0.14 runtime packs.
- Live probe was not rerun. The supplied User Secrets endpoints still close during pre-authentication before
  SQL/DDL, so wire cancellation, server timeout enforcement and native pool release are not claimed.

Local commit: `16da9b7`.
