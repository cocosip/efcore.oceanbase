# OceanBase Entity Framework Core Provider documentation

Start with the [package guide](../src/OceanBase.EntityFrameworkCore/README.md) for configuration,
supported behavior and usage examples, or the [samples](../samples/README.md) for runnable scenarios.

## Version inventories

| EF Core | Features and scope by mode | Development gaps and closure conditions | Acceptance and release evidence |
| --- | --- | --- | --- |
| 10 | [Feature inventory](efcore/efcore-10-feature-matrix.md) | [Gap inventory](efcore/efcore-10-gap-matrix.md) | [Release checklist](efcore/efcore-10-release-checklist.md) |
| 9 | [Feature inventory](efcore/efcore-9-feature-matrix.md) | [Gap inventory](efcore/efcore-9-gap-matrix.md) | [Release checklist](efcore/efcore-9-release-checklist.md) |
| 8 | [Feature inventory](efcore/efcore-8-feature-matrix.md) | [Gap inventory](efcore/efcore-8-gap-matrix.md) | [Release checklist](efcore/efcore-8-release-checklist.md) |

Use the gap inventory to select development work, the feature inventory to assess implemented scope,
and the release checklist to assess release readiness. Implemented code, accepted runtime profiles and
published packages are different states. The EF10 checklist records its scoped Windows ordinary-direct
profile. EF8 and EF9 start with porting requirements and pending acceptance.

Shared topics have one owner:

| Topic | Document |
| --- | --- |
| Provider, Connector and server responsibilities; source layout | [Architecture](provider-architecture.md) |
| Branch policy, CI/CD, NuGet publishing, ports and dependency isolation | [Version support](efcore/version-support-matrix.md) |
| Development, testing, credentials and release rules | [Repository guidelines](repository-guidelines.md) |
| Official EF documentation used when implementing or porting | [References](efcore/official-references.md) |

Git retains history and ignored `artifacts/` retains raw local evidence.

Write documentation in English and organize it by lasting topic across all EF Core versions. New
independent topics are allowed; small features do not get their own documents. See the root
[AGENTS.md](../AGENTS.md) for these rules.
