# Provider Architecture

## Intent

The provider exposes normal EF Core relational APIs over OceanBaseConnector. It supports both OceanBase MySQL
and Oracle tenant compatibility modes without duplicating the ADO.NET transport or connection implementation.

## Boundaries

OceanBaseConnector owns connections, commands, parameters, readers, transactions, batching, schema metadata,
protocol behavior, pooling and transport security. This repository owns EF Core options, provider service
registration, model conventions, type mappings, query translation, update SQL, database creation and migrations.

The EF provider must not infer unsupported server behavior from a mode name. Server version, tenant mode and
connector capability must be treated as separate facts.

## Compatibility mode

The mode is an explicit provider option and is part of the EF model-cache identity:

    optionsBuilder.UseOceanBase(
        connectionString,
        OceanBaseCompatibilityMode.MySql);

    optionsBuilder.UseOceanBase(
        connectionString,
        OceanBaseCompatibilityMode.Oracle);

This prevents a model built for MySQL quoting, type mappings or key generation from being reused for an Oracle
connection. Automatic server detection may be added later only if the mode is still included in the model cache
key and every mode-dependent service is safe to resolve at runtime.

## Version isolation

EF Core provider services use APIs that can change between major releases. The source is therefore maintained in
long-lived EF Core branches:

    dev       -> current integration work
    efcore-8  -> EF Core 8 provider and 8.x package line
    efcore-9  -> EF Core 9 provider and 9.x package line
    efcore-10 -> EF Core 10 provider and 10.x package line

The package ID stays OceanBase.EntityFrameworkCore. The package major version and dependency range identify the
EF Core major. A .NET 9 application can reference the 8.x package and EF Core 8 net8 assets; application TFM and
EF Core major are intentionally independent.

Shared code is kept in a small project or shared source directory and is backported deliberately. A provider
branch must remain buildable and testable without compiling another EF Core branch.

## Provider layers

Each EF Core branch implements these layers in order:

1. Options extension and provider invariant name.
2. Relational connection and database creator.
3. Mode-specific SQL generator and identifier delimiters.
4. CLR/provider type mappings and model conventions.
5. Query translation and parameter binding.
6. Update SQL generation, generated-key retrieval and modification batching.
7. Migrations and schema introspection after the core CRUD path is stable.

MySQL and Oracle differences are represented by mode-specific services or small strategy objects. A common base is
appropriate only when the generated SQL and EF contract are demonstrably identical.

## Release process

1. Read the official EF Core documentation and breaking-change notes for the target major.
2. Create or update the target branch from the shared baseline.
3. Port the provider service registrations and compile against the exact EF Core dependency range.
4. Run provider, relational, package-consumer and available live tests for both modes.
5. Publish OceanBase.EntityFrameworkCore with the matching major version.
6. Record unverified server, proxy, TLS and runtime boundaries in the branch documentation.

When EF Core 11 is released, create efcore-11, port only the version-local layer, reuse the shared code after
review, and publish the 11.x package line. Existing 8.x-10.x branches remain independently buildable.
