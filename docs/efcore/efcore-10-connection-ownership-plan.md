# EF Core 10 connection ownership module

**Status:** Active local module

**Scope:** `CON-03` (caller-owned/provider-owned connection lifetime). `CON-04` and `CON-05`
remain separate follow-up modules except where this module must prove cleanup after an open or command
failure. No `OceanBaseConnector` source or dependency change is in scope.

## Required behavior

The provider must preserve the ownership selected by `UseOceanBase` for every context lifetime path:

| Connection supplied by | Entry state | Context disposal | Required result |
| --- | --- | --- | --- |
| Caller | Closed | Sync or async, after successful open | Native connection remains usable and undisposed. |
| Caller | Open | Sync or async, after successful open/reuse | Native connection remains open and usable; EF releases only its lease. |
| Caller | Closed/open | Sync or async after query/open failure | Caller connection remains usable; the provider may release only its own lease. |
| Provider | Closed | Sync or async context disposal | Provider-created native connection is disposed. |
| Provider | Opened by EF | Sync or async context disposal | Provider-created native connection is closed and disposed. |
| Provider | Open/command failure | Sync or async context disposal | The provider-owned connection is cleaned up; the original operation error is preserved. |

The same rules apply in MySQL and Oracle compatibility modes. A nested EF open lease must not cause
context disposal to close a caller-opened connection. A provider-created connection must not remain
usable after its owning context is disposed.

## Code boundaries

- `src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRelationalConnection.cs` owns mode validation,
  open/close interception and the provider-created connection marker.
- EF Core `RelationalConnection` owns the normal context ownership flag and disposal path; do not copy
  that implementation into the provider unless a failing regression identifies a provider defect.
- `tests/OceanBase.EntityFrameworkCore.UnitTests/ConnectionOwnershipTests.cs` exercises the registered
  provider with a network-suppressing interceptor and checks both sync/async disposal paths.
- Existing `ConnectionLifecycleTests` remains the accounting/failed-open regression suite. Its physical
  `DbConnection` double proves state restoration, not OceanBase wire cleanup.

## Test-first acceptance

1. Add the ownership matrix tests before any production edit. Run the focused Release test and record
   whether the current EF/provider implementation already satisfies the contract. The 16 cases passed
   against the existing implementation, so this module has characterization evidence rather than a
   production RED/GREEN fix.
2. If a test fails, keep the failure as the RED evidence, fix only the shared provider boundary, and
   rerun the same test GREEN. No connector change is allowed without explicit user approval.
3. Run the complete Unit and Functional Release suites in both modes, then Specification and consumer
   checks. The result must report zero failures and zero skips. This module's result is Unit 2349,
   Functional 1869, Specification 868 and Consumer 2.
4. Repeat package content and package-consumer checks on EF10.0.0 and EF10.0.14 using the same packed
   provider binary. Tooling checks may use `-SkipBundle` only when the required runtime pack is absent;
   record that bundle limitation explicitly. Package
   `10.0.0-connection-ownership-20261115` passed `Verify-Package.ps1` and PackageConsumer in both modes
   on both patches. ToolingConsumer passed catalog, compiled-model and migration/script/remove checks on
   both patches with `-SkipBundle`; the bundle check remains blocked by missing EF10.0.14 runtime packs.
5. Run an independent source/test review. The review must confirm that caller connections are never
   disposed, provider connections are disposed on success and error, and no test substitutes a fake
   server for live connector evidence.

## Evidence boundary and release gate

Passing local tests proves EF ownership flags, lease accounting, disposal calls and reuse behavior at the
provider/ADO.NET boundary. It does not prove OceanBase server pool cleanup, wire-level cancellation,
native handshake mode detection, or live transaction behavior. Those remain `Blocked-live` until the
supplied User Secrets endpoints complete a real handshake and the authorized non-destructive matrix runs.

The module is complete only when the ownership matrix, focused RED/GREEN evidence (or a documented
characterization result when no provider change is needed), full local suites, package consumers and review
are recorded in `efcore-10-development-acceptance-plan.md` and `efcore-10-progress.md`, followed by the
local commit `f116d6e`. It is not a release claim and must not mark `CON-03` Done while native/live gates
remain open.
