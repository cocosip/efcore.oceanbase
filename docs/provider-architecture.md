# Provider Architecture

The provider implements EF Core relational behavior over OceanBaseConnector's public ADO.NET APIs.
MySQL and Oracle refer to OceanBase tenant compatibility modes; upstream database behavior alone does
not establish support on OceanBase. The current dependency and assets are declared in
[Directory.Packages.props](../Directory.Packages.props) and [the provider project](../src/OceanBase.EntityFrameworkCore/OceanBase.EntityFrameworkCore.csproj).

## Layer responsibilities

| Layer | Responsibility |
| --- | --- |
| EF Provider | Options/service registration, model conventions, type mappings, LINQ translation, materialization/tracking integration, update and migration SQL, reverse-engineering models and operation orchestration |
| OceanBaseConnector | Connections, commands, parameters, readers, native transactions, public schema catalogs, protocol, authentication, TLS, pooling and routing |
| OceanBase Database | Available SQL syntax, types, system views, constraints, DDL behavior and server execution semantics |

EF batching and retry orchestration do not implement Connector wire batching, routing or failover.
A missing `GetSchema` field does not prove that the server lacks the information or that ordinary
Connector commands cannot query it. Distinguish a database limitation, a driver API limitation,
unfinished Provider integration and missing acceptance evidence before assigning ownership.
Proposed driver changes require the authorization described in [repository guidelines](repository-guidelines.md).

## Compatibility mode and lifetime

Configure the mode explicitly with `UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql)`
or `OceanBaseCompatibilityMode.Oracle`. Provider-created connections receive `CompatibilityMode`; supplied
connections retain their configuration. The relational connection validates configured and detected
modes when opening, rejects conflicts and preserves caller ownership.

With Connector 0.5.0, an omitted or `Auto` configured mode is accepted. Provider-created connections
receive the explicit EF mode; supplied connections are checked without rewriting their configuration.
Reverse engineering still requires an explicit MySql or Oracle configuration before opening.

Mode participates in EF's service-provider, model and compiled-query cache identity. The model key
preserves EF's base context/design-time identity. Compiled models must be generated and consumed with
matching mode and proxy options. Reusing an incompatible model or sharing a connection concurrently
is outside that contract.

## Source map

Paths below are relative to `src/OceanBase.EntityFrameworkCore/`:

| Area | Entry points |
| --- | --- |
| Public configuration and service registration | `OceanBaseDbContextOptionsBuilderExtensions.cs`, `Internal/OceanBaseOptionsExtension.cs` |
| Model identity and conventions | `Internal/OceanBaseModelCacheKeyFactory.cs`, `Metadata/` |
| Connections, type/parameter mappings and transaction ownership | `Storage/` |
| LINQ translation, query shaping, buffering and precompilation | `Query/` |
| Write SQL, generated-value readback and modification batches | `Update/` |
| Migration SQL, model differences, history and locking | `Migrations/` |
| Public Connector catalog consumption and generated model code | `Design/` |
| Sequence-backed client value allocation | `ValueGeneration/` |

Mode-specific behavior stays explicit. Share code only when the SQL, mapping, ownership and error
contracts agree. EF internal APIs are version-local; [version support](efcore/version-support-matrix.md)
owns branch and porting policy. The [feature inventory](efcore/efcore-10-feature-matrix.md) owns detailed
capabilities, the [gap inventory](efcore/efcore-10-gap-matrix.md) owns unresolved scope, and the
[release checklist](efcore/efcore-10-release-checklist.md) owns revision-specific acceptance and publication.
