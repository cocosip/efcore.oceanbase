# EF Core 10 Provider Technical Baseline

## Purpose

This document is the implementation reference for the first provider vertical slice. It records the EF Core
contracts that must be checked against the exact EF Core 10 package before code is written. It is intentionally
smaller than a complete EF Core feature catalog.

## Version and package baseline

- Development branch: dev.
- Release branch: efcore/10.0.
- EF dependency range: Microsoft.EntityFrameworkCore and Microsoft.EntityFrameworkCore.Relational [10.0.0, 11.0.0).
- Initial provider asset: the runtime target required by EF Core 10.
- ADO.NET dependency: the public OceanBaseConnector package APIs.
- Public package: OceanBase.EntityFrameworkCore 10.x.

EF Core 10 requires .NET 10 to build and run. Restore resolves the highest available EF Core 10 patch inside the
range; the first consumer test records the resolved SDK, runtime, EF Core patch, provider package and connector.

## Provider entry points

The first implementation must establish these contracts:

1. A UseOceanBase options extension for a connection string and an existing DbConnection.
2. An explicit OceanBaseCompatibilityMode value for MySQL or Oracle mode.
3. An options extension implementing the EF Core database-provider contract and applying provider services.
4. A provider invariant name and IsOceanBase database-facade check.
5. A model-cache key that includes the compatibility mode.

The connection string must remain owned by OceanBaseConnector. The EF provider passes it through and does not
reimplement connection-string parsing, pooling or protocol negotiation.

## EF Core service map

The implementation is split by EF Core contract. The exact constructor signatures and service lifetimes must be
verified against the 10.x assemblies before each task:

| Area | Contract to verify | First-slice responsibility |
| --- | --- | --- |
| Options | IDbContextOptionsExtension and its extension-info object | Register the provider and validate mode/options. |
| Model cache | IModelCacheKeyFactory | Separate MySQL and Oracle models. |
| Connection | IRelationalConnection / RelationalConnection | Create and own an OceanBaseConnection through the connector. |
| SQL helpers | ISqlGenerationHelper | Quote identifiers and create parameter names per mode. |
| Type mapping | IRelationalTypeMappingSource | Map common CLR and OceanBase scalar types. |
| Query SQL | IQuerySqlGeneratorFactory and provider query generator | Emit mode-correct SELECT, predicates, joins and paging. |
| Updates | IUpdateSqlGenerator and modification-command batch services | Emit INSERT, UPDATE and DELETE with parameters. |
| Key retrieval | Generated-value and result-column mapping services | Read database-generated keys without assuming SQL Server OUTPUT. |
| Database | IRelationalDatabaseCreator | Implement connection/database existence and EnsureCreated boundaries. |
| Conventions | IProviderConventionSetBuilder and model validation services | Apply only conventions required by the first feature profile. |

Migrations, reverse engineering, design-time services, JSON/LOB/OBJECT/REF CURSOR mappings and provider-specific
bulk operations are separate milestones. Their service contracts must not be added as placeholders.

## Mode-specific SQL contract

The MySQL and Oracle paths must have separate tests for:

- identifier delimiters and reserved words;
- parameter prefix and binding rules;
- Boolean and temporal literals/results;
- decimal precision and scale;
- paging syntax;
- identity or sequence key generation;
- affected-row and concurrency semantics;
- transaction and savepoint behavior.

Common code is allowed only where both modes produce the same SQL and result semantics. A mode check based only
on the connection string is insufficient when the EF model cache can reuse a model.

## Test entry points

The provider follows the EF Core provider-writing guidance:

- provider unit tests for options, mode selection and SQL fragments;
- selected Microsoft.EntityFrameworkCore.Relational.Specification.Tests classes with provider fixtures;
- SQL assertions for mode-specific SQL where result-only tests would miss a dialect error;
- package-consumer tests using packed provider and connector packages;
- live MySQL and Oracle tests when the server and permissions are available.

The first vertical slice should prove model creation, a scalar query, insert with generated key, update, delete,
transaction rollback and both sync/async paths in each mode. A passing unit test without a valid relational
command or reader path is not a provider milestone.

## EF10 feature review

EF10 release notes mention complex-type improvements, LINQ and SQL translation changes, relational JSON updates,
named query filters, ExecuteUpdateAsync changes and security-related logging behavior. These remain deferred until
the first slice is stable and their OceanBase SQL semantics are designed separately.

## Implementation gate

Before changing a version-local EF service, inspect the EF Core 10 assembly/XML documentation and the corresponding
specification-test base class. Record the verified interface, constructor dependencies, service lifetime and test
class in the implementation plan. Do not guess from an EF Core 8 or EF Core 9 signature.
