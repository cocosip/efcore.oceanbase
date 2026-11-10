# EF Core 10 Provider Feature Checklist

This checklist is the acceptance boundary for the first `OceanBase.EntityFrameworkCore` 10.x release.
It answers two questions for every feature: whether the provider claims support, and what evidence is required
before that claim can be made.

The checklist applies independently to OceanBase MySQL mode and Oracle mode. A row is complete only when both
modes pass, unless the row explicitly says that it is mode-specific. A passing unit test is evidence of provider
behavior; it is not evidence that a live OceanBase server, proxy, TLS setup or every connector option works.

## Status vocabulary

| Status | Meaning |
| --- | --- |
| `Done` | Implemented and covered by the required automated tests. |
| `In progress` | The contract is agreed and implementation or tests are being written. |
| `Planned` | Required for the first release, but implementation has not started. |
| `Deferred` | Intentionally outside the first release; do not advertise it as supported. |
| `Blocked` | The behavior depends on an unavailable connector or server capability. Record the dependency. |

## Release gate

The 10.x package is releasable only when all rows marked `Required` are `Done`:

- provider configuration and service-provider identity;
- connector-backed connections, commands, parameters, transactions and cancellation;
- model building and mode-aware model caching;
- scalar mappings and mode-specific SQL generation;
- basic query, update, generated-key and concurrency paths;
- package restore with the bounded EF Core dependency range `[10.0.0,11.0.0)`;
- consumer, unit, functional and selected relational specification tests;
- live validation against the supported OceanBase MySQL and Oracle environments, with unavailable environments
  recorded as unverified.

The first release does not claim support merely because an EF Core API exists. The provider must either implement
and test it or list it under `Deferred`.

## Core provider contract

| Area | Requirement | Priority | Evidence | Status |
| --- | --- | --- | --- | --- |
| Configuration | `UseOceanBase` accepts a connection string and an existing `DbConnection` | Required | Unit tests for both overloads and both modes | Done |
| Configuration | MySQL and Oracle compatibility mode is explicit and validated | Required | Unit tests for valid and invalid modes | Done |
| Configuration | `Database.IsOceanBase()` and provider invariant identify the provider | Required | Consumer test through the real EF service graph | Done |
| Configuration | MySQL and Oracle mode participate in service-provider and model-cache identity | Required | Distinct cache-key tests | Done |
| Diagnostics | Provider logging definitions and debug information are registered | Required | Options/debug-info tests and build with warnings as errors | Done |
| Packaging | Package ID is `OceanBase.EntityFrameworkCore` and the EF dependency range is bounded to 10.x | Required | Pack and isolated consumer restore | In progress |
| Versioning | The package targets the runtime required by EF Core 10 | Required | Build and consumer matrix | Planned |

## Connector and relational services

| Area | Requirement | Priority | Evidence | Status |
| --- | --- | --- | --- | --- |
| Connection | Create `OceanBaseConnection` through the public OceanBaseConnector API | Required | Relational connection unit test | Done |
| Connection | Map provider mode to connector `ExpectedMode` and reject conflicting settings | Required | Connection-string tests for MySQL and Oracle | Planned |
| Connection | Respect caller-owned connections and dispose only provider-owned connections | Required | Ownership and disposal tests | Planned |
| Execution | Create connector commands and parameters without protocol code in the provider | Required | Command/parameter integration tests | Planned |
| Execution | Sync and async open, execute, read and close paths use the same mode contract | Required | Functional tests for sync and async paths | Planned |
| Transactions | Begin, commit, rollback and use-existing-transaction work through the connector | Required | Functional transaction tests | Planned |
| Transactions | Cancellation and command timeout are passed to the connector | Required | Cancellation/timeout tests where the connector exposes the behavior | Planned |
| Relational services | Register `IRelationalConnection`, `ISqlGenerationHelper` and `IRelationalTypeMappingSource` | Required | Consumer service-resolution test | Done |

## Model and scalar type mapping

| Area | Requirement | Priority | Evidence | Status |
| --- | --- | --- | --- | --- |
| Model | Entity types, keys, properties, relationships, indexes and owned navigations build normally | Required | Model-building tests | Planned |
| Model | Value generation metadata is preserved for generated keys | Required | Model and insert tests | Planned |
| Scalar mapping | Boolean, signed/unsigned integer, floating-point, decimal and `Guid` mappings | Required | Mapping metadata and parameter tests in both modes | Planned |
| Scalar mapping | Unicode/non-Unicode text, fixed text and binary mappings | Required | Mapping metadata and parameter tests in both modes | Planned |
| Scalar mapping | `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` and `TimeSpan` mappings | Required | Mapping metadata and round-trip tests | Planned |
| Scalar mapping | Null values, size, precision, scale and parameter direction are preserved | Required | Parameter creation tests | Planned |
| Specialized mapping | Enums, spatial, JSON, XML, arrays, LOB locators, OBJECT and REF CURSOR values | Deferred | Design note required before implementation | Deferred |

## Query SQL

| Area | Requirement | Priority | Evidence | Status |
| --- | --- | --- | --- | --- |
| SQL helper | Quote identifiers according to MySQL backtick and Oracle double-quote rules | Required | Helper tests for reserved and embedded identifiers | Done |
| SQL helper | Generate mode-appropriate parameter names and placeholders | Required | Helper tests for both modes | Done |
| Query | SELECT, projection, scalar predicates, null checks and parameterization | Required | SQL generator and functional tests | Planned |
| Query | Ordering, joins, grouping and aggregates | Required | SQL generator and functional tests | Planned |
| Query | Paging with `Skip`/`Take` using syntax supported by both modes | Required | SQL generator tests for both modes | Planned |
| Query | String, date, numeric and conditional translations advertised by the provider | Required | Translation matrix with positive and negative cases | Planned |
| Query | Raw SQL and interpolated SQL use connector parameters | Required | Functional parameterization tests | Planned |
| Query | Unsupported translation fails clearly and does not silently evaluate on the client | Required | Negative translation tests | Planned |

## Update SQL and concurrency

| Area | Requirement | Priority | Evidence | Status |
| --- | --- | --- | --- | --- |
| Insert | INSERT commands bind values and retrieve generated keys without SQL Server `OUTPUT` assumptions | Required | Generated-key functional tests in both modes | Planned |
| Update | UPDATE commands bind values and report affected rows | Required | Update functional tests | Planned |
| Delete | DELETE commands bind keys and report affected rows | Required | Delete functional tests | Planned |
| Concurrency | Concurrency tokens produce the expected conflict result | Required | Positive and conflict functional tests | Planned |
| Bulk DML | `ExecuteUpdate` and `ExecuteDelete` are supported only after separate EF10 translation tests pass | Deferred | Dedicated design and functional evidence | Deferred |

## Schema, migrations and tooling

| Area | Requirement | Priority | Evidence | Status |
| --- | --- | --- | --- | --- |
| Database creator | Connection/database existence checks use OceanBaseConnector safely | Required | Functional tests where server capability is available | Planned |
| Schema creation | `EnsureCreated`/`EnsureDeleted` support only the documented first-release schema profile | Required | Functional schema tests | Planned |
| Migrations | Migration SQL generation and execution | Deferred | Separate feature plan and specification-test selection | Deferred |
| Reverse engineering | Scaffold an OceanBase schema | Deferred | Separate design and live fixtures | Deferred |
| Design-time tooling | `dotnet ef` design-time factory and provider registration | Deferred | Separate package-consumer/tooling test | Deferred |

## EF Core 10 review items

These features are reviewed before release because EF Core 10 changes their surrounding contracts. They are not
implicitly supported by the provider's EF Core 10 package reference.

| EF Core 10 area | Release decision | Required evidence |
| --- | --- | --- |
| Complex types | Planned only if both modes have stable relational mappings | Model, query and update tests |
| Named query filters | Planned only if SQL generation preserves filter composition | Query translation tests |
| `ExecuteUpdateAsync` lambda overload | Deferred until update SQL coverage is complete | Async update tests |
| JSON column improvements | Deferred with JSON mappings | Dedicated JSON design and server tests |
| EF10 seeding changes | Deferred until database creator and insert paths are complete | Seeding functional tests |
| New EF10 logging/security events | Review provider diagnostics after the core slice | Logging tests and release notes |

## Required test evidence

Every required row must point to at least one test. The release test layers are:

1. Unit tests for options, mode identity, SQL helper, mappings and parameter metadata.
2. Consumer tests that restore and resolve the packed provider through EF's real service provider.
3. Functional tests for queries, updates, generated keys, concurrency, transactions and schema operations.
4. Selected EF Core relational specification tests, with every excluded base test listed and justified.
5. Live OceanBase tests recorded separately for MySQL mode, Oracle mode and the connector/server environment.

Missing proxy, TLS, HA, compression or server-version infrastructure is recorded as unverified evidence. It is not
converted into a provider support claim.

## Definition of complete

A feature is complete only when its implementation, public behavior, both-mode test coverage, documentation status
and package/consumer impact are all updated together. A branch may be merged only when the release gate rows above
are `Done`, and a package may be published only after the matching branch, dependency range and test evidence are
recorded in the release notes.
