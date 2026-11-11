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

The efcore/10.0 branch is the active EF10 development, packaging and release branch as requested on resumption.
dev retains the preceding checkpoint; synchronize other branches only deliberately. The efcore/8.0 and efcore/9.0 branches fork from an
efcore/10.0 release baseline and then adapt independently to their EF Core major. Shared source is kept small
and dependency-free where practical. Do not introduce a compatibility wrapper for an API that is used in only one
EF Core version.

## C# and project style

### Line endings

The root `.gitattributes` is authoritative for Git line endings on every platform: detected text uses LF,
including C#, XML project/solution files, Markdown, YAML and PowerShell scripts; `.bat`/`.cmd` use CRLF.
Binary detection remains automatic. These repository rules take precedence over a developer's global
`core.autocrlf` setting, so CI and fresh checkouts do not depend on the host operating system.

Existing tracked blobs are already LF-normalized; no bulk renormalization is needed for this change.
Existing working files may retain their prior endings until rewritten or checked out again; Git still
normalizes text when staging. Use an LF-capable editor. Do not discard local changes to refresh line endings.

The GitHub Actions CI workflow runs the Release solution tests and package-content gate on both Windows and
Linux using PowerShell 7. It does not connect to databases or publish packages. Both jobs and all four gates
passed for e41981e in [run 42865290840](https://github.com/cocosip/efcore.oceanbase/actions/runs/42865290840).
That evidence applies to that commit; subsequent changes need their own run. Local Windows checks alone do
not establish Linux or hosted-runner compatibility.

The standalone PackageConsumer executable is a manually run, offline NuGet-consumer smoke check;
it is not included in the solution or executed by CI. CI only invokes its Verify-Package.ps1 archive
content check. The solution's ConsumerTests project contains ordinary provider configuration tests.
PackageConsumer reports managed verification failures on stderr with exit code1, including expected
negative probes against older packages; it must not use unhandled exceptions as a failure signal.
This executable is separate from the postponed comprehensive real-database test application.

### C# conventions

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
- The provider package must use a bounded dependency range for the matching EF Core major version, such as
  [10.0.0, 11.0.0), instead of forcing one patch version on consumer applications.
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
5. Required live tests for claimed runtime features against at least one declared supported environment in each
   MySQL and Oracle mode. Additional proxy, TLS, HA and server-version environments extend the evidence matrix.

An unavailable database, proxy, TLS or authentication environment is recorded as an unverified boundary; it is
not converted into a client pass or a client defect.

### Live-test credentials and ownership

The user will provide database access later. Store local live-test connection strings with .NET User Secrets,
outside the repository, using `ConnectionStrings:OceanBaseMySql` and `ConnectionStrings:OceanBaseOracle`.
When adding the live-test fixture, assign its project a stable `UserSecretsId` and explicitly load User Secrets
in test configuration; a test project does not load them automatically. The ID is public metadata, not a secret.
This is the agreed fixture contract; the current intercepted tests do not load secrets or connect to a server.

Do not put credentials in source, tracked configuration, documentation, command output or test artifacts. Do not
print secret values or full connection strings when configuring or diagnosing tests. Keep sensitive-data logging
disabled for live acceptance runs and redact connector errors before recording evidence. Store credentials via a
local input path that avoids echoing them into tool output or shell history.

Before tests create or delete objects, record the user-authorized database/schema and cleanup scope for each mode.
Use only test-owned objects within that scope. Until access and scope are provided, live gates remain unverified;
continue local implementation and tests without treating unavailable infrastructure as a successful acceptance run.

## Documentation and review

Public behavior, unsupported features and server prerequisites are documented before implementation. A change
that affects more than one EF Core branch must update the shared design document and each affected branch's
compatibility notes.

Use docs/efcore/efcore-10-feature-checklist.md as the EF10 acceptance inventory. Name affected feature IDs before
development and update their criteria, tests and per-mode evidence together. Add missing features to the inventory
before implementation. Inherited EF behavior, generated SQL and intercepted commands are not live server proof.

Before merging a feature, review the generated SQL, parameter names, identifier quoting, null semantics,
identity/key retrieval, transaction behavior and cancellation behavior for both compatibility modes.

## Git and releases

- efcore/10.0 is the active development and release branch used to pack and publish the 10.x package.
- dev retains the preceding integration checkpoint; synchronization is not implicit.
- efcore/8.0 and efcore/9.0 fork from an efcore/10.0 release commit and are adapted independently.
- A version branch is compiled against exactly one EF Core major version.
- All branches publish the package ID OceanBase.EntityFrameworkCore; the package major version identifies
  the EF Core major (8.x, 9.x, 10.x, 11.x).
- Use Gitmoji Conventional Commits, for example: ✨ feat(efcore8): add relational options registration.
- Review and test changes on efcore/10.0 before committing; pack and publish from that branch after release gates pass.
- A release requires passing unit tests, package-consumer tests and the documented compatibility checks for the
  target branch. Live evidence is reported separately from local validation.
