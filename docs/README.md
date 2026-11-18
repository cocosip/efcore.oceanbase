# OceanBase Entity Framework Core Provider documentation

EF Core 10 maintains three primary inventories:

1. [Feature inventory](efcore/efcore-10-feature-matrix.md): 55 feature groups, implementation status in
   both modes, supported scope and source entry points.
2. [Gap inventory](efcore/efcore-10-gap-matrix.md): development status, priority, ownership and closure
   conditions for each gap; external dependencies, uncertain capabilities and deferrals are separate.
3. [Release checklist](efcore/efcore-10-release-checklist.md): test, live database, package, platform,
   review and release gates with their evidence.

Use the gap inventory to select development work, the feature inventory to assess implemented scope,
and the release checklist to assess release readiness. Module plans, duplicate gap lists and progress
logs have been merged and removed. Git retains implementation history; ignored `artifacts/` retains
raw local test evidence.

The [interactive samples](../samples/README.md) demonstrate entity shapes, everyday EF Core queries
and writes, and generation and application of migrations against OceanBase.

Write documentation in English and organize it by lasting topic across all EF Core versions. New
independent topics are allowed; small features do not get their own documents. See the root
[AGENTS.md](../AGENTS.md) for these rules.

See the [repository guidelines](repository-guidelines.md),
[provider architecture](provider-architecture.md) and
[official EF references](efcore/official-references.md) for shared guidance.
