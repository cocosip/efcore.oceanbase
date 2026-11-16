# EF Core 10 Provider Design

## Goal

Implement EF Core 10 on efcore/10.0, the active development and release line, with explicit MySQL/Oracle modes.
EF8/EF9 compatibility branches adapt a tested EF10 baseline later; they are not existing implementation evidence.

## Porting rules

1. Read the EF Core 10 what's-new and breaking-change documents listed in official-references.md.
2. Compile against the exact EF Core 10 dependency range and target the runtime required by EF Core 10.
3. Treat all EF Core internal service APIs as version-local; do not copy EF8/EF9 constructor signatures blindly.
4. Establish the shared behavioral tests first, then add EF10-specific features only with a documented support claim.
5. Keep MySQL and Oracle SQL generation and type semantics independently verifiable.

## Expected implementation boundary

The release acceptance boundary is maintained in [the EF Core 10 feature checklist](efcore-10-feature-checklist.md).
That checklist is the source of truth for feature status and completion evidence; this document explains the design
constraints behind the boundary.

The initial release requires provider registration, explicit compatibility mode, model-cache identity, scalar
mappings, basic relational queries, updates, generated keys and transactions. These are acceptance targets,
not claims of current implementation. EF10 features such as new query operators,
seeding or migration behavior require individual design notes and tests before being advertised.

## Package and runtime

- Branch: efcore/10.0 for development, packaging and release; other branch synchronization is deliberate.
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

The 2026-11-16 [grouped-result module](efcore-10-grouped-results-plan.md) explicitly compensates nullable
grouping-key correlation before projecting aggregate/ordered group results. GROUP BY retains a NULL
group with either null configuration; ordinary LINQ Join keeps its existing SQL equality behavior.
Only the grouping enumerable's key correlation is rewritten, using standard SQL OR/IS NULL and existing
OceanBase lateral translation. EF still owns aggregation/projection/shaping; Connector0.3.0 transports
commands/readers through its existing public APIs. Nullable correlations may retain LATERAL rather than
ROW_NUMBER decorrelation and require the previously documented OceanBase lateral-capable cluster profile;
native evaluation and plan/performance are not established. Both GroupBy result forms, computed/composite/
converted keys and local runtime/package checks are recorded in the module, with no Required-row promotion.

Release requires the efcore/10.0 revision to pass the shared behavioral suite, EF10-specific provider
tests, selected EF relational specification tests, packed-package consumer tests and the available MySQL/Oracle
live matrix. Unsupported server features and missing infrastructure remain recorded as verification limits rather
than inferred support.
