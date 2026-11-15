# EF Core 10 server clock members

**Status:** Local complete; native server clock/session-timezone validation remains open.
**Date:** 2026-11-16
**Feature IDs:** `QRY-07`, `QRY-11`, `MAP-05`

## Scope

Translate `DateTime.Now`, `DateTime.UtcNow` and `DateTime.Today` as server expressions. The slice covers both
MySQL and Oracle modes and keeps `DateTimeOffset` clock members outside the boundary because their offset and
session-timezone semantics require a separate contract.

## Provider implementation

- MySQL uses `CURRENT_TIMESTAMP(6)`, `UTC_TIMESTAMP(6)` and `CURDATE()`.
- Oracle uses `CAST(CURRENT_TIMESTAMP AS TIMESTAMP)`,
  `CAST(SYS_EXTRACT_UTC(SYSTIMESTAMP) AS TIMESTAMP)` and `TRUNC(CURRENT_DATE)`.
- The temporal member translator creates non-nullable `DateTime` results with the existing mode-specific
  `DATETIME`, `TIMESTAMP` or `DATE` mappings. Internal markers are validated and never emitted.
- No Connector change was needed.

## Tests and evidence

The RED queries produced `SELECT 1` without clock expressions before the translator was added; intercepted
materialization also returned the client-side clock values.

- Unit: 2/2 SQL-shape cases pass in Release.
- Functional: 4/4 sync/async MySQL and Oracle cases pass with supplied server-like reader values.
- PackageConsumer includes explicit SQL assertions for all three members in both modes.
- Package `10.0.0-date-time-clocks-20261116` passes content verification and the packed consumer on
  EF10.0.0 and EF10.0.14 in both modes, using the isolated Connector 0.3.0 package cache.
- Full current source regression passes Release Unit 2373, Functional 1911 and Specification 1446 with 16
  explicitly mapped skips; Debug Unit 2373 and Functional 1911 also pass.

The local reader proves SQL generation and DateTime materialization only. It does not prove server clock
accuracy, session timezone configuration, UTC conversion, precision or live OceanBase behavior.

## Acceptance checklist

- [x] MySQL and Oracle SQL functions are explicit and mode-specific.
- [x] `Now`, `UtcNow` and `Today` have synchronous and asynchronous intercepted coverage.
- [x] Results use existing DateTime mappings and no internal marker reaches SQL.
- [x] PackageConsumer has both-mode assertions.
- [x] No Connector change is required.
- [x] Package content and EF10.0.0/10.0.14 packed consumers pass in both modes.
- [ ] Run non-destructive live clock queries in both declared tenants.
- [ ] Re-run final release-revision gates after all Required rows are complete.

## Remaining risk

The supplied endpoint still closes during the pre-authentication handshake. Session timezone, UTC conversion,
precision and server clock behavior therefore remain unverified, and the broad `MAP-05`/`QRY-07` rows remain
`In progress`.
