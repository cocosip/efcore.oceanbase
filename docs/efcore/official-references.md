# EF Core Official References

Before implementing or porting a branch, read the target version's official documentation and record any API or
behavior change that affects this provider. These links are the starting set; the branch design must add links for
any feature it implements.

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
expressible in OceanBase MySQL and Oracle compatibility modes and be covered by provider tests.

The provider-writing guidance recommends extending the EF Core specification tests published as NuGet packages.
Each branch must record the selected test classes and any intentional exclusions.

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
| DEF / future scope | [Migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/), [reverse engineering](https://learn.microsoft.com/ef/core/managing-schemas/scaffolding/), [ExecuteUpdate/ExecuteDelete](https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete) |
