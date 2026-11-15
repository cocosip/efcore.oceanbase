# EF Core 10 `DateTime.TimeOfDay` plan

## Scope

This module adds the supported `DateTime.TimeOfDay` member for the existing unzoned temporal carriers:

- MySQL mode: `DateTime` on `DATETIME`, returned through the existing `TimeSpan`/`TIME` mapping;
- Oracle mode: `DateTime` on `DATE` or `TIMESTAMP`, returned through the existing
  `TimeSpan`/`INTERVAL DAY TO SECOND` mapping.

`DateTimeOffset.TimeOfDay`, timezone conversion, `Ticks`, `DateTime` arithmetic and native precision/range
behavior remain separate contracts.

## Implementation

`OceanBaseTemporalMemberTranslator` emits the internal `TimeOfDayMarker` only when the input mapping retains a
DateTime provider carrier and does not convert nulls. The SQL generator renders:

```sql
-- MySQL mode
TIME(input)

-- Oracle mode
-- DATE input
NUMTODSINTERVAL((input - TRUNC(input)), 'DAY')

-- TIMESTAMP input
input - CAST(TRUNC(input) AS TIMESTAMP)
```

The result reuses the existing `TimeSpan` mapping. The SQL marker validates its input and result mappings before
rendering, so an unrelated binary/text/numeric carrier or an invalid internal expression cannot silently fall
through to provider SQL.

No public API, dependency or OceanBaseConnector change is required.

## Acceptance evidence

| Layer | Required evidence | Result |
| --- | --- | --- |
| RED | Query using `DateTime.TimeOfDay` fails before the translator/SQL marker is added | Passed: both modes failed with EF's expected could-not-be-translated error |
| Unit | Both modes, projection/predicate SQL shape, result carrier and unsupported/null-converting carriers | Passed: 4 focused cases; temporal unit selection 174/174 |
| Functional | Both modes, sync/async, mode-specific reader carrier and NULL propagation | Passed: 4/4 focused cases; temporal functional selection 36/36 |
| Full local source | Unit and Functional Release suites | Passed: Unit 2357, Functional 1891, zero failures/skips |
| Native OceanBase | Time extraction, interval result metadata, precision/range and server coercion | Open: supplied endpoints still close before authentication |
| Package/release | Package content, packed consumer on EF10.0.0/10.0.14 and final release revision | Passed: `10.0.0-time-of-day-20261116-r2` passed Verify-Package and the packed consumer in both modes on EF10.0.0 and EF10.0.14; final release revision remains open |

The module is `Local complete` for translation, mapping selection and intercepted sync/async materialization.
`MAP-05` and `QRY-07` remain `In progress` until native temporal behavior and the broader temporal matrix pass.

## Reproduction commands

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj `
  -c Release --no-restore --filter "FullyQualifiedName~DateTime_TimeOfDay"

dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests/OceanBase.EntityFrameworkCore.FunctionalTests.csproj `
  -c Release --no-restore --filter "FullyQualifiedName~DateTimeTimeOfDayFunctionalTests"
```

Implementation commit: `6ecca4f` (local only).
