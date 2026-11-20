# EF Core 8 Release Checklist

Record implementation in the [feature matrix](efcore-8-feature-matrix.md)
and development work in the [gap matrix](efcore-8-gap-matrix.md). The EF10 release checklist owns
the common acceptance scenarios and historical evidence. Run branch-specific checks before claiming
EF8 acceptance. [Baseline acceptance scenarios](efcore-10-release-checklist.md#acceptance-requirements-by-feature)

## Target and branch

- Record the exact fork revision of the completed `efcore/9.0` branch, candidate revision, SDK, runtime, operating system and
  dependency resolution. Use an SDK that reads the existing solution and install the .NET 8 runtime
  for `net8.0` tests. Treat later runtime targets as separate acceptance profiles.
- Target `Sharp.OceanBase.EntityFrameworkCore` 8.x with a `net8.0` asset and EF runtime dependencies in
  `[8.0.0,9.0.0)`. Build against the floor. Use EF 8.0.0 and 8.0.34 to exercise the
  dependency range. Align test, Design, Proxies, specification and `dotnet-ef` versions with each run.
- Verify the selected `OceanBaseConnector` asset and transitive dependencies. Connector 0.4.0 has a
  `net8.0` asset and is the initial compatibility target. Record the exact published dependency
  used in the candidate. Consult the repository's [branch and publication policy](version-support-matrix.md#branches-cicd-and-publishing).

## Release gates

| Gate | State | Required branch evidence |
| --- | --- | --- |
| R-01 Dependency and source identity | Passed locally | Verify SDK, target framework, resolved Connector, dependency advisories and candidate source revision. |
| R-02 Local and relational specifications | Passed locally | Release unit, functional, consumer configuration and EF8 relational model-building tests against 8.0.0 and 8.0.34. |
| R-03 Live database | Passed for the recorded direct profiles | Run the opt-in live suite and feature sample against both OceanBase modes. |
| R-04 Package compatibility | Passed locally | Verify the floor package and isolated consumers on both patches. Require strict restore failures on adjacent majors. |
| R-05 Migrations, scaffolding and code generation | Passed for Windows tooling and direct live migrations | Verify generated C#, reverse engineering, compiled models, migration SQL, history, upgrade, rollback and bundles with EF8 tools. |
| R-06 Platform and CI | Pending | Run branch CI tests and package validation on the intended Windows and Linux runners. Record separately any platform exclusions or manual acceptance. |
| R-07 Deployment and compatibility modes | Direct profile only | Use independent evidence for each additional proxy, TLS or HA deployment profile. |
| R-08 Review | Passed for the local scope | Review EF8 API, query and migration changes and reconcile the version inventories. |
| R-09 Sensitive data and database cleanup | Passed for the recorded fixtures | Use User Secrets and verify cleanup of fixture-owned objects. |
| R-10 Publication | Pending | Confirm package availability on the intended feed, verify candidate provenance and get release authorization. Publish only for the matching release tag under the repository policy. |

## Recording results

Record exact restore, test, package and tooling commands and their candidate identity at the affected
gate. Keep detailed logs and TRX in ignored `artifacts/`. Use EF8-generated artifacts for tooling
checks and reuse the EF10 scenario list only to select cases. The package-content script and isolated
consumers must verify the candidate independently of project references.

## Stable release preparation

The Windows package checkpoint `156c9bc` passed the Release solution, package archive and
source revision checks, and isolated consumers on EF 8.0.0 and 8.0.34. The package-only
live probes passed MySQL and Oracle direct connections and four MySQL computed-column migration
cases with TLS disabled. NuGet.org had no package under the selected ID at the time of the check,
and the resolved package and consumer dependencies had no matches in the live NuGet advisory feed.
Evidence is under `artifacts/release-prep/ef8/` and `artifacts/release-prep/audit.log`.
Hosted verification of `ci-cd.yml` and activation of the release repository's NuGet Trusted
Publishing policy remain pending. See the [release policy](version-support-matrix.md#branches-cicd-and-publishing).

## Local acceptance evidence

The branch starts from `efcore/9.0` revision `7ca675f`. Windows ran SDK 10.0.403 with the
.NET 8.0.33 runtime. Local logs, TRX files and packages are under `artifacts/efcore8-port/`.

- **R-01** — Release assets selected `net8.0` and OceanBaseConnector 0.4.0. NuGet advisory
  data dated November 19, 2026 identified affected floor dependencies. The branch pins
  `Microsoft.Extensions.Caching.Memory` 8.0.1 and raises the private floor tooling graph to
  `System.Text.Json` 8.0.5. A repeat audit of the Provider, package-consumer and
  tooling graphs for both patches found zero matches. See `audit-secured.log` and
  `audit-tooling-secured.log`.
- **R-02** — `dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false`
  passed 5,813 cases with EF 8.0.0 and 5,817 with EF 8.0.34. Each passed 2,775 unit, 2,450
  intercepted functional and 2 consumer configuration tests. The six relational model-building
  families passed 586 and 590 cases respectively, in both modes. There were zero failures or skips.
  See `secured-floor.log` and `secured-patch.log`. The patch run resolves matching EF runtime,
  Design, Proxies and specification packages through `EfCore8Patch.Packages.props`.
- **R-03** — The final serial Release live run with EF 8.0.0 passed 931 cases across MySQL
  and Oracle, with zero failures or skips. See `test-results/live-secured-full.trx`.
  The feature sample passed 24 synchronous and 24 asynchronous scenarios across both modes.
  See `sample-secured-sync.log` and `sample-secured-async.log`.
  In earlier runs two MySQL namespace probes failed with a connection reset or timeout before
  fixture DDL. The affected test families passed all 20 focused retry cases before the full
  rerun. See `live-final.log`, `live-secured-collections.log` and `live-secured-retry.log`.
- **R-04** — The floor build passed the package archive, dependency and revision checks.
  Both package-only consumers passed against the same package with EF 8.0.0 and 8.0.34.
  Strict restore rejected EF 7.0.0 with NU1605 and EF 9.0.0 with NU1608. See
  `consumer-secured-*.log` and `negative-secured-*.log`.
- **R-05** — `samples/OceanBaseProviderValidation/Run-Migrations.ps1 -Mode Both -NoTls`
  with `dotnet-ef` 8.0.0 generated and compiled both revisions and wrote forward,
  idempotent and rollback SQL. Both modes passed initial application, upgrade with data
  preservation, rollback, repeat application, rollback to zero and cleanup. See
  `live-migrations-floor.log`. Use external deployment serialization for migrations.
  `tests/OceanBase.EntityFrameworkCore.ToolingConsumer/Verify-Tooling.ps1` with matching
  8.0.0 and 8.0.34 CLIs verified the same floor package. Both modes passed catalog
  scaffolding, generated model compilation with and without proxies, model-change checks,
  migration script generation and `win-x64` bundle startup with `--help`. See
  `tooling-secured-*.log`.
- **R-06** — The branch workflow defines Release verification and package-content checks
  on Windows and Linux. Hosted runner results remain pending.
- **R-07** — The recorded profiles use direct MySQL and Oracle connections to OceanBase
  4.4.2.2 with the configured test accounts and TLS disabled. Oracle generated-value cases
  use the public Connector `UseOraclePrepareExecute` option.
- **R-08** — Review covered EF8 collection mapping and query-plan reuse, nullable Boolean
  bulk setters, async cancellation and command return, .NET 8 temporal arithmetic,
  migration command ordering and model-managed data. The review fixed colliding aliases
  for independent empty collection roots and corrected package fixture expectations
  for EF8 SQL shapes.
- **R-09** — Credentials come from User Secrets. Samples and schema fixtures use unique
  names, with cleanup scoped to their own objects. The final live run and migration sample
  passed the fixture cleanup checks. The two namespace probe failures occurred before fixture
  object creation.
