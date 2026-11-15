# EF Core 10 `TimeOnly.Ticks` Plan

**Status:** Local complete; native OceanBase and release gates remain open.
**Date:** 2026-11-16
**Feature IDs:** `QRY-07`, `QRY-11`, `MAP-05`

## Scope

Translate `TimeOnly.Ticks` as the non-negative number of 100ns ticks since midnight through the provider's
existing MySQL `TIME` and Oracle `INTERVAL DAY TO SECOND` carriers. The same SQL marker is shared with
`TimeSpan.Ticks`, but TimeOnly uses midnight-based semantics and never emits a negative duration sign.

The module covers projection, predicates, sync/async materialization and NULL propagation. It does not change
Connector codecs or temporal parameter conversion.

## Provider implementation

- The temporal member translator admits only a `TimeOnly` expression with its existing non-null-converting
  mode-specific duration carrier and returns an `Int64` result mapping.
- MySQL mode combines `TIME_TO_SEC(input)` with the absolute microsecond remainder, scaled to 100ns ticks;
  the duration sign branch is replaced with a positive constant for TimeOnly.
- Oracle mode reuses the interval component expression used by `TimeSpan.Ticks`.
- Unrelated carriers and null-converting converters return no translation and remain EF translation errors
  before command execution.

## Tests and evidence

The RED query failed in both modes before the translator admitted `TimeOnly.Ticks`.

Focused Release evidence:

- `TemporalMemberQueryTests.TimeOnly_Ticks_translate_from_midnight_in_both_modes`: 2 passed, 0 failed.
- `TimeSpanTicksFunctionalTests.TimeOnly_Ticks_materialize_from_midnight`: 4 passed, 0 failed.
- Functional cases cover sync and async execution, projection, predicate, exact 100ns values and NULL
  propagation using the existing offline reader interception boundary.
- The standalone PackageConsumer includes a `TimeOnly.Ticks` SQL assertion for both modes.
- Package `10.0.0-time-only-ticks-20261116` passed `Verify-Package.ps1` and the standalone PackageConsumer
  on EF10.0.0 and EF10.0.14 in both modes.

The intercepted reader proves provider SQL/materialization behavior only. It does not prove native interval
precision, server coercion or calendar/session behavior.

## Acceptance checklist

- [x] MySQL and Oracle paths use the existing temporal carriers.
- [x] Midnight-based non-negative tick semantics are asserted.
- [x] Sync and async materialization pass in both modes.
- [x] NULL input propagates to a nullable projection.
- [x] No Connector change is required.
- [x] Unique package content and both selected EF10 package consumers pass.
- [ ] Run a real non-destructive query in both declared OceanBase tenants.
- [ ] Re-run package and release-revision gates after the module commit.

## Remaining risk

The supplied live endpoint still closes the packet during pre-authentication for both modes, so native time
arithmetic, interval precision and server behavior remain unverified. The feature remains `In progress` in the
Required matrix until the live gate and broader release evidence close.
