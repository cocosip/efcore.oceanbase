# EF Core Version Support

EF Core 10 is the current development baseline. EF Core 8 and 9 are planned ports. Check package
availability and publication status on the intended feed before release.

| EF major | Branch | Provider line / target asset | Current state |
| --- | --- | --- | --- |
| 10 | `efcore/10.0` | `OceanBase.EntityFrameworkCore` 10.x / `net10.0` | Active implementation; accepted profiles and remaining publication steps are in the [release checklist](efcore-10-release-checklist.md) |
| 9 | `efcore/9.0` | 9.x / planned `net8.0` | Port from `efcore/10.0` under the [EF Core 9 inventories](../README.md#version-inventories) |
| 8 | `efcore/8.0` (planned) | 8.x / planned `net8.0` | Port from `efcore/9.0` after the EF Core 9 port is complete, under the [EF Core 8 inventories](../README.md#version-inventories) |
| 11 | `dev` when development begins | 11.x / target to be verified | Development has yet to begin |

The EF10 provider asset requires a .NET 10 application target or a compatible later target that receives
its own verification. It cannot be consumed by a .NET 8/9 application merely by changing the EF package.
For proposed EF8/EF9 ports, confirm target framework, SDK/runtime and version-specific APIs against the
[official references](official-references.md) before implementation. Do not infer cross-runtime support
from NuGet asset selection.

## Branches, CI/CD and publishing

- `dev` is the development branch for the latest EF Core major. It follows `efcore/10.0` while EF Core
  10 is current. Start the EF Core 11 upgrade on `dev` when that work begins.
- `master` is the integration branch for `dev`. CI/CD for work on `dev` runs on `master` after the merge.
  The `dev` branch itself is excluded from CI/CD.
- Each of `efcore/8.0`, `efcore/9.0` and `efcore/10.0` must run its own CI/CD and build the provider package
  for its matching EF Core major.
- Publishing a provider package to NuGet requires a release tag on the intended release commit. Runs
  without a release tag may build validation packages and must skip NuGet publication. Follow the
  [release authorization rules](../repository-guidelines.md#git-and-releases).

## Porting and dependency rules

- Preserve applicable behavior and explicit limitations. Adapt version-local APIs, service registration,
  model/query generation and tooling on each branch.
- Keep the package ID `OceanBase.EntityFrameworkCore`. Match its major to EF Core and bound dependencies
  to that major: 8.x uses `[8.0.0,9.0.0)`, 9.x uses `[9.0.0,10.0.0)`, and 10.x uses `[10.0.0,11.0.0)`.
  The EF10 range is implemented in [Directory.Packages.props](../../Directory.Packages.props); the others
  are requirements for future ports.
- Review target-major breaking changes and the current Provider feature contract. EF release notes,
  inherited EF behavior and EF10 test results do not automatically establish support on a port.
- Keep shared code small. Backport only EF-independent fixes deliberately; each version must build and
  test without compiling another version branch.
- Before releasing a port, run that major's provider/relational/specification, package-consumer and both-mode
  live checks. Record version differences and exclusions in its owning inventories under the same
  [documentation rules](../../AGENTS.md). Collect fresh evidence for each major.

## EF Core 8 and 9 runtime scope

EF Core 8 and 9 both target .NET 8. Microsoft ended upstream support for EF Core 8, EF Core 9,
.NET 8 and .NET 9 on November 10, 2026. These Provider lines target compatibility for existing
applications. Record supported runtime profiles in each version's release checklist.
[Lifecycle and upstream API references](official-references.md#version-references)
