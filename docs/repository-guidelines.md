# Repository Guidelines

## Scope

This repository contains the Entity Framework Core provider for the managed OceanBaseConnector ADO.NET
driver. Provider code must consume the ADO.NET driver through its public APIs. Protocol, pooling, TLS,
authentication and wire-format work belongs in the connector repository.

Each module must identify its owning layer and public Connector APIs. Follow the
[architecture boundaries](provider-architecture.md): a driver API gap is a recorded dependency/proposal,
not permission to add a driver implementation or bypass its API inside EF. Explain proposed driver
changes to protocol, authentication, TLS, pooling or wire codecs and obtain user authorization first.

Both compatibility modes are OceanBase Database modes, not Oracle Database or MySQL Server. Base
feature support, SQL syntax and restrictions on the targeted OceanBase version's own documentation
and source, and verify execution against OceanBase. Upstream Oracle/MySQL references may explain a
comparison; they do not establish an OceanBase capability or replace its live acceptance evidence.

## Repository layout

    docs/                         Design and support contracts
    src/OceanBase.EntityFrameworkCore.Shared/   EF-independent shared code
    src/OceanBase.EntityFrameworkCore/          EF-version provider project
    tests/                        Unit, relational and live integration tests

The [version-support document](efcore/version-support-matrix.md) owns branch, porting and release
policy. Keep shared source small and dependency-free where practical. Do not introduce a compatibility
wrapper for an API used in only one EF Core version.

## C# and project style

### Line endings

The root `.gitattributes` is authoritative for Git line endings on every platform: detected text uses LF,
including C#, XML project/solution files, Markdown, YAML and PowerShell scripts; `.bat`/`.cmd` use CRLF.
Binary detection remains automatic. These repository rules take precedence over a developer's global
`core.autocrlf` setting, so CI and fresh checkouts do not depend on the host operating system.

Existing tracked blobs are already LF-normalized; no bulk renormalization is needed for this change.
Existing working files may retain their prior endings until rewritten or checked out again; Git still
normalizes text when staging. Use an LF-capable editor. Do not discard local changes to refresh line endings.

The shared [CI/CD workflow](../.github/workflows/ci-cd.yml) runs Release solution tests and
package-content validation on Windows and Linux for version branches and `master`. An authorized
matching release tag runs the Windows verification and isolated PackageConsumer before GitHub Actions
publishes the verified artifact. See the [branch and release policy](efcore/version-support-matrix.md#branches-cicd-and-publishing).

PackageConsumer is a standalone NuGet-consumer smoke check. The solution's ConsumerTests project
contains ordinary provider configuration tests.
PackageConsumer reports managed verification failures on stderr with exit code1, including expected
negative probes against older packages; it must not use unhandled exceptions as a failure signal.
Native acceptance lives in the opt-in LiveTests project; the [samples](../samples/README.md) provide
separate interactive database and migration probes. None are executed by the default solution CI run.

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

The solution's SpecificationTests project currently runs six complete inherited official relational
model-building families (one-to-many, many-to-one, one-to-one, many-to-many, inheritance and owned types)
against the real provider in both modes. It blocks EF connection opening and is safe for ordinary offline
CI execution. Selected store-backed official runtime fixtures are tested separately, as recorded in the
release checklist; offline metadata tests do not establish runtime query or write support.

An unavailable database, proxy, TLS or authentication environment is recorded as an unverified boundary; it is
not converted into a client pass or a client defect.

### Live-test credentials and ownership

The live fixture explicitly loads .NET User Secrets ID `OceanBase.EntityFrameworkCore.LiveTests` with
`ConnectionStrings:OceanBaseMySql` and `ConnectionStrings:OceanBaseOracle`. Administration access uses
separate keys when needed. Keep values outside the repository; the ID and key names are public metadata.
The opt-in fixture lives in `tests/OceanBase.EntityFrameworkCore.LiveTests` and is intentionally excluded from
the default solution run; set `OCEANBASE_RUN_LIVE_TESTS=1` only for an explicit live acceptance command.
Intercepted tests remain separate: they do not load secrets or connect to a server.

Live tests preserve the configured TLS policy by default. For a server without TLS, the explicitly authorized
ordinary-direct profile can set `OCEANBASE_LIVE_DISABLE_TLS=1` in the test process. This only overrides test
connection strings in memory; it never changes User Secrets, the Provider or Connector defaults. Record that
profile as TLS Disabled, not TLS acceptance. The connection-lifecycle profile creates no database objects.

The read-only EF query profile uses four inline SELECT/UNION ALL rows without creating tables or writing
data. Run local shape checks with `--filter FullyQualifiedName~ReadOnlyQueryProfileTests`; they do not read
secrets or open connections. Native execution requires the opt-in and
`--filter FullyQualifiedName~LiveReadOnlyQueryTests`. Keep these filters distinct: default solution tests
exclude this project, while an unfiltered explicit project run includes live cases. Native profile errors
record only stage/type/server code/reader count, never arbitrary connector messages or inner exceptions.
See `docs/efcore/efcore-9-release-checklist.md` for the EF Core 9 acceptance status.

Do not put credentials in source, tracked configuration, documentation, command output or test artifacts. Do not
print secret values or full connection strings when configuring or diagnosing tests. Keep sensitive-data logging
disabled for live acceptance runs and redact connector errors before recording evidence. Store credentials via a
local input path that avoids echoing them into tool output or shell history.

Before tests create or delete objects, record the user-authorized database/schema and cleanup scope for each mode.
Use only test-owned objects within that scope. Existing ordinary-direct acceptance creates owned objects
in pre-provisioned namespaces; it does not authorize namespace creation/deletion or prefix-based cleanup.
Record unavailable infrastructure as unverified, never as a successful acceptance run.

## Documentation and review

Public behavior, unsupported features and server prerequisites are documented before implementation. A change
that affects more than one EF Core branch must update the owning shared contract and each affected branch's
compatibility notes.

Follow [AGENTS.md](../AGENTS.md) for English-only documentation, stable IDs and the three-inventory
ownership rule across all EF versions. The [documentation index](README.md) identifies each topic's owner.
Name affected feature/gap IDs before implementation; record implementation separately from acceptance.
Inherited EF behavior, generated SQL and intercepted commands are not live server proof; missing evidence
does not make implemented code undeveloped. Do not create per-feature plans, reports or duplicate inventories.

Before merging a feature, review the generated SQL, parameter names, identifier quoting, null semantics,
identity/key retrieval, transaction behavior and cancellation behavior for both compatibility modes.

## Git and releases

- Use the branch and dependency rules in [version support](efcore/version-support-matrix.md).
- Use Gitmoji Conventional Commits, for example: ✨ feat(efcore8): add relational options registration.
- Preserve unrelated changes, review and test the affected scope before committing, and do not push,
  tag or publish without explicit authorization.
- A release requires passing unit tests, package-consumer tests and the documented compatibility checks for the
  target branch. Live evidence is reported separately from local validation.
