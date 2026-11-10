# Repository Guidelines

## Scope

This repository contains the Entity Framework Core provider for the managed OceanBaseConnector ADO.NET
driver. Provider code must consume the ADO.NET driver through its public APIs. Protocol, pooling, TLS,
authentication and wire-format work belongs in the connector repository.

## Repository layout

    docs/                         Design and support contracts
    src/OceanBase.EntityFrameworkCore.Shared/   EF-independent shared code
    src/OceanBase.EntityFrameworkCore/          EF-version provider project
    tests/                        Unit, relational and live integration tests

Each long-lived EF Core branch contains one provider project compiled against one EF Core major version.
Shared source is kept small and dependency-free where practical. Do not introduce a compatibility wrapper
for an API that is used in only one EF Core version.

## C# and project style

- Use the repository's existing block namespace style, four-space indentation and braces on all non-trivial
  blocks.
- Keep Nullable enabled and ImplicitUsings disabled.
- Use explicit using directives and keep them ordered by framework, third-party and local namespaces.
- Use PascalCase for public types and members, camelCase for parameters and locals, and _camelCase
  for private fields.
- Prefer the simplest expression that keeps validation, cancellation and error handling visible.
- Do not expose provider-specific types from a general EF Core API unless the feature is documented and tested.
- Treat EF Core internal APIs as version-local. Do not hide a breaking API change behind reflection or a broad
  catch unless the design document explicitly requires it.
- Warnings are errors. A warning suppression requires a comment stating why it is safe and when it can be removed.

## Dependencies

- Use central package management for package versions.
- The provider package must have a bounded dependency on the matching EF Core major version.
- Reference the published OceanBaseConnector package for consumer-facing builds. A local project reference is
  allowed only for coordinated development and must be covered by a package-consumer test.
- Do not add a dependency for a small helper that the BCL or an existing project already provides.

## Testing

Every provider feature has a unit or relational test that proves its generated SQL, type mapping or service
registration. Live tests are separate and must record the OceanBase server version, tenant mode, transport,
authentication, client runtime and provider options.

Required test layers are:

1. Provider unit tests for options, mode selection, mappings and SQL fragments.
2. EF relational tests for query, update, transaction and model behavior.
3. EF Core relational specification tests, extended with provider fixtures and SQL assertions where useful.
4. Package-consumer tests using only the packed provider and connector packages.
5. Optional live tests against MySQL and Oracle compatibility tenants.

An unavailable database, proxy, TLS or authentication environment is recorded as an unverified boundary; it is
not converted into a client pass or a client defect.

## Documentation and review

Public behavior, unsupported features and server prerequisites are documented before implementation. A change
that affects more than one EF Core branch must update the shared design document and each affected branch's
compatibility notes.

Before merging a feature, review the generated SQL, parameter names, identifier quoting, null semantics,
identity/key retrieval, transaction behavior and cancellation behavior for both compatibility modes.

## Git and releases

- dev is the integration branch for the current implementation.
- Long-lived branches are named efcore-8, efcore-9, efcore-10, and so on.
- A branch is compiled against exactly one EF Core major version.
- All branches publish the package ID OceanBase.EntityFrameworkCore; the package major version identifies
  the EF Core major (8.x, 9.x, 10.x, 11.x).
- Use Gitmoji Conventional Commits, for example: ✨ feat(efcore8): add relational options registration.
- A release requires passing unit tests, package-consumer tests and the documented compatibility checks for the
  target branch. Live evidence is reported separately from local validation.
