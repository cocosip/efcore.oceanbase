# EF Core 10 `DateOnly.AddYears` Plan

**Status:** Local implementation complete; native OceanBase and release gates remain open.
**Date:** 2026-11-16
**Feature IDs:** `QRY-07`, `QRY-11`, `MAP-05`

## Contract

Translate `DateOnly.AddYears(int)` through the provider's existing `DateOnly` `DATE` carrier.
The operation preserves the `DateOnly` result; native arithmetic remains unverified. Timezone and
local-clock semantics are outside this slice.

- MySQL emits `DATE_ADD(input, INTERVAL years YEAR)`.
- Oracle anchors ADD_MONTHS at `TRUNC(input, 'MM')` with `(years) * 12`, then clamps the original day
  to the target month's last day. It retains DATE and requires NLS_CALENDAR=GREGORIAN.
- Only a non-null-converting `DateOnly` to `DATE` mapping and an `int` year argument are admitted.
- Unrelated and null-converting carriers return no translation and remain EF translation errors.
- No Connector API or dependency changed.

## Implementation and tests

The RED query failed in both modes with EF's `DateOnly.AddYears` translation error. The provider now
uses a private marker validated by the SQL generator before emitting either dialect expression.

Initial checkpoint evidence (historical; the correction below supersedes its Oracle SQL):

- `TemporalMemberQueryTests.DateOnly_add_years*`: 4 passed, 0 failed.
- `DateOnlyDayNumberFunctionalTests.DateOnly_AddYears_materializes_server_value`: 4 passed, 0 failed,
  covering MySQL/Oracle and sync/async intercepted reader materialization.
- Full Debug/Release Unit: 2,395 passed, 0 failed, 0 skipped.
- Full Debug/Release Functional: 1,931 passed, 0 failed, 0 skipped.
- Release Specification: 1,446 passed, 16 mapped skips; ConsumerTests: 2 passed, 0 skipped.
- Package `10.0.0-date-only-add-years-20261116` passed `Verify-Package.ps1` and the packed
  PackageConsumer on EF10.0.0 and EF10.0.14 in both modes.

The intercepted reader and packed consumer establish provider SQL, mapping and package-consumption
behavior only. They do not prove OceanBase year arithmetic, leap-day rules, calendar settings or native coercion.

## Acceptance checklist

- [x] MySQL and Oracle translations are explicit and marker-free in generated SQL.
- [x] Projection and predicate use the same translation.
- [x] Sync and async materialization pass in both modes.
- [x] Unrelated and null-converting carriers are rejected.
- [x] Full source, specification, consumer, package and EF patch checks pass locally.
- [x] No Connector change is required.
- [ ] Run non-destructive year arithmetic queries in both declared OceanBase tenants.
- [ ] Re-run final release and live gates at the release revision.

## Remaining risk

The 2026-11-16 [DateOnly calendar correction](efcore-10-date-only-calendar-fix-plan.md) fixes both
the source-month-end rule and missing compound-year grouping. For example, `Id + 1` now becomes
`(Id + 1) * 12`. New SQL regressions catch the old renderer; intercepted values do not establish native
calendar equivalence. The initial constant-amount checkpoint above remains historical evidence only.

The supplied live endpoint still closes the packet during pre-authentication for both modes, so native
`DATE_ADD`, `ADD_MONTHS`, leap-day behavior, tenant calendar behavior and server coercion remain unverified.
The feature remains `In progress` in the Required matrix until the live and release gates close.
