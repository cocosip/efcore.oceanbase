# OceanBase Entity Framework Core Provider documentation

Start with the repository [README](../README.md) for installation and a quick start, the
[package guide](../src/OceanBase.EntityFrameworkCore/README.md) for configuration and usage examples, or
the [samples](../samples/README.md) for runnable scenarios. Per-feature behavior, storage mappings and
acceptance boundaries are in [provider behavior](provider-behavior.md).

## Version state

Each Provider package major matches one EF Core major. The shared branch, porting and publishing policy,
the version-local API differences and the official references are owned by
[version support](efcore/version-support-matrix.md).

| EF Core | Package | Owned on this branch |
| --- | --- | --- |
| 10 | 10.0.0 | [Feature inventory](efcore/efcore-10-feature-matrix.md), [gap inventory](efcore/efcore-10-gap-matrix.md), [release checklist](efcore/efcore-10-release-checklist.md) |
| 9 | 9.0.0 | Policy only — feature, gap and acceptance records live on `efcore/9.0` |
| 8 | 8.0.0 | Policy only — feature, gap and acceptance records live on `efcore/8.0` |

The three EF Core 10 inventories keep stable feature and gap IDs: use the gap inventory to select
development work, the feature inventory to assess implemented scope, and the release checklist to assess
release readiness. Implemented code, accepted runtime profiles and published packages are different
states.

## Shared topics

| Topic | Document |
| --- | --- |
| Installation, configuration and usage examples | [Package guide](../src/OceanBase.EntityFrameworkCore/README.md) |
| Feature behavior, mappings and limitations | [Provider behavior](provider-behavior.md) |
| Provider, Connector and server responsibilities; source layout | [Architecture](provider-architecture.md) |
| Development, testing, credentials and release rules | [Repository guidelines](repository-guidelines.md) |
| Branch policy, porting, publishing and EF references | [Version support](efcore/version-support-matrix.md) |
| Runnable feature and migration probes | [Samples](../samples/README.md) |

Git retains history and ignored `artifacts/` retains raw local evidence.

Write documentation in English and organize it by lasting topic across all EF Core versions. New
independent topics are allowed; small features do not get their own documents. See the root
[AGENTS.md](../AGENTS.md) for these rules.
