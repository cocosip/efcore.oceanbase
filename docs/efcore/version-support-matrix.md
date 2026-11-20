# EF Core Version Support

This checkout develops EF Core 10. Other major versions below are plans, not implemented or published
compatibility claims. Package existence and publication status must be checked on the intended feed
before release; local branches, validation packages and test passes do not establish publication.

| EF major | Branch | Provider line / target asset | Current state |
| --- | --- | --- | --- |
| 10 | `efcore/10.0` | `OceanBase.EntityFrameworkCore` 10.x / `net10.0` | Active implementation; accepted profiles and remaining publication steps are in the [release checklist](efcore-10-release-checklist.md) |
| 8 | `efcore/8.0` (planned) | 8.x / proposed `net8.0` | Port postponed until a stable EF10 baseline is selected; no acceptance claimed here |
| 9 | `efcore/9.0` (planned) | 9.x / proposed `net8.0` | Independent port from the selected EF10 baseline; no acceptance claimed here |
| Later majors | To be selected | Matching package major and independently verified target | No implementation or runtime support implied |

The EF10 provider asset requires a .NET 10 application target or a compatible later target that receives
its own verification. It cannot be consumed by a .NET 8/9 application merely by changing the EF package.
For proposed EF8/EF9 ports, confirm target framework, SDK/runtime and version-specific APIs against the
[official references](official-references.md) before implementation. Do not infer cross-runtime support
from NuGet asset selection.

## Porting and dependency rules

- Develop, test and package EF10 on `efcore/10.0`; `dev` retains an earlier integration checkpoint.
  Synchronization with other branches is deliberate.
- Fork EF8 and EF9 independently from a selected stable EF10 release commit. EF9 is not contingent on
  first completing an EF8 port. Preserve applicable behavior and explicit limitations; adapt version-local
  APIs, service registration, model/query generation and tooling on each branch.
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
  [documentation rules](../../AGENTS.md), rather than copying EF10 evidence or creating parallel process logs.
