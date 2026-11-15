# EF Core 10 DatabaseFacade service graph module

**Status:** Completed local characterization; native/schema gates remain open

**Scope:** `CFG-04`: prove that the EF10 `DatabaseFacade` dependency graph resolves in both
compatibility modes and that the ordinary read-only facade entry points use the registered provider
services. This module does not claim native OceanBase lifecycle behavior and does not change
`OceanBaseConnector`.

## Approach

1. Resolve the public relational facade dependencies and the services consumed by EF's database and
   migration facade extensions from a real provider-registered `DbContext`.
2. Assert scoped identity for the database creator and relational connection, and assert that the
   provider's mode-specific SQL remains selected through `GenerateCreateScript`.
3. Exercise `CanConnect`, `Exists`, `HasTables` and `GenerateCreateScript` through the normal facade/
   creator APIs with the existing network-suppressing command interceptor.
4. Run both modes, the focused test, the full Release source matrix and package consumers. Record
   native schema, authentication, permission and deletion behavior as a separate live gate.

## Acceptance boundary

Local success proves service registration, scoped lifetime, facade dependency wiring and SQL generation.
It does not prove server existence classification, privileges, DDL, deletion, pool cleanup or native
OceanBase results. Those remain `Blocked-live` under `SCH-01..03` and `TST-02` until a real handshake
and authorized non-destructive matrix succeed.

## Test-first task

- Add `DatabaseFacadeServiceGraphTests` before any production edit.
- If the registered provider already satisfies the contract, keep the test as characterization and make
  no duplicate implementation.
- If a service or facade path fails, preserve the RED result and fix only the provider registration or
  boundary that owns it.

## Required evidence

- MySQL and Oracle, with zero failures and zero skips.
- `Verify-Package.ps1` and PackageConsumer on EF10.0.0 and EF10.0.14.
- ToolingConsumer catalog/compiled-model/migration checks; use `-SkipBundle` only when the runtime pack
  is unavailable and record that limitation.
- A local commit and an entry in `efcore-10-development-acceptance-plan.md`, `efcore-10-progress.md`
  and `efcore-10-feature-checklist.md`.

## Recorded evidence

- The focused MySQL/Oracle test passes with two cases and zero failures/skips.
- Release source suites pass Unit 2349, Functional 1879 and Specification 868; the two package consumers
  also pass.
- Package `10.0.0-cfg04-20261115` passes content and both EF10.0.0/10.0.14 package consumers in both
  modes. Tooling catalog, compiled-model and migration/script/remove checks pass on both patches with
  `-SkipBundle`; the bundle substep remains unavailable because the EF10.0.14 runtime pack is absent.
- No provider production code or `OceanBaseConnector` code changed. Native schema, privilege, deletion and
  live OceanBase behavior remain open.

Local commit: `d4fc4df`.
