# EF Core 10 `DateTimeOffset.UtcNow` plan

**Status:** Local implementation complete; native server clock and timezone acceptance remains open.
**Date:** 2026-11-16
**Feature IDs:** `MAP-05`, `QRY-07`, `QRY-11`

## Contract

`DateTimeOffset.UtcNow` returns the current instant with a zero offset.

- MySQL emits `UTC_TIMESTAMP(6)` and uses the existing UTC-normalized `DATETIME` carrier.
- Oracle emits `CAST(SYS_EXTRACT_UTC(SYSTIMESTAMP) AS TIMESTAMP)` and uses the existing
  `DateTimeOffset` mapping, whose DateTime reader path restores offset zero.
- The provider translates the static member in predicates and projections; the internal marker never reaches SQL.
- `DateTimeOffset.Now` remains outside this slice. MySQL does not retain an application-local offset, so claiming
  its local-clock offset would be incorrect. It remains a separate contract requiring an explicit timezone policy.
- No Connector API or dependency changed.

## Acceptance evidence

| Layer | Result |
| --- | --- |
| RED | Unit predicate/projection queries failed to translate in both modes before the translator branch |
| Unit | UtcNow SQL-shape cases: 2/2; full temporal member group: 69/69 |
| Functional | MySQL/Oracle sync/async reader materialization and predicate: 4/4; zero-offset value verified |
| Specification / consumer | Release Specification 1446 passed with 16 mapped skips; ConsumerTests 2/2 passed |
| Package | `10.0.0-date-time-offset-utc-now-20261116` passed Verify-Package and packed consumers on EF10.0.0/10.0.14 in both modes using isolated caches |
| Native OceanBase | Open: supplied endpoint still closes during pre-authentication handshake |

## Verification commands

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~DateTimeOffset_utc_now"
dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests/OceanBase.EntityFrameworkCore.FunctionalTests.csproj -c Release --no-restore --filter "FullyQualifiedName~DateTimeOffset_utc_now_materializes_server_value"
```

The local contract is complete after focused, full source, package content and EF10.0.0 packed consumer gates pass.
Server clock accuracy, session timezone, precision and live OceanBase results remain separate gates; this does not
close `MAP-05`, `QRY-07`, `QRY-11`, `TST-02` or release rows.
