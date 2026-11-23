# OceanBase Entity Framework Core provider

Entity Framework Core provider for OceanBase MySQL and Oracle compatibility modes, built on the managed
[OceanBaseConnector](https://www.nuget.org/packages/OceanBaseConnector) ADO.NET driver. It brings
modeling, LINQ translation, writes, transactions, schema creation, migrations and reverse engineering
to OceanBase tenants from ordinary .NET applications.

## Install

```shell
dotnet add package Sharp.OceanBase.EntityFrameworkCore --version 10.0.1
```

| EF Core major | Package version | Target framework | EF Core dependency | Branch |
| --- | --- | --- | --- | --- |
| 10 | 10.0.1 | `net10.0` | `[10.0.0,11.0.0)` | `efcore/10.0` |
| 9 | 9.0.1 | `net9.0` | `[9.0.0,10.0.0)` | `efcore/9.0` |
| 8 | 8.0.1 | `net8.0` | `[8.0.0,9.0.0)` | `efcore/8.0` |

The table lists the next maintenance versions; 8.0.0, 9.0.0 and 10.0.0 are already published on NuGet.org.
Match the package major to the application's EF Core major;
`Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational` and
`OceanBaseConnector` 0.5.0 or later are package dependencies. Each release branch records its own
acceptance evidence — see [version support](docs/efcore/version-support-matrix.md).

The next maintenance releases require the Connector 0.5.0 `CompatibilityMode` configuration API.
See the [upgrade notes](src/OceanBase.EntityFrameworkCore/README.md#upgrade-from-800-900-or-1000)
before updating an application or its connection strings.

## Quick start

Select the tenant's compatibility mode explicitly and keep the connection string in configuration:

```csharp
using Microsoft.EntityFrameworkCore;
using OceanBase.EntityFrameworkCore;

public sealed class AppDbContext(string connectionString) : DbContext
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder
            .UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql)
            .EnableOceanBaseSchemaManagement("app_database");
}
```

```csharp
// Order is an ordinary entity type; conventions create its table, key and indexes.
var connectionString = configuration.GetConnectionString("OceanBase");
await using var context = new AppDbContext(connectionString);

await context.Database.EnsureCreatedAsync(); // supported tables and HasData for a provisioned namespace
context.Orders.Add(new Order { Number = "SO-1001" });
await context.SaveChangesAsync();
```

The mode must match the tenant; a supplied `OceanBaseConnection` is validated rather than rewritten.
Connection configuration (authentication, TLS, pooling, routing) belongs to OceanBaseConnector.
See the [package guide](src/OceanBase.EntityFrameworkCore/README.md) for migrations, scaffolding,
retries, split queries, compiled models, sequences, JSON documents and bulk DML.

## Documentation

| Topic | Document |
| --- | --- |
| Package usage, configuration and examples | [Package guide](src/OceanBase.EntityFrameworkCore/README.md) |
| Per-feature behavior, storage mappings and boundaries | [Provider behavior](docs/provider-behavior.md) |
| EF Core 10 implementation status | [Feature inventory](docs/efcore/efcore-10-feature-matrix.md) |
| EF Core 10 gaps, owners and closure conditions | [Gap inventory](docs/efcore/efcore-10-gap-matrix.md) |
| EF Core 10 tests, packages and release gates | [Release checklist](docs/efcore/efcore-10-release-checklist.md) |
| Layer responsibilities and source map | [Architecture](docs/provider-architecture.md) |
| Branch, porting and publishing policy | [Version support](docs/efcore/version-support-matrix.md) |
| Build, test, credential and review rules | [Repository guidelines](docs/repository-guidelines.md) |
| Runnable console and migration probes | [Samples](samples/README.md) |

Start at the [documentation index](docs/README.md) if you are looking for a specific inventory.

## Repository layout

    src/OceanBase.EntityFrameworkCore/          EF Core provider
    tests/                                      Unit, functional, specification, consumer and live tests
    samples/                                    Feature probe and migration validation consoles
    docs/                                       Support contracts, inventories and repository rules

The provider consumes only public OceanBaseConnector APIs. Protocol, authentication, TLS, pooling and
wire codec work belongs to the connector project; EF-side mapping, translation, update and migration
behavior belongs here.

## Building and testing

Requires the .NET 10 SDK on this `efcore/10.0` branch for the `net10.0` provider and solution:

```shell
dotnet build OceanBase.EntityFrameworkCore.slnx -c Release
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release
```

The default solution run excludes live database tests. They are opt-in and read connection strings from
the `OceanBase.EntityFrameworkCore.LiveTests` User Secrets store
(`ConnectionStrings:OceanBaseMySql` and `ConnectionStrings:OceanBaseOracle`):

```powershell
$env:OCEANBASE_RUN_LIVE_TESTS = '1'
dotnet test tests/OceanBase.EntityFrameworkCore.LiveTests -c Release
```

Never commit credentials or connection strings. Live schema tests create and remove only the objects
they own, in a namespace the operator has authorized. See the
[repository guidelines](docs/repository-guidelines.md) and the
[release checklist](docs/efcore/efcore-10-release-checklist.md) for the full command set and recording
rules.

## Contributing

Read [AGENTS.md](AGENTS.md) and the [repository guidelines](docs/repository-guidelines.md) before
changing behavior. Public behavior, unsupported features and server prerequisites are documented before
implementation; feature IDs from the [feature inventory](docs/efcore/efcore-10-feature-matrix.md) and
[gap inventory](docs/efcore/efcore-10-gap-matrix.md) are named in review.
