# EF Core 10 `TimeSpan.Ticks` Plan

**Status:** Local complete; native OceanBase and release gates remain open.
**Date:** 2026-11-16
**Feature IDs:** `QRY-07`, `QRY-11`, `MAP-05`

## Scope

Translate `TimeSpan.Ticks` for the provider's existing MySQL `TIME` and Oracle `INTERVAL DAY TO SECOND`
carriers. The result is the signed number of 100ns ticks represented by the duration. Fractional values are
truncated to the .NET tick boundary when the Oracle interval has finer precision.

The module covers projection, predicates, ordering, sync/async materialization, NULL propagation and the
unsupported mapping boundary. It does not change Connector codecs or temporal parameter conversion.

## Provider implementation

- The temporal member translator admits only a `TimeSpan` expression with the existing non-null-converting
  mode-specific duration carrier and returns an `Int64` result mapping.
- MySQL mode emits `TIME_TO_SEC(input) * 10000000` plus the signed absolute microsecond remainder multiplied
  by 10. The existing sign helper handles negative subsecond `TIME` values.
- Oracle mode combines `EXTRACT(DAY|HOUR|MINUTE|SECOND)` into ticks and truncates the second fraction at
  seven decimal places.
- Text, unrelated carriers and null-converting converters return no translation and remain EF translation
  errors before command execution.

The generated SQL uses a private marker consumed only by the provider SQL generator. The marker validates the
result type and the mode-specific TimeSpan carrier before emitting SQL.

## Tests and evidence

The RED query failed in both modes before the translator and SQL generator changes.

Focused Release evidence:

- `TemporalMemberQueryTests.TimeSpan_Ticks*`: 4 passed, 0 failed.
- `TimeSpanTicksFunctionalTests`: 4 passed, 0 failed.
- Functional cases cover sync and async execution, projection, predicate, exact 100ns values and NULL
  propagation using the existing offline reader interception boundary.
- The standalone PackageConsumer includes a `TimeSpan.Ticks` SQL assertion for both modes.
- Package `10.0.0-time-span-ticks-20261116` passed `Verify-Package.ps1` and the standalone PackageConsumer
  on EF10.0.0 and EF10.0.14 in both modes.

The intercepted reader proves provider SQL/materialization behavior only. It does not prove OceanBase server
function availability, interval sign rules, precision coercion or native result behavior.

## Acceptance checklist

- [x] MySQL and Oracle translator paths are explicit.
- [x] Exact 100ns tick scaling is asserted.
- [x] Projection, predicate and ordering use the same translation.
- [x] Sync and async materialization pass in both modes.
- [x] NULL input propagates to a nullable projection.
- [x] Unrelated and null-converting carriers are rejected before SQL generation.
- [x] No Connector change is required.
- [x] Unique package content and both selected EF10 package consumers pass.
- [ ] Run a real non-destructive query in both declared OceanBase tenants.
- [ ] Re-run package and release-revision gates after the module commit.

## Remaining risk

The supplied live endpoint still closes the packet during pre-authentication for both modes, so native
`TIME_TO_SEC`, `EXTRACT`, interval sign behavior and sub-tick coercion remain unverified. The feature remains
`In progress` in the Required matrix until the live gate and broader release evidence close.
