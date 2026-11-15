# EF Core 10 temporal fractional member plan

## Scope

This module adds the exact fractional component members supported by the current temporal
carriers:

- `DateTime.Millisecond`, `DateTime.Microsecond`, `DateTime.Nanosecond`;
- `TimeOnly.Millisecond`, `TimeOnly.Microsecond`, `TimeOnly.Nanosecond`;
- MySQL `DATETIME`/`TIME` and Oracle unzoned `DATE`/`TIMESTAMP`/`INTERVAL DAY TO SECOND`.

`DateTimeOffset`, timezone conversion, `Ticks`, `Date`, `TimeOfDay`, `Add` methods and native
precision/range behavior remain separate contracts.

## Implementation

The existing temporal member translator emits the allowlisted internal `ExtractMarker` for
the six fractional members. MySQL renders milliseconds as `MICROSECOND(...) DIV 1000`,
microseconds as the remainder modulo 1000, and nanoseconds as that remainder multiplied by
100. Oracle reuses `EXTRACT(SECOND FROM ...)` with the existing truncated and modulo paths.
The source mapping remains untouched and all results use the existing converter-free `int`
mapping with nullable propagation.

No public API, dependency or Connector change is required.

## Acceptance standard

| Layer | Required evidence | Current result |
| --- | --- | --- |
| RED | Existing `DateTime.Millisecond` and `TimeOnly.Nanosecond` queries fail before implementation | Passed: all four new intercepted mode/sync cases failed to translate |
| Unit | Both modes, projection/predicate SQL shape, balanced SQL, exact int result mapping and unsupported carriers | Passed: `TemporalMemberQueryTests` 39/39 |
| Functional | Both modes, sync/async, six component results and supplied reader materialization | Passed: `TemporalFractionalMemberFunctionalTests` 4/4 |
| Regression | Existing temporal members and signed TimeSpan paths | Passed: temporal functional selection 32/32 |
| Full Release source | Unit, Functional, Specification and Consumer projects | Passed: 2353 + 1887 + 868 + 2, zero failures/skips |
| Live OceanBase | Native fractional extraction, precision and range | Open: supplied endpoints close during pre-authentication |
| Package/release | Full package and final release matrix | Open: local feature checkpoint only |

The module is `Local complete` for code and intercepted tests. `MAP-05` and `QRY-07` remain
`In progress` because native temporal precision, timezone and live result evidence are still
required.

## Reproduction commands

```text
dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~TemporalMemberQueryTests"
dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests/OceanBase.EntityFrameworkCore.FunctionalTests.csproj -c Release --no-restore --filter "FullyQualifiedName~DateTimeMemberFunctionalTests|FullyQualifiedName~TimeSpanComponentFunctionalTests|FullyQualifiedName~TimeSpanTotalFunctionalTests|FullyQualifiedName~TemporalFractionalMemberFunctionalTests"
```

Implementation commit: `e3119f5` (local only).
