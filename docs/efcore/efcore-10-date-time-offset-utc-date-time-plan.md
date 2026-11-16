# EF Core 10 `DateTimeOffset.UtcDateTime` plan

**Status:** Local implementation complete; native timestamp-with-time-zone acceptance remains open.
**Date:** 2026-11-16
**Feature IDs:** `MAP-05`, `QRY-07`, `QRY-11`

## Contract

`DateTimeOffset.UtcDateTime` returns the instant represented by the value as an unzoned UTC `DateTime`.

- MySQL stores `DateTimeOffset` as a UTC-normalized `DATETIME`; the SQL expression can reuse the stored carrier.
- Oracle stores the value as `TIMESTAMP WITH TIME ZONE`; SQL must call `SYS_EXTRACT_UTC` and cast the result to
  the existing unzoned `TIMESTAMP` mapping.
- The provider rejects unrelated carriers and null-converting mappings before SQL generation.
- `DateTimeOffset.Ticks`, `DateTimeOffset.Offset`, and `LocalDateTime` remain separate contracts because
  MySQL does not retain the original offset and cannot reconstruct those local-clock semantics.

## Implementation

`OceanBaseTemporalMemberTranslator` admits the exact `UtcDateTime` member only when the input retains the
mode-specific DateTimeOffset carrier. `OceanBaseQuerySqlGenerator` validates the internal marker and renders the
mode-specific expression; the marker never reaches SQL. No Connector API or dependency changed.

## Acceptance evidence

| Layer | Result |
| --- | --- |
| RED | Unit and functional predicate queries failed to translate in both modes before the translator branch |
| Unit | `UtcDateTime` projection/predicate and unrelated/null-converting carrier rejection: 4/4 focused cases |
| Functional | MySQL/Oracle sync/async materialization and predicate: 4/4 cases; DateTime reader value verified |
| PackageConsumer | EF10.0.0 packed consumer passes in both modes using the isolated repository cache; the default global cache is invalid because its `OceanBaseConnector/0.3.0` path contains a 0.2.0.0 assembly |
| Native OceanBase | Open: supplied endpoint still closes during pre-authentication handshake |

## Verification commands

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~DateTimeOffset_utc_date_time"
dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests/OceanBase.EntityFrameworkCore.FunctionalTests.csproj -c Release --no-restore --filter "FullyQualifiedName~DateTimeOffset_parts_materialize_server_values"
```

The focused local contract is complete after the full temporal selections, full Debug/Release suites, package content
and EF10.0.0 packed consumer gates pass. EF10.0.14 packed compatibility, native timezone/precision/range and live
OceanBase results remain separate gates; this does not close `MAP-05`, `QRY-07`, `QRY-11`, `TST-02` or release rows.
