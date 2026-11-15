# EF Core 10 connection execution and reuse module

**Status:** Completed local module; native/live gates remain open

**Scope:** `CON-04`: sync/async open, command execution, reader disposal, close and connection reuse in
both MySQL and Oracle compatibility modes. `CON-05` timeout/wire-cancellation and `DIA-01/02` logging/error
contracts remain separate. No Connector source or dependency change is in scope.

## Required behavior

- A successful sync or async query opens the provider connection through EF's normal pipeline, executes the
  command, reads all rows and disposes the reader before the operation returns.
- EF closes a provider-owned connection after each operation and can reopen and reuse it for a second
  operation on the same context.
- The generated provider connection retains the selected `ExpectedMode` and MySQL's
  `UseAffectedRows=false` concurrency setting for every reuse.
- MySQL and Oracle follow the same lifecycle contract while retaining their mode-specific SQL/parameter
  behavior. A failed operation must leave the EF-visible connection reusable; physical wire state remains a
  native/live gate.

## Test-first acceptance

1. Add focused registered-provider tests before production edits. Use a connection-opening interceptor only
   to suppress network access and a command interceptor that returns a tracking reader; assert rows,
   command count, reader disposal, connection state and mode settings independently.
2. Run the focused Release tests. If they pass against existing code, record a characterization result and
   do not add a duplicate provider implementation. If they fail, preserve the RED failure, fix the smallest
   shared boundary, then rerun GREEN.
3. Run the full Release Unit, Functional, Specification and Consumer suites. Require zero failures and zero
   skips, then run package content, both EF10 patch consumers and both-patch tooling checks with `-SkipBundle`
   only when the runtime pack is unavailable.
4. Perform a read-only source/test review. Confirm the reader is disposed on success and failure, no
   connection is leaked between operations, and assertions do not treat intercepted rows as server evidence.

## Evidence boundary

The local tests prove EF/provider command construction, mode settings, reader disposal, lease close/reopen and
reuse at the ADO.NET boundary. They do not prove connector wire cancellation, server timeout enforcement,
native pool reuse, server result semantics or live OceanBase behavior. Those remain `Blocked-live` until the
supplied User Secrets endpoints complete a real handshake and the authorized non-destructive matrix runs.

This module is complete only after its focused and full local/package evidence, review and one local commit
are recorded in the acceptance and progress documents. It must not mark `CON-04` Done while native/live
connection gates remain open.

## Recorded evidence

- Focused `Successful_query_disposes_reader_closes_and_reuses_connection`: 4 passed across both modes and
  sync/async paths. The existing provider implementation already satisfied the contract; no production RED/
  GREEN fix or Connector change was needed.
- Release source matrix: Unit 2349, Functional 1873, Specification 868 and Consumer 2 passed, with zero
  failures/skips.
- Package `10.0.0-connection-execution-20261115` passed `Verify-Package.ps1` and PackageConsumer on
  EF10.0.0/10.0.14 in both modes. ToolingConsumer passed catalog, compiled-model and migration/script/remove
  checks on both patches with `-SkipBundle`; bundle remains blocked by missing EF10.0.14 runtime packs.
- Live probe was not rerun. The supplied User Secrets endpoints still close during pre-authentication before
  SQL/DDL, so physical connector cleanup, server timeout/cancellation and native reuse are not claimed.

Local commit: `42ff0e9`.
