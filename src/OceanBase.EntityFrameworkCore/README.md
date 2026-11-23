# Sharp.OceanBase.EntityFrameworkCore

Entity Framework Core provider for OceanBase MySQL and Oracle compatibility modes. The provider runs
on the managed [OceanBaseConnector](https://www.nuget.org/packages/OceanBaseConnector) ADO.NET driver
and implements EF Core relational behavior: modeling, LINQ translation, writes, transactions, schema
creation, migrations and reverse engineering.

- [Install](#install)
- [Requirements](#requirements)
- [Quick start](#quick-start)
- [Choose the compatibility mode](#choose-the-compatibility-mode)
- [Migrations and schema creation](#migrations-and-schema-creation)
- [Reverse engineering](#reverse-engineering)
- [Retries and transactions](#retries-and-transactions)
- [Graph loading, lazy loading and compiled models](#graph-loading-lazy-loading-and-compiled-models)
- [Sequences and HiLo keys](#sequences-and-hilo-keys)
- [JSON documents](#json-documents)
- [Bulk updates and deletes](#bulk-updates-and-deletes)
- [Supported behavior](#supported-behavior)
- [Known limitations](#known-limitations)
- [Documentation](#documentation)

## Install

```shell
dotnet add package Sharp.OceanBase.EntityFrameworkCore --version 10.0.0
dotnet add package Microsoft.EntityFrameworkCore.Design # migrations, scaffolding, bundles; match the EF Core patch
```

| Package version | Target framework | EF Core dependency | Minimum runtime |
| --- | --- | --- | --- |
| 10.0.0 | `net10.0` | `[10.0.0,11.0.0)` | .NET 10 |
| 9.0.0 | `net9.0` | `[9.0.0,10.0.0)` | .NET 9 |
| 8.0.0 | `net8.0` | `[8.0.0,9.0.0)` | .NET 8 |

`Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational` and `OceanBaseConnector`
(0.4.0 or later) are package dependencies. Match the package major to the application's EF Core major;
the bounded EF range accepts its patch updates without pinning one patch version.

## Requirements

- An OceanBase tenant in MySQL or Oracle compatibility mode. Upstream MySQL Server and Oracle Database
  behavior alone does not establish support; check the target OceanBase version's own documentation.
- A direct connection. Locally tested evidence covers OceanBase 4.4.2.2, Windows x64, ordinary direct
  connections with TLS disabled; Linux, TLS and HA deployments need their own acceptance.
- Connection strings, authentication, TLS and pooling are Connector configuration. The provider selects
  the tenant mode and validates it; it does not replace the Connector's connection handling.

## Quick start

Store the connection string in application configuration or a secret store, then select the mode
explicitly:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OceanBase.EntityFrameworkCore;

public sealed class BloggingContext : DbContext
{
    private readonly string _connectionString;

    public BloggingContext(string connectionString) => _connectionString = connectionString;

    public DbSet<Blog> Blogs => Set<Blog>();

    public DbSet<Post> Posts => Set<Post>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseOceanBase(_connectionString, OceanBaseCompatibilityMode.MySql);
}

public sealed class Blog
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public List<Post> Posts { get; } = [];
}

public sealed class Post
{
    public int Id { get; set; }

    public int BlogId { get; set; }

    public string Title { get; set; } = "";

    public Blog? Blog { get; set; }
}
```

Table, key, index and relationship conventions produce the relational model; configure names, lengths
and facets with the ordinary `OnModelCreating` API. Queries, writes and transactions use standard EF
Core code:

```csharp
await using var context = new BloggingContext(connectionString);

context.Blogs.Add(new Blog { Name = "OceanBase" });
await context.SaveChangesAsync();

var blogs = await context.Blogs
    .Where(blog => blog.Name.StartsWith("Ocean"))
    .OrderBy(blog => blog.Id)
    .ToListAsync();
```

Creating tables requires an explicitly named, already provisioned database or schema. `EnsureCreated`
and `Database.Migrate` do not create the namespace itself:

```csharp
optionsBuilder
    .UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql)
    .EnableOceanBaseSchemaManagement("blogging"); // exact database (MySQL) or schema (Oracle) name

// await context.Database.EnsureCreatedAsync();  // supported tables and HasData for a new schema
```

## Choose the compatibility mode

- `OceanBaseCompatibilityMode.MySql` or `OceanBaseCompatibilityMode.Oracle` must match the tenant.
  A supplied `OceanBaseConnection` keeps its own configuration and is validated, not rewritten.
  Provider-created connections receive the selected expected mode before opening.
- The mode participates in EF model, service-provider and compiled-query cache identity. Generate and
  consume compiled models with the matching mode and proxy options.
- Oracle generated-value writes use `RETURNING INTO`. Set `Use Oracle Prepare Execute=true` on the
  Connector connection string for those writes and for stored procedures that return generated values;
  the Connector default is disabled and the provider does not enable it silently.

## Migrations and schema creation

Migrations, `EnsureCreated`, `CreateTables` and `Migrate` require the same schema opt-in shown above and
the namespace must already exist and match the configured name exactly, including case. Tooling needs
`Microsoft.EntityFrameworkCore.Design` in the startup project and the same explicit mode in its host or
`IDesignTimeDbContextFactory`:

```shell
dotnet ef migrations add InitialCreate
dotnet ef database update
dotnet ef migrations script --idempotent --output artifacts/migrate.sql
```

```csharp
optionsBuilder
    .UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql, options => options
        .MigrationsAssembly("Application.Migrations")
        .MigrationsHistoryTable("__ApplicationHistory"))
    .EnableOceanBaseSchemaManagement("blogging");
```

- DDL commits implicitly. Caller transactions and retry strategies are rejected for DDL, migration and
  `Migrate` operations, so use a separate context without retries for them.
- Runtime migrations serialize through `__EFMigrationsHistoryLock`; generated scripts require external
  serialization and stop-on-error execution.
- `EnsureCreated` returns false when ordinary tables already exist and never upgrades a schema.
- Table and column comments from `HasComment` are part of create scripts and migration diffs.
  `HasTrigger` records trigger metadata only; author trigger DDL with `migrationBuilder.Sql`.
- MySQL model-level `UseCollation` changes an explicitly owned database's default with
  `EnableOceanBaseDatabaseManagement`; Oracle and table-only ownership reject that setting.

## Reverse engineering

Scaffolding reads the Connector's public schema catalogs. The scaffolding connection string must declare
`ExpectedMode=MySql` or `ExpectedMode=Oracle`; generated `UseOceanBase` code selects that mode.

```shell
dotnet ef dbcontext scaffold "Server=<host>;Port=2881;User ID=<user>;Password=<secret>;Database=<database>;ExpectedMode=MySql" \
    OceanBase.EntityFrameworkCore -o Models
```

Supported scalar columns, keys, ordinary indexes, relationships and keyless views use EF's standard
generation. Provider annotations preserve behavior that the EF model cannot express by itself, such as
Oracle identity definitions and sequence options, MySQL `ON UPDATE CURRENT_TIMESTAMP`, BTREE prefix
indexes and Oracle expression indexes. Unavailable behavior metadata produces design-time warnings; the
result is a usable model scaffold, not a lossless DDL backup.

## Retries and transactions

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql,
    options => options.EnableRetryOnFailure());
```

- Defaults are six retries with a maximum 30-second delay for known transient server errors: MySQL
  1205/1213, Oracle 54/60/8177, or SQLSTATE 40001.
- Unknown execution outcomes, cancellation, XA, commit-stage errors and failed cleanup are never
  replayed. Put user transactions inside `Database.CreateExecutionStrategy().Execute`/`ExecuteAsync`.
- Retrying queries buffer results, so memory use increases; delegates, callbacks and raw SQL must be
  replayable.
- Single-resource `TransactionScope` and `Database.EnlistTransaction` are supported through the
  Connector in both modes. Distributed and multiple-resource participation are not.
- Savepoints, save changes inside an existing transaction and automatic savepoint recovery after
  cancellation are supported; Oracle savepoint release is logical rather than a server `RELEASE`.

## Graph loading, lazy loading and compiled models

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql,
    options => options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
```

- Single-query loading is the default. `AsSplitQuery()`/`AsSingleQuery()` override the context setting
  per query. Split queries buffer each result set before opening the next reader, which costs memory and
  round trips; page with a fully unique order.
- Lazy loading works through an injected `ILazyLoader` or the optional
  `Microsoft.EntityFrameworkCore.Proxies` package at the application's EF patch version. Proxy entities
  must be public, unsealed and use virtual navigations.
- OceanBase allows one active native reader per connection: materialize a root query before accessing
  unloaded navigations and do not use a context concurrently.
- Generate ordinary compiled models with `dotnet ef dbcontext optimize` and select them with
  `UseModel(...)` next to `UseOceanBase`. Generate separate models per compatibility mode and
  regenerate after model or provider changes.

## Sequences and HiLo keys

```csharp
modelBuilder.HasSequence<long>("OrderNumbers").StartsAt(1).IncrementsBy(32);
modelBuilder.Entity<Order>().Property(order => order.Id).UseOceanBaseHiLo("OrderNumbers");
```

Both modes create, alter, restart and drop Int64 model sequences, and HiLo allocates Int32/Int64 primary
keys from them. The sequence must be ascending, positive and non-cycling; the increment is the block
size. Rollbacks and disposed contexts can leave gaps, and drain active contexts before restarting a
sequence. TPC hierarchies can also allocate keys from a shared Oracle sequence default.

## JSON documents

```csharp
modelBuilder.Entity<Article>().Property(article => article.Document).HasColumnType("json");

var titles = await context.Articles
    .Select(article => EF.Functions.OceanBaseJsonValue(article.Document, "$.title"))
    .ToListAsync();
```

An explicit `string` property declared as `json` supports schema creation, projection, reverse
engineering and whole-document writes through `SaveChanges`. Paths must be constants or evaluatable
captured values. Native mode semantics apply: MySQL defaults to 512-character `JSON_VALUE` results and
Oracle to 4000 bytes, with NULL for overflow, missing paths, JSON null and malformed documents.
Partial-document updates, mapped collections and JSON-owned types are not supported.

## Bulk updates and deletes

`ExecuteUpdate` and `ExecuteDelete` have a bounded native profile in both modes, including
joined-source and paged shapes:

```csharp
var affected = await context.Posts
    .Where(post => post.BlogId == blogId)
    .ExecuteUpdateAsync(setters => setters.SetProperty(post => post.Title, post => post.Title + " (archived)"));
```

They execute immediately, return the affected row count and bypass the change tracker, so supply
concurrency predicates yourself and wrap several calls in one transaction when they must share one.
MySQL joined updates use native table-reference syntax; Oracle joined and paged updates use correlated
scalar source queries and require a keyed target.

## Supported behavior

The provider translates the relational model, LINQ and update pipeline for ordinary EF Core use, with
the detailed contracts in [provider behavior and limitations](../../docs/provider-behavior.md):

- Scalars: Boolean, all integer widths, exact decimal facets, Single/Double, text and LOB storage,
  binary/Guid, and five temporal mappings with documented precision and time-zone rules.
- Queries: projections, CASE/coalesce, integer bitwise operators, ordering and paging, joins,
  correlated subqueries, Any/All/Contains, set operations, grouping and aggregates, cardinality
  operators with explicit scalar defaults, and string/search/padding/trim/temporal/math translations
  listed in the [feature inventory](../../docs/efcore/efcore-10-feature-matrix.md).
- Collection parameters and inline roots across numeric, text, binary and temporal types, including the
  single JSON `JSON_TABLE` parameter for captured Int32/Int64 collections.
- Writes: identity and generated-value readback, concurrency tokens, multi-entity graph saves,
  dependency ordering, TPT/TPC/TPH, entity and table splitting, mapped stored-procedure writes.
- Lifecycle: `EnsureCreated`, `CreateTables`, `Migrate`, comments, sequences, seeding callbacks and
  explicit namespace creation/deletion for owned databases and Oracle users.

## Known limitations

Short list; each item's scope, owner and closure condition is in the
[gap inventory](../../docs/efcore/efcore-10-gap-matrix.md):

- Extended deployments (Proxy, OB20, HA) and distributed ambient transactions are outside the tested
  profile; Linux and TLS acceptance is still open.
- MySQL stored LOB defaults and Oracle `NCLOB` are rejected by the tested server version. Oracle column
  collation, filtered indexes, stored computed columns and `ON DELETE SET DEFAULT` are not implemented.
- Database-generated `rowversion` columns, custom comparer overloads and `StringComparison`/culture
  string overloads are not translated.
- MySQL `DateTimeOffset` maps to UTC `DATETIME` and cannot restore the original offset; named zones and
  DST rules are not preserved in either mode.
- MySQL cannot read back server-generated or computed keys, and MySQL sequence defaults for TPC keys
  are rejected by the tested server.
- Spatial, XML, native enum/OBJECT and LOB locator mappings are deferred; JSON support is limited to
  scalar documents.
- `EnsureDeleted` for owned MySQL databases drops the entire database, including objects outside the
  model; namespace creation/deletion remains an administrative, unaccepted extension.

## Documentation

- [Provider behavior and limitations](../../docs/provider-behavior.md): per-feature contracts,
  storage mappings and acceptance boundaries.
- [EF Core 10 feature inventory](../../docs/efcore/efcore-10-feature-matrix.md), [gap
  inventory](../../docs/efcore/efcore-10-gap-matrix.md) and [release
  checklist](../../docs/efcore/efcore-10-release-checklist.md): implementation status, remaining gaps
  and revision-specific evidence.
- [Architecture](../../docs/provider-architecture.md): layer responsibilities and source map.
- [Repository README](../../README.md): building, testing and samples.
