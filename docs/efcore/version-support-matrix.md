# EF Core Version Support Matrix

The provider package version identifies the EF Core major. The application target framework is a separate choice.

| Application TFM | EF Core | Provider package | Provider asset | Status |
| --- | --- | --- | --- | --- |
| net8.0 | 8.x | OceanBase.EntityFrameworkCore 8.x | net8.0 | First target |
| net9.0 | 8.x | OceanBase.EntityFrameworkCore 8.x | net8.0 | Must be tested as a supported runtime combination |
| net9.0 | 9.x | OceanBase.EntityFrameworkCore 9.x | net8.0 | Later target |
| net10.0 | 8.x | OceanBase.EntityFrameworkCore 8.x | net8.0 | Allowed when EF Core 8 support matrix permits it |
| net10.0 | 10.x | OceanBase.EntityFrameworkCore 10.x | net10.0 | Later target |
| net9.0 | 10.x | OceanBase.EntityFrameworkCore 10.x | net10.0 | Unsupported when the runtime cannot load the asset |

The table is a planning matrix, not a compatibility guarantee. Each release must verify the exact SDK, runtime,
EF Core patch level, provider package and connector package used by the test.

## Dependency rule

Each package line has a bounded EF Core dependency:

    8.x -> [8.0.0, 9.0.0)
    9.x -> [9.0.0, 10.0.0)
    10.x -> [10.0.0, 11.0.0)

The upper bound prevents NuGet from silently selecting a provider implementation built for another EF Core major.
