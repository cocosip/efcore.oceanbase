# EF Core Version Support Matrix

The provider package version identifies the EF Core major. The application target framework is a separate choice.

| Application TFM | EF Core | Branch | Provider package | Provider asset | Status |
| --- | --- | --- | --- | --- | --- |
| net8.0 | 8.x | efcore/8.0 | OceanBase.EntityFrameworkCore 8.x | net8.0 | Compatibility line |
| net9.0 | 8.x | efcore/8.0 | OceanBase.EntityFrameworkCore 8.x | net8.0 | Requires explicit runtime verification |
| net9.0 | 9.x | efcore/9.0 | OceanBase.EntityFrameworkCore 9.x | net8.0 | Compatibility line |
| net10.0 | 8.x | efcore/8.0 | OceanBase.EntityFrameworkCore 8.x | net8.0 | Requires explicit runtime verification |
| net10.0 | 10.x | efcore/10.0 | OceanBase.EntityFrameworkCore 10.x | net10.0 | Main release line |
| net9.0 | 10.x | efcore/10.0 | OceanBase.EntityFrameworkCore 10.x | net10.0 | Unsupported; EF Core 10 requires .NET 10 |

EF Core 8 requires .NET 8 to build and run according to its official release documentation. EF Core 9 targets
.NET 8 and can be used with .NET 8 or .NET 9. EF Core 10 requires .NET 10 to build and run. The table is a
planning matrix, not a compatibility guarantee; each release must verify the exact SDK, runtime, EF Core patch
level, provider package and connector package used by the test.

## Dependency rule

Each package line has a bounded EF Core dependency:

    8.x -> [8.0.0, 9.0.0)
    9.x -> [9.0.0, 10.0.0)
    10.x -> [10.0.0, 11.0.0)

The upper bound prevents NuGet from silently selecting a provider implementation built for another EF Core major.
