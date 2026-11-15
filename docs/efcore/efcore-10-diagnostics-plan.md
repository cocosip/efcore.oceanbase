# EF Core 10 diagnostics and error-boundary module

**Status:** Completed local characterization; live connector gates remain open

**Scope:** `DIA-01/02`: command logging and sensitive-data protection, provider failure identity,
failing entry retention and cancellation propagation for both compatibility modes. The module reuses
EF's diagnostics pipeline and does not add a provider logging wrapper or change `OceanBaseConnector`.

## Required behavior

- Successful and failed commands emit EF command diagnostics in sync and async paths.
- Parameter values are absent from default logs and appear only after
  `EnableSensitiveDataLogging`; Oracle user passwords remain redacted even when sensitive logging is on.
- Connector failures are exposed as `DbUpdateException` with the original connector exception and
  failing entries preserved.
- An `OperationCanceledException` from an in-flight save is returned unchanged with its original token;
  it is not wrapped as `DbUpdateException`.

## Evidence

- `CommandLoggingTests` covers both modes, sync/async success and failure, and sensitive/default redaction.
- `GraphUpdateTests` covers both modes, sync/async connector failures, generated readback failure,
  failing entry identity, in-flight cancellation, token propagation and pre-cancelled save short-circuiting.
- PackageConsumer `SchemaSmoke` covers packed Oracle credential redaction in sync/async lifecycle paths.

## Acceptance boundary

Local and package evidence proves EF/provider diagnostics and exception boundaries at the intercepted
ADO.NET layer. It does not prove native server error text, network disconnect diagnostics, wire cancellation,
or production logging sink policy; those remain `Blocked-live` under `TST-02`.

No provider production or Connector change was required for this characterization.
