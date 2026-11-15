# EF Core 10 `DateTimeOffset` member plan

**Status:** Local implementation complete for the listed members; native temporal acceptance remains open.
**Date:** 2026-11-16
**Feature IDs:** `MAP-05`, `QRY-07`, `QRY-11`

## Scope

Translate the `DateTimeOffset` calendar, fractional and carrier members that have a provider mapping:
`Year`, `Month`, `Day`, `Hour`, `Minute`, `Second`, `Millisecond`, `Microsecond`, `Nanosecond`,
`DayOfYear`, `DayOfWeek`, `Date` and `TimeOfDay`.

MySQL follows the provider baseline and normalizes `DateTimeOffset` to UTC `DateTime` storage. Oracle keeps the
numeric offset through its timestamp mapping. The module does not add offset conversion, `Ticks`, arithmetic,
native precision/range claims or Connector APIs.

## Implementation and boundaries

- `OceanBaseTemporalMemberTranslator` admits only the matching `DateTimeOffset` carrier and rejects unrelated or
  null-converting mappings before SQL generation.
- MySQL uses the existing `YEAR`/`MONTH`/`DAYOFMONTH`/`HOUR`/`MINUTE`/`SECOND`/`MICROSECOND` functions,
  `DATE` and `TIME`; Oracle reuses `EXTRACT`, `TRUNC`, and the existing `INTERVAL DAY TO SECOND` path.
- The SQL generator accepts `DateTimeOffset` only at these internal temporal boundaries and never emits provider
  marker names.
- No public API, dependency or OceanBaseConnector change is required.

## Acceptance evidence

| Layer | Result |
| --- | --- |
| Unit SQL shape | Release focused `DateTimeOffset` selection: 8/8 passed across both modes and negative carrier boundaries |
| Functional materialization | Release focused `DateTimeOffset` selection: 8/8 passed, sync/async and both modes; Date/TimeOfDay use MySQL `TimeSpan` or Oracle interval readers |
| Package consumer | Added both-mode SQL-shape assertions for calendar, fractional, `Date` and `TimeOfDay` members |
| Full local suites | Release/Debug Unit 2377 and Functional 1915; Specification 1446 passed with 16 mapped skips |
| Native OceanBase | Open: supplied endpoint still closes during pre-authentication handshake |

## Verification commands

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~DateTimeOffset"
dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests/OceanBase.EntityFrameworkCore.FunctionalTests.csproj -c Release --no-restore --filter "FullyQualifiedName~DateTimeOffset"
```

This module may be marked locally complete after the focused tests, full source suites, package content checks and
both EF10.0.0/10.0.14 PackageConsumer runs pass. `MAP-05`, `QRY-07`, `QRY-11`, `TST-02` and release rows remain
`In progress` until live server evidence and the final release revision are accepted.
