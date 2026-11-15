# EF Core 10 `DateTime.Ticks` Plan

**Status:** Local complete; native OceanBase and release gates remain open.
**Date:** 2026-11-16
**Feature IDs:** `QRY-07`, `QRY-11`, `MAP-05`

## Scope

Translate `DateTime.Ticks` as the signed `Int64` number of 100ns ticks since the Gregorian epoch
`0001-01-01T00:00:00`. The module covers MySQL `DATETIME` and Oracle `DATE`/`TIMESTAMP` mappings. It does not
extend the behavior to `DateTimeOffset.Ticks`, whose offset semantics require a separate acceptance boundary.

## Provider implementation

- The temporal member translator admits only a `DateTime` expression with its existing non-null-converting
  date carrier and returns an `Int64` result mapping.
- MySQL mode combines `DATEDIFF` date days, `TIME_TO_SEC(TIME(input))` whole seconds and the microsecond
  remainder at 100ns precision.
- Oracle `DATE` mode combines truncated date days with the day fraction. Oracle `TIMESTAMP` mode combines
  truncated date days with `EXTRACT` hour/minute/second components and truncates at seven fractional digits.
- Unrelated and null-converting carriers return no translation and remain EF translation errors before command
  execution.

## Tests and evidence

The RED query failed in both modes before the translator and SQL generator changes.

Focused Release evidence:

- `TemporalMemberQueryTests.DateTime_Ticks_translate_from_the_gregorian_epoch`: 2 passed, 0 failed.
- `TimeSpanTicksFunctionalTests.DateTime_Ticks_materialize_from_the_gregorian_epoch`: 4 passed, 0 failed.
- Functional cases cover sync and async execution, projection, predicate, exact epoch-based tick values and
  NULL propagation using the existing offline reader interception boundary.
- The standalone PackageConsumer includes a `DateTime.Ticks` SQL assertion for both modes.
- Package `10.0.0-date-time-ticks-20261116`, rebuilt from commit `45c81fb` after the Oracle `DATE` fraction
  correction, passed `Verify-Package.ps1` and the standalone PackageConsumer on EF10.0.0 and EF10.0.14 in
  both modes.

The intercepted reader proves provider SQL/materialization behavior only. It does not prove server date
arithmetic, timestamp precision, calendar/session behavior or native coercion.

## Acceptance checklist

- [x] MySQL and Oracle `DateTime` carrier paths are explicit.
- [x] Gregorian epoch and 100ns scaling are asserted.
- [x] Oracle DATE and TIMESTAMP paths are generated separately.
- [x] Sync and async materialization pass in both modes.
- [x] NULL input propagates to a nullable projection.
- [x] `DateTimeOffset.Ticks` remains outside this module pending offset semantics.
- [x] No Connector change is required.
- [x] Unique package content and both selected EF10 package consumers pass.
- [ ] Run a real non-destructive query in both declared OceanBase tenants.
- [x] Re-run package content and selected EF10 package-consumer gates after commit `45c81fb`.
- [ ] Run the final release-revision gates after all Required rows are complete.

## Remaining risk

The supplied live endpoint still closes the packet during pre-authentication for both modes, so native date
arithmetic, timestamp precision and server behavior remain unverified. The feature remains `In progress` in the
Required matrix until the live gate and broader release evidence close.
