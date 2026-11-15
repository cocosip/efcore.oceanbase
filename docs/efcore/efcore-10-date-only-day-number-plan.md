# EF Core 10 `DateOnly.DayNumber` Plan

**Status:** Local complete; native OceanBase and release gates remain open.
**Date:** 2026-11-16
**Feature IDs:** `QRY-07`, `QRY-11`, `MAP-05`

## Scope

Translate `DateOnly.DayNumber` for the provider's existing `DATE` mapping in both compatibility modes.
`DayNumber` is the zero-based number of days since `0001-01-01`; therefore `0001-01-01` must translate to
zero and `2024-02-29` to `738944`.

The module covers projection, predicate, sync/async materialization and unsupported mapping boundaries.
It does not add a new Connector API or change DateOnly parameter/read conversion.

## Provider implementation

- The temporal member translator admits only a `DateOnly` expression with its existing non-null-converting
  `DATE` carrier and returns an `int` result mapping.
- MySQL mode emits `DATEDIFF(input, '0001-01-01')`.
- Oracle mode emits `TRUNC(input) - DATE '0001-01-01'`.
- Text, unrelated carriers and null-converting converters return no translation and remain EF translation
  errors before command execution.

The generated SQL uses a private marker consumed only by the provider SQL generator. The marker is validated
for expression shape, result type and DateOnly mapping before SQL is emitted.

## Tests and evidence

The RED query failed in both modes before the translator and SQL generator changes.

Focused Release evidence:

- `TemporalMemberQueryTests.DateOnly_DayNumber*`: 4 passed, 0 failed.
- `DateOnlyDayNumberFunctionalTests`: 4 passed, 0 failed.
- Functional cases cover sync and async execution, projection, predicate, NULL propagation and the
  mode-specific SQL shape using the existing offline reader interception boundary.
- Package `10.0.0-date-only-day-number-20261116` passed `Verify-Package.ps1` and the standalone
  PackageConsumer on EF10.0.0 and EF10.0.14 in both modes. The consumer includes an explicit
  `DateOnly.DayNumber` SQL assertion.

The intercepted reader proves provider SQL/materialization behavior only. It does not prove OceanBase server
function availability, date arithmetic, calendar settings or native result coercion.

## Acceptance checklist

- [x] MySQL and Oracle translator paths are explicit.
- [x] `DayNumber` zero-based semantics are asserted with a known Gregorian value.
- [x] Projection and predicate use the same translation.
- [x] Sync and async materialization pass in both modes.
- [x] NULL input propagates to a nullable projection.
- [x] Unrelated and null-converting carriers are rejected before SQL generation.
- [x] No Connector change is required.
- [x] Unique package content and both selected EF10 package consumers pass.
- [ ] Run a real non-destructive query in both declared OceanBase tenants.
- [ ] Re-run package and release-revision gates after the module commit.

## Remaining risk

The supplied live endpoint still closes the packet during pre-authentication for both modes, so native
`DATEDIFF`/Oracle date subtraction, tenant calendar behavior and server coercion remain unverified. The
feature remains `In progress` in the Required matrix until the live gate and broader release evidence close.
