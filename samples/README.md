# OceanBase EF Core samples

Run these console projects manually from the repository root with the .NET 10 SDK. Both projects
live outside the CI solution and report individual passes and failures in the terminal. They use
unique `EFT_` tables in the selected test database or schema and verify cleanup through the catalog.

Both projects read `ConnectionStrings:OceanBaseMySql` and `ConnectionStrings:OceanBaseOracle` from
the existing `OceanBase.EntityFrameworkCore.LiveTests` User Secrets store. The connection strings
stay local. Use `--no-tls` or `-NoTls` only with an explicitly authorized private test endpoint
that lacks TLS. The option applies to the sample process. Omitting it preserves the configured
transport settings.

## Feature probe

`OceanBaseProviderFeatureProbe` runs the selected cases independently for MySQL and Oracle. It
checks generated and composite keys, relationships including explicit and implicit join tables,
owned and split entities, TPH/TPT/TPC, scalar round trips and defaults, LINQ grouping and pagination,
named filters and tracking, parameterized raw SQL, bulk DML, optimistic concurrency and savepoints.

```powershell
dotnet run --project samples/OceanBaseProviderFeatureProbe -- --list
dotnet run --project samples/OceanBaseProviderFeatureProbe -- --mode both
dotnet run --project samples/OceanBaseProviderFeatureProbe -- --mode oracle --only inheritance
```

`--dry-run` builds models and renders DDL without connecting. `--sync` uses the synchronous EF
operations in the selected scenarios. The default live run uses asynchronous EF operations.

## Migration validation

`OceanBaseProviderValidation` uses PowerShell 7 and an EF Core 10 `dotnet-ef` tool. Its runner
scaffolds `InitialJournal` and `AddJournalNotes` with `dotnet ef migrations add` for each selected
mode. Each run leaves the generated C# files, snapshot, and forward, idempotent and rollback SQL
under its own `artifacts/sample-migrations-*/` directory, printed in the terminal.

```powershell
./samples/OceanBaseProviderValidation/Run-Migrations.ps1 -Mode Both -GenerateOnly
./samples/OceanBaseProviderValidation/Run-Migrations.ps1 -Mode Both
```

The initial migration creates a journal and a detail table with a composite primary key, a foreign
key and a unique index. The live console applies the compiled migrations through EF, inserts a
journal and its detail row, then reads and updates the journal after the upgrade. It verifies that
the relationship survives the upgrade and rollback, reapplies the upgrade twice, and rolls back to
zero. It checks migration history and verifies cleanup of all sample tables. The exported SQL files
are available for inspection or use with the respective database clients. `-GenerateOnly` performs
the scaffolding and SQL generation offline.
