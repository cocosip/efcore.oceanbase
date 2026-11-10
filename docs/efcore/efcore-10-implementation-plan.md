# EF Core 10 Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Build the first tested OceanBase EF Core provider for EF Core 10 with explicit MySQL and Oracle compatibility modes.

**Architecture:** Keep dev and efcore/10.0 aligned, with efcore/10.0 used for packaging. The provider consumes the
public OceanBaseConnector ADO.NET API. Mode-independent EF services are shared only when generated SQL and result
semantics are identical; mode-specific SQL and mappings stay explicit.

**Tech Stack:** C#, .NET 10, EF Core 10, EF Core Relational, OceanBaseConnector, xUnit, relational specification tests.

**Spec:** docs/efcore/efcore-10-design.md and docs/efcore/efcore-10-technical-baseline.md

**Acceptance checklist:** docs/efcore/efcore-10-feature-checklist.md. A task is complete only when the affected
checklist rows, implementation, tests and package impact are updated together.

## Acceptance map and current state

The feature checklist is the release authority. Task checkboxes describe work steps, not proof that all features
in a category are supported. Before starting a step, name its checklist IDs and required evidence; after it,
update MySQL and Oracle statuses independently. Generated SQL and intercepted tests do not close live gates.

| Task | Checklist IDs | Remaining acceptance work |
| --- | --- | --- |
| 1: Package boundary | PKG-01 through PKG-04 | f645608 package passes both selected patches; repeatable release package-content gate remains. |
| 2: Configuration | CFG-01 through CFG-04 | Negative cases, both-mode service graph and actual cache behavior. |
| 3: Connection and mappings | CON-01 through CON-05, SQL-01/02, MOD-01 through MOD-03, MAP-01 through MAP-07 | Closed-connection cleanup/facets/scalar/temporal/binary conversion/precision/length hooks have tests; opened ownership, remaining explicit types and live round trips remain. |
| 4: Runtime and schema | DIA-01/02, QRY-01 through QRY-11, UPD-01 through UPD-05, TXN-01 through TXN-03, SCH-01 through SCH-03 | Live query/write/transaction evidence and schema implementation; incomplete. |
| 5: Release evidence | PKG-02 through PKG-04, TST-01/02, REL-01/02 | Specification tests, release-revision package/patch validation and supported-environment records. |

The connection-disposal regressions have local regression coverage. Finish Task 3 acceptance, including live
ownership and complete scalar semantics, before expanding Task 4 support claims. Passing metadata tests alone
do not complete mappings.

Before further production changes, use the checklist's **Before starting a feature** record to enumerate concrete
cases for the selected IDs. The **Next mapping acceptance cases** table defines the immediate scalar work;
its initial failing tests are the RED baseline. Temporal query/write revision f645608 passes 450 tests
in Debug/Release and isolated EF10.0.14 Release (339 unit, 110 intercepted pipeline, one project consumer), including
10 new query-transport unit cases and eight query/write pipeline cases. The same baseline-built package passes
both modes on EF10.0.0 and EF10.0.14. This is partial acceptance, not Task 3/4 completion or live evidence.

## Global Constraints

- The release branch is efcore/10.0 and is merged from dev after tests pass.
- The package ID is OceanBase.EntityFrameworkCore and the package line is 10.x.
- EF Core and EF Core Relational dependencies use the NuGet range [10.0.0, 11.0.0).
- MySQL and Oracle compatibility mode is explicit and participates in the model-cache key.
- Connector protocol, pooling, TLS, authentication and connection-string parsing remain in OceanBaseConnector.
- Migrations, reverse engineering, JSON/LOB/OBJECT/REF CURSOR mappings and bulk APIs are deferred.
- All public behavior must be tested locally; live database evidence is reported separately.
- The user will supply database access later; local live credentials must use .NET User Secrets under the
  [repository credential/ownership contract](../repository-guidelines.md#live-test-credentials-and-ownership).
- Commit each completed task with a Gitmoji Conventional Commit before starting the next task.

## Review Focus

- A model built for MySQL must never be reused for Oracle; Task 2 tests the mode-aware model cache key.
- Identifier quoting and parameter prefixes must match each tenant mode; Task 3 tests both SQL dialects.
- Generated keys must not assume SQL Server OUTPUT; Task 4 tests the connector-compatible key path.
- Sync and async commands must use the same mode-specific SQL; Task 4 tests both execution paths.
- A packed provider must resolve only EF Core 10 dependencies; Task 5 tests the package consumer.

### Task 1: Create the EF10 solution and package boundary

**Files:**
- Create: OceanBase.EntityFrameworkCore.slnx
- Create: Directory.Build.props
- Create: Directory.Packages.props
- Create: src/OceanBase.EntityFrameworkCore/OceanBase.EntityFrameworkCore.csproj
- Create: tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj
- Create: tests/OceanBase.EntityFrameworkCore.ConsumerTests/OceanBase.EntityFrameworkCore.ConsumerTests.csproj

**Interfaces:**
- Consumes: EF Core 10 and the public OceanBaseConnector package or coordinated project reference.
- Produces: A net10.0 provider assembly with package ID OceanBase.EntityFrameworkCore and central package versions.

- [ ] Step 1: Write the failing package-consumer smoke test.

Create a consumer test that references only the packed provider and connector packages, configures a DbContext with
UseOceanBase, and asserts that the provider invariant is OceanBase.EntityFrameworkCore.

- [ ] Step 2: Run the smoke test to verify the provider API boundary is still missing.

Run: dotnet test tests/OceanBase.EntityFrameworkCore.ConsumerTests/OceanBase.EntityFrameworkCore.ConsumerTests.csproj

Expected: FAIL because the provider project exists but the UseOceanBase, mode, and provider-check APIs are
not implemented until Task 2.

- [ ] Step 3: Create the solution, central package files and provider project.

Target net10.0, enable nullable, disable implicit usings, treat warnings as errors, reference EF Core 10 and
EF Core Relational 10, and reference OceanBaseConnector through its public package boundary. Add the unit and
consumer test projects to the solution.

- [ ] Step 4: Run restore and build for the completed Task 1 projects.

Run: dotnet build src/OceanBase.EntityFrameworkCore/OceanBase.EntityFrameworkCore.csproj
and `dotnet build tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj`.

Expected: PASS with the provider and unit-test projects restored against EF Core 10. The consumer project remains
the intentional RED test until Task 2 adds the provider configuration API.

### Task 2: Add options, mode selection and model-cache identity

**Files:**
- Create: src/OceanBase.EntityFrameworkCore/OceanBaseCompatibilityMode.cs
- Create: src/OceanBase.EntityFrameworkCore/OceanBaseDbContextOptionsBuilderExtensions.cs
- Create: src/OceanBase.EntityFrameworkCore/Internal/OceanBaseOptionsExtension.cs
- Create: src/OceanBase.EntityFrameworkCore/Internal/OceanBaseModelCacheKeyFactory.cs
- Create: tests/OceanBase.EntityFrameworkCore.UnitTests/OptionsTests.cs
- Modify: src/OceanBase.EntityFrameworkCore/OceanBase.EntityFrameworkCore.csproj

**Interfaces:**
- Produces: UseOceanBase(string, OceanBaseCompatibilityMode), UseOceanBase(DbConnection, OceanBaseCompatibilityMode),
  OceanBaseCompatibilityMode, and an IsOceanBase provider check.

- [ ] Step 1: Write tests for both modes and model-cache separation.

Assert that MySql and Oracle options retain their mode, both UseOceanBase overloads register the provider, and two
contexts that differ only by mode produce different model-cache keys.

- [ ] Step 2: Run the focused tests to verify failure.

Run: dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj --filter FullyQualifiedName~OptionsTests

Expected: FAIL because the options extension and mode types are missing.

- [ ] Step 3: Implement the EF10 options extension and model-cache key.

Implement the exact EF10 interfaces verified from the 10.x assemblies. Apply provider services through
IDbContextOptionsExtension and include the explicit mode in the cache key.

- [ ] Step 4: Run the focused tests.

Expected: PASS, with no warnings and distinct cache keys for MySQL and Oracle.

### Task 3: Implement relational connection, SQL helper and scalar mappings

Task 3 is incomplete. Connection cleanup, closed-connection ownership and string/decimal facets have local tests;
opened-connection behavior, remaining scalar semantics and live round trips still need CON/MOD/MAP evidence.
The decimal slice adds full declaration parsing, property precision/scale conflict checks, converter-preserving
clones and exact DML-only write validation. Query inputs infer precision/scale from their value independently of
column capacity; output-only metadata retains physical facets. Decimal alias defaults and live round trips remain
open, along with remaining string contracts and the wider mapping inventory.

**Files:**
- Create: src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRelationalConnection.cs
- Create: src/OceanBase.EntityFrameworkCore/Storage/OceanBaseSqlGenerationHelper.cs
- Create: src/OceanBase.EntityFrameworkCore/Storage/OceanBaseTypeMappingSource.cs
- Create: src/OceanBase.EntityFrameworkCore/Storage/OceanBaseTypeMappings.cs
- Create: tests/OceanBase.EntityFrameworkCore.UnitTests/SqlGenerationHelperTests.cs
- Create: tests/OceanBase.EntityFrameworkCore.UnitTests/TypeMappingTests.cs

**Interfaces:**
- Consumes: Task 2 options and OceanBaseConnector DbConnection, DbCommand and DbParameter APIs.
- Produces: Mode-aware relational connection, identifier/parameter generation and mappings for common scalar CLR types.

- [x] Step 1: Write mode-specific helper and mapping tests.

Assert MySQL and Oracle identifier delimiters, parameter names, null handling, numeric/text/binary/date-time/decimal
mapping metadata and provider parameter creation.

- [x] Step 2: Run focused tests to verify failure.

Run: dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests/OceanBase.EntityFrameworkCore.UnitTests.csproj

Expected: FAIL because the relational services are missing.

- [ ] Step 3: Implement the relational connection, SQL helper and mappings.

Use the exact EF10 service contracts and lifetimes. Delegate opening, commands, parameters and transactions to
OceanBaseConnector. Keep mode-specific quoting and parameter rules in explicit branches.

- [ ] Step 4: Run focused tests and build.

Expected: PASS with both mode mapping suites and no direct protocol code in the provider.

### Task 4: Implement query and update SQL for the first vertical slice

Runtime implementation and test boundaries are recorded in
[the query and update design](efcore-10-query-update-design.md). Query SQL and intercepted sync/async command tests
are present. Database creation, live generated keys and transaction verification are still open; Task 4 is incomplete.

**Files:**
- Create: src/OceanBase.EntityFrameworkCore/Query/OceanBaseQuerySqlGenerator.cs
- Create: src/OceanBase.EntityFrameworkCore/Query/OceanBaseQuerySqlGeneratorFactory.cs
- Create: src/OceanBase.EntityFrameworkCore/Update/OceanBaseUpdateSqlGenerator.cs
- Create: src/OceanBase.EntityFrameworkCore/Update/OceanBaseModificationCommandBatchFactory.cs
- Create: src/OceanBase.EntityFrameworkCore/Storage/OceanBaseDatabaseCreator.cs
- Create: tests/OceanBase.EntityFrameworkCore.UnitTests/SqlGeneratorTests.cs
- Create: tests/OceanBase.EntityFrameworkCore.FunctionalTests/BasicRelationalTests.cs
- Create: tests/OceanBase.EntityFrameworkCore.FunctionalTests/OceanBase.EntityFrameworkCore.FunctionalTests.csproj

**Interfaces:**
- Consumes: Tasks 2 and 3 services.
- Produces: Basic SELECT, predicate, projection, join, paging, INSERT, UPDATE and DELETE execution with generated
  key handling for both modes.

- [ ] Step 1: Write SQL and functional tests.

Assert generated SQL for both modes and execute a small model covering model creation, scalar query, generated-key
insert, update, delete, transaction rollback, synchronous execution and asynchronous execution.

- [ ] Step 2: Run tests to verify failure.

Run: dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests/OceanBase.EntityFrameworkCore.FunctionalTests.csproj

Expected: FAIL because query/update services and relational database creation are missing.

- [ ] Step 3: Implement the minimal EF10 query/update pipeline.

Register only the services needed by the tested feature profile. Use connector-compatible generated-key retrieval;
do not copy SQL Server OUTPUT behavior. Keep paging and key-generation differences mode-specific.

- [ ] Step 4: Run unit and available live tests.

Run: dotnet test OceanBase.EntityFrameworkCore.slnx

Expected: PASS for local tests. Live results are recorded by mode and environment; unavailable infrastructure is
reported as unverified.

### Task 5: Add specification tests, package consumer validation and release packaging

**Files:**
- Create: tests/OceanBase.EntityFrameworkCore.SpecificationTests/OceanBase.EntityFrameworkCore.SpecificationTests.csproj
- Create: tests/OceanBase.EntityFrameworkCore.SpecificationTests/OceanBaseSpecificationTestFixture.cs
- Create: tests/OceanBase.EntityFrameworkCore.SpecificationTests/SelectedRelationalSpecificationTests.cs
- Modify: OceanBase.EntityFrameworkCore.slnx
- Modify: src/OceanBase.EntityFrameworkCore/OceanBase.EntityFrameworkCore.csproj

**Interfaces:**
- Consumes: The EF10 provider and selected Microsoft.EntityFrameworkCore.Relational.Specification.Tests fixtures.
- Produces: A packed OceanBase.EntityFrameworkCore 10.x package and consumer evidence that only EF Core 10 is resolved.

- [ ] Step 1: Select and wire specification-test bases.

Start with relational query, update, transaction and model tests that match the first feature profile. Record each
selected class and intentional exclusion in the EF10 design documentation.

- [ ] Step 2: Run selected specification tests.

Run: dotnet test tests/OceanBase.EntityFrameworkCore.SpecificationTests/OceanBase.EntityFrameworkCore.SpecificationTests.csproj

Expected: Passing tests for the selected profile, with unsupported cases explicitly excluded and documented.

- [ ] Step 3: Pack and test the package.

Run: dotnet pack src/OceanBase.EntityFrameworkCore/OceanBase.EntityFrameworkCore.csproj -c Release
then run the package-consumer project against the produced package.

Expected: The package contains net10.0 assets, has a bounded EF Core 10 dependency, and consumer restore does not
select EF Core 8 or EF Core 9 provider dependencies.

- [ ] Step 4: Merge and tag the release branch.

Run: git switch efcore/10.0, merge the validated dev commit, run the release checks, and create the 10.x tag.

Expected: efcore/10.0 contains the same tested provider as dev and is the only branch used for the first package.
