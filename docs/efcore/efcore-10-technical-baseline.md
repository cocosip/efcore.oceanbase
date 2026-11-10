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

EF Core 10 requires .NET 10 to build and run. NuGet normally selects the lowest applicable version inside a bounded
range, unless a consumer dependency requires a higher version. A range permits consumer patch upgrades; it does not
automatically test those upgrades. The package-consumer gate must record the SDK, runtime, resolved EF Core patch,
provider package and connector.

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

## Connection validation and mapping checkpoint

The following contracts were checked against EF Core Relational 10.0.0 sources and the published
OceanBaseConnector 0.2.0 public API:

- [RelationalConnection lifecycle](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Storage/RelationalConnection.cs).
- [DecimalTypeMapping](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Storage/DecimalTypeMapping.cs)
  and [StringTypeMapping](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Storage/StringTypeMapping.cs).
- [Property types, length and precision](https://learn.microsoft.com/ef/core/modeling/entity-properties).

`RelationalConnection` reads `DbConnection` during disposal/reset. The provider therefore validates caller-supplied
connections during construction, replacements before assignment, created connection strings before allocation,
and existing connections before each sync/async open. A getter does not revalidate a rejected connection during
cleanup. Failed replacement validation preserves the previous connection. EF's base implementation retains
ownership and disposal behavior. Connection interceptors must not replace connector connections with incompatible
implementations; validation before opening also covers intercepted creation.

| Mapping facet | MySQL contract | Oracle contract |
| --- | --- | --- |
| Default decimal | DECIMAL(65,30), enough integer/fractional capacity for CLR decimal values | Unqualified NUMBER, allowing connector value-based precision/scale inference |
| Configured decimal | Precision 1..65; scale 0..30 and no greater than precision | Precision 1..38; scale -84..127, including scale greater than precision |
| Precision without scale | Scale defaults to zero | Scale defaults to zero |
| Explicit decimal type | DECIMAL/NUMERIC declarations are preserved; unqualified type uses store defaults (10,0) | NUMBER/DECIMAL/NUMERIC declarations are preserved |
| Default string | LONGTEXT; bounded VARCHAR(255) for a key/index | NVARCHAR2(2000); bounded NVARCHAR2(255) for a key/index |
| Bounded variable string | VARCHAR(n) up to 16383 characters, otherwise LONGTEXT | NVARCHAR2(n) up to 2000; non-Unicode VARCHAR2(n CHAR) up to 4000, otherwise NCLOB/CLOB |
| Fixed string | CHAR(n), at most 255 | NCHAR(n), at most 1000; non-Unicode CHAR(n CHAR), at most 2000 |
| String parameter facets | DbType reflects Unicode/fixed length; Size is set explicitly | Same metadata contract; CHAR/BYTE units in explicit store declarations preserve Size |

Decimal mappings explicitly configure parameter Precision and the connector's DecimalScale, because the generic
EF mapping does not set these parameter facets. DecimalScale preserves Oracle negative scales through the public
connector API; values that require rounding remain subject to connector validation. String mappings explicitly set
parameter Size. Cloning retains these provider parameter hooks. Input null becomes DBNull; output-only parameters
retain their unset value, direction and facets until execution.

The text limits are conservative mapping choices, not a guarantee of every charset/server byte limit. MySQL
Unicode depends on the configured tenant/column character set; the Unicode flag does not configure that charset.
Oracle CHAR/BYTE declarations retain their explicit storage semantics. Large Oracle strings still use LOB storage,
which restricts ordering/grouping. Live charset, byte-limit, decimal round-trip and LOB execution tests remain open.
Scalar SQL literals, Boolean/Guid/temporal behavior, remaining explicit store types and value converters also remain
open acceptance work; this checkpoint does not close the entire MAP category.

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
