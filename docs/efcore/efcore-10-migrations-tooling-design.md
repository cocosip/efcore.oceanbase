# EF Core 10 Migrations Tooling

## Outcome and boundary

Finish the developer-facing migrations workflow over the existing core: configure a migrations assembly and
history table, discover provider design services, scaffold initial/evolution migrations and snapshots, compile
them, detect pending model changes, generate upgrade/revert/idempotent scripts and build runnable bundles.
This advances DEF-01/02, CFG-04 and PKG-03/04. Reverse engineering and database application remain separate
acceptance work. EF8/EF9, push, tag and publication remain postponed.

Existing graph/materialization and graph-write fixtures cover the basic runtime paths. Official query/write
execution specifications still need a usable database. The core migrations module has no design-service
attribute or public assembly/history configuration, so tooling is the next complete local caller to finish.
This ordering is an implementation decision within the user's continuing whole-feature development request.

## Approach

Register EF's relational design services in the existing provider assembly, using only the already referenced
EF Core/Relational contracts. A separate design package would add packaging/discovery work without a separate
deployment need; custom C# migration/snapshot generation would duplicate EF's generators. Reuse EF's
generators, including raw provider annotations, and test their actual output through compilation and CLI calls.
Microsoft.EntityFrameworkCore.Design is an explicit development dependency of the tooling test consumer only.

Add an OceanBaseDbContextOptionsBuilder facade for MigrationsAssembly(string?) and
MigrationsHistoryTable(string, string?). Four new action overloads of UseOceanBase cover typed/untyped builders
and connection strings/existing DbConnections. Keep every existing three-argument overload intact for binary
compatibility. Configure the mode first, then invoke the callback once; relational option clones must preserve
schema/namespace management settings and connection ownership. Explicit modes remain mandatory.

Use DesignTimeProviderServicesAttribute and a public parameterless IDesignTimeServices implementation that
calls EntityFrameworkRelationalDesignServicesBuilder.TryAddCoreServices. Do not register placeholder reverse
engineering services or a mode-guessing context generator. No new runtime dependency or provider package ID.

## Verification

Unit tests use EF's actual design-time builder/scaffolder to verify discovery, identity/default/converter
metadata and repeatable snapshots. Configuration tests cover all overloads, callbacks, null/invalid values,
clone preservation, history scope and a separate migration assembly. Existing runtime consumers must continue
working without EF Design installed.

A package-only ToolingConsumer and PowerShell verifier create a unique disposable work folder under artifacts.
They invoke the installed dotnet-ef CLI on both modes and EF10.0.0/10.0.14: initial migration, compiled snapshot,
expanded model, no pending changes, forward/reverse/idempotent scripts, migration listing and bundle build/help.
EF10 has no offline remove flag. A no-network interceptor blocks connection attempts; --force removal may
inspect the database but must never reach one. Record this EF behavior and verify the snapshot after removal.
Do not run database update or execute a bundle against a server. All sample connection strings are inert;
real credentials are never copied into generated code or bundle configuration.

Use full Debug/Release and isolated selected-patch suites, unchanged package-content checks, the existing
runtime package consumer, independent whole-module review, documentation updates and one feature commit.
Native application, SQL-client acceptance and runtime query/write specifications remain open.

## Version contracts

EF Core/Relational 10.0.0 supply `DesignTimeProviderServicesAttribute`, `IDesignTimeServices`,
`EntityFrameworkRelationalDesignServicesBuilder`, `RelationalOptionsExtension.WithMigrationsAssembly` and
the history-table clone methods. The provider design builder registers the `ITypeMappingSource` alias to
the context's `IRelationalTypeMappingSource` with EF's singleton lifetime, then EF's relational code-generation
services. EF Design supplies the consuming `DesignTimeServicesBuilder` and `IMigrationsScaffolder`; it is not
a provider runtime dependency. The same source contracts are checked against EF10.0.14.

References: EF [design-service registration](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Design/EntityFrameworkRelationalDesignServicesBuilder.cs),
[relational options](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Infrastructure/RelationalOptionsExtension.cs),
[design-time discovery](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Design/Design/Internal/DesignTimeServicesBuilder.cs),
and [bundle generation](https://github.com/dotnet/efcore/blob/v10.0.0/src/ef/Commands/MigrationsBundleCommand.cs).
The installed CLI is dotnet-ef 10.0.15; runtime and Design packages are selected explicitly per fixture run.
