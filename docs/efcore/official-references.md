# EF Core Official References

Before implementing or porting a branch, read the target version's official documentation and record any API or
behavior change that affects this provider. Add feature-specific references to the existing owning topic or
inventory; a new feature does not require a separate design document.

## General provider references

- [Writing a database provider](https://learn.microsoft.com/ef/core/providers/writing-a-provider)
- [Database providers](https://learn.microsoft.com/ef/core/providers/)
- [EF Core breaking changes](https://learn.microsoft.com/ef/core/what-is-new/breaking-changes)
- [EF Core testing](https://learn.microsoft.com/ef/core/testing/)
- [Relational specification tests package](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Relational.Specification.Tests)

## Version references

- [EF Core 8 what's new](https://learn.microsoft.com/ef/core/what-is-new/ef-core-8.0/whatsnew)
- [EF Core 8 breaking changes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-8.0/breaking-changes)
- [EF Core 9 what's new](https://learn.microsoft.com/ef/core/what-is-new/ef-core-9.0/whatsnew)
- [EF Core 9 breaking changes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-9.0/breaking-changes)
- [EF Core 10 what's new](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew)
- [EF Core 10 breaking changes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/breaking-changes)

The provider does not claim support for a feature merely because EF Core exposes it. The feature must also be
expressible in the claimed OceanBase compatibility mode and be covered by provider tests. A mode-specific
implementation does not imply support in the other mode, and neither implies live acceptance.

The provider-writing guidance recommends extending the EF Core specification tests published as NuGet packages.
Each branch must record the selected test classes and any intentional exclusions.

### EF Core 8 and 9 compatibility

- [EF Core release platforms and support dates](https://learn.microsoft.com/ef/core/what-is-new/)
- [.NET runtime support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)
- [EF8 relational query pipeline source](https://github.com/dotnet/efcore/tree/v8.0.0/src/EFCore.Relational/Query)
- [EF9 relational query pipeline source](https://github.com/dotnet/efcore/tree/v9.0.0/src/EFCore.Relational/Query)
- [EF 8.0.34 relational specification fixtures](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Relational.Specification.Tests/8.0.34)
- [EF 9.0.23 relational specification fixtures](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Relational.Specification.Tests/9.0.23)

## Feature-specific reading map

Read these alongside the target major's source contracts before implementing the corresponding checklist IDs.
This map is a reading requirement, not a claim that each feature has been implemented or verified.

| Checklist area | Official documentation |
| --- | --- |
| CFG / CON | [DbContext configuration](https://learn.microsoft.com/ef/core/dbcontext-configuration/) |
| MOD / MAP | [Entity properties](https://learn.microsoft.com/ef/core/modeling/entity-properties), [relationships](https://learn.microsoft.com/ef/core/modeling/relationships), [value conversions](https://learn.microsoft.com/ef/core/modeling/value-conversions), [generated values](https://learn.microsoft.com/ef/core/modeling/generated-properties) |
| QRY | [Querying](https://learn.microsoft.com/ef/core/querying/), [client evaluation](https://learn.microsoft.com/ef/core/querying/client-eval), [null comparisons](https://learn.microsoft.com/ef/core/querying/null-comparisons), [SQL queries](https://learn.microsoft.com/ef/core/querying/sql-queries) |
| UPD / TXN | [Saving data](https://learn.microsoft.com/ef/core/saving/), [concurrency](https://learn.microsoft.com/ef/core/saving/concurrency), [transactions and savepoints](https://learn.microsoft.com/ef/core/saving/transactions) |
| SCH | [Create and drop APIs](https://learn.microsoft.com/ef/core/managing-schemas/ensure-created) |
| DIA | [Logging and events](https://learn.microsoft.com/ef/core/logging-events-diagnostics/), [interceptors](https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors) |
| DEF | [Migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/), [reverse engineering](https://learn.microsoft.com/ef/core/managing-schemas/scaffolding/), [ExecuteUpdate/ExecuteDelete](https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete) |
| DEF-08 | [Lazy loading](https://learn.microsoft.com/ef/core/querying/related-data/lazy), [untracked lazy loading](https://learn.microsoft.com/ef/core/what-is-new/ef-core-8.0/whatsnew#enhancements-to-lazy-loading), [compiled models and restrictions](https://learn.microsoft.com/ef/core/performance/advanced-performance-topics#compiled-models) |
