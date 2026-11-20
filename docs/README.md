# OceanBase Entity Framework Core Provider documentation

Start with the [package guide](../src/OceanBase.EntityFrameworkCore/README.md) for configuration,
supported behavior and usage examples, or the [samples](../samples/README.md) for runnable scenarios.

EF Core 10 maintains three primary inventories:

1. [Feature inventory](efcore/efcore-10-feature-matrix.md): 55 feature groups, implementation status in
   both modes, supported scope and source entry points.
2. [Gap inventory](efcore/efcore-10-gap-matrix.md): development status, priority, ownership and closure
   conditions for each gap; external dependencies, uncertain capabilities and deferrals are separate.
3. [Release checklist](efcore/efcore-10-release-checklist.md): test, live database, package, platform,
   review and release gates with their evidence.

Use the gap inventory to select development work, the feature inventory to assess implemented scope,
and the release checklist to assess release readiness. Implemented code, accepted runtime profiles and
published packages are different states. The checklist records the current Windows ordinary-direct
profile and the documented Linux/TLS/HA exclusions; it does not claim unrestricted deployment support.

Shared topics have one owner:

| Topic | Document |
| --- | --- |
| Provider, Connector and server responsibilities; source layout | [Architecture](provider-architecture.md) |
| Current EF major, proposed ports and dependency isolation | [Version support](efcore/version-support-matrix.md) |
| Development, testing, credentials and release rules | [Repository guidelines](repository-guidelines.md) |
| Official EF documentation used when implementing or porting | [References](efcore/official-references.md) |

EF8/EF9 are planned ports, not accepted package lines in this checkout. Their common porting rules live
in the version-support document rather than separate speculative designs. Superseded feature plans and
progress logs are removed; Git retains history and ignored `artifacts/` retains raw local evidence.

Write documentation in English and organize it by lasting topic across all EF Core versions. New
independent topics are allowed; small features do not get their own documents. See the root
[AGENTS.md](../AGENTS.md) for these rules.
