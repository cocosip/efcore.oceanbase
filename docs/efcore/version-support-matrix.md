# EF Core Version Support

EF Core 10 is the current development baseline. EF Core 8 and 9 are released on their own maintenance branches. Check package
availability and publication status on the intended feed before release.

| EF major | Branch | Provider line / target asset | Current state |
| --- | --- | --- | --- |
| 10 | `efcore/10.0` | 10.x / `net10.0` | 10.0.0 published; 10.0.1 in preparation; accepted profiles and remaining publication steps are in the [release checklist](efcore-10-release-checklist.md) |
| 9 | `efcore/9.0` | 9.x / `net9.0` | 9.0.0 published; 9.0.1 in preparation under the [EF Core 9 inventories](../README.md#version-inventories) |
| 8 | `efcore/8.0` | 8.x / `net8.0` | 8.0.0 published; 8.0.1 in preparation under the [EF Core 8 inventories](../README.md#version-inventories) |
| 11 | `dev` when development begins | 11.x / target to be verified | Development has yet to begin |

The EF10 provider asset requires a .NET 10 application target or a compatible later target that receives
its own verification. It cannot be consumed by a .NET 8/9 application merely by changing the EF package.
Confirm each line's SDK, runtime and version-specific APIs using the [official references](official-references.md).
Verify any claimed later runtime through its own consumer checks.

Maintenance targets are 8.0.1, 9.0.1 and 10.0.1, consuming published OceanBaseConnector 0.5.0.
The initial 8.0.0, 9.0.0 and 10.0.0 packages are published; these maintenance versions remain
unpublished candidates until their own live, package and release gates pass. `dev` follows the
EF10 maintenance candidate; `master` is outside this change.

## Branches, CI/CD and publishing

- `dev` is the development branch for the latest EF Core major. It follows `efcore/10.0` while EF Core
  10 is current. Start the EF Core 11 upgrade on `dev` when that work begins.
- `master` is the integration branch for `dev`. CI/CD for work on `dev` runs on `master` after the merge.
  The `dev` branch itself is excluded from CI/CD.
- Each of `efcore/8.0`, `efcore/9.0` and `efcore/10.0` must run its own CI/CD and build the provider package
  for its matching EF Core major.
- Use the same [CI/CD workflow](../../.github/workflows/ci-cd.yml) on all three release branches.
  Branch and pull request runs test and verify a validation package on Windows and Linux.
- Use `v8.0.0`, `v9.0.0` or `v10.0.0` for the initial stable releases. The tag must match the
  project version and identify a commit on its matching version branch. The Windows tag run tests the
  branch, verifies the stable package and runs its isolated consumer. GitHub Actions publishes that artifact.
- Configure NuGet Trusted Publishing for `cocosip/efcore.oceanbase`, workflow `ci-cd.yml`,
  owner `virtual` and package `Sharp.OceanBase.EntityFrameworkCore`. The initial policy must allow
  new packages. Follow the [release authorization rules](../repository-guidelines.md#git-and-releases)
  before creating or pushing a release tag.

## Porting and dependency rules

- Preserve applicable behavior and explicit limitations. Adapt version-local APIs, service registration,
  model/query generation and tooling on each branch.
- Use `Sharp.OceanBase.EntityFrameworkCore` as the default NuGet package ID across EF Core majors.
  Override `OceanBasePackagePrefix` with `-p:OceanBasePackagePrefix=<prefix>` to pack as
  `<prefix>.OceanBase.EntityFrameworkCore`. Match the package major to EF Core and bound dependencies
  to that major: 8.x uses `[8.0.0,9.0.0)`, 9.x uses `[9.0.0,10.0.0)`, and 10.x uses `[10.0.0,11.0.0)`.
  The branch defines its range in [Directory.Packages.props](../../Directory.Packages.props).
- Review target-major breaking changes and the current Provider feature contract. EF release notes,
  inherited EF behavior and EF10 test results do not automatically establish support on a port.
- Keep shared code small. Backport only EF-independent fixes deliberately; each version must build and
  test without compiling another version branch.
- Before releasing a port, run that major's provider/relational/specification, package-consumer and both-mode
  live checks. Record version differences and exclusions in its owning inventories under the same
  [documentation rules](../../AGENTS.md). Collect fresh evidence for each major.

## EF Core 8 and 9 runtime scope

Match each Provider package's target framework to its EF Core major. The EF9 Provider targets
`net9.0`, and the EF8 Provider targets `net8.0`. Microsoft ended upstream support for EF Core 8, EF Core 9,
.NET 8 and .NET 9 on November 10, 2026. Record verified runtime profiles in each version's release checklist.
[Lifecycle and upstream API references](official-references.md#version-references)
