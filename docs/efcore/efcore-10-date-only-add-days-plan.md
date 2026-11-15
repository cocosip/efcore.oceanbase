# EF Core 10 `DateOnly.AddDays` Plan

**Status:** Local implementation complete; native OceanBase and release gates remain open.
**Date:** 2026-11-16
**Feature IDs:** `QRY-07`, `QRY-11`, `MAP-05`

## Contract

Translate `DateOnly.AddDays(int)` through the provider's existing `DateOnly` `DATE` carrier.
The operation preserves the `DateOnly` result and .NET day arithmetic; `DateOnly.Now` or timezone
semantics are outside this slice.

- MySQL emits `DATE_ADD(input, INTERVAL days DAY)`.
- Oracle emits `(input + days)`, keeping the Oracle `DATE` carrier for the existing DateOnly reader.
- Only a non-null-converting `DateOnly` to `DATE` mapping and an `int` day argument are admitted.
- Unrelated and null-converting carriers return no translation and remain EF translation errors.
- No Connector API or dependency changed.

## Implementation and tests

The RED query failed in both modes with EF's `DateOnly.AddDays` translation error. The provider now
uses a private marker validated by the SQL generator before emitting either dialect expression.

Focused Release evidence:

- `TemporalMemberQueryTests.DateOnly_add_days*`: 4 passed, 0 failed.
- `DateOnlyDayNumberFunctionalTests.DateOnly_AddDays_materializes_server_value`: 4 passed, 0 failed,
  covering MySQL/Oracle and sync/async intercepted reader materialization.
- Full Debug/Release Unit: 2,387 passed, 0 failed, 0 skipped.
- Full Debug/Release Functional: 1,923 passed, 0 failed, 0 skipped.
- Release Specification: 1,446 passed, 16 mapped skips; ConsumerTests: 2 passed, 0 skipped.
- Package `10.0.0-date-only-add-days-20261116` passed `Verify-Package.ps1` and the packed
  PackageConsumer on EF10.0.0 and EF10.0.14 in both modes.

The intercepted reader and packed consumer establish provider SQL, mapping and package-consumption
behavior only. They do not prove OceanBase server date arithmetic, calendar settings or native coercion.

## Acceptance checklist

- [x] MySQL and Oracle translations are explicit and marker-free in generated SQL.
- [x] Projection and predicate use the same translation.
- [x] Sync and async materialization pass in both modes.
- [x] Unrelated and null-converting carriers are rejected.
- [x] Full source, specification, consumer, package and EF patch checks pass locally.
- [x] No Connector change is required.
- [ ] Run a non-destructive query in both declared OceanBase tenants.
- [ ] Re-run final release and live gates at the release revision.

## Remaining risk

The supplied live endpoint still closes the packet during pre-authentication for both modes, so native
`DATE_ADD`, Oracle date addition, tenant calendar behavior and server coercion remain unverified. The
feature remains `In progress` in the Required matrix until the live and release gates close.
