# EF Core 10 Provider Design

## Goal

Port the provider to EF Core 10 as an independent release line while retaining the same explicit MySQL/Oracle
mode contract and the tested relational behavior of earlier branches.

## Porting rules

1. Read the EF Core 10 what's-new and breaking-change documents listed in official-references.md.
2. Compile against the exact EF Core 10 dependency range and target the runtime required by EF Core 10.
3. Treat all EF Core internal service APIs as version-local; do not copy EF8/EF9 constructor signatures blindly.
4. Port the shared behavioral tests first, then add EF10-specific features only with a documented support claim.
5. Keep MySQL and Oracle SQL generation and type semantics independently verifiable.

## Expected implementation boundary

The port preserves provider registration, explicit compatibility mode, model-cache identity, scalar mappings,
basic relational queries, updates, generated keys and transactions. EF10 features such as new query operators,
seeding or migration behavior require individual design notes and tests before being advertised.

## Package and runtime

- Branch: efcore/10.0, kept in sync with dev for packaging and release.
- Package: OceanBase.EntityFrameworkCore 10.x.
- EF dependency: the 10.x range only.
- Initial asset: the runtime target required by EF Core 10.
- EF10 requires .NET 10 to build and run.

## EF10 release review

The EF10 documentation highlights complex-type improvements, LINQ and SQL translation changes, ExecuteUpdate
support for relational JSON columns, named query filters, a regular lambda for ExecuteUpdateAsync and security
related logging changes. These are compatibility review items. They are not advertised until the feature can be
implemented in both OceanBase modes and covered by provider tests.

## Verification gates

Release requires dev to be merged into efcore/10.0, followed by the shared behavioral suite, EF10-specific provider
tests, selected EF relational specification tests, packed-package consumer tests and the available MySQL/Oracle
live matrix. Unsupported server features and missing infrastructure remain recorded as verification limits rather
than inferred support.
