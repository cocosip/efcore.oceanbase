# OceanBase Entity Framework Core Provider Documentation

This directory contains the design, support boundaries, development rules and release process for the
OceanBase Entity Framework Core provider built on top of OceanBaseConnector.

Start development and acceptance review with the [EF Core 10 feature checklist](efcore/efcore-10-feature-checklist.md).
It assigns stable feature IDs, separate MySQL/Oracle statuses, acceptance criteria, evidence gaps and explicit
deferred features. The implementation plan follows those requirements; it does not replace them.

## Documents

| Document | Purpose |
| --- | --- |
| [Repository guidelines](repository-guidelines.md) | C# style, project layout, tests, commits and review rules. |
| [Provider architecture](provider-architecture.md) | Provider boundaries, MySQL/Oracle mode handling, branch and package strategy. |
| [Version support matrix](efcore/version-support-matrix.md) | Relationship between application TFM, EF Core version, provider branch and package. |
| [EF Core 8 design](efcore/efcore-8-design.md) | First implementation target and its supported feature profile. |
| [EF Core 9 design](efcore/efcore-9-design.md) | EF Core 9 porting boundary and feature review checklist. |
| [EF Core 10 design](efcore/efcore-10-design.md) | EF Core 10 porting boundary and feature review checklist. |
| [EF Core 10 technical baseline](efcore/efcore-10-technical-baseline.md) | Exact provider contracts and first-slice verification gates. |
| [EF Core 10 feature checklist](efcore/efcore-10-feature-checklist.md) | Release feature boundary, statuses and definition of complete. |
| [EF Core 10 implementation plan](efcore/efcore-10-implementation-plan.md) | Development tasks mapped to checklist IDs and remaining acceptance work. |
| [EF Core 10 query and update pipeline](efcore/efcore-10-query-update-design.md) | EF10 service contracts, generated-value paths and command-test evidence limits. |
| [EF Core references](efcore/official-references.md) | Official Microsoft documentation to read before implementing each version. |

The version documents describe intended support. They are not evidence of implementation or live OceanBase
compatibility until the corresponding tests and server matrix pass.
