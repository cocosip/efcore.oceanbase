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

## Version isolation and branch flow

EF Core provider services use APIs that can change between major releases. The source is therefore maintained in
an EF10 mainline and independent compatibility branches:

    dev         -> historical integration checkpoint
    efcore/10.0 -> active EF10 development, packaging and release branch
                    \
                     +-> efcore/8.0  -> EF Core 8 compatibility line
                     +-> efcore/9.0  -> EF Core 9 compatibility line

The version-line names use the efcore/ prefix so a branch cannot be confused with a package version or tag.
The 8.0, 9.0 and 10.0 numbers follow Npgsql's release-line convention, while the prefix makes the branch role
explicit for this repository. Patch releases use tags and, when needed, short-lived fix branches such as
fix/efcore-10.0.1.

The normal release flow is:

1. Develop and test on efcore/10.0 (the user's current development branch).
2. Review the tested checkpoint on efcore/10.0; synchronization with other branches is deliberate.
3. Pack and publish the 10.x package from efcore/10.0.
4. Fork efcore/8.0 and efcore/9.0 from a stable efcore/10.0 release commit when compatibility work is needed.
5. Adapt each compatibility branch independently and publish only its matching package line.

The package ID stays OceanBase.EntityFrameworkCore. The package major version and dependency range identify the
EF Core major. Application TFM and EF Core major are intentionally independent.

Shared code is kept in a small project or shared source directory. Fixes are backported deliberately with
cherry-picks or focused merges; a compatibility branch must remain buildable and testable without compiling
another EF Core branch.

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
2. Develop the current implementation on efcore/10.0.
3. Review the checkpoint and compile against the bounded EF Core 10 dependency range.
4. Run provider, relational, package-consumer and available live tests for both modes.
5. Pack and publish OceanBase.EntityFrameworkCore 10.x from efcore/10.0.
6. When required, fork efcore/8.0 or efcore/9.0 from that release commit and port the version-local APIs.
7. Record unverified server, proxy, TLS and runtime boundaries in the branch documentation.

When EF Core 11 is released, create efcore/11.0, port only the version-local layer, reuse the shared code after
review, and publish the 11.x package line. Existing efcore/8.0, efcore/9.0, and efcore/10.0 branches remain
independently buildable.
