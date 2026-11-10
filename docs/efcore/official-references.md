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
