# EF Core 10 `DateTimeOffset.UtcTicks` Plan

**Status:** Local implementation complete; native OceanBase and release gates remain open.
**Date:** 2026-11-16
**Feature IDs:** `QRY-07`, `QRY-11`, `MAP-05`

## Scope

Translate `DateTimeOffset.UtcTicks` as the signed `Int64` number of 100ns ticks since the Gregorian
epoch `0001-01-01T00:00:00` in UTC. The module uses the provider's existing UTC-normalized
`DateTimeOffset` storage contract: MySQL stores the UTC `DATETIME` carrier and Oracle extracts UTC
from the `TIMESTAMP WITH TIME ZONE` carrier. It does not claim support for `DateTimeOffset.Ticks`,
`DateTimeOffset.Offset` or `DateTimeOffset.LocalDateTime`, whose local-offset
semantics require a separate contract.

## Provider implementation

- The temporal member translator admits only a `DateTimeOffset` expression with its existing
  non-null-converting UTC carrier and returns an `Int64` result mapping.
- Translation composes the existing `DateTimeOffset.UtcDateTime` marker with the existing
  `DateTime.Ticks` marker, so the Gregorian epoch and 100ns arithmetic have one implementation.
- MySQL evaluates the normalized `DATETIME` carrier directly. Oracle evaluates
  `SYS_EXTRACT_UTC` before the existing `DATE`/`TIMESTAMP` tick arithmetic.
- Unrelated carriers and null-converting converters return no translation and remain EF translation
  errors before command execution.

## Tests and evidence

The RED query failed in both modes with EF's standard translation error before the translator change.

Focused Release evidence:

- `TemporalMemberQueryTests.DateTimeOffset_UtcTicks_translate_from_the_utc_gregorian_epoch`: 2 passed.
- `TemporalMemberQueryTests.DateTimeOffset_UtcTicks_reject_unowned_and_null_converting_carriers`: 2 passed.
- `TimeSpanTicksFunctionalTests.DateTimeOffset_UtcTicks_materialize_from_the_utc_gregorian_epoch`: 4 passed,
  covering both modes, sync/async execution, projection, predicate, exact UTC tick values and NULL
  propagation through the existing offline reader interception boundary.
- The packed PackageConsumer contains SQL assertions for both modes.

These tests prove local translation, mapping, binding and materialization only. They do not prove server
timezone/session behavior, timestamp precision, native date arithmetic or live OceanBase execution.

## Acceptance checklist

- [x] MySQL and Oracle UTC carrier paths are explicit.
- [x] Gregorian epoch and 100ns scaling reuse the existing `DateTime.Ticks` implementation.
- [x] Oracle applies UTC extraction before tick arithmetic.
- [x] Sync and async materialization pass in both modes.
- [x] NULL input propagates to a nullable projection.
- [x] Unowned and null-converting carriers reject before SQL generation.
- [x] Packed consumer SQL assertions cover both modes.
- [x] Package content and selected EF10 package-consumer gates pass for EF10.0.0 and EF10.0.14 in both modes.
- [ ] Run a real non-destructive query in both declared OceanBase tenants.
- [ ] Run the final release-revision gates after all Required rows are complete.

## Remaining risk

The supplied live endpoint still closes the packet during pre-authentication for both modes, so native
timezone conversion, timestamp precision and server behavior remain unverified. The feature remains
`In progress` in the Required matrix until the live gate and broader release evidence close.
