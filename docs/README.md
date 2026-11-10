# OceanBase Entity Framework Core Provider Documentation

This directory contains the design, support boundaries, development rules and release process for the
OceanBase Entity Framework Core provider built on top of OceanBaseConnector.

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
| [EF Core references](efcore/official-references.md) | Official Microsoft documentation to read before implementing each version. |

The version documents describe intended support. They are not evidence of implementation or live OceanBase
compatibility until the corresponding tests and server matrix pass.
