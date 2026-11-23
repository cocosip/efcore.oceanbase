# EF Core Version Support

The provider ships one package major per EF Core major. EF Core 10 is the current development baseline;
EF Core 8 and 9 are released on their own branches and remain in maintenance scope.

| EF major | Branch | Package / target asset | EF Core dependency | State |
| --- | --- | --- | --- | --- |
| 10 | `efcore/10.0` | `10.x` / `net10.0` | `[10.0.0,11.0.0)` | Current: 10.0.0 published; 10.0.1 in preparation; acceptance scope and remaining gates in the [release checklist](efcore-10-release-checklist.md) |
| 9 | `efcore/9.0` | `9.x` / `net9.0` | `[9.0.0,10.0.0)` | 9.0.0 published; 9.0.1 in preparation; branch-owned inventories record ports and acceptance |
| 8 | `efcore/8.0` | `8.x` / `net8.0` | `[8.0.0,9.0.0)` | 8.0.0 published; 8.0.1 in preparation; branch-owned inventories record ports and acceptance |
| 11 | `dev` when development begins | `11.x` / target to be verified | `[11.0.0,12.0.0)` expected | Development has not started |

Each Provider asset requires an application target that can consume it: the EF10 provider needs a .NET 10
or compatible later target and cannot be used from a .NET 8/9 application by changing the EF package
alone. Confirm each line's SDK, runtime and version-specific APIs with the [official
references](#official-references), and verify any claimed later runtime with its own consumer checks.
Microsoft ended upstream support for EF Core 8, EF Core 9, .NET 8 and .NET 9 on November 10, 2026; the
packages remain installable and the provider keeps its documented behavior on those lines.

## Documentation ownership

Shared policy lives on every branch, but version state does not. This branch owns:

- the shared branch, porting, publishing and version policy on this page;
- the [EF Core 10 feature](efcore-10-feature-matrix.md), [gap](efcore-10-gap-matrix.md) and
  [release](efcore-10-release-checklist.md) inventories;
- the [package guide](../../src/OceanBase.EntityFrameworkCore/README.md) and
  [provider behavior](../provider-behavior.md) contracts, which describe all supported lines.

EF Core 8 and EF Core 9 keep their own feature, gap and acceptance records on `efcore/8.0` and
`efcore/9.0`. Do not copy those inventories here: a duplicated copy reports another branch's state as
this branch's status and drifts on the next port.

## Branches, CI/CD and publishing

- `dev` is the development branch for the latest EF Core major. It follows `efcore/10.0` while EF Core 10
  is current. Start the EF Core 11 upgrade on `dev` when that work begins.
- `master` is the integration branch for `dev`. CI/CD for work on `dev` runs on `master` after the merge.
  The `dev` branch itself is excluded from CI/CD.
- Each of `efcore/8.0`, `efcore/9.0` and `efcore/10.0` runs its own CI/CD and builds the provider package
  for its matching EF Core major.
- Use the same [CI/CD workflow](../../.github/workflows/ci-cd.yml) on all release branches. Branch and
  pull request runs test and verify a validation package on Windows and Linux.
- Use `v8.0.0`, `v9.0.0` or `v10.0.0` for the initial stable releases. The tag must match the project
  version and identify a commit on its matching version branch. The Windows tag run tests the branch,
  verifies the stable package and runs its isolated consumer; GitHub Actions publishes that artifact.
- Configure NuGet Trusted Publishing for `cocosip/efcore.oceanbase`, workflow `ci-cd.yml`, owner
  `virtual` and package `Sharp.OceanBase.EntityFrameworkCore`. The policy must allow new packages; hosted
  workflow verification and final activation remain tracked in the release checklist. Follow the
  [release authorization rules](../repository-guidelines.md#git-and-releases) before creating or pushing
  a release tag.

## Porting and dependency rules

- Preserve applicable behavior and explicit limitations. Adapt version-local APIs, service registration,
  model/query generation and tooling on each branch.
- Use `Sharp.OceanBase.EntityFrameworkCore` as the default NuGet package ID across EF Core majors.
  Override `OceanBasePackagePrefix` with `-p:OceanBasePackagePrefix=<prefix>` to pack as
  `<prefix>.OceanBase.EntityFrameworkCore`. Match the package major to EF Core and bound dependencies to
  that major: 8.x uses `[8.0.0,9.0.0)`, 9.x uses `[9.0.0,10.0.0)`, and 10.x uses `[10.0.0,11.0.0)`.
- Review target-major breaking changes and the current Provider feature contract. EF release notes,
  inherited EF behavior and EF10 test results do not automatically establish support on another line.
- Keep shared code small. Backport only EF-independent fixes deliberately; each version must build and
  test without compiling another version branch.
- Before releasing a port, run that major's provider/relational/specification, package-consumer and
  both-mode live checks. Record version differences and exclusions in that branch's own inventories.
  Collect fresh evidence for each major.

## Version-local API differences

These are the differences that change provider code when a port moves between lines. Confirm each one
against the target EF Core version before implementing it.

| Area | EF Core 8 | EF Core 9 | EF Core 10 |
| --- | --- | --- | --- |
| Query filters | No named filters: compose one global filter per entity; `IgnoreQueryFilters()` disables all | Same as EF 8 | Named filters with selective or all disable and current-context parameter refresh |
| Parameter and collection translation | EF8 parameter dictionaries and `canCache`; `EF.Constant` arrives after 8.0.0, and `EF.Parameter` is not in the 8.x contract | `EF.Constant`/`EF.Parameter`, `TranslateParameterizedCollectionsToConstants`/`...ToParameters`; do not assume the EF10 scalar-list option | `ParametersCacheDecorator`/`Process`, EF10 collection translation options |
| Bulk updates and deletes | Expression-tree setters (`SetProperty` with expressions) | Expression-tree setters | EF10 setter lambdas (`ExecuteUpdate` action-based construction) |
| Seeding and migration locking | No `UseSeeding`/`UseAsyncSeeding` and no `IMigrationsDatabaseLock`: keep `HasData`, document serialized execution | `UseSeeding`/`UseAsyncSeeding`, `IMigrationsDatabaseLock`, pending-model checks | Same as EF 9 |
| Generated queries and NativeAOT | Compiled models in scope; precompiled queries and NativeAOT out of scope | Experimental query precompilation and NativeAOT: confirm the branch's narrower scope by compiling and executing artifacts | Experimental precompiled queries plus a bounded Windows NativeAOT profile, recorded in the release checklist |

## EF Core 8 and 9 runtime scope

Match each Provider package's target framework to its EF Core major: the EF9 Provider targets `net9.0`
and the EF8 Provider targets `net8.0`. Record verified runtime profiles in each version's release
checklist on its own branch. See the [lifecycle references](#official-references) for upstream support
dates.

## Official references

Before implementing or porting a branch, read the target version's official documentation and record any
API or behavior change that affects this provider. Add feature-specific references to the owning topic or
inventory; a new feature does not require a separate design document.

General:

- [Writing a database provider](https://learn.microsoft.com/ef/core/providers/writing-a-provider)
- [Database providers](https://learn.microsoft.com/ef/core/providers/)
- [EF Core breaking changes](https://learn.microsoft.com/ef/core/what-is-new/breaking-changes)
- [EF Core testing](https://learn.microsoft.com/ef/core/testing/)
- [Relational specification tests package](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Relational.Specification.Tests)

Per version:

- [EF Core 8 what's new](https://learn.microsoft.com/ef/core/what-is-new/ef-core-8.0/whatsnew) ·
  [breaking changes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-8.0/breaking-changes) ·
  [relational query pipeline source](https://github.com/dotnet/efcore/tree/v8.0.0/src/EFCore.Relational/Query) ·
  [specification fixtures 8.0.34](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Relational.Specification.Tests/8.0.34)
- [EF Core 9 what's new](https://learn.microsoft.com/ef/core/what-is-new/ef-core-9.0/whatsnew) ·
  [breaking changes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-9.0/breaking-changes) ·
  [relational query pipeline source](https://github.com/dotnet/efcore/tree/v9.0.0/src/EFCore.Relational/Query) ·
  [specification fixtures 9.0.23](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Relational.Specification.Tests/9.0.23)
- [EF Core 10 what's new](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew) ·
  [breaking changes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/breaking-changes)
- [EF release platforms and support dates](https://learn.microsoft.com/ef/core/what-is-new/) ·
  [.NET runtime support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)

The provider does not claim support for a feature merely because EF Core exposes it. The feature must
also be expressible in the claimed OceanBase compatibility mode and be covered by provider tests. A
mode-specific implementation does not imply support in the other mode, and neither implies live
acceptance. EF's provider-writing guidance recommends extending the published relational specification
tests: each branch records its selected test classes and intentional exclusions in its own checklist.

Feature-specific reading map:

| Area | Official documentation |
| --- | --- |
| Configuration and connections | [DbContext configuration](https://learn.microsoft.com/ef/core/dbcontext-configuration/) |
| Model and mappings | [Entity properties](https://learn.microsoft.com/ef/core/modeling/entity-properties), [relationships](https://learn.microsoft.com/ef/core/modeling/relationships), [value conversions](https://learn.microsoft.com/ef/core/modeling/value-conversions), [generated values](https://learn.microsoft.com/ef/core/modeling/generated-properties) |
| Queries | [Querying](https://learn.microsoft.com/ef/core/querying/), [client evaluation](https://learn.microsoft.com/ef/core/querying/client-eval), [null comparisons](https://learn.microsoft.com/ef/core/querying/null-comparisons), [SQL queries](https://learn.microsoft.com/ef/core/querying/sql-queries) |
| Writes and transactions | [Saving data](https://learn.microsoft.com/ef/core/saving/), [concurrency](https://learn.microsoft.com/ef/core/saving/concurrency), [transactions and savepoints](https://learn.microsoft.com/ef/core/saving/transactions) |
| Schema creation | [Create and drop APIs](https://learn.microsoft.com/ef/core/managing-schemas/ensure-created) |
| Logging and diagnostics | [Logging and events](https://learn.microsoft.com/ef/core/logging-events-diagnostics/), [interceptors](https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors) |
| Migrations and reverse engineering | [Migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/), [scaffolding](https://learn.microsoft.com/ef/core/managing-schemas/scaffolding/), [ExecuteUpdate/ExecuteDelete](https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete) |
| Loading and compiled models | [Lazy loading](https://learn.microsoft.com/ef/core/querying/related-data/lazy), [compiled models](https://learn.microsoft.com/ef/core/performance/advanced-performance-topics#compiled-models) |
